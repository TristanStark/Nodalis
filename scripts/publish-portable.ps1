[CmdletBinding()]
param(
    [string] $Version = "0.0.0-local",
    [string] $OutputRoot
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root "artifacts"
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$publishDirectory = Join-Path $OutputRoot "Nodalis-win-x64"
$archivePath = Join-Path $OutputRoot "Nodalis-win-x64.zip"
$checksumPath = Join-Path $OutputRoot "SHA256SUMS.txt"
$projectPath = Join-Path $root "src\Nodalis.App\Nodalis.App.csproj"
$updaterProjectPath = Join-Path $root "src\Nodalis.Updater\Nodalis.Updater.csproj"
$updaterPublishDirectory = Join-Path $OutputRoot ".Nodalis.Updater-win-x64"
$userGuidePath = Join-Path $root "docs\README-UTILISATEUR.md"

if (Test-Path $publishDirectory) {
    Remove-Item $publishDirectory -Recurse -Force
}

if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}

if (Test-Path $checksumPath) {
    Remove-Item $checksumPath -Force
}

if (Test-Path $updaterPublishDirectory) {
    Remove-Item $updaterPublishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

Write-Host "Publishing Nodalis $Version for win-x64..."

$publishArguments = @(
    "publish",
    $projectPath,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:PublishProfile=win-x64-portable",
    "-p:Version=$Version",
    "--output", $publishDirectory
)

& dotnet @publishArguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executablePath = Join-Path $publishDirectory "Nodalis.exe"

if (-not (Test-Path $executablePath)) {
    throw "Portable publish did not produce Nodalis.exe."
}

Write-Host "Publishing standalone updater..."

$updaterPublishArguments = @(
    "publish",
    $updaterProjectPath,
    "--configuration", "Release",
    "--runtime", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "--output", $updaterPublishDirectory
)

& dotnet @updaterPublishArguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish for Nodalis.Updater failed with exit code $LASTEXITCODE."
}

$updaterExecutablePath = Join-Path $updaterPublishDirectory "Nodalis.Updater.exe"

if (-not (Test-Path $updaterExecutablePath)) {
    throw "Updater publish did not produce Nodalis.Updater.exe."
}

Copy-Item -Path $updaterExecutablePath -Destination (Join-Path $publishDirectory "Nodalis.Updater.exe") -Force
Copy-Item -Path $userGuidePath -Destination (Join-Path $publishDirectory "README.md") -Force

$commit = "unknown"

try {
    $commit = (& git -C $root rev-parse --short=12 HEAD 2>$null).Trim()

    if ([string]::IsNullOrWhiteSpace($commit)) {
        $commit = "unknown"
    }
}
catch {
    $commit = "unknown"
}

@(
    "Nodalis $Version"
    "Target: win-x64"
    "Mode: self-contained, single-file"
    "Commit: $commit"
) | Set-Content -Path (Join-Path $publishDirectory "VERSION.txt") -Encoding UTF8

$manifestFiles = @(
    Get-ChildItem -Path $publishDirectory -File -Recurse |
        Where-Object { $_.Name -ne "release-manifest.json" } |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath(
                $publishDirectory,
                $_.FullName).Replace("\", "/")

            [ordered]@{
                path = $relativePath
                sha256 = (Get-FileHash -Path $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                length = $_.Length
            }
        }
)

$releaseManifest = [ordered]@{
    manifestSchemaVersion = 1
    product = "Nodalis"
    version = $Version
    targetRid = "win-x64"
    minimumWorkspaceSchemaVersion = 1
    maximumWorkspaceSchemaVersion = 1
    payloadDirectory = "Nodalis-win-x64"
    files = $manifestFiles
}

$releaseManifest |
    ConvertTo-Json -Depth 6 |
    Set-Content -Path (Join-Path $publishDirectory "release-manifest.json") -Encoding UTF8

if (Test-Path $updaterPublishDirectory) {
    Remove-Item $updaterPublishDirectory -Recurse -Force
}

# Build the ZIP with stable entry order and timestamps. This keeps archives
# reproducible for the same published payload instead of inheriting machine
# extraction/build timestamps from Compress-Archive.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archiveStream = [System.IO.File]::Open(
    $archivePath,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)

try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $archiveStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $true)

    try {
        $files = Get-ChildItem -Path $publishDirectory -File -Recurse | Sort-Object FullName
        $fixedTimestamp = [DateTimeOffset]::Parse("2000-01-01T00:00:00+00:00")

        foreach ($file in $files) {
            $relative = [System.IO.Path]::GetRelativePath(
                $publishDirectory,
                $file.FullName).Replace("\", "/")

            $entryName = "Nodalis-win-x64/$relative"
            $entry = $archive.CreateEntry(
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal)

            $entry.LastWriteTime = $fixedTimestamp
            $entryStream = $entry.Open()

            try {
                $sourceStream = [System.IO.File]::OpenRead($file.FullName)

                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $sourceStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $archiveStream.Dispose()
}

$hash = (Get-FileHash -Path $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()

"$hash  Nodalis-win-x64.zip" | Set-Content -Path $checksumPath -Encoding ASCII

Write-Host ""
Write-Host "Portable package ready:"
Write-Host "  $archivePath"
Write-Host "  $checksumPath"
Write-Host "  SHA-256: $hash"
