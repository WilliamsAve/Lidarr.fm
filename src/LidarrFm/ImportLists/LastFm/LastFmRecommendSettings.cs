using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.LidarrFm;

public class LastFmRecommendSettingsValidator : AbstractValidator<LastFmRecommendSettings>
{
    public LastFmRecommendSettingsValidator()
    {
        RuleFor(c => c.UserId)
            .NotEmpty()
            .WithMessage("Last.fm Username is required.");

        RuleFor(c => c.ApiKey)
            .NotEmpty()
            .WithMessage("Last.fm API Key is required.");

        RuleFor(c => c.BaseUrl)
            .NotEmpty()
            .WithMessage("Last.fm API Base URL is required.");

        RuleFor(c => c.SeedLimit)
            .InclusiveBetween(1, 100)
            .WithMessage("Activity Seed Limit must be between 1 and 100.");

        RuleFor(c => c.SimilarArtistsPerSeed)
            .InclusiveBetween(1, 25)
            .WithMessage("Similar Artists per Seed must be between 1 and 25.");

        RuleFor(c => c.TopAlbumsPerArtist)
            .InclusiveBetween(1, 10)
            .WithMessage("Albums per Recommended Artist must be between 1 and 10.");

        RuleFor(c => c.MaxRecommendedAlbums)
            .InclusiveBetween(1, 100)
            .WithMessage("Max Recommended Albums must be between 1 and 100.");
    }
}

public class LastFmRecommendSettings : IImportListSettings
{
    private static readonly LastFmRecommendSettingsValidator Validator = new();

    public const string DefaultLastFmBaseUrl = "https://ws.audioscrobbler.com/2.0/";
    public const string DefaultLastFmApiKey = "204c76646d6020eee36bbc51a2fcd810";
    public const string DefaultMusicBrainzBaseUrl = "https://musicbrainz.org/ws/2/";

    public LastFmRecommendSettings()
    {
        BaseUrl = DefaultLastFmBaseUrl;
        ApiKey = DefaultLastFmApiKey;
        MusicBrainzBaseUrl = DefaultMusicBrainzBaseUrl;
        ActivitySource = (int)LastFmActivitySource.TopArtists;
        TimePeriod = (int)LastFmTimePeriod.LastThreeMonths;
        SeedLimit = 15;
        SimilarArtistsPerSeed = 5;
        TopAlbumsPerArtist = 2;
        MaxRecommendedAlbums = 20;
        NewArtistMonitorMode = (int)LidarrFmNewArtistMonitorMode.ThatAlbum;
    }

    [FieldDefinition(0, Label = "Last.fm Username", HelpText = "Your Last.fm account username to query activity and generate album recommendations from.", Placeholder = "your_lastfm_username")]
    public string UserId { get; set; } = string.Empty;

    [FieldDefinition(1, Label = "Last.fm API Key", Type = FieldType.Textbox, Privacy = PrivacyLevel.ApiKey, HelpText = "Last.fm API Key used to authenticate requests to Last.fm (a default key is pre-filled, or enter your own from last.fm/api/account/create).")]
    public string ApiKey { get; set; }

    [FieldDefinition(2, Label = "Activity Source", Type = FieldType.Select, SelectOptions = typeof(LastFmActivitySource), HelpText = "Which Last.fm user activity feed to analyze when generating album recommendations.")]
    public int ActivitySource { get; set; }

    [FieldDefinition(3, Label = "Activity Time Period", Type = FieldType.Select, SelectOptions = typeof(LastFmTimePeriod), HelpText = "Time period for Top Artists / Top Albums / Top Tracks activity queries.")]
    public int TimePeriod { get; set; }

    [FieldDefinition(4, Label = "Activity Seed Count", Type = FieldType.Number, HelpText = "Number of artists/albums/tracks from your Last.fm activity to use as recommendation seeds (1-100).")]
    public int SeedLimit { get; set; }

    [FieldDefinition(5, Label = "Similar Artists per Seed", Type = FieldType.Number, HelpText = "Number of similar artists to retrieve per seed item (1-25).")]
    public int SimilarArtistsPerSeed { get; set; }

    [FieldDefinition(6, Label = "Albums per Recommended Artist", Type = FieldType.Number, HelpText = "Maximum number of top albums to pull for each recommended artist (1-10).")]
    public int TopAlbumsPerArtist { get; set; }

