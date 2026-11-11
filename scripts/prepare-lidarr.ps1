$ErrorActionPreference = "Stop"

$Version = "3.1.3.4968"
$ExpectedSha256 = "4bd64517008f319d37d4d4585146a474ac2ff2d0183b1b751661f89f2b434280"
$Rid = "windows-core-x64"
$ArchiveName = "Lidarr.develop.$Version.$Rid.zip"

$RootDir = Split-Path -Parent $PSScriptRoot
$CacheDir = Join-Path $RootDir "_temp\lidarr"
$DestDir = Join-Path $CacheDir $Version
$ArchivePath = Join-Path $CacheDir $ArchiveName

New-Item -ItemType Directory -Force -Path $CacheDir | Out-Null

if (-not (Test-Path $ArchivePath)) {
    $Url = "https://github.com/Lidarr/Lidarr/releases/download/v$Version/$ArchiveName"
    Write-Host "Downloading $Url ..."
    Invoke-WebRequest -Uri $Url -OutFile $ArchivePath -UseBasicParsing
}

$ActualHash = (Get-FileHash -Path $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ActualHash -ne $ExpectedSha256) {
    throw "SHA-256 mismatch for $ArchiveName. Expected $ExpectedSha256, got $ActualHash"
}

if (Test-Path $DestDir) {
    Remove-Item -Recurse -Force $DestDir
}
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
$Zip = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    $Count = 0
    foreach ($Entry in $Zip.Entries) {
        if ($Entry.FullName -match '^Lidarr/[^/]+\.dll$') {
            $OutFile = Join-Path $DestDir $Entry.Name
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($Entry, $OutFile, $true)
            $Count++
        }
    }
    Write-Host "Prepared $Count verified Lidarr $Version assemblies in $DestDir"
}
finally {
    $Zip.Dispose()
}
