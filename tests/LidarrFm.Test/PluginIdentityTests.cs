using DryIoc;
using Lidarr.Api.V1.ImportLists;
using Lidarr.Http.ClientSchema;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Reflection;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.LidarrFm;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Plugins;
using Xunit;

namespace LidarrFm.Test;

public class PluginIdentityTests
{
    [Fact]
    public void Plugin_ExposesExpectedMetadata()
    {
        var plugin = new LidarrFmPlugin();

        Assert.Equal("Lidarr.fm", plugin.Name);
        Assert.Equal("WilliamsAve", plugin.Owner);
        Assert.Equal("https://github.com/WilliamsAve/Lidarr.fm", plugin.GithubUrl);
        Assert.NotNull(plugin.InstalledVersion);
    }

    [Fact]
    public void ExportedTypes_AllPrefixedWithLidarrFm_AvoidingCrossPluginCollisions()
    {
        var exportedTypes = typeof(LidarrFmPlugin).Assembly.GetExportedTypes();

        Assert.Contains(typeof(LidarrFmPlugin), exportedTypes);
        Assert.Contains(typeof(LidarrFmImportList), exportedTypes);
        Assert.Contains(typeof(LidarrFmImportListSettings), exportedTypes);
        Assert.True(typeof(IPlugin).IsAssignableFrom(typeof(LidarrFmPlugin)));
        Assert.True(typeof(IImportList).IsAssignableFrom(typeof(LidarrFmImportList)));

        // Every exported type must be uniquely prefixed with LidarrFm (or ILidarrFm for interfaces)
        // so ReflectionExtensions.FindTypeByName never collides with other plugins (e.g. Tubifarry's LastFmRecommendSettings).
        foreach (var exportedType in exportedTypes)
        {
            var normalizedName = exportedType.IsInterface && exportedType.Name.StartsWith('I')
                ? exportedType.Name[1..]
                : exportedType.Name;
            Assert.StartsWith("LidarrFm", normalizedName);
        }

        var foundSettingsType = ReflectionExtensions.FindTypeByName(nameof(LidarrFmImportListSettings));
        Assert.Equal(typeof(LidarrFmImportListSettings), foundSettingsType);
    }

    [Fact]
    public void ImportListResourceMapper_SchemaToModelRoundtrip_ResolvesConfigContractAndPreservesDefaultApiKey()
    {
        using var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance<ILocalizationService>(new FakeLocalizationService());
        SchemaBuilder.Initialize(container);

        var mapper = new ImportListResourceMapper();
        var defaultDef = new ImportListDefinition
        {
            Name = "Last.fm Recommended Albums (Lidarr.fm)",
            Implementation = nameof(LidarrFmImportList),
            ImplementationName = "Last.fm Recommended Albums (Lidarr.fm)",
            ConfigContract = nameof(LidarrFmImportListSettings),
            EnableAutomaticAdd = true,
            Settings = new LidarrFmImportListSettings()
        };

        // 1. Simulate GET /api/v1/importlist/schema
        var schemaResource = mapper.ToResource(defaultDef);
        var apiKeyField = Assert.Single(schemaResource.Fields, f => f.Name == "apiKey");
        Assert.Equal("********", apiKeyField.Value);

        // 2. Simulate user entering their Last.fm Username in the UI and posting to POST /api/v1/importlist/test
        var userIdField = Assert.Single(schemaResource.Fields, f => f.Name == "userId");
        userIdField.Value = "nick_lastfm";

        var jsonPayload = STJson.ToJson(schemaResource);
        var postedResource = STJson.Deserialize<ImportListResource>(jsonPayload);

        // 3. Simulate ProviderControllerBase.Test / CreateProvider (existingDefinition is null for new list)
        var mappedDefinition = mapper.ToModel(postedResource, existingDefinition: null);

        var mappedSettings = Assert.IsType<LidarrFmImportListSettings>(mappedDefinition.Settings);
        Assert.Equal("nick_lastfm", mappedSettings.UserId);
        Assert.Equal(LidarrFmImportListSettings.DefaultLastFmApiKey, mappedSettings.ApiKey);
        Assert.Equal(LidarrFmImportListSettings.DefaultLastFmBaseUrl, mappedSettings.BaseUrl);
        Assert.Equal(LidarrFmImportListSettings.DefaultMusicBrainzBaseUrl, mappedSettings.MusicBrainzBaseUrl);
        Assert.True(mappedSettings.Validate().IsValid);
    }

