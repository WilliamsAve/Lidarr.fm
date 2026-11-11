using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LidarrFm.Test")]

namespace NzbDrone.Core.Plugins;

/// <summary>
/// Lidarr plugin entry point for Lidarr.fm (Last.fm Recommended Albums).
/// </summary>
public sealed class LidarrFmPlugin : Plugin
{
    public const string DisplayName = "Lidarr.fm";
    public const string RepositoryOwner = "WilliamsAve";
    public const string RepositoryUrl = "https://github.com/WilliamsAve/Lidarr.fm";

    public override string Name => DisplayName;

    public override string Owner => RepositoryOwner;

    public override string GithubUrl => RepositoryUrl;
}
