using NLog;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.LidarrFm;
using NzbDrone.Core.Music;
using NzbDrone.Core.Music.Commands;
using Xunit;

namespace LidarrFm.Test;

public class LastFmAlbumImportServiceTests
{
    [Fact]
    public void ExistingArtist_WithNewAlbum_AddsAlbumUnmonitored_AndLeavesArtistProfilesAndMonitoringUntouched()
    {
        const string existingArtistMbid = "10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8";
        const string newAlbumMbid = "12345678-1234-1234-1234-1234567890ab";

        var artistService = new FakeArtistService();
        var existingArtist = new Artist
        {
            Id = 42,
            ArtistMetadataId = 7,
            Monitored = false,
            MonitorNewItems = NewItemMonitorTypes.None,
            QualityProfileId = 3,
            MetadataProfileId = 4,
            RootFolderPath = "/existing/music",
            Tags = new HashSet<int> { 99 },
            Metadata = new ArtistMetadata
            {
                Id = 7,
                ForeignArtistId = existingArtistMbid,
                Name = "Massive Attack"
            }
        };
        artistService.ExistingArtistsByMbid[existingArtistMbid] = existingArtist;

        var albumService = new FakeAlbumService();
        var addArtistService = new FakeAddArtistService();
        var addAlbumService = new FakeAddAlbumService();
        var commandQueue = new FakeCommandQueueManager();

        var service = CreateService(
            artistService: artistService,
            albumService: albumService,
            addArtistService: addArtistService,
            addAlbumService: addAlbumService,
            commandQueue: commandQueue);

        // Configure different profiles/monitoring for new artists to prove they are NOT applied to existing artists
        var definition = new ImportListDefinition
        {
            Id = 1,
            Name = "Last.fm Recs",
            ProfileId = 10,
            MetadataProfileId = 20,
            RootFolderPath = "/new/music",
            ShouldSearch = true
        };
        var settings = new LidarrFmImportListSettings
        {
            UserId = "nick",
            NewArtistQualityProfileId = 11,
            NewArtistMetadataProfileId = 22,
            NewArtistRootFolderPath = "/custom/music",
            NewArtistMonitorMode = (int)LidarrFmNewArtistMonitorMode.AllAlbums
        };

        var resolved = new List<LidarrFmResolvedAlbum>
        {
            new(
                AlbumMusicBrainzId: newAlbumMbid,
                AlbumTitle: "Mezzanine",
                ArtistMusicBrainzId: existingArtistMbid,
                ArtistName: "Massive Attack")
        };

        var summary = service.ImportRecommendedAlbums(definition, settings, resolved);

        // Verify existing artist was NOT modified or re-added
        Assert.Equal(0, summary.NewArtistsAdded);
        Assert.Equal(0, summary.NewArtistAlbumsAdded);
        Assert.Equal(1, summary.ExistingArtistAlbumsAdded);
        Assert.Empty(addArtistService.AddedArtists);
        Assert.Empty(artistService.UpdatedArtists);
        Assert.Empty(albumService.MonitoredChanges);

        Assert.False(existingArtist.Monitored);
        Assert.Equal(NewItemMonitorTypes.None, existingArtist.MonitorNewItems);
        Assert.Equal(3, existingArtist.QualityProfileId);
        Assert.Equal(4, existingArtist.MetadataProfileId);
        Assert.Equal("/existing/music", existingArtist.RootFolderPath);
        Assert.Single(existingArtist.Tags, 99);

        // Verify the new album was added using its MusicBrainz ID with Monitored=false and existing artist's QualityProfileId
        var addedAlbum = Assert.Single(addAlbumService.AddedAlbums);
        Assert.Equal(newAlbumMbid, addedAlbum.ForeignAlbumId);
        Assert.False(addedAlbum.Monitored);
        Assert.False(addedAlbum.AddOptions.SearchForNewAlbum);
        Assert.Equal(3, addedAlbum.ProfileId);
        Assert.Same(existingArtist, addedAlbum.Artist.Value);

        // Verify only RefreshAlbumCommand was queued (not BulkRefreshArtistCommand for the existing artist)
        var refreshCmd = Assert.Single(commandQueue.QueuedCommands);
        var refreshAlbumCmd = Assert.IsType<RefreshAlbumCommand>(refreshCmd);
        Assert.Equal(addedAlbum.Id, refreshAlbumCmd.AlbumId);
    }

