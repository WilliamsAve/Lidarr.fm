using NLog;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.ImportLists.LidarrFm;

public sealed record LidarrFmImportSummary(
    IReadOnlyList<LidarrFmResolvedAlbum> ProcessedAlbums,
    int NewArtistsAdded,
    int NewArtistAlbumsAdded,
    int ExistingArtistAlbumsAdded,
    int ExistingAlbumsSkipped,
    int ExcludedItemsSkipped);

public interface ILidarrFmAlbumImportService
{
    LidarrFmImportSummary ImportRecommendedAlbums(
        ImportListDefinition definition,
        LidarrFmImportListSettings settings,
        IReadOnlyList<LidarrFmResolvedAlbum> resolvedAlbums);
}

/// <summary>
/// Adds recommended albums to Lidarr using their MusicBrainz Release-Group ID (ForeignAlbumId)
/// while enforcing strict separation between existing artists and new artists:
///
/// 1. Existing Artist in Lidarr:
///    - None of the existing artist's monitoring options (Monitored, MonitorNewItems), profiles
///      (QualityProfileId, MetadataProfileId), root folder, tags, or existing albums' monitoring
///      states are ever modified.
///    - If the recommended album is not yet in Lidarr's database for that artist, it is added using
///      its MusicBrainz ID with Monitored = false and SearchForNewAlbum = false.
///
/// 2. New Artist not yet in Lidarr:
///    - Uses the user-configured Quality Profile, Metadata Profile, Root Folder, and New Artist
///      Monitoring Mode (Monitor That Album, Monitor Existing Albums, Monitor All Albums, or
///      Monitor Future Albums).
/// </summary>
public class LidarrFmAlbumImportService : ILidarrFmAlbumImportService
{
    private readonly IArtistService _artistService;
    private readonly IAlbumService _albumService;
    private readonly IAddArtistService _addArtistService;
    private readonly IAddAlbumService _addAlbumService;
    private readonly IProvideAlbumInfo _albumInfo;
    private readonly IImportListExclusionService _exclusionService;
    private readonly IQualityProfileRepository _qualityProfileRepository;
    private readonly IMetadataProfileRepository _metadataProfileRepository;
    private readonly IRootFolderService _rootFolderService;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly Logger _logger;

    public LidarrFmAlbumImportService(
        IArtistService artistService,
        IAlbumService albumService,
        IAddArtistService addArtistService,
        IAddAlbumService addAlbumService,
        IProvideAlbumInfo albumInfo,
        IImportListExclusionService exclusionService,
        IQualityProfileRepository qualityProfileRepository,
        IMetadataProfileRepository metadataProfileRepository,
        IRootFolderService rootFolderService,
        IManageCommandQueue commandQueueManager,
        Logger logger)
    {
        _artistService = artistService;
        _albumService = albumService;
        _addArtistService = addArtistService;
        _addAlbumService = addAlbumService;
        _albumInfo = albumInfo;
        _exclusionService = exclusionService;
        _qualityProfileRepository = qualityProfileRepository;
        _metadataProfileRepository = metadataProfileRepository;
        _rootFolderService = rootFolderService;
        _commandQueueManager = commandQueueManager;
        _logger = logger;
    }

