using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.LidarrFm;

public sealed class LastFmApiResponse
{
    [JsonPropertyName("error")]
    public int? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("topartists")]
    public LastFmArtistListContainer? TopArtists { get; set; }

    [JsonPropertyName("topalbums")]
    public LastFmAlbumListContainer? TopAlbums { get; set; }

    [JsonPropertyName("toptracks")]
    public LastFmTrackListContainer? TopTracks { get; set; }

    [JsonPropertyName("recenttracks")]
    public LastFmTrackListContainer? RecentTracks { get; set; }

    [JsonPropertyName("lovedtracks")]
    public LastFmTrackListContainer? LovedTracks { get; set; }

    [JsonPropertyName("similarartists")]
    public LastFmArtistListContainer? SimilarArtists { get; set; }

    [JsonPropertyName("similartracks")]
    public LastFmTrackListContainer? SimilarTracks { get; set; }
}

public sealed class LastFmArtistListContainer
{
    [JsonPropertyName("artist")]
    public List<LastFmArtistDto> Artist { get; set; } = new();
}

public sealed class LastFmAlbumListContainer
{
    [JsonPropertyName("album")]
    public List<LastFmAlbumDto> Album { get; set; } = new();
}

public sealed class LastFmTrackListContainer
{
    [JsonPropertyName("track")]
    public List<LastFmTrackDto> Track { get; set; } = new();
}

public sealed class LastFmArtistDto
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

public sealed class LastFmAlbumDto
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
    public LastFmArtistDto? Artist { get; set; }
}

public sealed class LastFmTrackDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mbid")]
    public string? Mbid { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("artist")]
    public LastFmArtistDto? Artist { get; set; }
}

/// <summary>
/// Represents a recommended album candidate from Last.fm prior to MusicBrainz Release-Group resolution.
/// </summary>
public sealed record LastFmRecommendedAlbumCandidate(
    string AlbumTitle,
    string? LastFmAlbumMbid,
    string ArtistName,
    string? LastFmArtistMbid,
    int SimilarityOccurrences,
    int BestSimilarityRank);

/// <summary>
/// Represents a recommended album resolved to its canonical Lidarr MusicBrainz Release-Group ID and Artist MusicBrainz ID.
/// </summary>
public sealed record ResolvedRecommendedAlbum(
    string AlbumMusicBrainzId,
    string AlbumTitle,
    string ArtistMusicBrainzId,
    string ArtistName);

/// <summary>
/// MusicBrainz WS/2 Release lookup response (/ws/2/release/{mbid}?inc=release-groups+artists&fmt=json).
/// </summary>
public sealed class MusicBrainzReleaseResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("release-group")]
    public MusicBrainzReleaseGroupDto? ReleaseGroup { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MusicBrainzArtistCreditDto>? ArtistCredit { get; set; }
}

/// <summary>
/// MusicBrainz WS/2 Release-Group search response (/ws/2/release-group/?query=...&fmt=json).
/// </summary>
public sealed class MusicBrainzReleaseGroupSearchResponse
{
    [JsonPropertyName("release-groups")]
    public List<MusicBrainzReleaseGroupDto>? ReleaseGroups { get; set; }
}

public sealed class MusicBrainzReleaseGroupDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MusicBrainzArtistCreditDto>? ArtistCredit { get; set; }
}

public sealed class MusicBrainzArtistCreditDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("artist")]
    public MusicBrainzArtistDto? Artist { get; set; }
}

public sealed class MusicBrainzArtistDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