    [Fact]
    public void ExistingArtist_WithExistingAlbum_DoesNotModifyAnything()
    {
        const string existingArtistMbid = "10adbe5e-a2c0-4bf3-8249-2b4cbf6e6ca8";
        const string existingAlbumMbid = "12345678-1234-1234-1234-1234567890ab";

        var artistService = new FakeArtistService();
        var existingArtist = new Artist
        {
            Id = 42,
            Monitored = false,
            QualityProfileId = 3,
            MetadataProfileId = 4,
            Metadata = new ArtistMetadata
            {
                ForeignArtistId = existingArtistMbid,
                Name = "Massive Attack"
            }
        };
        artistService.ExistingArtistsByMbid[existingArtistMbid] = existingArtist;

        var albumService = new FakeAlbumService();
        var existingAlbum = new Album
        {
            Id = 99,
            ForeignAlbumId = existingAlbumMbid,
            Title = "Mezzanine",
            Monitored = false,
            Artist = existingArtist
        };
        albumService.ExistingAlbumsByMbid[existingAlbumMbid] = existingAlbum;

        var addArtistService = new FakeAddArtistService();
        var addAlbumService = new FakeAddAlbumService();
        var commandQueue = new FakeCommandQueueManager();

        var service = CreateService(
            artistService: artistService,
            albumService: albumService,
            addArtistService: addArtistService,
            addAlbumService: addAlbumService,
            commandQueue: commandQueue);

        var summary = service.ImportRecommendedAlbums(
            new ImportListDefinition { Id = 1, ShouldSearch = true },
            new LidarrFmImportListSettings { UserId = "nick" },
            new List<LidarrFmResolvedAlbum>
            {
                new(existingAlbumMbid, "Mezzanine", existingArtistMbid, "Massive Attack")
            });

        Assert.Equal(0, summary.NewArtistsAdded);
        Assert.Equal(0, summary.ExistingArtistAlbumsAdded);
        Assert.Equal(1, summary.ExistingAlbumsSkipped);
        Assert.Empty(addArtistService.AddedArtists);
        Assert.Empty(addAlbumService.AddedAlbums);
        Assert.Empty(artistService.UpdatedArtists);
        Assert.Empty(albumService.MonitoredChanges);
        Assert.False(existingAlbum.Monitored);
        Assert.False(existingArtist.Monitored);
        Assert.Empty(commandQueue.QueuedCommands);
    }

