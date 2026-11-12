using NLog;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.LidarrFm;
using Xunit;

namespace LidarrFm.Test;

public class LastFmRecommendImportListTests
{
    [Fact]
    public void Fetch_ResolvesAndImportsAlbums_AndForcesShouldMonitorExistingFalse()
    {
        const string albumMbid = "fb3760f0-831b-3c06-8e09-269c0a8b326c";
        const string artistMbid = "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11";

        var fakeApiClient = new StubLastFmApiClient(
            candidates: new List<LidarrFmRecommendedAlbumCandidate>
            {
                new("Dummy", null, "Portishead", artistMbid, 2, 0)
            });

        var fakeResolver = new StubMusicBrainzAlbumResolver(
            new LidarrFmResolvedAlbum(albumMbid, "Dummy", artistMbid, "Portishead"));

        var fakeImportService = new StubAlbumImportService();
        var statusService = new FakeImportListStatusService();

        var importList = new LidarrFmImportList(
            fakeApiClient,
            fakeResolver,
            fakeImportService,
            statusService,
            null!,
            null!,
            LogManager.GetCurrentClassLogger());

        var definition = new ImportListDefinition
        {
            Id = 7,
            Name = "My Last.fm Recommendations",
            EnableAutomaticAdd = true,
            ShouldMonitorExisting = true,
            ShouldMonitor = ImportListMonitorType.EntireArtist,
            Settings = new LidarrFmImportListSettings
            {
                UserId = "nick_lastfm",
                NewArtistMonitorMode = (int)LidarrFmNewArtistMonitorMode.ThatAlbum
            }
        };

        importList.Definition = definition;

        var items = importList.Fetch();

        // Ensures ImportListSyncService won't overwrite existing artist/album monitoring
        Assert.False(definition.ShouldMonitorExisting);
        Assert.Equal(ImportListMonitorType.None, definition.ShouldMonitor);

        var item = Assert.Single(items);
        Assert.Equal(albumMbid, item.AlbumMusicBrainzId);
        Assert.Equal(artistMbid, item.ArtistMusicBrainzId);
        Assert.Equal("Dummy", item.Album);
        Assert.Equal("Portishead", item.Artist);
        Assert.Equal(7, item.ImportListId);
        Assert.Single(statusService.SuccessIds, 7);
    }

    private sealed class StubLastFmApiClient : ILidarrFmLastFmApiClient
    {
        private readonly IReadOnlyList<LidarrFmRecommendedAlbumCandidate> _candidates;

        public StubLastFmApiClient(IReadOnlyList<LidarrFmRecommendedAlbumCandidate> candidates)
        {
            _candidates = candidates;
        }

        public void ValidateConnection(LidarrFmImportListSettings settings) { }
        public IReadOnlyList<LidarrFmRecommendedAlbumCandidate> FetchRecommendedAlbums(LidarrFmImportListSettings settings) => _candidates;
    }

    private sealed class StubMusicBrainzAlbumResolver : ILidarrFmMusicBrainzAlbumResolver
    {
        private readonly LidarrFmResolvedAlbum? _resolved;

        public StubMusicBrainzAlbumResolver(LidarrFmResolvedAlbum? resolved)
        {
            _resolved = resolved;
        }

        public LidarrFmResolvedAlbum? ResolveAlbum(LidarrFmRecommendedAlbumCandidate candidate, LidarrFmImportListSettings settings) => _resolved;
    }

    private sealed class StubAlbumImportService : ILidarrFmAlbumImportService
    {
        public LidarrFmImportSummary ImportRecommendedAlbums(
            ImportListDefinition definition,
            LidarrFmImportListSettings settings,
            IReadOnlyList<LidarrFmResolvedAlbum> resolvedAlbums)
        {
            return new LidarrFmImportSummary(resolvedAlbums, 1, 1, 0, 0, 0);
        }
    }
}
