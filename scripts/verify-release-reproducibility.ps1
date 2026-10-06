[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [string] $BaselineArchive
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not [System.IO.Path]::IsPathRooted($BaselineArchive)) {
    $BaselineArchive = Join-Path $root $BaselineArchive
}

$BaselineArchive = [System.IO.Path]::GetFullPath($BaselineArchive)

if (-not (Test-Path $BaselineArchive)) {
    throw "Baseline archive does not exist: $BaselineArchive"
}

$baselineChecksumPath = Join-Path (Split-Path -Parent $BaselineArchive) "SHA256SUMS.txt"

if (-not (Test-Path $baselineChecksumPath)) {
    throw "Baseline checksum file does not exist: $baselineChecksumPath"
}

$baselineHash = (Get-FileHash -Path $BaselineArchive -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = (Get-Content -Path $baselineChecksumPath -Raw).Trim()
$declaredHash = ($checksumLine -split "\s+")[0].ToLowerInvariant()

if ($declaredHash -ne $baselineHash) {
    throw "Baseline SHA256SUMS.txt does not match Nodalis-win-x64.zip."
}

$verificationRoot = Join-Path $root "artifacts-reproducibility"

try {
    if (Test-Path $verificationRoot) {
        Remove-Item $verificationRoot -Recurse -Force
    }

    & (Join-Path $PSScriptRoot "publish-portable.ps1") -Version $Version -OutputRoot $verificationRoot

    $verificationArchive = Join-Path $verificationRoot "Nodalis-win-x64.zip"
    $verificationChecksumPath = Join-Path $verificationRoot "SHA256SUMS.txt"

    if (-not (Test-Path $verificationArchive) -or
        -not (Test-Path $verificationChecksumPath)) {
        throw "Reproducibility build did not produce the expected archive and checksum."
    }

    $verificationHash = (Get-FileHash -Path $verificationArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $verificationChecksumLine = (Get-Content -Path $verificationChecksumPath -Raw).Trim()

    if ($verificationHash -ne $baselineHash) {
        throw "Portable archive is not reproducible. Baseline: $baselineHash; rebuilt: $verificationHash"
    }

    if ($verificationChecksumLine -ne $checksumLine) {
        throw "SHA256SUMS.txt is not reproducible between identical release builds."
    }

    Write-Host "Portable release is reproducible."
    Write-Host "SHA-256: $baselineHash"
}
finally {
    if (Test-Path $verificationRoot) {
        Remove-Item $verificationRoot -Recurse -Force
    }
}
