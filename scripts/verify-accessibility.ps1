$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appRoot = Join-Path $repoRoot "src/Nodalis.App"
$themePath = Join-Path $appRoot "Themes/Dark.xaml"
$appXamlPath = Join-Path $appRoot "App.xaml"
$mainWindowCodePath = Join-Path $appRoot "MainWindow.xaml.cs"

$xamlFiles = Get-ChildItem -Path $appRoot -Recurse -Filter *.xaml -File
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($file in $xamlFiles) {
    $content = Get-Content -Raw -LiteralPath $file.FullName
    $relativePath = [System.IO.Path]::GetRelativePath($repoRoot, $file.FullName)

    if ($content -match 'FocusVisualStyle\s*=\s*"\{x:Null\}"') {
        $failures.Add("$relativePath disables the keyboard focus visual.")
    }

    if ($content -match '(?i)(Background|Foreground|BorderBrush)\s*=\s*"(White|Black|#FFF(?:FFF)?|#000(?:000)?)"') {
        $failures.Add("$relativePath contains a hard-coded black/white UI brush instead of the shared dark theme.")
    }

    if ($content -match '<Window\b' -and $content -notmatch '\bTitle\s*=') {
        $failures.Add("$relativePath declares a Window without a Title for native accessibility.")
    }
}

$theme = Get-Content -Raw -LiteralPath $themePath
$requiredThemeTokens = @(
    'NodalisWindowStyle',
    'NodalisKeyboardFocusVisual',
    'KeyboardNavigation.TabNavigation',
    'KeyboardNavigation.ControlTabNavigation',
    'TextOptions.TextFormattingMode',
    'TargetType="{x:Type ContextMenu}"',
    'ScrollViewer.VerticalScrollBarVisibility="Auto"',
    'TargetType="{x:Type MenuItem}"',
    'TargetType="DatePicker"',
    'TargetType="Calendar"',
    'TargetType="ListBoxItem"',
    'TargetType="TreeViewItem"'
)

foreach ($token in $requiredThemeTokens) {
    if (-not $theme.Contains($token)) {
        $failures.Add("Themes/Dark.xaml is missing required accessibility baseline token: $token")
    }
}

$appXaml = Get-Content -Raw -LiteralPath $appXamlPath
if (-not $appXaml.Contains('ResourceDictionary Source="Themes/Dark.xaml"')) {
    $failures.Add("App.xaml no longer loads Themes/Dark.xaml globally.")
}

$mainWindowCode = Get-Content -Raw -LiteralPath $mainWindowCodePath
$requiredContextMenuCodeTokens = @(
    'InitializeContextMenus();',
    'PrepareNodalisContextMenu',
    'CreateNodalisMenuItem',
    'PlacementMode.MousePoint',
    'Ajouter au glossaire du projet'
)

foreach ($token in $requiredContextMenuCodeTokens) {
    if (-not $mainWindowCode.Contains($token)) {
        $failures.Add("MainWindow.xaml.cs is missing required context-menu regression token: $token")
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Accessibility audit failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Host "Accessibility and context-menu audit passed for $($xamlFiles.Count) XAML files."
