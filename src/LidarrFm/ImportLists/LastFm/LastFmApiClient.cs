using System.Net;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Exceptions;

namespace NzbDrone.Core.ImportLists.LidarrFm;

public interface ILidarrFmLastFmApiClient
{
    void ValidateConnection(LidarrFmImportListSettings settings);
    IReadOnlyList<LidarrFmRecommendedAlbumCandidate> FetchRecommendedAlbums(LidarrFmImportListSettings settings);
}

public class LidarrFmLastFmApiClient : ILidarrFmLastFmApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan DefaultRateLimit = TimeSpan.FromMilliseconds(250);

    private readonly IHttpClient _httpClient;
    private readonly Logger _logger;

    public LidarrFmLastFmApiClient(IHttpClient httpClient, Logger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public void ValidateConnection(LidarrFmImportListSettings settings)
    {
        var request = BuildActivityRequest(settings, limitOverride: 1);
        var response = ExecuteAndParse(request);

        var hasAnyContainer = response.TopArtists != null
                              || response.TopAlbums != null
                              || response.TopTracks != null
                              || response.RecentTracks != null
                              || response.LovedTracks != null;

        if (!hasAnyContainer)
        {
            throw new InvalidOperationException("Last.fm returned an empty or unrecognized response payload.");
        }
    }

    public IReadOnlyList<LidarrFmRecommendedAlbumCandidate> FetchRecommendedAlbums(LidarrFmImportListSettings settings)
    {
        var activityRequest = BuildActivityRequest(settings);
        var activityResponse = ExecuteAndParse(activityRequest);

        var source = (LidarrFmActivitySource)settings.ActivitySource;
        var similarArtistLists = source switch
        {
            LidarrFmActivitySource.TopArtists => FetchSimilarFromArtists(
                settings,
                activityResponse.TopArtists?.Artist ?? new List<LidarrFmLastFmArtistDto>()),

            LidarrFmActivitySource.TopAlbums => FetchSimilarFromArtists(
                settings,
                (activityResponse.TopAlbums?.Album ?? new List<LidarrFmLastFmAlbumDto>())
                    .Where(a => a.Artist != null)
                    .Select(a => a.Artist!)
                    .ToList()),

            LidarrFmActivitySource.TopTracks => FetchSimilarFromTracks(
                settings,
                activityResponse.TopTracks?.Track ?? new List<LidarrFmLastFmTrackDto>()),

            LidarrFmActivitySource.RecentTracks => FetchSimilarFromTracks(
                settings,
                activityResponse.RecentTracks?.Track ?? new List<LidarrFmLastFmTrackDto>()),

            LidarrFmActivitySource.LovedTracks => FetchSimilarFromTracks(
                settings,
                activityResponse.LovedTracks?.Track ?? new List<LidarrFmLastFmTrackDto>()),

            _ => FetchSimilarFromArtists(
                settings,
                activityResponse.TopArtists?.Artist ?? new List<LidarrFmLastFmArtistDto>())
        };

        var rankedArtists = GroupAndRankSimilarArtists(similarArtistLists);
        _logger.Debug("Lidarr.fm aggregated {0} unique recommended artists from Last.fm activity ({1}) for user '{2}'.",
            rankedArtists.Count, source, settings.UserId);

        var candidates = new List<LidarrFmRecommendedAlbumCandidate>();
        var seenAlbumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (artist, occurrences, bestRank) in rankedArtists)
        {
            if (candidates.Count >= settings.MaxRecommendedAlbums)
            {
                break;
            }

            var topAlbums = FetchTopAlbumsForArtist(settings, artist);
            foreach (var album in topAlbums.Take(Math.Max(1, settings.TopAlbumsPerArtist)))
            {
                if (string.IsNullOrWhiteSpace(album.Name))
                {
                    continue;
                }

                var artistName = !string.IsNullOrWhiteSpace(album.Artist?.EffectiveName)
                    ? album.Artist!.EffectiveName
                    : artist.EffectiveName;

                if (string.IsNullOrWhiteSpace(artistName))
                {
                    continue;
                }

                var dedupKey = $"{artistName.Trim()}::{album.Name.Trim()}";
                if (!seenAlbumKeys.Add(dedupKey))
                {
                    continue;
                }

                var artistMbid = !string.IsNullOrWhiteSpace(album.Artist?.Mbid)
                    ? album.Artist!.Mbid
                    : artist.Mbid;

                candidates.Add(new LidarrFmRecommendedAlbumCandidate(
                    AlbumTitle: album.Name.Trim(),
                    LastFmAlbumMbid: NullIfWhiteSpace(album.Mbid),
                    ArtistName: artistName.Trim(),
                    LastFmArtistMbid: NullIfWhiteSpace(artistMbid),
                    SimilarityOccurrences: occurrences,
                    BestSimilarityRank: bestRank));

                if (candidates.Count >= settings.MaxRecommendedAlbums)
                {
                    break;
                }
            }
        }

        return candidates;
    }

    private List<List<LidarrFmLastFmArtistDto>> FetchSimilarFromArtists(
        LidarrFmImportListSettings settings,
        IReadOnlyList<LidarrFmLastFmArtistDto> seedArtists)
    {
        var results = new List<List<LidarrFmLastFmArtistDto>>();
        var seenSeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seedArtists)
        {
            var name = seed.EffectiveName.Trim();
            if (string.IsNullOrWhiteSpace(name) || !seenSeeds.Add(name))
            {
                continue;
            }

            if (seenSeeds.Count > settings.SeedLimit)
            {
                break;
            }

            var similar = FetchSimilarArtistsForArtist(settings, name);
            if (similar.Count > 0)
            {
                results.Add(similar);
            }
        }

        return results;
    }

    private List<List<LidarrFmLastFmArtistDto>> FetchSimilarFromTracks(
        LidarrFmImportListSettings settings,
        IReadOnlyList<LidarrFmLastFmTrackDto> seedTracks)
    {
        var results = new List<List<LidarrFmLastFmArtistDto>>();
        var seenTrackKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fallbackArtistSeeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in seedTracks)
        {
            var trackName = track.Name?.Trim() ?? string.Empty;
            var artistName = track.Artist?.EffectiveName.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(trackName) || string.IsNullOrWhiteSpace(artistName))
            {
                continue;
            }

            var trackKey = $"{artistName}::{trackName}";
            if (!seenTrackKeys.Add(trackKey))
            {
                continue;
            }

            if (seenTrackKeys.Count > settings.SeedLimit)
            {
                break;
            }

            var request = BuildMethodRequest(
                settings,
                "track.getSimilar",
                settings.SimilarArtistsPerSeed,
                new Dictionary<string, string>
                {
                    ["artist"] = artistName,
                    ["track"] = trackName,
                    ["autocorrect"] = "1"
                });

            try
            {
                var response = ExecuteAndParse(request);
                var similarArtists = (response.SimilarTracks?.Track ?? new List<LidarrFmLastFmTrackDto>())
                    .Where(t => t.Artist != null && !string.IsNullOrWhiteSpace(t.Artist.EffectiveName))
                    .Select(t => t.Artist!)
                    .ToList();

                if (similarArtists.Count > 0)
                {
                    results.Add(similarArtists);
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "track.getSimilar failed for '{0}' by '{1}', falling back to artist.getSimilar.", trackName, artistName);
            }

            if (fallbackArtistSeeds.Add(artistName))
            {
                var artistSimilar = FetchSimilarArtistsForArtist(settings, artistName);
                if (artistSimilar.Count > 0)
                {
                    results.Add(artistSimilar);
                }
            }
        }

        return results;
    }

    private List<LidarrFmLastFmArtistDto> FetchSimilarArtistsForArtist(LidarrFmImportListSettings settings, string artistName)
    {
        var request = BuildMethodRequest(
            settings,
            "artist.getSimilar",
            settings.SimilarArtistsPerSeed,
            new Dictionary<string, string>
            {
                ["artist"] = artistName,
                ["autocorrect"] = "1"
            });

        try
        {
            var response = ExecuteAndParse(request);
            return response.SimilarArtists?.Artist ?? new List<LidarrFmLastFmArtistDto>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to fetch similar artists from Last.fm for '{0}'.", artistName);
            return new List<LidarrFmLastFmArtistDto>();
        }
    }

    private List<LidarrFmLastFmAlbumDto> FetchTopAlbumsForArtist(LidarrFmImportListSettings settings, LidarrFmLastFmArtistDto artist)
    {
        var parameters = new Dictionary<string, string>
        {
            ["autocorrect"] = "1"
        };

        if (!string.IsNullOrWhiteSpace(artist.Mbid) && Guid.TryParse(artist.Mbid, out _))
        {
            parameters["mbid"] = artist.Mbid!;
        }
        else
        {
            parameters["artist"] = artist.EffectiveName;
        }

        var request = BuildMethodRequest(
            settings,
            "artist.getTopAlbums",
            Math.Max(1, settings.TopAlbumsPerArtist),
            parameters);

        try
        {
            var response = ExecuteAndParse(request);
            return response.TopAlbums?.Album ?? new List<LidarrFmLastFmAlbumDto>();
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to fetch top albums from Last.fm for artist '{0}'.", artist.EffectiveName);
            return new List<LidarrFmLastFmAlbumDto>();
        }
    }

    internal static List<(LidarrFmLastFmArtistDto Artist, int Occurrences, int BestRank)> GroupAndRankSimilarArtists(
        IReadOnlyList<List<LidarrFmLastFmArtistDto>> similarLists)
    {
        var map = new Dictionary<string, (LidarrFmLastFmArtistDto Artist, int Occurrences, int BestRank)>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in similarLists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var candidate = list[rank];
                var name = candidate.EffectiveName.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var key = !string.IsNullOrWhiteSpace(candidate.Mbid) && Guid.TryParse(candidate.Mbid, out _)
                    ? candidate.Mbid!
                    : name;

                if (map.TryGetValue(key, out var existing))
                {
                    var chosenArtist = string.IsNullOrWhiteSpace(existing.Artist.Mbid) && !string.IsNullOrWhiteSpace(candidate.Mbid)
                        ? candidate
                        : existing.Artist;

                    map[key] = (chosenArtist, existing.Occurrences + 1, Math.Min(existing.BestRank, rank));
                }
                else
                {
                    map[key] = (candidate, 1, rank);
                }
            }
        }

        return map.Values
            .OrderByDescending(x => x.Occurrences)
            .ThenBy(x => x.BestRank)
            .ThenBy(x => x.Artist.EffectiveName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static HttpRequest BuildActivityRequest(LidarrFmImportListSettings settings, int? limitOverride = null)
    {
        var method = (LidarrFmActivitySource)settings.ActivitySource switch
        {
            LidarrFmActivitySource.TopArtists => "user.getTopArtists",
            LidarrFmActivitySource.TopAlbums => "user.getTopAlbums",
            LidarrFmActivitySource.TopTracks => "user.getTopTracks",
            LidarrFmActivitySource.RecentTracks => "user.getRecentTracks",
            LidarrFmActivitySource.LovedTracks => "user.getLovedTracks",
            _ => "user.getTopArtists"
        };

        var period = (LidarrFmTimePeriod)settings.TimePeriod switch
        {
            LidarrFmTimePeriod.LastWeek => "7day",
            LidarrFmTimePeriod.LastMonth => "1month",
            LidarrFmTimePeriod.LastThreeMonths => "3month",
            LidarrFmTimePeriod.LastSixMonths => "6month",
            LidarrFmTimePeriod.LastTwelveMonths => "12month",
            _ => "overall"
        };

        var parameters = new Dictionary<string, string>
        {
            ["user"] = settings.UserId.Trim()
        };

        if ((LidarrFmActivitySource)settings.ActivitySource is LidarrFmActivitySource.TopArtists
            or LidarrFmActivitySource.TopAlbums
            or LidarrFmActivitySource.TopTracks)
        {
            parameters["period"] = period;
        }

        return BuildMethodRequest(settings, method, limitOverride ?? settings.SeedLimit, parameters);
    }

    private static HttpRequest BuildMethodRequest(
        LidarrFmImportListSettings settings,
        string method,
        int limit,
        IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new HttpRequestBuilder(settings.BaseUrl)
            .AddQueryParam("method", method)
            .AddQueryParam("api_key", settings.ApiKey.Trim())
            .AddQueryParam("limit", limit)
            .AddQueryParam("format", "json")
            .WithRateLimit(DefaultRateLimit.TotalSeconds)
            .Accept(HttpAccept.Json);

        foreach (var kvp in parameters)
        {
            builder.AddQueryParam(kvp.Key, kvp.Value);
        }

        return builder.Build();
    }

    private LidarrFmLastFmApiResponse ExecuteAndParse(HttpRequest request)
    {
        var httpResponse = _httpClient.Execute(request);
        if (httpResponse.StatusCode != HttpStatusCode.OK)
        {
            var importResponse = new ImportListResponse(new ImportListRequest(request), httpResponse);
            throw new ImportListException(importResponse, "Last.fm API call resulted in unexpected HTTP status code [{0}]", httpResponse.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<LidarrFmLastFmApiResponse>(httpResponse.Content, JsonOptions);
        if (parsed == null)
        {
            throw new InvalidOperationException("Unable to deserialize Last.fm API JSON response.");
        }

        if (parsed.Error.HasValue && parsed.Error.Value != 0)
        {
            throw new InvalidOperationException($"Last.fm API error {parsed.Error.Value}: {parsed.Message ?? "Unknown error"}");
        }

        return parsed;
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
