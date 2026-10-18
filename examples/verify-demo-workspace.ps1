# Vérification statique du workspace de démonstration Nodalis.
# Aucune modification de fichiers, aucun appel réseau et aucune dépendance tierce.
[CmdletBinding()]
param(
    [string]$Workspace = (Join-Path $PSScriptRoot 'Nodalis-Demo-Workspace')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Workspace).Path
$errors = New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $script:errors.Add($Message) }
}
function ReadJson([string]$Path) {
    return Get-Content -LiteralPath (Join-Path $root $Path) -Raw -Encoding UTF8 | ConvertFrom-Json
}
function InWorkspace([string]$RelativePath) {
    return Join-Path $root ($RelativePath -replace '/', [IO.Path]::DirectorySeparatorChar)
}

$workspace = ReadJson '.workspace.json'
$links = ReadJson '.nodalis-links.json'
$views = ReadJson '.nodalis-views.json'
Check ($workspace.schemaVersion -eq 1) 'Schéma workspace invalide'
Check ($links.schemaVersion -eq 3) 'Schéma index des liens invalide'
Check ($views.schemaVersion -eq 1) 'Schéma des vues invalide'
Check ($views.views.Count -ge 3) 'Vues dynamiques manquantes'

$ids = @{}
$paths = @{}
foreach ($target in $links.targets) {
    $id = [string]$target.id
    Check (-not $ids.ContainsKey($id)) "GUID cible dupliqué : $id"
    $ids[$id] = $target
    if ([string]$target.kind -eq 'document') {
        $path = [string]$target.relativePath
        Check (-not $paths.ContainsKey($path)) "Chemin documentaire dupliqué : $path"
        $paths[$path] = $target
        Check (Test-Path -LiteralPath (InWorkspace $path) -PathType Leaf) "Document absent : $path"
    }
}

$markdowns = @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.md')
Check ($markdowns.Count -eq $paths.Count) "Documents MD : disque=$($markdowns.Count), registre=$($paths.Count)"
foreach ($document in $markdowns) {
    $relative = $document.FullName.Substring($root.Length).TrimStart([char[]]@('\','/')) -replace '\\','/'
    Check ($paths.ContainsKey($relative)) "Document non répertorié : $relative"
    $text = Get-Content -LiteralPath $document.FullName -Raw -Encoding UTF8
    $relations = [regex]::Matches($text, '(?m)^>\s*Relation:.*?\|\s*Cible:.*?\|\s*ID:\s*([0-9a-fA-F-]{36})\s*$')
    foreach ($match in $relations) {
        Check ($ids.ContainsKey($match.Groups[1].Value)) "Relation orpheline dans $relative : $($match.Groups[1].Value)"
    }
    $includes = [regex]::Matches($text, '(?m)^!\[\[([0-9a-fA-F-]{36})(?:#([^\]]+))?\]\]\s*$')
    foreach ($match in $includes) {
        $id = $match.Groups[1].Value
        Check ($ids.ContainsKey($id)) "Inclusion orpheline dans $relative : $id"
        if ($ids.ContainsKey($id) -and $match.Groups[2].Success) {
            $source = InWorkspace ([string]$ids[$id].relativePath)
            $section = $match.Groups[2].Value
            $escaped = [regex]::Escape($section)
            $body = Get-Content -LiteralPath $source -Raw -Encoding UTF8
            Check ([regex]::IsMatch($body, "(?m)^#{1,6} $escaped\s*$")) "Section composite introuvable : $section"
        }
    }
    # Les wikilinks lisibles doivent rester non ambigus après chargement.
    $wikilinks = [regex]::Matches($text, '(?<!\!)\[\[([^\]]+)\]\]')
    foreach ($link in $wikilinks) {
        $key = $link.Groups[1].Value.Split('#')[0]
        $candidates = @($links.targets | Where-Object {
            ([string]$_.id) -eq $key -or
            ([string]$_.displayName) -eq $key -or
            ([string]$_.qualifiedName) -eq $key
        })
        Check ($candidates.Count -eq 1) "Lien Markdown introuvable ou ambigu dans $relative : $key"
    }
    foreach ($image in [regex]::Matches($text, '!\[[^\]]*\]\(([^)]+)\)')) {
        $imageRel = $image.Groups[1].Value -replace '/', [IO.Path]::DirectorySeparatorChar
        $imagePath = [IO.Path]::GetFullPath((Join-Path $document.DirectoryName $imageRel))
        Check ($imagePath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) "Image hors workspace : $relative"
        Check (Test-Path -LiteralPath $imagePath -PathType Leaf) "Image introuvable dans $relative : $imageRel"
    }
}

foreach ($path in @('Attachments/architecture-workspace.bmp','Attachments/worktree-navigation.bmp')) {
    $full = InWorkspace $path
    Check (Test-Path -LiteralPath $full -PathType Leaf) "Image manquante : $path"
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        $bytes = [IO.File]::ReadAllBytes($full)
        Check (($bytes.Length -gt 54) -and ($bytes[0] -eq 66) -and ($bytes[1] -eq 77)) "Image BMP invalide : $path"
    }
}

$manifestPaths = @(
    'Applications/Nodalis/.application.json',
    'Applications/Nodalis/Projets/Nodalis - Évolutions 1.x/.project.json',
    'Applications/Nodalis/Modules/Expérience utilisateur/.module.json',
    'Applications/Nodalis/Modules/Expérience utilisateur/Projets/Refonte explorateur 122/.project.json',
    'Applications/Nodalis/Projets/Nodalis - Évolutions 1.x/Sous-projets/Recherche lexicale 1.7/.project.json'
)
foreach ($path in $manifestPaths) {
    $manifest = ReadJson $path
    Check ($ids.ContainsKey([string]$manifest.id)) "Manifest non enregistré : $path"
}
$ultra = ReadJson 'Applications/Nodalis/Modules/Expérience utilisateur/Projets/Refonte explorateur 122/.project.json'
Check (([string]$ultra.initialComplexity) -eq 'ultraSimple') 'Profil Ultra-simple invalide'
Check ($ultra.sections.Count -eq 0) 'Un projet Ultra-simple ne doit pas avoir de sections obligatoires'
$milestones = @()
foreach ($document in ($markdowns | Where-Object { $_.Name -eq 'Jalons.md' })) {
    $raw = Get-Content -LiteralPath $document.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($raw, '(?m)^\|\s*[^|]+\|\s*(\d{4}-\d{2}-\d{2})\s*\|[^\r\n]+$')) {
        $cells = $match.Value.Trim('|').Split('|')
        if ($cells.Length -lt 7) { continue }
        $id = $cells[5].Trim()
        $parsedId = [guid]::Empty
        Check ([guid]::TryParse($id, [ref]$parsedId)) "GUID de jalon invalide : $id"
        $milestones += $id
        try { [void][datetime]::ParseExact($cells[1].Trim(),'yyyy-MM-dd',[cultureinfo]::InvariantCulture) }
        catch { Check $false "Date jalon invalide : $($cells[1])" }
    }
}
Check ($milestones.Count -ge 7) 'Jalons insuffisants'
Check (($milestones | Select-Object -Unique).Count -eq $milestones.Count) 'GUID de jalon dupliqué'

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    exit 1
}
Write-Host "OK — $($markdowns.Count) Markdown, $($links.targets.Count) cibles, $($views.views.Count) vues, $($milestones.Count) jalons, 2 images." -ForegroundColor Green
