using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.LidarrFm;

public interface IMusicBrainzAlbumResolver
{
    ResolvedRecommendedAlbum? ResolveAlbum(LastFmRecommendedAlbumCandidate candidate, LastFmRecommendSettings settings);
}

/// <summary>
/// Resolves a Last.fm album recommendation into Lidarr's canonical MusicBrainz Release-Group ID
/// (ForeignAlbumId) and Artist MusicBrainz ID (ForeignArtistId).
///
/// Last.fm's album 'mbid' field is a MusicBrainz Release ID (specific edition) rather than a
/// MusicBrainz Release-Group ID (which Lidarr requires for albums). This resolver converts
/// Release MBIDs to Release-Group MBIDs via MusicBrainz WS/2 and falls back to Lidarr SkyHook
/// and MusicBrainz Release-Group search when Last.fm omits the MBID.
/// </summary>
public class MusicBrainzAlbumResolver : IMusicBrainzAlbumResolver
{
    private const string UserAgent = "Lidarr.fm/1.0.0 (https://github.com/WilliamsAve/Lidarr.fm)";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClient _httpClient;
    private readonly ISearchForNewAlbum _albumSearchService;
    private readonly Logger _logger;

    public MusicBrainzAlbumResolver(
        IHttpClient httpClient,
        ISearchForNewAlbum albumSearchService,
        Logger logger)
    {
        _httpClient = httpClient;
        _albumSearchService = albumSearchService;
        _logger = logger;
    }

    public ResolvedRecommendedAlbum? ResolveAlbum(
        LastFmRecommendedAlbumCandidate candidate,
        LastFmRecommendSettings settings)
    {
        if (string.IsNullOrWhiteSpace(candidate.AlbumTitle) || string.IsNullOrWhiteSpace(candidate.ArtistName))
        {
            return null;
        }

        // 1. If Last.fm supplied a valid GUID MBID, resolve the Release MBID -> Release-Group MBID via MusicBrainz WS/2,
        //    or check if it is already a Release-Group MBID recognized by Lidarr SkyHook.
        if (IsValidMbid(candidate.LastFmAlbumMbid))
        {
            var fromReleaseLookup = TryResolveFromMusicBrainzRelease(candidate, settings.MusicBrainzBaseUrl);
            if (fromReleaseLookup != null)
            {
                return fromReleaseLookup;
            }

            var fromDirectLidarrId = TryResolveFromSkyHookByMbid(candidate.LastFmAlbumMbid!, candidate);
            if (fromDirectLidarrId != null)
            {
                return fromDirectLidarrId;
            }
        }

        // 2. Query Lidarr's SkyHook search service by album title + artist name.
        var fromSkyHookSearch = TryResolveFromSkyHookByTitleAndArtist(candidate);
        if (fromSkyHookSearch != null)
        {
            return fromSkyHookSearch;
        }

        // 3. Fallback: Query MusicBrainz WS/2 release-group endpoint directly by title and artist.
        var fromReleaseGroupSearch = TryResolveFromMusicBrainzReleaseGroupSearch(candidate, settings.MusicBrainzBaseUrl);
        if (fromReleaseGroupSearch != null)
        {
            return fromReleaseGroupSearch;
        }

        _logger.Debug("Lidarr.fm could not resolve a MusicBrainz Release-Group ID for album '{0}' by '{1}'. Skipping.",
            candidate.AlbumTitle, candidate.ArtistName);
        return null;
    }

    private ResolvedRecommendedAlbum? TryResolveFromMusicBrainzRelease(
        LastFmRecommendedAlbumCandidate candidate,
        string musicBrainzBaseUrl)
    {
        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(musicBrainzBaseUrl)
                ? LastFmRecommendSettings.DefaultMusicBrainzBaseUrl
                : musicBrainzBaseUrl.TrimEnd('/') + "/";

            var request = new HttpRequestBuilder($"{baseUrl}release/{candidate.LastFmAlbumMbid}")
                .AddQueryParam("inc", "release-groups+artists")
                .AddQueryParam("fmt", "json")
                .SetHeader("User-Agent", UserAgent)
                .WithRateLimit(1.1)
                .Accept(HttpAccept.Json)
                .Build();

            request.SuppressHttpError = true;
            var response = _httpClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(response.Content))
            {
                return null;
            }

            var release = JsonSerializer.Deserialize<MusicBrainzReleaseResponse>(response.Content, JsonOptions);
            var releaseGroupId = release?.ReleaseGroup?.Id;
            if (!IsValidMbid(releaseGroupId))
            {
                return null;
            }

            var creditArtist = release?.ArtistCredit?.FirstOrDefault()?.Artist
                               ?? release?.ReleaseGroup?.ArtistCredit?.FirstOrDefault()?.Artist;

            var artistMbid = IsValidMbid(creditArtist?.Id)
                ? creditArtist!.Id!
                : (IsValidMbid(candidate.LastFmArtistMbid) ? candidate.LastFmArtistMbid! : null);

            var artistName = !string.IsNullOrWhiteSpace(creditArtist?.Name)
                ? creditArtist!.Name!
                : candidate.ArtistName;

            var albumTitle = !string.IsNullOrWhiteSpace(release?.ReleaseGroup?.Title)
                ? release!.ReleaseGroup!.Title!
                : (!string.IsNullOrWhiteSpace(release?.Title) ? release!.Title! : candidate.AlbumTitle);

            // If MusicBrainz release didn't include the artist MBID, verify via SkyHook using the Release-Group ID.
            if (!IsValidMbid(artistMbid))
            {
                var skyHookMatch = TryResolveFromSkyHookByMbid(releaseGroupId!, candidate);
                if (skyHookMatch != null)
                {
                    return skyHookMatch;
                }

                return null;
            }

