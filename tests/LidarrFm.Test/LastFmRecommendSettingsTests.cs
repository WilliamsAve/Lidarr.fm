using NzbDrone.Core.ImportLists.LidarrFm;
using Xunit;

namespace LidarrFm.Test;

public class LastFmRecommendSettingsTests
{
    [Fact]
    public void DefaultSettings_RequireUsername_ToBeValid()
    {
        var settings = new LidarrFmImportListSettings();
        var result = settings.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LidarrFmImportListSettings.UserId));
    }

    [Fact]
    public void Settings_WithUsername_AreValidByDefault()
    {
        var settings = new LidarrFmImportListSettings
        {
            UserId = "nick_lastfm"
        };

        var result = settings.Validate();

        Assert.True(result.IsValid);
        Assert.Equal((int)LidarrFmNewArtistMonitorMode.ThatAlbum, settings.NewArtistMonitorMode);
    }

    [Fact]
    public void Settings_WhenApiKeyIsMaskedOrEmpty_FallBackToDefaultApiKey()
    {
        var settings = new LidarrFmImportListSettings
        {
            UserId = "nick_lastfm",
            ApiKey = "********"
        };

        Assert.Equal(LidarrFmImportListSettings.DefaultLastFmApiKey, settings.ApiKey);

        settings.ApiKey = "";
        Assert.Equal(LidarrFmImportListSettings.DefaultLastFmApiKey, settings.ApiKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Settings_RejectInvalidSeedLimit(int invalidSeedLimit)
    {
        var settings = new LidarrFmImportListSettings
        {
            UserId = "nick_lastfm",
            SeedLimit = invalidSeedLimit
        };

        var result = settings.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LidarrFmImportListSettings.SeedLimit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public void Settings_RejectInvalidSimilarArtistsPerSeed(int invalidCount)
    {
        var settings = new LidarrFmImportListSettings
        {
            UserId = "nick_lastfm",
            SimilarArtistsPerSeed = invalidCount
        };

        var result = settings.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LidarrFmImportListSettings.SimilarArtistsPerSeed));
    }
}
