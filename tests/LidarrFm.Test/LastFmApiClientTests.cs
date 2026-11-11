using System.Net;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.LidarrFm;
using Xunit;

namespace LidarrFm.Test;

public class LastFmApiClientTests
{
    [Fact]
    public void FetchRecommendedAlbums_TopArtists_RanksSimilarArtistsAndReturnsTopAlbums()
    {
        var httpClient = new FakeHttpClient(request =>
        {
            var url = request.Url.ToString();
            if (url.Contains("method=user.getTopArtists", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "topartists": {
                    "artist": [
                      { "name": "Radiohead", "mbid": "a74b1b7f-71a5-4011-9441-d0b5e4122711" },
                      { "name": "Thom Yorke", "mbid": "8ed2e0b3-aa4c-4e13-bec3-dc739374d6b5" }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("method=artist.getSimilar", StringComparison.OrdinalIgnoreCase) &&
                url.Contains("Radiohead", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "similarartists": {
                    "artist": [
                      { "name": "Portishead", "mbid": "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11" },
                      { "name": "Massive Attack", "mbid": "10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8" }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("method=artist.getSimilar", StringComparison.OrdinalIgnoreCase) &&
                url.Contains("Thom", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "similarartists": {
                    "artist": [
                      { "name": "Massive Attack", "mbid": "10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8" }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("method=artist.getTopAlbums", StringComparison.OrdinalIgnoreCase) &&
                url.Contains("10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "topalbums": {
                    "album": [
                      {
                        "name": "Mezzanine",
                        "mbid": "4c2d1111-2222-3333-4444-555555555555",
                        "artist": {
                          "name": "Massive Attack",
                          "mbid": "10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8"
                        }
                      }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("method=artist.getTopAlbums", StringComparison.OrdinalIgnoreCase) &&
                url.Contains("8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "topalbums": {
                    "album": [
                      {
                        "name": "Dummy",
                        "mbid": "",
                        "artist": {
                          "name": "Portishead",
                          "mbid": "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11"
                        }
                      }
                    ]
                  }
                }
                """);
            }

            return CreateJsonResponse(request, "{}");
        });

        var client = new LastFmApiClient(httpClient, LogManager.GetCurrentClassLogger());
        var settings = new LastFmRecommendSettings
        {
            UserId = "nick_lastfm",
            ActivitySource = (int)LastFmActivitySource.TopArtists,
            SeedLimit = 5,
            SimilarArtistsPerSeed = 5,
            TopAlbumsPerArtist = 1,
            MaxRecommendedAlbums = 10
        };

        var results = client.FetchRecommendedAlbums(settings);

        Assert.Equal(2, results.Count);
        // Massive Attack was recommended by both seeds (Occurrences = 2), so Mezzanine comes first.
        Assert.Equal("Mezzanine", results[0].AlbumTitle);
        Assert.Equal("Massive Attack", results[0].ArtistName);
        Assert.Equal("4c2d1111-2222-3333-4444-555555555555", results[0].LastFmAlbumMbid);
        Assert.Equal(2, results[0].SimilarityOccurrences);

        Assert.Equal("Dummy", results[1].AlbumTitle);
        Assert.Equal("Portishead", results[1].ArtistName);
        Assert.Null(results[1].LastFmAlbumMbid);
    }

    [Fact]
    public void FetchRecommendedAlbums_RecentTracks_UsesTrackGetSimilarWithFallback()
    {
        var httpClient = new FakeHttpClient(request =>
        {
            var url = request.Url.ToString();
            if (url.Contains("method=user.getRecentTracks", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "recenttracks": {
                    "track": [
                      {
                        "name": "Teardrop",
                        "artist": { "#text": "Massive Attack" }
                      }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("method=track.getSimilar", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "similartracks": {
                    "track": [
                      {
                        "name": "Glory Box",
                        "artist": {
                          "name": "Portishead",
                          "mbid": "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11"
                        }
                      }
                    ]
                  }
                }
                """);
            }

            if (url.Contains("method=artist.getTopAlbums", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(request, """
                {
                  "topalbums": {
                    "album": [
                      {
                        "name": "Dummy",
                        "mbid": "99999999-8888-7777-6666-555555555555",
                        "artist": {
                          "name": "Portishead",
                          "mbid": "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11"
                        }
                      }
                    ]
                  }
                }
                """);
            }

            return CreateJsonResponse(request, "{}");
        });

        var client = new LastFmApiClient(httpClient, LogManager.GetCurrentClassLogger());
        var settings = new LastFmRecommendSettings
        {
            UserId = "nick_lastfm",
            ActivitySource = (int)LastFmActivitySource.RecentTracks,
            SeedLimit = 5,
            SimilarArtistsPerSeed = 5,
            TopAlbumsPerArtist = 1,
            MaxRecommendedAlbums = 5
        };

        var results = client.FetchRecommendedAlbums(settings);

        Assert.Single(results);
        Assert.Equal("Dummy", results[0].AlbumTitle);
        Assert.Equal("Portishead", results[0].ArtistName);
    }

    internal static HttpResponse CreateJsonResponse(HttpRequest request, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var headers = new HttpHeader
        {
            ["Content-Type"] = "application/json; charset=utf-8"
        };
        return new HttpResponse(request, headers, json, status);
    }
}
