$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appRoot = Join-Path $repoRoot "src/Nodalis.App"
$themePath = Join-Path $appRoot "Themes/Dark.xaml"
$appXamlPath = Join-Path $appRoot "App.xaml"
$mainWindowCodePath = Join-Path $appRoot "MainWindow.xaml.cs"
$mainWindowXamlPath = Join-Path $appRoot "MainWindow.xaml"
$appCodePath = Join-Path $appRoot "App.xaml.cs"
$recoverableErrorDialogPath = Join-Path $appRoot "Dialogs/RecoverableErrorDialog.xaml"
$documentTabsCodePath = Join-Path $appRoot "MainWindow.DocumentTabs.cs"
$diagnosticsServicePath = Join-Path $repoRoot "src/Nodalis.Infrastructure/Reliability/LocalDiagnosticsService.cs"
$exceptionPolicyPath = Join-Path $repoRoot "src/Nodalis.Infrastructure/Reliability/RecoverableExceptionPolicy.cs"
$taskCreationXamlPath = Join-Path $appRoot "Dialogs/TaskCreationDialog.xaml"
$calendarXamlPath = Join-Path $appRoot "Dialogs/WorkspaceCalendarDialog.xaml"
$kanbanXamlPath = Join-Path $appRoot "Dialogs/WorkspaceKanbanDialog.xaml"
$appProjectPath = Join-Path $appRoot "Nodalis.App.csproj"
$appManifestPath = Join-Path $appRoot "app.manifest"
$appIconPath = Join-Path $appRoot "Assets/Nodalis.ico"

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

# The Worktree uses recycling containers, and the document outline must not
# inherit the heavier navigation-only row template. A literal alignment
# setter avoids WPF's transient FindAncestor ItemsControl binding errors.
if ($theme -notmatch '(?s)<Style\s+x:Key="NodalisTreeViewItemStyle"\s+TargetType="TreeViewItem">\s*<Setter\s+Property="VerticalContentAlignment"\s+Value="Center"') {
    $failures.Add("All TreeViewItem containers need an explicit vertical alignment without a FindAncestor binding.")
}
# Worktree sticky paths must use the projection-maintained index instead
# of searching all visible siblings on every scroll event.
$worktreeVmPath = Join-Path $appRoot "Navigation/NavigationNodeViewModel.cs"
$stickyWorktreePath = Join-Path $appRoot "MainWindow.WorktreeSticky.cs"
$worktreeP1Path = Join-Path $appRoot "MainWindow.WorktreeP1.cs"
$quickOpenPath = Join-Path $appRoot "MainWindow.QuickOpen.cs"
$quickOpenEntryPath = Join-Path $appRoot "Navigation/QuickOpenEntry.cs"
$worktreeVm = Get-Content -Raw -LiteralPath $worktreeVmPath
$stickyWorktree = Get-Content -Raw -LiteralPath $stickyWorktreePath
$worktreeP1 = Get-Content -Raw -LiteralPath $worktreeP1Path
$quickOpenCode = Get-Content -Raw -LiteralPath $quickOpenPath
$quickOpenEntry = Get-Content -Raw -LiteralPath $quickOpenEntryPath
if (-not $worktreeVm.Contains('internal int VisibleSiblingIndex') -or
    -not $stickyWorktree.Contains('int index = current.VisibleSiblingIndex;') -or
    $stickyWorktree.Contains('candidateIndex < parent.VisibleChildren.Count')) {
    $failures.Add("Worktree sticky headers must use O(depth) visible sibling positions.")
}
if (-not $worktreeP1.Contains('_worktreeP1RefreshPending') -or
    -not $worktreeP1.Contains('candidate.VisibleSiblingIndex')) {
    $failures.Add("Worktree tab refreshes and visibility checks must remain coalesced and indexed.")
}
if (-not $quickOpenCode.Contains('documentsByRelativePath.GetValueOrDefault(qualifiedPath)') -or
    -not $quickOpenCode.Contains('targetsById.GetValueOrDefault(node.Id)')) {
    $failures.Add("Quick Open should resolve link targets via O(1) maps, not repeated linear searches.")
}
if (-not $quickOpenEntry.Contains('PreferenceKey { get; init; }') -or
    -not $worktreeP1.Contains('EnsureWorktreeShortcutIndex();') -or
    -not $worktreeP1.Contains('ReferenceEquals(_linkIndex, _worktreeShortcutCachedLinkIndex)')) {
    $failures.Add("Worktree shortcuts must reuse navigation/link snapshots while refreshing preferences.")
}
if (-not $stickyWorktree.Contains('TrackOperation("Worktree · en-têtes persistants")') -or
    -not $worktreeP1.Contains('TrackOperation("Worktree · projection du filtre")') -or
    -not $worktreeP1.Contains('TrackOperation("Worktree · favoris et récents")')) {
    $failures.Add("Worktree must keep privacy-safe duration probes for scroll, filtering and shortcuts.")
}

