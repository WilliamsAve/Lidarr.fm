using NzbDrone.Core.ImportLists.LidarrFm;
using Xunit;

namespace LidarrFm.Test;

public class LastFmRecommendSettingsTests
{
    [Fact]
    public void DefaultSettings_RequireUsername_ToBeValid()
    {
        var settings = new LastFmRecommendSettings();
        var result = settings.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LastFmRecommendSettings.UserId));
    }

    [Fact]
    public void Settings_WithUsername_AreValidByDefault()
    {
        var settings = new LastFmRecommendSettings
        {
            UserId = "nick_lastfm"
        };

        var result = settings.Validate();

        Assert.True(result.IsValid);
        Assert.Equal((int)LidarrFmNewArtistMonitorMode.ThatAlbum, settings.NewArtistMonitorMode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Settings_RejectInvalidSeedLimit(int invalidSeedLimit)
    {
        var settings = new LastFmRecommendSettings
        {
            UserId = "nick_lastfm",
            SeedLimit = invalidSeedLimit
        };

        var result = settings.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LastFmRecommendSettings.SeedLimit));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(26)]
    public void Settings_RejectInvalidSimilarArtistsPerSeed(int invalidCount)
    {
        var settings = new LastFmRecommendSettings
        {
            UserId = "nick_lastfm",
            SimilarArtistsPerSeed = invalidCount
        };

        var result = settings.Validate();
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LastFmRecommendSettings.SimilarArtistsPerSeed));
    }
}
