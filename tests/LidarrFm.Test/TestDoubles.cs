using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Profiles.Metadata;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;

namespace LidarrFm.Test;

internal sealed class FakeHttpClient : IHttpClient
{
    private readonly Func<HttpRequest, HttpResponse> _handler;

    public FakeHttpClient(Func<HttpRequest, HttpResponse> handler)
    {
        _handler = handler;
    }

    public HttpResponse Execute(HttpRequest request) => _handler(request);
    public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult(_handler(request));
    public void DownloadFile(string url, string fileName) => throw new NotSupportedException();
    public Task DownloadFileAsync(string url, string fileName) => throw new NotSupportedException();
    public HttpResponse Get(HttpRequest request) => _handler(request);
    public Task<HttpResponse> GetAsync(HttpRequest request) => Task.FromResult(_handler(request));
    public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotSupportedException();
    public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotSupportedException();
    public HttpResponse Head(HttpRequest request) => _handler(request);
    public Task<HttpResponse> HeadAsync(HttpRequest request) => Task.FromResult(_handler(request));
    public HttpResponse Post(HttpRequest request) => _handler(request);
    public Task<HttpResponse> PostAsync(HttpRequest request) => Task.FromResult(_handler(request));
    public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotSupportedException();
    public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotSupportedException();
}

internal sealed class FakeSearchForNewAlbum : ISearchForNewAlbum
{
    private readonly Func<string, string?, List<Album>> _handler;

    public FakeSearchForNewAlbum(Func<string, string?, List<Album>> handler)
    {
        _handler = handler;
    }

    public List<Album> SearchForNewAlbum(string title, string artist) => _handler(title, artist);
    public List<Album> SearchForNewAlbumByRecordingIds(List<string> recordingIds) => new();
}