    [Theory]
    [InlineData(LidarrFmNewArtistMonitorMode.ThatAlbum, MonitorTypes.All, NewItemMonitorTypes.None, true, true)]
    [InlineData(LidarrFmNewArtistMonitorMode.ExistingAlbums, MonitorTypes.Existing, NewItemMonitorTypes.None, false, false)]
    [InlineData(LidarrFmNewArtistMonitorMode.AllAlbums, MonitorTypes.All, NewItemMonitorTypes.All, true, false)]
    [InlineData(LidarrFmNewArtistMonitorMode.FutureAlbums, MonitorTypes.Future, NewItemMonitorTypes.All, false, false)]
    public void NewArtist_AppliesConfiguredProfilesAndMonitoringOption(
        LidarrFmNewArtistMonitorMode mode,
        MonitorTypes expectedArtistMonitorType,
        NewItemMonitorTypes expectedMonitorNewItems,
        bool expectedAlbumMonitoredForPastRelease,
        bool expectAlbumInAlbumsToMonitorList)
    {
        const string newArtistMbid = "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11";
        const string newAlbumMbid = "fb3760f0-831b-3c06-8e09-269c0a8b326c";

        var addArtistService = new FakeAddArtistService();
        var addAlbumService = new FakeAddAlbumService();
        var commandQueue = new FakeCommandQueueManager();
        var albumInfo = new FakeProvideAlbumInfo(mbid => Tuple.Create(
            newArtistMbid,
            new Album
            {
                ForeignAlbumId = mbid,
                Title = "Dummy",
                ReleaseDate = new DateTime(1994, 8, 22, 0, 0, 0, DateTimeKind.Utc)
            },
            new List<ArtistMetadata>
            {
                new() { ForeignArtistId = newArtistMbid, Name = "Portishead" }
            }));

        var service = CreateService(
            addArtistService: addArtistService,
            addAlbumService: addAlbumService,
            albumInfo: albumInfo,
            commandQueue: commandQueue);

        var definition = new ImportListDefinition
        {
            Id = 1,
            ProfileId = 1,
            MetadataProfileId = 1,
            RootFolderPath = "/default/music",
            ShouldSearch = true
        };

        var settings = new LidarrFmImportListSettings
        {
            UserId = "nick",
            NewArtistQualityProfileId = 5,
            NewArtistMetadataProfileId = 6,
            NewArtistRootFolderPath = "/configured/music",
            NewArtistMonitorMode = (int)mode
        };

        var summary = service.ImportRecommendedAlbums(
            definition,
            settings,
            new List<LidarrFmResolvedAlbum>
            {
                new(newAlbumMbid, "Dummy", newArtistMbid, "Portishead")
            });

        Assert.Equal(1, summary.NewArtistsAdded);
        Assert.Equal(1, summary.NewArtistAlbumsAdded);

        var addedArtist = Assert.Single(addArtistService.AddedArtists);
        Assert.Equal(newArtistMbid, addedArtist.ForeignArtistId);
        Assert.Equal("Portishead", addedArtist.Name);
        Assert.Equal(5, addedArtist.QualityProfileId);
        Assert.Equal(6, addedArtist.MetadataProfileId);
        Assert.Equal("/configured/music", addedArtist.RootFolderPath);
        Assert.True(addedArtist.Monitored);
        Assert.Equal(expectedMonitorNewItems, addedArtist.MonitorNewItems);
        Assert.Equal(expectedArtistMonitorType, addedArtist.AddOptions.Monitor);

        if (expectAlbumInAlbumsToMonitorList)
        {
            Assert.Single(addedArtist.AddOptions.AlbumsToMonitor, newAlbumMbid);
        }
        else
        {
            Assert.Empty(addedArtist.AddOptions.AlbumsToMonitor);
        }

        var addedAlbum = Assert.Single(addAlbumService.AddedAlbums);
        Assert.Equal(newAlbumMbid, addedAlbum.ForeignAlbumId);
        Assert.Equal(expectedAlbumMonitoredForPastRelease, addedAlbum.Monitored);
        Assert.Equal(5, addedAlbum.ProfileId);

        var bulkRefresh = Assert.Single(commandQueue.QueuedCommands);
        var bulkRefreshCmd = Assert.IsType<BulkRefreshArtistCommand>(bulkRefresh);
        Assert.True(bulkRefreshCmd.AreNewArtists);
        Assert.Contains(addedArtist.Id, bulkRefreshCmd.ArtistIds);
    }

    [Fact]
    public void ExcludedAlbumOrArtist_IsSkipped()
    {
        const string artistMbid = "8f6bd1e4-fbe1-4f50-aa9b-94c450ec0f11";
        const string albumMbid = "fb3760f0-831b-3c06-8e09-269c0a8b326c";

        var exclusionService = new FakeImportListExclusionService();
        exclusionService.Exclusions.Add(new ImportListExclusion { ForeignId = albumMbid });

        var addArtistService = new FakeAddArtistService();
        var addAlbumService = new FakeAddAlbumService();

        var service = CreateService(
            addArtistService: addArtistService,
            addAlbumService: addAlbumService,
            exclusionService: exclusionService);

        var summary = service.ImportRecommendedAlbums(
            new ImportListDefinition { Id = 1 },
            new LidarrFmImportListSettings { UserId = "nick" },
            new List<LidarrFmResolvedAlbum>
            {
                new(albumMbid, "Dummy", artistMbid, "Portishead")
            });

        Assert.Equal(1, summary.ExcludedItemsSkipped);
        Assert.Empty(addArtistService.AddedArtists);
        Assert.Empty(addAlbumService.AddedAlbums);
    }

    private static LidarrFmAlbumImportService CreateService(
        FakeArtistService? artistService = null,
        FakeAlbumService? albumService = null,
        FakeAddArtistService? addArtistService = null,
        FakeAddAlbumService? addAlbumService = null,
        FakeProvideAlbumInfo? albumInfo = null,
        FakeImportListExclusionService? exclusionService = null,
        FakeCommandQueueManager? commandQueue = null)
    {
        return new LidarrFmAlbumImportService(
            artistService ?? new FakeArtistService(),
            albumService ?? new FakeAlbumService(),
            addArtistService ?? new FakeAddArtistService(),
            addAlbumService ?? new FakeAddAlbumService(),
            albumInfo ?? new FakeProvideAlbumInfo(),
            exclusionService ?? new FakeImportListExclusionService(),
            new FakeQualityProfileRepository(),
            new FakeMetadataProfileRepository(),
            new FakeRootFolderService(),
            commandQueue ?? new FakeCommandQueueManager(),
            LogManager.GetCurrentClassLogger());
    }
}
