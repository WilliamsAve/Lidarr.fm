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
}