$requiredThemeTokens = @(
    'NodalisWindowStyle',
    'NodalisKeyboardFocusVisual',
    'KeyboardNavigation.TabNavigation',
    'KeyboardNavigation.ControlTabNavigation',
    'TextOptions.TextFormattingMode',
    'TargetType="{x:Type ContextMenu}"',
    'VerticalScrollBarVisibility="Auto"',
    'TargetType="{x:Type MenuItem}"',
    'TargetType="{x:Type TabControl}"',
    'PART_SelectedContentHost',
    'NodalisCalendarStyle',
    'NodalisCalendarItemStyle',
    'CalendarDayButtonStyle',
    'CalendarButtonStyle',
    'CalendarItemStyle',
    'TargetType="{x:Type DatePicker}"',
    'TargetType="{x:Type Calendar}"',
    'TargetType="ListBoxItem"',
    'TargetType="TreeViewItem"',
    'SystemColors.InactiveSelectionHighlightBrushKey',
    'ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"'
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
if ($mainWindowXaml -notmatch '(?s)<TreeView\s+x:Name="DocumentOutlineTree"\s+ItemContainerStyle="\{StaticResource NodalisTreeViewItemStyle\}"') {
    $failures.Add("Document outline must use the lightweight TreeViewItem base style, not Worktree containers.")
}
$appCode = Get-Content -Raw -LiteralPath $appCodePath
$recoverableErrorDialog = Get-Content -Raw -LiteralPath $recoverableErrorDialogPath
$documentTabsCode = Get-Content -Raw -LiteralPath $documentTabsCodePath
$diagnosticsService = Get-Content -Raw -LiteralPath $diagnosticsServicePath
$exceptionPolicy = Get-Content -Raw -LiteralPath $exceptionPolicyPath

if ($mainWindowXaml.Contains('<Run Text="{Binding OverdueDisplay}" />') -or
    $mainWindowXaml -match '\{Binding\s+OverdueDisplay\s*\}') {
    $failures.Add("Project dashboard OverdueDisplay must bind OneWay because TaskItem.OverdueDisplay is read-only.")
}

if (-not $mainWindowXaml.Contains('<Run Text="{Binding OverdueDisplay, Mode=OneWay}" />')) {
    $failures.Add("Project dashboard is missing the safe OneWay OverdueDisplay binding.")
}

$taskCreationXaml = Get-Content -Raw -LiteralPath $taskCreationXamlPath
$calendarXaml = Get-Content -Raw -LiteralPath $calendarXamlPath
$kanbanXaml = Get-Content -Raw -LiteralPath $kanbanXamlPath

if (-not $taskCreationXaml.Contains('x:Name="DueDatePicker"') -or
    $taskCreationXaml.Contains('x:Name="DueDateTextBox"')) {
    $failures.Add("Task creation must use the WPF DatePicker for the optional due date.")
}

if ($taskCreationXaml.Contains('IsEditable="True"')) {
    $failures.Add("Task creation priority/status ComboBoxes must expose their selected labels in the closed state.")
}

if (-not $calendarXaml.Contains('Text="{Binding Label, Mode=OneWay}"')) {
    $failures.Add("Workspace calendar filters must render user-facing labels instead of record ToString() values.")
}

if (-not $kanbanXaml.Contains('Text="{Binding Label, Mode=OneWay}"')) {
    $failures.Add("Workspace Kanban filters must render user-facing labels instead of record ToString() values.")
}

$milestoneEditorXamlPath = Join-Path $appRoot "Dialogs/MilestoneEditorDialog.xaml"
$milestoneListXamlPath = Join-Path $appRoot "Dialogs/MilestoneListDialog.xaml"
$milestoneEditorXaml = Get-Content -Raw -LiteralPath $milestoneEditorXamlPath
$milestoneListXaml = Get-Content -Raw -LiteralPath $milestoneListXamlPath

if (-not $milestoneEditorXaml.Contains('x:Name="TargetDatePicker"') -or
    -not $milestoneEditorXaml.Contains('SelectedDateFormat="Short"')) {
    $failures.Add("Milestone editing must keep a keyboard-editable themed DatePicker.")
}

if (-not $milestoneListXaml.Contains('<TabItem Header="Liste">') -or
    -not $milestoneListXaml.Contains('<TabItem Header="Timeline">')) {
    $failures.Add("Milestone List/Timeline tabs are missing.")
}

if (-not $mainWindowCode.Contains('Math.Clamp(') -or
    -not $mainWindowCode.Contains('availableLineCount - 1')) {
    $failures.Add("Source navigation must clamp editor line indices before ScrollToLine.")
}

if (-not $mainWindowCode.Contains('GetMilestoneFileForContextAsync(')) {
    $failures.Add("The command-palette milestone workflow must resolve the canonical milestone document.")
}

$requiredAppExceptionTokens = @(
    'DispatcherUnhandledException += App_DispatcherUnhandledException;',
    'RecoverableExceptionPolicy.CanContinue(',
    'e.Handled =',
    'TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;',
    'e.SetObserved();'
)

foreach ($token in $requiredAppExceptionTokens) {
    if (-not $appCode.Contains($token)) {
        $failures.Add("App.xaml.cs is missing recoverable-exception boundary token: $token")
    }
}

$requiredMainWindowBoundaryTokens = @(
    'RunUiActionAsync(',
    'ReportRecoverableUiError(',
    'Navigation · ',
    'Palette · ',
    'Aperçu Markdown / Mermaid'
)

foreach ($token in $requiredMainWindowBoundaryTokens) {
    if (-not $mainWindowCode.Contains($token)) {
        $failures.Add("MainWindow.xaml.cs is missing recoverable UI boundary token: $token")
    }
}

$requiredDiagnosticsTokens = @(
    'ActiveLogPath',
    'LogRecoverableException(',
    'ErrorId='
)

foreach ($token in $requiredDiagnosticsTokens) {
    if (-not $diagnosticsService.Contains($token)) {
        $failures.Add("LocalDiagnosticsService is missing recoverable-error diagnostics token: $token")
    }
}

$requiredExceptionPolicyTokens = @(
    'OutOfMemoryException',
    'StackOverflowException',
    'AccessViolationException',
    'IndexOutOfRangeException',
    'NullReferenceException',
    'System.Windows.Markup.XamlParseException'
)

foreach ($token in $requiredExceptionPolicyTokens) {
    if (-not $exceptionPolicy.Contains($token)) {
        $failures.Add("RecoverableExceptionPolicy is missing required classification token: $token")
    }
}

if (-not $recoverableErrorDialog.Contains('Identifiant d''erreur') -or
    -not $recoverableErrorDialog.Contains('Copier les détails') -or
    -not $recoverableErrorDialog.Contains('Content="Diagnostics"')) {
    $failures.Add("RecoverableErrorDialog must expose context, error identifier, copy-details and diagnostics actions.")
}

if (-not $documentTabsCode.Contains('Autosave · ') -or
    -not $documentTabsCode.Contains('LogRecoverableException(')) {
    $failures.Add("Document autosave failures must be logged with an action context and error identifier.")
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

if (-not $appProject.Contains('<ApplicationIcon>Assets\Nodalis.ico</ApplicationIcon>')) {
    $failures.Add("Nodalis.App.csproj must embed the Nodalis application icon.")
}

if (-not $appProject.Contains('<Resource Include="Assets\Nodalis.ico" />')) {
    $failures.Add("Nodalis.App.csproj must expose the Nodalis icon as a WPF resource.")
}

if (-not (Test-Path -LiteralPath $appIconPath)) {
    $failures.Add("Nodalis.App/Assets/Nodalis.ico is missing.")
}

if (-not $mainWindowXaml.Contains('Icon="Assets/Nodalis.ico"') -or
    -not $mainWindowXaml.Contains('Source="Assets/Nodalis.ico"')) {
    $failures.Add("MainWindow must use the Nodalis logo for the window/taskbar icon and header branding.")
}

if (-not $theme.Contains('Property="Icon" Value="/Nodalis;component/Assets/Nodalis.ico"')) {
    $failures.Add("NodalisWindowStyle must propagate the Nodalis icon to secondary windows.")
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
