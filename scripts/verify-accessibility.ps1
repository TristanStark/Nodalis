$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appRoot = Join-Path $repoRoot "src/Nodalis.App"
$themePath = Join-Path $appRoot "Themes/Dark.xaml"
$appXamlPath = Join-Path $appRoot "App.xaml"
$mainWindowCodePath = Join-Path $appRoot "MainWindow.xaml.cs"
$mainWindowXamlPath = Join-Path $appRoot "MainWindow.xaml"
$appProjectPath = Join-Path $appRoot "Nodalis.App.csproj"
$appManifestPath = Join-Path $appRoot "app.manifest"

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
    'VerticalScrollBarVisibility="Auto"',
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
$mainWindowXaml = Get-Content -Raw -LiteralPath $mainWindowXamlPath

if ($mainWindowXaml.Contains('<Run Text="{Binding OverdueDisplay}" />')) {
    $failures.Add("Project dashboard OverdueDisplay must bind OneWay because TaskItem.OverdueDisplay is read-only.")
}

if (-not $mainWindowXaml.Contains('<Run Text="{Binding OverdueDisplay, Mode=OneWay}" />')) {
    $failures.Add("Project dashboard is missing the safe OneWay OverdueDisplay binding.")
}
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

$appProject = Get-Content -Raw -LiteralPath $appProjectPath
if (-not $appProject.Contains('<ApplicationManifest>app.manifest</ApplicationManifest>')) {
    $failures.Add("Nodalis.App.csproj must embed app.manifest for explicit DPI awareness.")
}

if (-not (Test-Path -LiteralPath $appManifestPath)) {
    $failures.Add("Nodalis.App/app.manifest is missing.")
}
else {
    $appManifest = Get-Content -Raw -LiteralPath $appManifestPath

    if (-not $appManifest.Contains('PerMonitorV2')) {
        $failures.Add("Nodalis.App/app.manifest must declare PerMonitorV2 DPI awareness.")
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Accessibility audit failed:`n - " + ($failures -join "`n - "))
    exit 1
}

Write-Host "Accessibility and context-menu audit passed for $($xamlFiles.Count) XAML files."