internal sealed class FakeArtistService : IArtistService
{
    public Dictionary<string, Artist> ExistingArtistsByMbid { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Artist> UpdatedArtists { get; } = new();

    public Artist? FindById(string foreignArtistId) =>
        ExistingArtistsByMbid.TryGetValue(foreignArtistId, out var artist) ? artist : null;

    public Artist UpdateArtist(Artist artist, bool publishUpdatedEvent = true)
    {
        UpdatedArtists.Add(artist);
        return artist;
    }

    public List<Artist> UpdateArtists(List<Artist> artist, bool useExistingRelativeFolder)
    {
        UpdatedArtists.AddRange(artist);
        return artist;
    }

    public Artist GetArtist(int artistId) => throw new NotSupportedException();
    public Artist GetArtistByMetadataId(int artistMetadataId) => throw new NotSupportedException();
    public List<Artist> GetArtists(IEnumerable<int> artistIds) => throw new NotSupportedException();
    public Artist AddArtist(Artist newArtist, bool doRefresh) => throw new NotSupportedException();
    public List<Artist> AddArtists(List<Artist> newArtists, bool doRefresh) => throw new NotSupportedException();
    public Artist FindByName(string title) => throw new NotSupportedException();
    public Artist FindByNameInexact(string title) => throw new NotSupportedException();
    public List<Artist> GetCandidates(string title) => throw new NotSupportedException();
    public void DeleteArtist(int artistId, bool deleteFiles, bool addImportListExclusion = false) => throw new NotSupportedException();
    public void DeleteArtists(List<int> artistIds, bool deleteFiles, bool addImportListExclusion = false) => throw new NotSupportedException();
    public List<Artist> GetAllArtists() => ExistingArtistsByMbid.Values.ToList();
    public Dictionary<int, List<int>> GetAllArtistsTags() => new();
    public List<Artist> AllForTag(int tagId) => new();
    public Dictionary<int, string> AllArtistPaths() => new();
    public bool ArtistPathExists(string folder) => false;
    public void RemoveAddOptions(Artist artist) { }
}

internal sealed class FakeAlbumService : IAlbumService
{
    public Dictionary<string, Album> ExistingAlbumsByMbid { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(int AlbumId, bool Monitored)> MonitoredChanges { get; } = new();

    public Album? FindById(string foreignId) =>
        ExistingAlbumsByMbid.TryGetValue(foreignId, out var album) ? album : null;

    public void SetAlbumMonitored(int albumId, bool monitored) =>
        MonitoredChanges.Add((albumId, monitored));

    public void SetMonitored(IEnumerable<int> ids, bool monitored)
    {
        foreach (var id in ids)
        {
            MonitoredChanges.Add((id, monitored));
        }
    }

    public Album GetAlbum(int albumId) => throw new NotSupportedException();
    public List<Album> GetAlbums(IEnumerable<int> albumIds) => throw new NotSupportedException();
    public List<Album> GetAlbumsByArtist(int artistId) => new();
    public List<Album> GetNextAlbumsByArtistMetadataId(IEnumerable<int> artistMetadataIds) => new();
    public List<Album> GetLastAlbumsByArtistMetadataId(IEnumerable<int> artistMetadataIds) => new();
    public List<Album> GetAlbumsByArtistMetadataId(int artistMetadataId) => new();
    public List<Album> GetAlbumsForRefresh(int artistMetadataId, List<string> foreignIds) => new();
    public Album AddAlbum(Album newAlbum, bool doRefresh) => newAlbum;
    public Album FindByTitle(int artistMetadataId, string title) => throw new NotSupportedException();
    public Album FindByTitleInexact(int artistMetadataId, string title) => throw new NotSupportedException();
    public List<Album> GetCandidates(int artistMetadataId, string title) => new();
    public void DeleteAlbum(int albumId, bool deleteFiles, bool addImportListExclusion = false) => throw new NotSupportedException();
    public List<Album> GetAllAlbums() => ExistingAlbumsByMbid.Values.ToList();
    public Album UpdateAlbum(Album album) => album;
    public void UpdateLastSearchTime(List<Album> albums) { }
    public PagingSpec<Album> AlbumsWithoutFiles(PagingSpec<Album> pagingSpec) => pagingSpec;
    public List<Album> AlbumsBetweenDates(DateTime start, DateTime end, bool includeUnmonitored) => new();
    public List<Album> ArtistAlbumsBetweenDates(Artist artist, DateTime start, DateTime end, bool includeUnmonitored) => new();
    public void InsertMany(List<Album> albums) { }
    public void UpdateMany(List<Album> albums) { }
    public void DeleteMany(List<Album> albums) { }
    public void SetAddOptions(IEnumerable<Album> albums) { }
    public Album FindAlbumByRelease(string albumReleaseId) => throw new NotSupportedException();
    public Album FindAlbumByTrackId(int trackId) => throw new NotSupportedException();
    public List<Album> GetArtistAlbumsWithFiles(Artist artist) => new();
}

internal sealed class FakeAddArtistService : IAddArtistService
{
    private int _nextId = 100;
    public List<Artist> AddedArtists { get; } = new();

    public Artist AddArtist(Artist newArtist, bool doRefresh = true)
    {
        newArtist.Id = _nextId++;
        AddedArtists.Add(newArtist);
        return newArtist;
    }

    public List<Artist> AddArtists(List<Artist> newArtists, bool doRefresh = true, bool ignoreErrors = false)
    {
        foreach (var artist in newArtists)
        {
            artist.Id = _nextId++;
            AddedArtists.Add(artist);
        }

        return newArtists;
    }
}

internal sealed class FakeAddAlbumService : IAddAlbumService
{
    private int _nextId = 500;
    public List<Album> AddedAlbums { get; } = new();

    public Album AddAlbum(Album album, bool doRefresh = true)
    {
        album.Id = _nextId++;
        AddedAlbums.Add(album);
        return album;
    }

    public List<Album> AddAlbums(List<Album> albums, bool doRefresh = true, bool ignoreErrors = false)
    {
        foreach (var album in albums)
        {
            album.Id = _nextId++;
            AddedAlbums.Add(album);
        }

        return albums;
    }
}

internal sealed class FakeProvideAlbumInfo : IProvideAlbumInfo
{
    private readonly Func<string, Tuple<string, Album, List<ArtistMetadata>>?> _handler;