    public LidarrFmImportSummary ImportRecommendedAlbums(
        ImportListDefinition definition,
        LidarrFmImportListSettings settings,
        IReadOnlyList<LidarrFmResolvedAlbum> resolvedAlbums)
    {
        var exclusions = _exclusionService.All()
            .Where(x => !string.IsNullOrWhiteSpace(x.ForeignId))
            .Select(x => x.ForeignId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var processed = new List<LidarrFmResolvedAlbum>();
        var newArtistsToAdd = new List<Artist>();
        var newArtistsByMbid = new Dictionary<string, Artist>(StringComparer.OrdinalIgnoreCase);
        var newArtistAlbumsToAdd = new List<Album>();
        var existingArtistAlbumsToAdd = new List<Album>();

        var existingAlbumsSkipped = 0;
        var excludedSkipped = 0;

        var effectiveQualityProfileId = ResolveQualityProfileId(definition, settings);
        var effectiveMetadataProfileId = ResolveMetadataProfileId(definition, settings);
        var effectiveRootFolderPath = ResolveRootFolderPath(definition, settings);
        var monitorMode = (LidarrFmNewArtistMonitorMode)settings.NewArtistMonitorMode;

        foreach (var item in resolvedAlbums)
        {
            if (!LidarrFmMusicBrainzAlbumResolver.IsValidMbid(item.AlbumMusicBrainzId))
            {
                continue;
            }

            // Enrich/verify canonical artist MBID and release date from SkyHook using the album's MusicBrainz ID when available.
            var (canonicalArtistMbid, canonicalArtistName, canonicalAlbumTitle, releaseDate) =
                LookupAlbumSkyHookMetadata(item);

            if (exclusions.Contains(item.AlbumMusicBrainzId) || exclusions.Contains(canonicalArtistMbid))
            {
                _logger.Debug("Lidarr.fm skipping '{0}' [{1}] by '{2}' [{3}] due to Import List Exclusion.",
                    canonicalAlbumTitle, item.AlbumMusicBrainzId, canonicalArtistName, canonicalArtistMbid);
                excludedSkipped++;
                continue;
            }

            var resolvedItem = new LidarrFmResolvedAlbum(
                AlbumMusicBrainzId: item.AlbumMusicBrainzId,
                AlbumTitle: canonicalAlbumTitle,
                ArtistMusicBrainzId: canonicalArtistMbid,
                ArtistName: canonicalArtistName);

            // Check whether the artist already exists in Lidarr.
            var existingArtist = _artistService.FindById(canonicalArtistMbid);
            if (existingArtist != null)
            {
                ProcessForExistingArtist(existingArtist, resolvedItem, existingArtistAlbumsToAdd, ref existingAlbumsSkipped);
                processed.Add(resolvedItem);
                continue;
            }

            // Check if the album already exists in Lidarr DB (e.g., via an alias/merged artist metadata ID).
            var existingAlbum = _albumService.FindById(resolvedItem.AlbumMusicBrainzId);
            if (existingAlbum != null)
            {
                _logger.Debug("Lidarr.fm skipping album '{0}' [{1}] because it already exists in Lidarr DB.",
                    resolvedItem.AlbumTitle, resolvedItem.AlbumMusicBrainzId);
                existingAlbumsSkipped++;
                processed.Add(resolvedItem);
                continue;
            }

            ProcessForNewArtist(
                definition,
                monitorMode,
                effectiveQualityProfileId,
                effectiveMetadataProfileId,
                effectiveRootFolderPath,
                resolvedItem,
                releaseDate,
                newArtistsToAdd,
                newArtistsByMbid,
                newArtistAlbumsToAdd);

            processed.Add(resolvedItem);
        }

        // 1. Add albums for existing artists WITHOUT modifying any artist monitoring options or profiles.
        var addedExistingArtistAlbums = new List<Album>();
        foreach (var album in existingArtistAlbumsToAdd)
        {
            try
            {
                var added = _addAlbumService.AddAlbum(album, doRefresh: false);
                if (added != null)
                {
                    addedExistingArtistAlbums.Add(added);
                    if (added.Id > 0)
                    {
                        _commandQueueManager.Push(new RefreshAlbumCommand(added.Id, true));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Lidarr.fm failed to add album [{0}] for existing artist.", album.ForeignAlbumId);
            }
        }

        // 2. Add new artists and their recommended albums using the user's configured profiles & monitoring mode.
        var addedNewArtists = newArtistsToAdd.Count > 0
            ? _addArtistService.AddArtists(newArtistsToAdd, doRefresh: false, ignoreErrors: true)
            : new List<Artist>();

        var addedNewArtistAlbums = newArtistAlbumsToAdd.Count > 0
            ? _addAlbumService.AddAlbums(newArtistAlbumsToAdd, doRefresh: false, ignoreErrors: true)
            : new List<Album>();

        var newArtistIdsToRefresh = addedNewArtists
            .Select(a => a.Id)
            .Concat(addedNewArtistAlbums
                .Where(a => a.Artist?.Value != null && a.Artist.Value.Id > 0)
                .Select(a => a.Artist.Value.Id))
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (newArtistIdsToRefresh.Count > 0)
        {
            _commandQueueManager.Push(new BulkRefreshArtistCommand(newArtistIdsToRefresh, areNewArtists: true));
        }

        _logger.Info(
            "Lidarr.fm sync completed: {0} albums processed, {1} new artists added, {2} new-artist albums added, {3} existing-artist albums added, {4} existing skipped, {5} excluded.",
            processed.Count,
            addedNewArtists.Count,
            addedNewArtistAlbums.Count,
            addedExistingArtistAlbums.Count,
            existingAlbumsSkipped,
            excludedSkipped);

        return new LidarrFmImportSummary(
            ProcessedAlbums: processed,
            NewArtistsAdded: addedNewArtists.Count,
            NewArtistAlbumsAdded: addedNewArtistAlbums.Count,
            ExistingArtistAlbumsAdded: addedExistingArtistAlbums.Count,
            ExistingAlbumsSkipped: existingAlbumsSkipped,
            ExcludedItemsSkipped: excludedSkipped);
    }

    private void ProcessForExistingArtist(
        Artist existingArtist,
        LidarrFmResolvedAlbum item,
        List<Album> existingArtistAlbumsToAdd,
        ref int existingAlbumsSkipped)
    {
        var existingAlbum = _albumService.FindById(item.AlbumMusicBrainzId);
        if (existingAlbum != null)
        {
            _logger.Debug(
                "Lidarr.fm: Artist '{0}' [{1}] and Album '{2}' [{3}] already exist in Lidarr. Leaving all monitoring options and profiles untouched.",
                existingArtist.Name,
                existingArtist.ForeignArtistId,
                item.AlbumTitle,
                item.AlbumMusicBrainzId);
            existingAlbumsSkipped++;
            return;
        }

        if (existingArtistAlbumsToAdd.Any(a => string.Equals(a.ForeignAlbumId, item.AlbumMusicBrainzId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _logger.Debug(
            "Lidarr.fm: Artist '{0}' [{1}] already exists in Lidarr. Adding album '{2}' [{3}] with Monitored=false and leaving artist monitoring options and profiles untouched.",
            existingArtist.Name,
            existingArtist.ForeignArtistId,
            item.AlbumTitle,
            item.AlbumMusicBrainzId);

        var albumToAdd = new Album
        {
            ForeignAlbumId = item.AlbumMusicBrainzId,
            Title = item.AlbumTitle,
            Monitored = false,
            AnyReleaseOk = true,
            ProfileId = existingArtist.QualityProfileId,
            Artist = existingArtist,
            ArtistMetadata = existingArtist.Metadata.Value,
            ArtistMetadataId = existingArtist.ArtistMetadataId,
            AddOptions = new AddAlbumOptions
            {
                AddType = AlbumAddType.Manual,
                SearchForNewAlbum = false
            }
        };

        existingArtistAlbumsToAdd.Add(albumToAdd);
    }

    private static void ProcessForNewArtist(
        ImportListDefinition definition,
        LidarrFmNewArtistMonitorMode monitorMode,
        int qualityProfileId,
        int metadataProfileId,
        string rootFolderPath,
        LidarrFmResolvedAlbum item,
        DateTime? releaseDate,
        List<Artist> newArtistsToAdd,
        Dictionary<string, Artist> newArtistsByMbid,
        List<Album> newArtistAlbumsToAdd)
    {
        var (artistMonitorType, monitorNewItems, shouldMonitorAlbum) = MapNewArtistMonitoring(monitorMode, releaseDate);

        if (!newArtistsByMbid.TryGetValue(item.ArtistMusicBrainzId, out var artistToAdd))
        {
            artistToAdd = new Artist
            {
                Metadata = new ArtistMetadata
                {
                    ForeignArtistId = item.ArtistMusicBrainzId,
                    Name = item.ArtistName
                },
                Monitored = true,
                MonitorNewItems = monitorNewItems,
                RootFolderPath = rootFolderPath,
                QualityProfileId = qualityProfileId,
                MetadataProfileId = metadataProfileId,
                Tags = definition.Tags != null ? new HashSet<int>(definition.Tags) : new HashSet<int>(),
                AddOptions = new AddArtistOptions
                {
                    Monitored = true,
                    Monitor = artistMonitorType,
                    SearchForMissingAlbums = definition.ShouldSearch,
                    AlbumsToMonitor = new List<string>()
                }
            };

            newArtistsByMbid[item.ArtistMusicBrainzId] = artistToAdd;
            newArtistsToAdd.Add(artistToAdd);
        }

        if (monitorMode == LidarrFmNewArtistMonitorMode.ThatAlbum &&
            artistToAdd.AddOptions != null &&
            !artistToAdd.AddOptions.AlbumsToMonitor.Contains(item.AlbumMusicBrainzId))
        {
            artistToAdd.AddOptions.AlbumsToMonitor.Add(item.AlbumMusicBrainzId);
        }

        if (newArtistAlbumsToAdd.All(a => !string.Equals(a.ForeignAlbumId, item.AlbumMusicBrainzId, StringComparison.OrdinalIgnoreCase)))
        {
            var albumToAdd = new Album
            {
                ForeignAlbumId = item.AlbumMusicBrainzId,
                Title = item.AlbumTitle,
                Monitored = shouldMonitorAlbum,
                AnyReleaseOk = true,
                ProfileId = qualityProfileId,
                Artist = artistToAdd,
                AddOptions = new AddAlbumOptions
                {
                    AddType = AlbumAddType.Manual,
                    SearchForNewAlbum = definition.ShouldSearch && shouldMonitorAlbum
                }
            };

            newArtistAlbumsToAdd.Add(albumToAdd);
        }
    }

    internal static (MonitorTypes ArtistMonitorType, NewItemMonitorTypes MonitorNewItems, bool ShouldMonitorRecommendedAlbum)
        MapNewArtistMonitoring(LidarrFmNewArtistMonitorMode mode, DateTime? albumReleaseDate)
    {
        return mode switch
        {
            // Monitor only that specific recommended album (via AddOptions.AlbumsToMonitor)
            LidarrFmNewArtistMonitorMode.ThatAlbum => (
                MonitorTypes.All,
                NewItemMonitorTypes.None,
                true),

            // Monitor albums that have existing files on disk
            LidarrFmNewArtistMonitorMode.ExistingAlbums => (
                MonitorTypes.Existing,
                NewItemMonitorTypes.None,
                false),

            // Monitor all albums for the new artist
            LidarrFmNewArtistMonitorMode.AllAlbums => (
                MonitorTypes.All,
                NewItemMonitorTypes.All,
                true),

            // Monitor future albums for the new artist
            LidarrFmNewArtistMonitorMode.FutureAlbums => (
                MonitorTypes.Future,
                NewItemMonitorTypes.All,
                albumReleaseDate.HasValue && albumReleaseDate.Value > DateTime.UtcNow),

            _ => (
                MonitorTypes.All,
                NewItemMonitorTypes.None,
                true)
        };
    }

    private (string ArtistMbid, string ArtistName, string AlbumTitle, DateTime? ReleaseDate)
        LookupAlbumSkyHookMetadata(LidarrFmResolvedAlbum item)
    {
        try
        {
            var tuple = _albumInfo.GetAlbumInfo(item.AlbumMusicBrainzId);
            if (tuple != null)
            {
                var skyhookArtistMbid = !string.IsNullOrWhiteSpace(tuple.Item1)
                    ? tuple.Item1
                    : item.ArtistMusicBrainzId;

                var matchedArtistMeta = tuple.Item3?.FirstOrDefault(x =>
                    string.Equals(x.ForeignArtistId, skyhookArtistMbid, StringComparison.OrdinalIgnoreCase));

                var artistName = !string.IsNullOrWhiteSpace(matchedArtistMeta?.Name)
                    ? matchedArtistMeta!.Name
                    : item.ArtistName;

                var albumTitle = !string.IsNullOrWhiteSpace(tuple.Item2?.Title)
                    ? tuple.Item2!.Title
                    : item.AlbumTitle;

                return (skyhookArtistMbid, artistName, albumTitle, tuple.Item2?.ReleaseDate);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "SkyHook GetAlbumInfo lookup fallback used for album MBID '{0}'.", item.AlbumMusicBrainzId);
        }

        return (item.ArtistMusicBrainzId, item.ArtistName, item.AlbumTitle, null);
    }

    private int ResolveQualityProfileId(ImportListDefinition definition, LidarrFmImportListSettings settings)
    {
        if (settings.NewArtistQualityProfileId > 0)
        {
            return settings.NewArtistQualityProfileId;
        }

        if (definition.ProfileId > 0)
        {
            return definition.ProfileId;
        }

        return _qualityProfileRepository.All().FirstOrDefault()?.Id ?? 1;
    }

    private int ResolveMetadataProfileId(ImportListDefinition definition, LidarrFmImportListSettings settings)
    {
        if (settings.NewArtistMetadataProfileId > 0)
        {
            return settings.NewArtistMetadataProfileId;
        }

        if (definition.MetadataProfileId > 0)
        {
            return definition.MetadataProfileId;
        }

        return _metadataProfileRepository.All().FirstOrDefault()?.Id ?? 1;
    }

    private string ResolveRootFolderPath(ImportListDefinition definition, LidarrFmImportListSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.NewArtistRootFolderPath))
        {
            return settings.NewArtistRootFolderPath;
        }

        if (!string.IsNullOrWhiteSpace(definition.RootFolderPath))
        {
            return definition.RootFolderPath;
        }

        return _rootFolderService.All().FirstOrDefault()?.Path ?? string.Empty;
    }
}
