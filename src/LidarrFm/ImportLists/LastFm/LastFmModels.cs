using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.LidarrFm;

internal sealed class LidarrFmLastFmApiResponse
{
    [JsonPropertyName("error")]
    public int? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("topartists")]
    public LidarrFmLastFmArtistListContainer? TopArtists { get; set; }

    [JsonPropertyName("topalbums")]
    public LidarrFmLastFmAlbumListContainer? TopAlbums { get; set; }

    [JsonPropertyName("toptracks")]
    public LidarrFmLastFmTrackListContainer? TopTracks { get; set; }

    [JsonPropertyName("recenttracks")]
    public LidarrFmLastFmTrackListContainer? RecentTracks { get; set; }

    [JsonPropertyName("lovedtracks")]
    public LidarrFmLastFmTrackListContainer? LovedTracks { get; set; }

    [JsonPropertyName("similarartists")]
    public LidarrFmLastFmArtistListContainer? SimilarArtists { get; set; }

    [JsonPropertyName("similartracks")]
    public LidarrFmLastFmTrackListContainer? SimilarTracks { get; set; }
}

internal sealed class LidarrFmLastFmArtistListContainer
{
    [JsonPropertyName("artist")]
    public List<LidarrFmLastFmArtistDto> Artist { get; set; } = new();
}

internal sealed class LidarrFmLastFmAlbumListContainer
{
    [JsonPropertyName("album")]
    public List<LidarrFmLastFmAlbumDto> Album { get; set; } = new();
}

internal sealed class LidarrFmLastFmTrackListContainer
{
    [JsonPropertyName("track")]
    public List<LidarrFmLastFmTrackDto> Track { get; set; } = new();
}

internal sealed class LidarrFmLastFmArtistDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mbid")]
    public string? Mbid { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("#text")]
    public string? TextName { get; set; }

    public string EffectiveName => !string.IsNullOrWhiteSpace(Name) ? Name : (TextName ?? string.Empty);
}

internal sealed class LidarrFmLastFmAlbumDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mbid")]
    public string? Mbid { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("playcount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public long? PlayCount { get; set; }

    [JsonPropertyName("artist")]
    public LidarrFmLastFmArtistDto? Artist { get; set; }
}

internal sealed class LidarrFmLastFmTrackDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mbid")]
    public string? Mbid { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("artist")]
    public LidarrFmLastFmArtistDto? Artist { get; set; }
}

/// <summary>
/// Represents a recommended album candidate from Last.fm prior to MusicBrainz Release-Group resolution.
/// </summary>
public sealed record LidarrFmRecommendedAlbumCandidate(
    string AlbumTitle,
    string? LastFmAlbumMbid,
    string ArtistName,
    string? LastFmArtistMbid,
    int SimilarityOccurrences,
    int BestSimilarityRank);

/// <summary>
/// Represents a recommended album resolved to its canonical Lidarr MusicBrainz Release-Group ID and Artist MusicBrainz ID.
/// </summary>
public sealed record LidarrFmResolvedAlbum(
    string AlbumMusicBrainzId,
    string AlbumTitle,
    string ArtistMusicBrainzId,
    string ArtistName);

/// <summary>
/// MusicBrainz WS/2 Release lookup response (/ws/2/release/{mbid}?inc=release-groups+artists&fmt=json).
/// </summary>
internal sealed class LidarrFmMusicBrainzReleaseResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("release-group")]
    public LidarrFmMusicBrainzReleaseGroupDto? ReleaseGroup { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<LidarrFmMusicBrainzArtistCreditDto>? ArtistCredit { get; set; }
}

/// <summary>
/// MusicBrainz WS/2 Release-Group search response (/ws/2/release-group/?query=...&fmt=json).
/// </summary>
internal sealed class LidarrFmMusicBrainzReleaseGroupSearchResponse
{
    [JsonPropertyName("release-groups")]
    public List<LidarrFmMusicBrainzReleaseGroupDto>? ReleaseGroups { get; set; }
}

internal sealed class LidarrFmMusicBrainzReleaseGroupDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<LidarrFmMusicBrainzArtistCreditDto>? ArtistCredit { get; set; }
}

internal sealed class LidarrFmMusicBrainzArtistCreditDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("artist")]
    public LidarrFmMusicBrainzArtistDto? Artist { get; set; }
}

internal sealed class LidarrFmMusicBrainzArtistDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
