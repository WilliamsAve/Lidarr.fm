using DryIoc;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.LidarrFm;
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
    public void ExportedTypes_IncludePluginAndImportList()
    {
        var exportedTypes = typeof(LidarrFmPlugin).Assembly.GetExportedTypes();

        Assert.Contains(typeof(LidarrFmPlugin), exportedTypes);
        Assert.Contains(typeof(LastFmRecommendImportList), exportedTypes);
        Assert.Contains(typeof(LastFmRecommendSettings), exportedTypes);
        Assert.True(typeof(IPlugin).IsAssignableFrom(typeof(LidarrFmPlugin)));
        Assert.True(typeof(IImportList).IsAssignableFrom(typeof(LastFmRecommendImportList)));
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
        // IQualityProfileService, IMetadataProfileService, ITagService, LastFmRecommendImportList,
        // IMusicBrainzAlbumResolver, ILastFmAlbumImportService, and ILastFmApiClient with zero errors.
        var errors = container.Validate(
            ServiceInfo.Of<IImportListFactory>(),
            ServiceInfo.Of<NzbDrone.Core.Profiles.Qualities.IQualityProfileService>(),
            ServiceInfo.Of<NzbDrone.Core.Profiles.Metadata.IMetadataProfileService>(),
            ServiceInfo.Of<NzbDrone.Core.Tags.ITagService>(),
            ServiceInfo.Of<LastFmRecommendImportList>(),
            ServiceInfo.Of<IMusicBrainzAlbumResolver>(),
            ServiceInfo.Of<ILastFmAlbumImportService>(),
            ServiceInfo.Of<ILastFmApiClient>());

        Assert.Empty(errors);
        Assert.NotNull(container.Resolve<IServiceProvider>());
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
