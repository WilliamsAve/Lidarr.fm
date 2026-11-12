using System.Net;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.LidarrFm;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using Xunit;

namespace LidarrFm.Test;

public class MusicBrainzAlbumResolverTests
{
    [Fact]
    public void ResolveAlbum_ConvertsLastFmReleaseMbid_ToMusicBrainzReleaseGroupMbid()
    {
        const string releaseMbid = "4c2d1111-2222-3333-4444-555555555555";
        const string expectedReleaseGroupMbid = "12345678-1234-1234-1234-1234567890ab";
        const string expectedArtistMbid = "10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8";

        var httpClient = new FakeHttpClient(request =>
        {
            var url = request.Url.ToString();
            if (url.Contains($"release/{releaseMbid}", StringComparison.OrdinalIgnoreCase))
            {
                return LastFmApiClientTests.CreateJsonResponse(request, $$"""
                {
                  "id": "{{releaseMbid}}",
                  "title": "Mezzanine",
                  "release-group": {
                    "id": "{{expectedReleaseGroupMbid}}",
                    "title": "Mezzanine"
                  },
                  "artist-credit": [
                    {
                      "name": "Massive Attack",
                      "artist": {
                        "id": "{{expectedArtistMbid}}",
                        "name": "Massive Attack"
                      }
                    }
                  ]
                }
                """);
            }

            return LastFmApiClientTests.CreateJsonResponse(request, "{}", HttpStatusCode.NotFound);
        });

        var albumSearch = new FakeSearchForNewAlbum((_, _) => new List<Album>());
        var resolver = new LidarrFmMusicBrainzAlbumResolver(httpClient, albumSearch, LogManager.GetCurrentClassLogger());

        var candidate = new LidarrFmRecommendedAlbumCandidate(
            AlbumTitle: "Mezzanine",
            LastFmAlbumMbid: releaseMbid,
            ArtistName: "Massive Attack",
            LastFmArtistMbid: null,
            SimilarityOccurrences: 2,
            BestSimilarityRank: 0);

        var resolved = resolver.ResolveAlbum(candidate, new LidarrFmImportListSettings { UserId = "nick" });

        Assert.NotNull(resolved);
        Assert.Equal(expectedReleaseGroupMbid, resolved!.AlbumMusicBrainzId);
        Assert.Equal(expectedArtistMbid, resolved.ArtistMusicBrainzId);
        Assert.Equal("Mezzanine", resolved.AlbumTitle);
        Assert.Equal("Massive Attack", resolved.ArtistName);
    }

    [Fact]
    public void ResolveAlbum_WhenNoLastFmMbid_ResolvesViaSkyHookSearch()
    {
        const string expectedReleaseGroupMbid = "fb3760f0-831b-3c06-8e09-269c0a8b326c";
        const string expectedArtistMbid = "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11";

        var httpClient = new FakeHttpClient(req => LastFmApiClientTests.CreateJsonResponse(req, "{}", HttpStatusCode.NotFound));
        var albumSearch = new FakeSearchForNewAlbum((title, artist) =>
        {
            if (title == "Dummy" && artist == "Portishead")
            {
                return new List<Album>
                {
                    new()
                    {
                        ForeignAlbumId = expectedReleaseGroupMbid,
                        Title = "Dummy",
                        ArtistMetadata = new ArtistMetadata
                        {
                            ForeignArtistId = expectedArtistMbid,
                            Name = "Portishead"
                        }
                    }
                };
            }

            return new List<Album>();
        });

        var resolver = new LidarrFmMusicBrainzAlbumResolver(httpClient, albumSearch, LogManager.GetCurrentClassLogger());
        var candidate = new LidarrFmRecommendedAlbumCandidate(
            AlbumTitle: "Dummy",
            LastFmAlbumMbid: null,
            ArtistName: "Portishead",
            LastFmArtistMbid: expectedArtistMbid,
            SimilarityOccurrences: 1,
            BestSimilarityRank: 0);

        var resolved = resolver.ResolveAlbum(candidate, new LidarrFmImportListSettings { UserId = "nick" });

        Assert.NotNull(resolved);
        Assert.Equal(expectedReleaseGroupMbid, resolved!.AlbumMusicBrainzId);
        Assert.Equal(expectedArtistMbid, resolved.ArtistMusicBrainzId);
    }
}
