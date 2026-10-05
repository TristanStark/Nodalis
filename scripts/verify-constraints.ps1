$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot

$packageReferences = Get-ChildItem -Path $root -Recurse -Filter *.csproj |
    Select-String -Pattern '<PackageReference\b'

if ($packageReferences) {
    Write-Error "External PackageReference entries are forbidden in Nodalis."
}

$networkPatterns = @(
    'using\s+System\.Net\b',
    'System\.Net\.',
    '\bHttpClient\b',
    '\bWebClient\b',
    '\bHttpWebRequest\b',
    '\bSocket\b',
    '\bTcpClient\b',
    '\bUdpClient\b'
)

$sourceFiles = Get-ChildItem -Path (Join-Path $root "src") -Recurse -Filter *.cs

foreach ($pattern in $networkPatterns) {
    $matches = $sourceFiles | Select-String -Pattern $pattern
    if ($matches) {
        $matches | ForEach-Object { Write-Host $_ }
        Write-Error "Potential network API usage detected: $pattern"
    }
}

Write-Host "Constraint verification passed: no PackageReference and no network API usage detected."
