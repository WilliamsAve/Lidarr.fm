# Lidarr.fm (`Lidarr.Plugin.LidarrFm`)

A native C# Import List plugin for **Lidarr** (targeting Lidarr's `plugins` / `develop` plugin ecosystem) that connects to a user's **Last.fm** account, derives personalized album recommendations based on listening activity, resolves each album's canonical **MusicBrainz Release-Group ID**, and adds it to Lidarr with strict control over existing vs. new artists.

---

## Features

- **Last.fm Account Activity Integration**:
  - Connects to Last.fm using your **Last.fm Username** and **API Key** (a default API key is pre-configured, or you can supply your own from [last.fm/api/account/create](https://www.last.fm/api/account/create)).
  - Supports multiple user activity sources:
    - **Top Artists** (`user.getTopArtists`)
    - **Top Albums** (`user.getTopAlbums`)
    - **Top Tracks** (`user.getTopTracks`)
    - **Recent Tracks** (`user.getRecentTracks`)
    - **Loved Tracks** (`user.getLovedTracks`)
  - Ranks similar artists (`artist.getSimilar` / `track.getSimilar`) by cross-seed frequency and similarity rank, then pulls top albums (`artist.getTopAlbums`).
- **MusicBrainz Release-Group ID Resolution**:
  - Last.fm's API returns edition-specific MusicBrainz *Release* MBIDs (or omits MBIDs), whereas Lidarr requires MusicBrainz *Release-Group* MBIDs (`ForeignAlbumId`).
  - `MusicBrainzAlbumResolver` resolves Release MBIDs to Release-Group MBIDs via MusicBrainz WS/2 (`/ws/2/release/{mbid}?inc=release-groups+artists`), Lidarr SkyHook (`ISearchForNewAlbum`), and MusicBrainz Release-Group search fallback.
- **Safe Existing Artist Handling**:
  - If a recommended album belongs to an artist **already in Lidarr**:
    - **None** of the existing artist's monitoring options (`Monitored`, `MonitorNewItems`), profiles (`QualityProfileId`, `MetadataProfileId`), root folder, tags, or existing albums' monitoring settings are modified.
    - If the recommended album is not yet in Lidarr's database for that artist, it is added using its MusicBrainz ID with `Monitored = false` and `SearchForNewAlbum = false`.
- **Configurable New Artist Profiles & Monitoring**:
  - If a recommended album belongs to a **new artist** not yet in Lidarr, you can configure:
    - **New Artist Quality Profile**
    - **New Artist Metadata Profile**
    - **New Artist Root Folder**
    - **New Artist Monitoring**:
      1. **Monitor That Album** (monitors only the recommended album(s))
      2. **Monitor Existing Albums** (monitors albums that already have files on disk)
      3. **Monitor All Albums** (monitors all albums by the new artist)
      4. **Monitor Future Albums** (monitors only future releases by the new artist)

---

## Installation in Lidarr

1. Ensure your Lidarr instance is running a build with plugin support (`plugins` / `develop` >= `3.1.0`).
2. In Lidarr, navigate to **System -> Plugins**.
3. Paste `https://github.com/WilliamsAve/Lidarr.fm` into the GitHub URL box and click **Install** (or extract `Lidarr.Plugin.LidarrFm.net8.0.zip` into `<LidarrAppData>/plugins/WilliamsAve/Lidarr.fm/`).
4. Restart Lidarr.
5. Navigate to **Settings -> Import Lists**, click **+**, and select **Last.fm Recommended Albums**.

---

## Building & Testing Locally

1. Prepare the verified Lidarr reference assemblies (`v3.1.3.4968`):
   - **Windows (PowerShell)**:
     ```powershell
     .\scripts\prepare-lidarr.ps1
     ```
   - **Linux / macOS (Python 3)**:
     ```bash
     python3 scripts/prepare-lidarr.py
     ```
2. Build and run unit tests:
   ```powershell
   dotnet test .\Lidarr.fm.sln -c Release
   ```
3. The compiled plugin package will be output to:
   `_temp/bin/Release/LidarrFm/Lidarr.Plugin.LidarrFm.net8.0.zip`
