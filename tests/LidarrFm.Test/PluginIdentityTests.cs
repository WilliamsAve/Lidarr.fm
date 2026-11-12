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

        // Verify all plugin internal services (ILastFmApiClient, IMusicBrainzAlbumResolver, ILastFmAlbumImportService)
        // are auto-registered by Lidarr's AutoAddServices convention.
        Assert.True(container.IsRegistered<ILastFmApiClient>());
        Assert.True(container.IsRegistered<IMusicBrainzAlbumResolver>());
        Assert.True(container.IsRegistered<ILastFmAlbumImportService>());
        Assert.True(container.IsRegistered<IImportList>());
    }
}