    [FieldDefinition(7, Label = "Max Recommended Albums", Type = FieldType.Number, HelpText = "Maximum total recommended albums to process per sync run (1-100).")]
    public int MaxRecommendedAlbums { get; set; }

    [FieldDefinition(8, Label = "New Artist Monitoring", Type = FieldType.Select, SelectOptions = typeof(LidarrFmNewArtistMonitorMode), HelpText = "Monitoring behavior when a recommended album belongs to a NEW artist not yet in Lidarr. Existing artists in Lidarr are never modified.")]
    public int NewArtistMonitorMode { get; set; }

    [FieldDefinition(9, Label = "New Artist Quality Profile", Type = FieldType.QualityProfile, HelpText = "Quality Profile for new artists not yet in Lidarr (falls back to the list's Quality Profile if unset). Existing artists are never modified.")]
    public int NewArtistQualityProfileId { get; set; }

    [FieldDefinition(10, Label = "New Artist Metadata Profile", Type = FieldType.MetadataProfile, HelpText = "Metadata Profile for new artists not yet in Lidarr (falls back to the list's Metadata Profile if unset). Existing artists are never modified.")]
    public int NewArtistMetadataProfileId { get; set; }

    [FieldDefinition(11, Label = "New Artist Root Folder", Type = FieldType.RootFolder, HelpText = "Root Folder path for new artists not yet in Lidarr (falls back to the list's Root Folder if unset). Existing artists are never modified.")]
    public string NewArtistRootFolderPath { get; set; } = string.Empty;

    [FieldDefinition(12, Label = "Last.fm API URL", Type = FieldType.Url, Advanced = true, HelpText = "Base URL for Last.fm Web Services 2.0 API.")]
    public string BaseUrl { get; set; }

    [FieldDefinition(13, Label = "MusicBrainz API URL", Type = FieldType.Url, Advanced = true, HelpText = "Base URL for MusicBrainz WS/2 API used to resolve Last.fm Release MBIDs to Lidarr Release-Group MBIDs.")]
    public string MusicBrainzBaseUrl { get; set; }

    public NzbDroneValidationResult Validate()
    {
        return new NzbDroneValidationResult(Validator.Validate(this));
    }
}

public enum LastFmActivitySource
{
    [FieldOption(Label = "Top Artists", Hint = "Recommend albums based on your most-played artists")]
    TopArtists = 0,

    [FieldOption(Label = "Top Albums", Hint = "Recommend albums based on the artists of your most-played albums")]
    TopAlbums = 1,

    [FieldOption(Label = "Top Tracks", Hint = "Recommend albums based on tracks similar to your most-played tracks")]
    TopTracks = 2,

    [FieldOption(Label = "Recent Tracks", Hint = "Recommend albums based on your recently scrobbled tracks")]
    RecentTracks = 3,

    [FieldOption(Label = "Loved Tracks", Hint = "Recommend albums based on your loved tracks on Last.fm")]
    LovedTracks = 4
}

public enum LastFmTimePeriod
{
    [FieldOption(Label = "Overall")]
    Overall = 0,

    [FieldOption(Label = "Last 7 Days")]
    LastWeek = 1,

    [FieldOption(Label = "Last 1 Month")]
    LastMonth = 2,

    [FieldOption(Label = "Last 3 Months")]
    LastThreeMonths = 3,

    [FieldOption(Label = "Last 6 Months")]
    LastSixMonths = 4,

    [FieldOption(Label = "Last 12 Months")]
    LastTwelveMonths = 5
}

public enum LidarrFmNewArtistMonitorMode
{
    [FieldOption(Label = "Monitor That Album", Hint = "Monitor only the recommended album(s) for the new artist")]
    ThatAlbum = 0,

    [FieldOption(Label = "Monitor Existing Albums", Hint = "Monitor albums that already have files on disk")]
    ExistingAlbums = 1,

    [FieldOption(Label = "Monitor All Albums", Hint = "Monitor all albums for the new artist")]
    AllAlbums = 2,

    [FieldOption(Label = "Monitor Future Albums", Hint = "Monitor only future releases for the new artist")]
    FutureAlbums = 3
}