    public FakeProvideAlbumInfo(Func<string, Tuple<string, Album, List<ArtistMetadata>>?>? handler = null)
    {
        _handler = handler ?? (_ => null);
    }

    public Tuple<string, Album, List<ArtistMetadata>> GetAlbumInfo(string id)
    {
        return _handler(id)!;
    }

    public HashSet<string> GetChangedAlbums(DateTime startTime) => new();
}

internal sealed class FakeImportListExclusionService : IImportListExclusionService
{
    public List<ImportListExclusion> Exclusions { get; } = new();

    public List<ImportListExclusion> All() => Exclusions;
    public ImportListExclusion Add(ImportListExclusion importListExclusion) => importListExclusion;
    public List<ImportListExclusion> Add(List<ImportListExclusion> importListExclusions) => importListExclusions;
    public void Delete(int id) { }
    public void Delete(List<int> ids) { }
    public void Delete(string foreignId) { }
    public ImportListExclusion FindByForeignId(string foreignId) => Exclusions.FirstOrDefault(x => x.ForeignId == foreignId)!;
    public List<ImportListExclusion> FindByForeignId(List<string> foreignIds) => Exclusions.Where(x => foreignIds.Contains(x.ForeignId)).ToList();
    public ImportListExclusion Get(int id) => Exclusions.First(x => x.Id == id);
    public PagingSpec<ImportListExclusion> GetPaged(PagingSpec<ImportListExclusion> pagingSpec) => pagingSpec;
    public ImportListExclusion Update(ImportListExclusion importListExclusion) => importListExclusion;
}

internal sealed class FakeQualityProfileRepository : IQualityProfileRepository
{
    public List<QualityProfile> Profiles { get; } = new() { new QualityProfile { Id = 1, Name = "Any" } };

    public IEnumerable<QualityProfile> All() => Profiles;
    public int Count() => Profiles.Count;
    public void Delete(int id) { }
    public void Delete(QualityProfile model) { }
    public void DeleteMany(IEnumerable<int> ids) { }
    public void DeleteMany(List<QualityProfile> models) { }
    public bool Exists(int id) => Profiles.Any(p => p.Id == id);
    public QualityProfile Find(int id) => Profiles.FirstOrDefault(p => p.Id == id)!;
    public QualityProfile Get(int id) => Profiles.First(p => p.Id == id);
    public IEnumerable<QualityProfile> Get(IEnumerable<int> ids) => Profiles.Where(p => ids.Contains(p.Id));
    public PagingSpec<QualityProfile> GetPaged(PagingSpec<QualityProfile> pagingSpec) => pagingSpec;
    public QualityProfile Insert(QualityProfile model) => model;
    public void InsertMany(IList<QualityProfile> models) { }
    public void Purge(bool vacuum = false) { }
    public void SetFields(QualityProfile model, params System.Linq.Expressions.Expression<Func<QualityProfile, object>>[] properties) { }
    public void SetFields(IList<QualityProfile> models, params System.Linq.Expressions.Expression<Func<QualityProfile, object>>[] properties) { }
    public QualityProfile Single() => Profiles.Single();
    public QualityProfile SingleOrDefault() => Profiles.SingleOrDefault()!;
    public QualityProfile Update(QualityProfile model) => model;
    public void UpdateMany(IList<QualityProfile> models) { }
    public QualityProfile Upsert(QualityProfile model) => model;
    public void UpsertMany(IList<QualityProfile> models) { }
    public bool HasItems() => Profiles.Count > 0;
}

internal sealed class FakeMetadataProfileRepository : IMetadataProfileRepository
{
    public List<MetadataProfile> Profiles { get; } = new() { new MetadataProfile { Id = 1, Name = "Standard" } };

