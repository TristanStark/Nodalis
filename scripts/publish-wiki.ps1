[CmdletBinding()]
param(
    [switch]$Preview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceDirectory = Join-Path $repositoryRoot 'docs\wiki'
$wikiUrl = 'https://github.com/TristanStark/Nodalis.wiki.git'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('nodalis-wiki-' + [guid]::NewGuid().ToString('N'))

if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
    throw "Le dossier source du Wiki n'existe pas : $sourceDirectory"
}

$pages = @(Get-ChildItem -LiteralPath $sourceDirectory -File -Filter '*.md')
if ($pages.Count -eq 0) {
    throw "Aucune page Markdown a publier dans $sourceDirectory."
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git n'est pas disponible dans PATH. Installez Git et configurez l'authentification GitHub."
}

try {
    Write-Host "Clonage du Wiki : $wikiUrl"
    & git clone --quiet $wikiUrl $temporaryDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "Clonage impossible. Initialisez le Wiki avec une page Home sur GitHub, puis verifiez vos droits Git."
    }

    foreach ($page in $pages) {
        Copy-Item -LiteralPath $page.FullName -Destination (Join-Path $temporaryDirectory $page.Name) -Force
    }

    & git -C $temporaryDirectory add --all -- '*.md'
    if ($LASTEXITCODE -ne 0) {
        throw "Impossible de preparer les pages Wiki."
    }

    $changes = @(& git -C $temporaryDirectory status --short)
    if ($LASTEXITCODE -ne 0) {
        throw "Impossible de lire les modifications."
    }

    if ($changes.Count -eq 0) {
        Write-Host 'Le Wiki est deja a jour.'
        return
    }

    Write-Host 'Pages modifiees :'
    $changes | ForEach-Object { Write-Host $_ }

    if ($Preview) {
        Write-Host 'Simulation terminee. Aucun commit ni push effectue.'
        return
    }

    & git -C $temporaryDirectory commit -m 'docs(wiki): publish Nodalis documentation'
    if ($LASTEXITCODE -ne 0) {
        throw "Le commit a echoue. Configurez user.name et user.email dans Git."
    }

    & git -C $temporaryDirectory push origin HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "Le push Wiki a echoue. Controlez votre authentification et les droits d'ecriture."
    }

    Write-Host 'Pages Wiki publiees.'
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