    [Fact]
    public void DryIocContainer_AutoAddServices_RegistersAndResolvesPluginAndImportList()
    {
        using var container = new Container(rules => rules.WithNzbDroneRules());
        container.AutoAddServices(new List<System.Reflection.Assembly> { typeof(LidarrFmPlugin).Assembly });

        var plugins = container.Resolve<IEnumerable<IPlugin>>().ToList();
        Assert.Single(plugins);
        Assert.Equal("Lidarr.fm", plugins[0].Name);
        Assert.Equal("WilliamsAve", plugins[0].Owner);
    }

    [Fact]
    public void DryIocContainer_WithFullLidarrAssemblies_ResolvesImportListFactoryAndTagServiceWithoutCycle()
    {
        using var container = new Container(rules => rules.WithNzbDroneRules());

        var assemblies = new List<System.Reflection.Assembly>
        {
            typeof(NzbDrone.Common.EnvironmentInfo.IPlatformInfo).Assembly, // Lidarr.Common
            typeof(NzbDrone.Core.ImportLists.IImportListFactory).Assembly,  // Lidarr.Core
            typeof(LidarrFmPlugin).Assembly                                 // Lidarr.Plugin.LidarrFm
        };

        container.AutoAddServices(assemblies);
        container.Register(Made.Of(() => NLog.LogManager.GetCurrentClassLogger()), Reuse.Transient);
        container.AddStartupContext(new NzbDrone.Common.EnvironmentInfo.StartupContext());
        container.RegisterInstance<NzbDrone.Core.Datastore.IMainDatabase>(new TestMainDatabase());
        container.RegisterInstance<NzbDrone.Core.Datastore.ILogDatabase>(new TestLogDatabase());
        container.RegisterDelegate<NzbDrone.Common.Disk.IDiskProvider>(_ => null!);
        container.RegisterDelegate<NzbDrone.Core.Configuration.IConfigFileProvider>(_ => null!);

        // Validate that DryIoc can compile the full dependency graph for IImportListFactory,
        // IQualityProfileService, IMetadataProfileService, ITagService, LidarrFmImportList,
        // ILidarrFmMusicBrainzAlbumResolver, ILidarrFmAlbumImportService, and ILidarrFmLastFmApiClient with zero errors.
        var errors = container.Validate(
            ServiceInfo.Of<IImportListFactory>(),
            ServiceInfo.Of<NzbDrone.Core.Profiles.Qualities.IQualityProfileService>(),
            ServiceInfo.Of<NzbDrone.Core.Profiles.Metadata.IMetadataProfileService>(),
            ServiceInfo.Of<NzbDrone.Core.Tags.ITagService>(),
            ServiceInfo.Of<LidarrFmImportList>(),
            ServiceInfo.Of<ILidarrFmMusicBrainzAlbumResolver>(),
            ServiceInfo.Of<ILidarrFmAlbumImportService>(),
            ServiceInfo.Of<ILidarrFmLastFmApiClient>());

        Assert.Empty(errors);
        Assert.NotNull(container.Resolve<IServiceProvider>());
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public Dictionary<string, string> GetLocalizationDictionary() => new();
        public string GetLocalizedString(string phrase) => phrase;
        public string GetLocalizedString(string phrase, Dictionary<string, object> tokens) => phrase;
    }

    private sealed class TestMainDatabase : NzbDrone.Core.Datastore.IMainDatabase
    {
        public System.Data.IDbConnection OpenConnection() => throw new NotImplementedException();
        public Version Version => new(1, 0);
        public int Migration => 1;
        public NzbDrone.Core.Datastore.DatabaseType DatabaseType => NzbDrone.Core.Datastore.DatabaseType.SQLite;
        public void Vacuum() { }
    }

    private sealed class TestLogDatabase : NzbDrone.Core.Datastore.ILogDatabase
    {
        public System.Data.IDbConnection OpenConnection() => throw new NotImplementedException();
        public Version Version => new(1, 0);
        public int Migration => 1;
        public NzbDrone.Core.Datastore.DatabaseType DatabaseType => NzbDrone.Core.Datastore.DatabaseType.SQLite;
        public void Vacuum() { }
    }
}