    public IEnumerable<MetadataProfile> All() => Profiles;
    public int Count() => Profiles.Count;
    public void Delete(int id) { }
    public void Delete(MetadataProfile model) { }
    public void DeleteMany(IEnumerable<int> ids) { }
    public void DeleteMany(List<MetadataProfile> models) { }
    public bool Exists(int id) => Profiles.Any(p => p.Id == id);
    public MetadataProfile Find(int id) => Profiles.FirstOrDefault(p => p.Id == id)!;
    public MetadataProfile Get(int id) => Profiles.First(p => p.Id == id);
    public IEnumerable<MetadataProfile> Get(IEnumerable<int> ids) => Profiles.Where(p => ids.Contains(p.Id));
    public PagingSpec<MetadataProfile> GetPaged(PagingSpec<MetadataProfile> pagingSpec) => pagingSpec;
    public MetadataProfile Insert(MetadataProfile model) => model;
    public void InsertMany(IList<MetadataProfile> models) { }
    public void Purge(bool vacuum = false) { }
    public void SetFields(MetadataProfile model, params System.Linq.Expressions.Expression<Func<MetadataProfile, object>>[] properties) { }
    public void SetFields(IList<MetadataProfile> models, params System.Linq.Expressions.Expression<Func<MetadataProfile, object>>[] properties) { }
    public MetadataProfile Single() => Profiles.Single();
    public MetadataProfile SingleOrDefault() => Profiles.SingleOrDefault()!;
    public MetadataProfile Update(MetadataProfile model) => model;
    public void UpdateMany(IList<MetadataProfile> models) { }
    public MetadataProfile Upsert(MetadataProfile model) => model;
    public void UpsertMany(IList<MetadataProfile> models) { }
    public bool HasItems() => Profiles.Count > 0;
}

internal sealed class FakeRootFolderService : IRootFolderService
{
    public List<RootFolder> Folders { get; } = new() { new RootFolder { Id = 1, Path = "/music" } };

    public List<RootFolder> All() => Folders;
    public List<RootFolder> AllWithSpaceStats() => Folders;
    public List<RootFolder> AllWithUnmappedFolders() => Folders;
    public List<RootFolder> AllForTag(int tagId) => Folders;
    public RootFolder Add(RootFolder rootDir) => rootDir;
    public RootFolder Update(RootFolder rootDir) => rootDir;
    public void Remove(int id) { }
    public RootFolder Get(int id, bool timeout) => Folders.First(f => f.Id == id);
    public RootFolder GetBestRootFolder(string path) => Folders.First();
    public string GetBestRootFolderPath(string path) => Folders.First().Path;
}

internal sealed class FakeCommandQueueManager : IManageCommandQueue
{
    public List<Command> QueuedCommands { get; } = new();

    public CommandModel Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
        where TCommand : Command
    {
        QueuedCommands.Add(command);
        return new CommandModel { Body = command };
    }

    public CommandModel Push(string commandName, DateTime? lastExecutionTime = null, DateTime? lastStartTime = null, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified) => null!;
    public List<CommandModel> PushMany<TCommand>(List<TCommand> commands) where TCommand : Command
    {
        QueuedCommands.AddRange(commands);
        return new List<CommandModel>();
    }
    public IEnumerable<CommandModel> Queue(CancellationToken cancellationToken) => Enumerable.Empty<CommandModel>();
    public CommandModel Get(int id) => null!;
    public List<CommandModel> GetStarted() => new();
    public void SetMessage(CommandModel command, string message) { }
    public void SetResult(CommandModel command, CommandResult result) { }
    public void Start(CommandModel command) { }
    public void Complete(CommandModel command, string message) { }
    public void Fail(CommandModel command, string message, Exception e) { }
    public void Requeue() { }
    public void CleanCommands() { }
    public bool AnyInProgress(string? commandName = null) => false;
    public List<CommandModel> All() => new();
    public void Cancel(int id) { }
}

internal sealed class FakeImportListStatusService : IImportListStatusService
{
    public List<int> SuccessIds { get; } = new();
    public List<int> FailureIds { get; } = new();

    public void RecordSuccess(int providerId) => SuccessIds.Add(providerId);
    public void RecordFailure(int providerId, TimeSpan minimumBackOff = default) => FailureIds.Add(providerId);
    public void RecordConnectionFailure(int providerId) => FailureIds.Add(providerId);
    public List<ImportListStatus> GetBlockedProviders() => new();
    public DateTime? GetLastSyncListInfo(int importListId) => null;
    public void UpdateListSyncStatus(int importListId) { }
    public void CleanUp() { }
}