            return new ResolvedRecommendedAlbum(
                AlbumMusicBrainzId: releaseGroupId!,
                AlbumTitle: albumTitle,
                ArtistMusicBrainzId: artistMbid!,
                ArtistName: artistName);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MusicBrainz release lookup failed for MBID '{0}'.", candidate.LastFmAlbumMbid);
            return null;
        }
    }

    private ResolvedRecommendedAlbum? TryResolveFromSkyHookByMbid(
        string albumMbid,
        LastFmRecommendedAlbumCandidate candidate)
    {
        try
        {
            var mapped = _albumSearchService.SearchForNewAlbum($"lidarr:{albumMbid}", null)?.FirstOrDefault();
            return MapSkyHookAlbum(mapped, candidate);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "SkyHook MBID lookup failed for '{0}'.", albumMbid);
            return null;
        }
    }

    private ResolvedRecommendedAlbum? TryResolveFromSkyHookByTitleAndArtist(LastFmRecommendedAlbumCandidate candidate)
    {
        try
        {
            var mapped = _albumSearchService.SearchForNewAlbum(candidate.AlbumTitle, candidate.ArtistName)?.FirstOrDefault();
            return MapSkyHookAlbum(mapped, candidate);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "SkyHook search failed for album '{0}' by '{1}'.", candidate.AlbumTitle, candidate.ArtistName);
            return null;
        }
    }

    private ResolvedRecommendedAlbum? TryResolveFromMusicBrainzReleaseGroupSearch(
        LastFmRecommendedAlbumCandidate candidate,
        string musicBrainzBaseUrl)
    {
        try
        {
            var baseUrl = string.IsNullOrWhiteSpace(musicBrainzBaseUrl)
                ? LastFmRecommendSettings.DefaultMusicBrainzBaseUrl
                : musicBrainzBaseUrl.TrimEnd('/') + "/";

            var escapedAlbum = EscapeLucene(candidate.AlbumTitle);
            var escapedArtist = EscapeLucene(candidate.ArtistName);
            var query = $"releasegroup:\"{escapedAlbum}\" AND artist:\"{escapedArtist}\"";

            var request = new HttpRequestBuilder($"{baseUrl}release-group/")
                .AddQueryParam("query", query)
                .AddQueryParam("limit", "5")
                .AddQueryParam("fmt", "json")
                .SetHeader("User-Agent", UserAgent)
                .WithRateLimit(1.1)
                .Accept(HttpAccept.Json)
                .Build();

            request.SuppressHttpError = true;
            var response = _httpClient.Execute(request);
            if (response.StatusCode != HttpStatusCode.OK || string.IsNullOrWhiteSpace(response.Content))
            {
                return null;
            }

            var searchResult = JsonSerializer.Deserialize<MusicBrainzReleaseGroupSearchResponse>(response.Content, JsonOptions);
            var releaseGroup = searchResult?.ReleaseGroups?
                .FirstOrDefault(rg => IsValidMbid(rg.Id));

            if (releaseGroup == null || !IsValidMbid(releaseGroup.Id))
            {
                return null;
            }

            var creditArtist = releaseGroup.ArtistCredit?.FirstOrDefault()?.Artist;
            var artistMbid = IsValidMbid(creditArtist?.Id)
                ? creditArtist!.Id!
                : (IsValidMbid(candidate.LastFmArtistMbid) ? candidate.LastFmArtistMbid! : null);

            if (!IsValidMbid(artistMbid))
            {
                return TryResolveFromSkyHookByMbid(releaseGroup.Id!, candidate);
            }

            return new ResolvedRecommendedAlbum(
                AlbumMusicBrainzId: releaseGroup.Id!,
                AlbumTitle: !string.IsNullOrWhiteSpace(releaseGroup.Title) ? releaseGroup.Title! : candidate.AlbumTitle,
                ArtistMusicBrainzId: artistMbid!,
                ArtistName: !string.IsNullOrWhiteSpace(creditArtist?.Name) ? creditArtist!.Name! : candidate.ArtistName);
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "MusicBrainz release-group search failed for '{0}' by '{1}'.", candidate.AlbumTitle, candidate.ArtistName);
            return null;
        }
    }

    private static ResolvedRecommendedAlbum? MapSkyHookAlbum(
        Album? mappedAlbum,
        LastFmRecommendedAlbumCandidate candidate)
    {
        if (mappedAlbum == null || !IsValidMbid(mappedAlbum.ForeignAlbumId))
        {
            return null;
        }

        var artistMetadata = mappedAlbum.ArtistMetadata?.Value;
        var artistMbid = IsValidMbid(artistMetadata?.ForeignArtistId)
            ? artistMetadata!.ForeignArtistId
            : (IsValidMbid(candidate.LastFmArtistMbid) ? candidate.LastFmArtistMbid : null);

        if (!IsValidMbid(artistMbid))
        {
            return null;
        }

        var artistName = !string.IsNullOrWhiteSpace(artistMetadata?.Name)
            ? artistMetadata!.Name
            : candidate.ArtistName;

        var albumTitle = !string.IsNullOrWhiteSpace(mappedAlbum.Title)
            ? mappedAlbum.Title
            : candidate.AlbumTitle;

        return new ResolvedRecommendedAlbum(
            AlbumMusicBrainzId: mappedAlbum.ForeignAlbumId,
            AlbumTitle: albumTitle,
            ArtistMusicBrainzId: artistMbid!,
            ArtistName: artistName);
    }

    internal static bool IsValidMbid(string? mbid)
    {
        return !string.IsNullOrWhiteSpace(mbid) && Guid.TryParse(mbid, out _);
    }

    private static string EscapeLucene(string input)
    {
        return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
