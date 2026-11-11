using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists.LidarrFm;

/// <summary>
/// Lidarr Import List plugin that queries a user's Last.fm account activity, derives recommended
/// albums, resolves each album's MusicBrainz Release-Group ID, and adds it to Lidarr while
/// preserving existing artists' monitoring/profile settings and applying configurable profiles
/// and monitoring modes for new artists.
/// </summary>
public class LastFmRecommendImportList : ImportListBase<LastFmRecommendSettings>
{
    private readonly ILastFmApiClient _lastFmApiClient;
    private readonly IMusicBrainzAlbumResolver _albumResolver;
    private readonly ILastFmAlbumImportService _albumImportService;

    public override string Name => "Last.fm Recommended Albums";

    public override ImportListType ListType => ImportListType.LastFm;

    public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(12);

    public LastFmRecommendImportList(
        ILastFmApiClient lastFmApiClient,
        IMusicBrainzAlbumResolver albumResolver,
        ILastFmAlbumImportService albumImportService,
        IImportListStatusService importListStatusService,
        IConfigService configService,
        IParsingService parsingService,
        Logger logger)
        : base(importListStatusService, configService, parsingService, logger)
    {
        _lastFmApiClient = lastFmApiClient;
        _albumResolver = albumResolver;
        _albumImportService = albumImportService;
    }

    public override IEnumerable<ProviderDefinition> DefaultDefinitions
    {
        get
        {
            var config = new LastFmRecommendSettings();
            yield return new ImportListDefinition
            {
                EnableAutomaticAdd = true,
                ShouldMonitor = ImportListMonitorType.None,
                ShouldMonitorExisting = false,
                Implementation = GetType().Name,
                Settings = config
            };
        }
    }

    public override IList<ImportListItemInfo> Fetch()
    {
        var items = new List<ImportListItemInfo>();
        if (Definition is not ImportListDefinition importListDefinition)
        {
            return items;
        }

        // Ensure Lidarr's built-in ImportListSyncService never alters existing artists or albums
        // after our LastFmAlbumImportService has applied the exact existing/new artist rules.
        importListDefinition.ShouldMonitorExisting = false;
        importListDefinition.ShouldMonitor = ImportListMonitorType.None;

        try
        {
            var candidates = _lastFmApiClient.FetchRecommendedAlbums(Settings);
            _logger.Debug("Lidarr.fm fetched {0} recommended album candidates from Last.fm for user '{1}'.",
                candidates.Count, Settings.UserId);

            var resolvedAlbums = new List<ResolvedRecommendedAlbum>();
            var seenAlbumMbids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                var resolved = _albumResolver.ResolveAlbum(candidate, Settings);
                if (resolved == null || !seenAlbumMbids.Add(resolved.AlbumMusicBrainzId))
                {
                    continue;
                }

                resolvedAlbums.Add(resolved);
            }

            if (resolvedAlbums.Count == 0)
            {
                _importListStatusService.RecordSuccess(importListDefinition.Id);
                return items;
            }

            var summary = _albumImportService.ImportRecommendedAlbums(importListDefinition, Settings, resolvedAlbums);

            foreach (var album in summary.ProcessedAlbums)
            {
                items.Add(new ImportListItemInfo
                {
                    Album = album.AlbumTitle,
                    AlbumMusicBrainzId = album.AlbumMusicBrainzId,
                    Artist = album.ArtistName,
                    ArtistMusicBrainzId = album.ArtistMusicBrainzId,
                    ImportListId = importListDefinition.Id,
                    ImportList = importListDefinition.Name
                });
            }

            _importListStatusService.RecordSuccess(importListDefinition.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Lidarr.fm failed during Last.fm Recommended Albums sync for '{0}'.", Settings.UserId);
            _importListStatusService.RecordFailure(importListDefinition.Id);
        }

        return CleanupListItems(items);
    }

    protected override IList<ImportListItemInfo> CleanupListItems(IEnumerable<ImportListItemInfo> releases)
    {
        var result = releases
            .DistinctBy(r => r.AlbumMusicBrainzId ?? $"{r.Artist}::{r.Album}")
            .ToList();

        if (Definition != null)
        {
            result.ForEach(c =>
            {
                c.ImportListId = Definition.Id;
                c.ImportList = Definition.Name;
            });
        }

        return result;
    }

    protected override void Test(List<ValidationFailure> failures)
    {
        var failure = TestConnection();
        if (failure != null)
        {
            failures.Add(failure);
        }
    }

    private ValidationFailure? TestConnection()
    {
        try
        {
            _lastFmApiClient.ValidateConnection(Settings);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Lidarr.fm Last.fm connection test failed for user '{0}'.", Settings.UserId);
            return new ValidationFailure(nameof(Settings.UserId), $"Unable to connect to Last.fm: {ex.Message}");
        }
    }
}
