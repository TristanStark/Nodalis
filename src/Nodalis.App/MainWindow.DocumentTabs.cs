using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.App.Editor;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Core.Settings;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.App;

public partial class MainWindow
{
    private readonly ObservableCollection<DocumentTabViewModel> _documentTabs = [];
    private readonly Dictionary<DocumentAutosaveController, DocumentTabViewModel> _autosaveTabs = [];
    private DocumentTabViewModel? _activeDocumentTab;
    private bool _suppressDocumentTabSelection;
    private Point _documentTabDragStart;
    private DocumentTabViewModel? _draggedDocumentTab;

    /// <summary>
    /// Initializes the document tab strip after the window controls are created.
    /// </summary>
    private void InitializeDocumentTabs()
    {
        DocumentTabsList.ItemsSource = _documentTabs;
        UpdateDocumentTabsVisibility();
    }

    /// <summary>
    /// Opens a document in a reusable tab or activates the existing tab for it.
    /// </summary>
    /// <param name="node">The document navigation node to open.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task OpenOrActivateDocumentTabAsync(
            NavigationNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);

        DocumentTabViewModel? tab = _documentTabs.FirstOrDefault(candidate =>
            candidate.DocumentId == node.Id ||
            string.Equals(
                candidate.FullPath,
                node.FullPath,
                StringComparison.OrdinalIgnoreCase));

        if (tab is null)
        {
            tab = await CreateDocumentTabAsync(node);
        }

        await ActivateDocumentTabForPaneAsync(
            tab,
            _focusedEditorPane,
            synchronizeNavigation: true);
    }

    /// <summary>
    /// Creates an independent document session and autosave controller for one tab.
    /// </summary>
    /// <param name="node">The document navigation node.</param>
    /// <returns>The created tab.</returns>
    private async Task<DocumentTabViewModel> CreateDocumentTabAsync(
            NavigationNodeViewModel node)
    {
        DocumentTabViewModel? existing =
            _documentTabs.FirstOrDefault(candidate =>
                candidate.DocumentId == node.Id ||
                string.Equals(
                    candidate.FullPath,
                    node.FullPath,
                    StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            return existing;
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            node.FullPath);

        DocumentAutosaveController autosave = CreateDocumentAutosaveController(
            session);

        DocumentTabViewModel tab = new DocumentTabViewModel(
            node.Id,
            node.DisplayName,
            node.FullPath,
            session,
            autosave);

        RegisterDocumentTabAutosave(
            tab,
            autosave);

        _documentTabs.Add(tab);
        UpdateDocumentTabsVisibility();
        return tab;
    }

    /// <summary>
    /// Creates an autosave controller using the current editor preference.
    /// </summary>
    /// <param name="session">The document session to save.</param>
    /// <returns>The configured autosave controller.</returns>
    private DocumentAutosaveController CreateDocumentAutosaveController(
            TextDocumentSession session) =>
            new(
                session,
                TimeSpan.FromMilliseconds(
                    _preferences.Editor.AutosaveDelayMilliseconds));

    /// <summary>
    /// Associates an autosave controller with its owning tab and subscribes shared handlers.
    /// </summary>
    /// <param name="tab">The owning tab.</param>
    /// <param name="autosave">The controller to register.</param>
    private void RegisterDocumentTabAutosave(
            DocumentTabViewModel tab,
            DocumentAutosaveController autosave)
    {
        _autosaveTabs[autosave] = tab;
        autosave.Saved += Autosave_Saved;
        autosave.ConflictDetected += Autosave_ConflictDetected;
        autosave.SaveFailed += Autosave_SaveFailed;
    }

    /// <summary>
    /// Removes shared event subscriptions for an autosave controller.
    /// </summary>
    /// <param name="autosave">The controller to unregister.</param>
    private void UnregisterDocumentTabAutosave(
            DocumentAutosaveController autosave)
    {
        autosave.Saved -= Autosave_Saved;
        autosave.ConflictDetected -= Autosave_ConflictDetected;
        autosave.SaveFailed -= Autosave_SaveFailed;
        _autosaveTabs.Remove(autosave);
    }

    /// <summary>
    /// Activates a tab and restores its in-memory text, caret and selection.
    /// </summary>
    /// <param name="tab">The tab to activate.</param>
    /// <param name="synchronizeNavigation">Whether to select the matching navigation node.</param>
    /// <returns>A task representing contextual refresh work.</returns>
    private async Task ActivateDocumentTabAsync(
            DocumentTabViewModel tab,
            bool synchronizeNavigation)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (!ReferenceEquals(
                _activeDocumentTab,
                tab))
        {
            StoreActiveDocumentTabState();
        }

        tab.IsMissing = !File.Exists(
            tab.FullPath);

        _activeDocumentTab = tab;
        _documentSession = tab.Session;
        _autosave = tab.Autosave;
        _documentDirty = tab.IsDirty;
        _conflictWarningShown = tab.ConflictWarningShown;

        _suppressDocumentTabSelection = true;
        DocumentTabsList.SelectedItem = tab;
        _suppressDocumentTabSelection = false;

        NavigationNodeViewModel? node = FindDocumentNavigationNode(tab);

        if (node is not null)
        {
            tab.UpdateNavigationIdentity(
                node.DisplayName,
                node.FullPath);

            _selectedNode = node;

            if (synchronizeNavigation)
            {
                _restoringSelection = true;
                node.IsSelected = true;
                _restoringSelection = false;
            }
        }

        _suppressEditorChanges = true;
        MarkdownEditorTextBox.Text = tab.Content;

        int selectionStart = Math.Clamp(
            tab.SelectionStart,
            0,
            MarkdownEditorTextBox.Text.Length);

        int selectionLength = Math.Clamp(
            tab.SelectionLength,
            0,
            MarkdownEditorTextBox.Text.Length - selectionStart);

        MarkdownEditorTextBox.Select(
            selectionStart,
            selectionLength);

        MarkdownEditorTextBox.CaretIndex = Math.Clamp(
            tab.CaretIndex,
            0,
            MarkdownEditorTextBox.Text.Length);

        MarkdownEditorTextBox.IsReadOnly =
            tab.IsMissing;

        _suppressEditorChanges = false;

        DashboardHost.Visibility = Visibility.Collapsed;
        NodeSummaryHost.Visibility = Visibility.Collapsed;
        EditorToolbar.Visibility = Visibility.Visible;
        DocumentEditorHost.Visibility = Visibility.Visible;

        DocumentTitleText.Text = tab.DisplayName;
        DocumentPathText.Text = Path.GetRelativePath(
            _root.FullPath,
            tab.FullPath);

        SaveStateText.Text =
            tab.IsMissing
                ? "Fichier introuvable"
                : tab.HasConflict
                    ? "Conflit externe"
                    : tab.IsDirty
                        ? "Modification…"
                        : "Enregistré";

        StatusText.Text =
            tab.IsMissing
                ? $"Document introuvable ou déplacé hors Nodalis · {DocumentPathText.Text}"
                : $"Document · {DocumentPathText.Text}";

        RefreshDocumentPropertiesContext(
            tab.Content);

        if (tab.IsMissing)
        {
            InternalLinkPopup.IsOpen = false;
            RenderPreview();
            MarkdownEditorTextBox.Focus();
            return;
        }

        if (_linkIndex.Targets.Count == 0)
        {
            await RefreshLinkIndexAsync();
        }

        UpdateLinkContext(
            tab.FullPath);

        await TrackRecentDocumentTabAsync(
            node);

        await RefreshGlossaryContextAsync(
            tab.FullPath);

        UpdateGlossaryAnnotations();
        RenderPreview();

        if (tab.HasConflict &&
            !tab.ConflictWarningShown)
        {
            tab.ConflictWarningShown = true;
            _conflictWarningShown = true;

            MessageBox.Show(
                this,
                "Ce document possède un conflit de modification externe non résolu. " +
                "Nodalis conserve votre contenu local sans écraser le fichier.",
                "Conflit de modification",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        MarkdownEditorTextBox.Focus();
    }

    /// <summary>
    /// Tracks a document activation when a current navigation node is available.
    /// </summary>
    /// <param name="node">The current document node, if still present.</param>
    /// <returns>A task representing recent-item persistence.</returns>
    private async Task TrackRecentDocumentTabAsync(
            NavigationNodeViewModel? node)
    {
        if (node is not null)
        {
            await TrackRecentContextAsync(node);
        }
    }

    /// <summary>
    /// Copies the active editor state back into its tab before another view is shown.
    /// </summary>
    private void StoreActiveDocumentTabState()
    {
        if (_activeDocumentTab is null)
        {
            return;
        }

        _activeDocumentTab.Content =
            MarkdownEditorTextBox.Text;

        _activeDocumentTab.CaretIndex =
            MarkdownEditorTextBox.CaretIndex;

        _activeDocumentTab.SelectionStart =
            MarkdownEditorTextBox.SelectionStart;

        _activeDocumentTab.SelectionLength =
            MarkdownEditorTextBox.SelectionLength;

        _activeDocumentTab.IsDirty =
            _documentDirty;

        _activeDocumentTab.ConflictWarningShown =
            _conflictWarningShown;
    }

    /// <summary>
    /// Detaches the editor view from the active tab without closing its session.
    /// </summary>
    private void DeactivateDocumentTabView()
    {
        StoreActiveDocumentTabState();
        StoreSecondaryDocumentTabState();

        _activeDocumentTab = null;
        _documentSession = null;
        _autosave = null;
        _documentDirty = false;
        _conflictWarningShown = false;

        _suppressDocumentTabSelection = true;
        DocumentTabsList.SelectedItem = null;
        _suppressDocumentTabSelection = false;

        MarkdownEditorTextBox.IsReadOnly = false;
        InternalLinkPopup.IsOpen = false;
        _previewTimer.Stop();
    }

    /// <summary>
    /// Finds the current navigation node for a tab by stable id first and path second.
    /// </summary>
    /// <param name="tab">The tab to resolve.</param>
    /// <returns>The matching navigation node, or null when the document disappeared.</returns>
    private NavigationNodeViewModel? FindDocumentNavigationNode(
            DocumentTabViewModel tab) =>
            _root
                .DescendantsAndSelf()
                .FirstOrDefault(node =>
                    node.Kind == WorkspaceNodeKind.Document &&
                    (node.Id == tab.DocumentId ||
                     string.Equals(
                         node.FullPath,
                         tab.FullPath,
                         StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Handles selecting an already-open document tab.
    /// </summary>
    /// <param name="sender">The tab list.</param>
    /// <param name="e">The selection change arguments.</param>
    private async void DocumentTabsList_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (_suppressDocumentTabSelection ||
            DocumentTabsList.SelectedItem is not DocumentTabViewModel tab)
        {
            return;
        }

        await RunUiActionAsync(
            "Ouvrir un onglet document",
            () =>
                ActivateDocumentTabForPaneAsync(
                    tab,
                    EditorPaneSlot.Primary,
                    synchronizeNavigation: true));
    }

    /// <summary>
    /// Closes the tab represented by a tab-strip close button.
    /// </summary>
    /// <param name="sender">The close button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void CloseDocumentTab_Click(
            object sender,
            RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button button &&
            button.Tag is DocumentTabViewModel tab)
        {
            await RunUiActionAsync(
                "Fermer un onglet document",
                async () =>
                {
                    await CloseDocumentTabAsync(
                        tab,
                        "fermer cet onglet",
                        activateNeighbor: true);
                });
        }
    }

    /// <summary>
    /// Closes one document tab after flushing and protecting unsaved content.
    /// </summary>
    /// <param name="tab">The tab to close.</param>
    /// <param name="actionDescription">The action described in a warning prompt.</param>
    /// <param name="activateNeighbor">Whether to activate a neighboring tab afterward.</param>
    /// <returns>True when the tab was closed.</returns>
    private async Task<bool> CloseDocumentTabAsync(
            DocumentTabViewModel tab,
            string actionDescription,
            bool activateNeighbor)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (ReferenceEquals(
                _activeDocumentTab,
                tab))
        {
            StoreActiveDocumentTabState();
        }

        try
        {
            await tab.Autosave.FlushAsync();
        }
        catch (Exception exception) when (
            RecoverableExceptionPolicy.CanContinue(
                exception))
        {
            tab.IsDirty = true;
            tab.IsMissing = !File.Exists(
                tab.FullPath);

            ReportRecoverableUiError(
                "Enregistrement avant fermeture d'onglet",
                exception,
                showDialog: false);
        }

        tab.IsDirty =
            !string.Equals(
                tab.Content,
                tab.Session.Content,
                StringComparison.Ordinal);

        if (tab.IsDirty)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                "Les dernières modifications de cet onglet n'ont pas pu être enregistrées.\n\n" +
                $"Voulez-vous quand même {actionDescription} et abandonner les modifications locales ?",
                "Modifications non enregistrées",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        int oldIndex = _documentTabs.IndexOf(tab);
        bool wasActive = ReferenceEquals(
            _activeDocumentTab,
            tab);
        bool wasSecondary = ReferenceEquals(
            _secondaryDocumentTab,
            tab);

        if (wasSecondary)
        {
            StoreSecondaryDocumentTabState();
            DetachSecondaryDocumentTabView();
        }

        UnregisterDocumentTabAutosave(
            tab.Autosave);

        await tab.Autosave.DisposeAsync();

        _documentTabs.Remove(tab);
        UpdateDocumentTabsVisibility();

        if (!wasActive)
        {
            return true;
        }

        _activeDocumentTab = null;
        _documentSession = null;
        _autosave = null;
        _documentDirty = false;
        _conflictWarningShown = false;

        if (activateNeighbor &&
            _documentTabs.Count > 0)
        {
            DocumentTabViewModel? nextTab =
                FindPrimaryReplacementAfterClose(
                    oldIndex);

            if (nextTab is not null)
            {
                await ActivateDocumentTabAsync(
                    nextTab,
                    synchronizeNavigation: true);
            }
            else if (_secondaryDocumentTab is not null)
            {
                DocumentTabViewModel promoted =
                    _secondaryDocumentTab;

                StoreSecondaryDocumentTabState();
                DetachSecondaryDocumentTabView();

                await ActivateDocumentTabAsync(
                    promoted,
                    synchronizeNavigation: true);
            }
        }
        else
        {
            _suppressDocumentTabSelection = true;
            DocumentTabsList.SelectedItem = null;
            _suppressDocumentTabSelection = false;
        }

        if (_documentTabs.Count == 0 &&
            activateNeighbor)
        {
            _selectedNode = _root;
            _root.IsSelected = true;
            ShowDashboard();
        }

        return true;
    }

    /// <summary>
    /// Closes the active tab when legacy document-closing workflows require it.
    /// </summary>
    /// <param name="actionDescription">The action described in a warning prompt.</param>
    /// <returns>True when there is no active tab or it was closed.</returns>
    private async Task<bool> TryCloseActiveDocumentTabAsync(
            string actionDescription)
    {
        if (_activeDocumentTab is null)
        {
            return true;
        }

        return await CloseDocumentTabAsync(
            _activeDocumentTab,
            actionDescription,
            activateNeighbor: false);
    }

    /// <summary>
    /// Flushes every open tab before backup or other workspace-wide persistence.
    /// </summary>
    /// <returns>A task representing the flush operation.</returns>
    private async Task FlushAllDocumentTabsAsync()
    {
        StoreActiveDocumentTabState();

        foreach (DocumentTabViewModel tab in _documentTabs)
        {
            await tab.Autosave.FlushAsync();

            tab.IsDirty =
                !string.Equals(
                    tab.Content,
                    tab.Session.Content,
                    StringComparison.Ordinal);
        }

        SyncActiveDocumentDirtyState();
    }

    /// <summary>
    /// Flushes every tab and asks before shutdown if any content still cannot be saved.
    /// </summary>
    /// <returns>True when shutdown may continue.</returns>
    private async Task<bool> TryFlushAllDocumentTabsForShutdownAsync()
    {
        StoreActiveDocumentTabState();

        foreach (DocumentTabViewModel tab in _documentTabs)
        {
            try
            {
                await tab.Autosave.FlushAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
            {
                tab.IsDirty = true;
                tab.IsMissing = !File.Exists(
                    tab.FullPath);
            }

            tab.IsDirty =
                !string.Equals(
                    tab.Content,
                    tab.Session.Content,
                    StringComparison.Ordinal);

            if (!tab.IsDirty)
            {
                continue;
            }

            MessageBoxResult answer = MessageBox.Show(
                this,
                $"« {tab.DisplayName} » contient des modifications qui n'ont pas pu être enregistrées.\n\n" +
                "Fermer Nodalis et abandonner ces modifications locales ?",
                "Modifications non enregistrées",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Disposes every independent document autosave controller during application shutdown.
    /// </summary>
    /// <returns>A task representing disposal.</returns>
    private async Task DisposeAllDocumentTabsAsync()
    {
        StoreSecondaryDocumentTabState();
        DetachSecondaryDocumentTabView();

        foreach (DocumentTabViewModel tab in _documentTabs.ToArray())
        {
            UnregisterDocumentTabAutosave(
                tab.Autosave);

            await tab.Autosave.DisposeAsync();
        }

        _documentTabs.Clear();
        _activeDocumentTab = null;
        _documentSession = null;
        _autosave = null;
        UpdateDocumentTabsVisibility();
    }

    /// <summary>
    /// Builds the persisted open-tab snapshot for the current workspace.
    /// </summary>
    /// <returns>The ordered list of tab references.</returns>
    private List<OpenDocumentTabReference> BuildOpenDocumentTabReferences()
    {
        StoreActiveDocumentTabState();
        StoreSecondaryDocumentTabState();

        return _documentTabs
            .Select(tab =>
                new OpenDocumentTabReference
                {
                    DocumentId = tab.DocumentId,
                    RelativePath = Path.GetRelativePath(
                            _root.FullPath,
                            tab.FullPath)
                        .Replace(
                            Path.DirectorySeparatorChar,
                            '/'),
                    CaretIndex = Math.Max(
                        0,
                        tab.CaretIndex),
                    SelectionStart = Math.Max(
                        0,
                        tab.SelectionStart),
                    SelectionLength = Math.Max(
                        0,
                        tab.SelectionLength)
                })
            .ToList();
    }

    /// <summary>
    /// Restores the previous open-tab session for the current workspace.
    /// </summary>
    /// <returns>A task representing restoration.</returns>
    private async Task RestoreDocumentTabsAsync()
    {
        if (_preferences.OpenDocumentTabs.Count == 0)
        {
            return;
        }

        int missingCount = 0;
        DocumentTabViewModel? preferredTab = null;

        foreach (OpenDocumentTabReference reference in _preferences.OpenDocumentTabs)
        {
            NavigationNodeViewModel? node = ResolvePersistedDocumentNode(
                reference);

            if (node is null ||
                !File.Exists(
                    node.FullPath))
            {
                missingCount++;
                continue;
            }

            try
            {
                DocumentTabViewModel tab = await CreateDocumentTabAsync(
                    node);

                tab.CaretIndex =
                    reference.CaretIndex;
                tab.SelectionStart =
                    reference.SelectionStart;
                tab.SelectionLength =
                    reference.SelectionLength;

                if (_preferences.ActiveDocumentTabId == tab.DocumentId)
                {
                    preferredTab = tab;
                }
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                DecoderFallbackException)
            {
                missingCount++;
            }
        }

        if (preferredTab is not null)
        {
            await ActivateDocumentTabAsync(
                preferredTab,
                synchronizeNavigation: true);
        }

        if (missingCount > 0)
        {
            StatusText.Text =
                $"{missingCount} onglet(s) de la session précédente n'ont pas pu être restaurés car le fichier est absent ou illisible.";
        }
    }

    /// <summary>
    /// Resolves a persisted tab reference against the current navigation tree.
    /// </summary>
    /// <param name="reference">The persisted tab reference.</param>
    /// <returns>The matching document node, or null.</returns>
    private NavigationNodeViewModel? ResolvePersistedDocumentNode(
            OpenDocumentTabReference reference)
    {
        NavigationNodeViewModel? byId = reference.DocumentId == Guid.Empty
            ? null
            : _root
                .DescendantsAndSelf()
                .FirstOrDefault(node =>
                    node.Kind == WorkspaceNodeKind.Document &&
                    node.Id == reference.DocumentId);

        if (byId is not null)
        {
            return byId;
        }

        string relativePath = reference.RelativePath
            .Replace(
                '/',
                Path.DirectorySeparatorChar);

        string fullPath = Path.GetFullPath(
            Path.Combine(
                _root.FullPath,
                relativePath));

        string relativeCheck = Path.GetRelativePath(
            _root.FullPath,
            fullPath);

        if (relativeCheck.Equals(
                "..",
                StringComparison.Ordinal) ||
            relativeCheck.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return null;
        }

        NavigationNodeViewModel? byPath =
            _root
                .DescendantsAndSelf()
                .FirstOrDefault(node =>
                    node.Kind == WorkspaceNodeKind.Document &&
                    string.Equals(
                        Path.GetFullPath(
                            node.FullPath),
                        fullPath,
                        StringComparison.OrdinalIgnoreCase));

        if (byPath is not null)
        {
            return byPath;
        }

        return ResolveMigratedSingletonDocumentNode(
            fullPath);
    }

    /// <summary>
    /// Resolves an obsolete project singleton path such as Jalons/Jalons.md or
    /// Glossaire/Glossaire.md to its canonical project-root document.
    /// </summary>
    /// <param name="legacyPath">The possibly obsolete document path.</param>
    /// <returns>The canonical navigation node, or <see langword="null"/> when no migration applies.</returns>
    private NavigationNodeViewModel? ResolveMigratedSingletonDocumentNode(
            string legacyPath)
    {
        string? migratedPath =
            TryResolveMigratedSingletonDocumentPath(
                legacyPath);

        if (migratedPath is null)
        {
            return null;
        }

        return _root
            .DescendantsAndSelf()
            .FirstOrDefault(node =>
                node.Kind ==
                    WorkspaceNodeKind.Document &&
                string.Equals(
                    Path.GetFullPath(
                        node.FullPath),
                    migratedPath,
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Maps the legacy folder-backed singleton roles to the 1.0 project-root files.
    /// </summary>
    /// <param name="legacyPath">The legacy file path.</param>
    /// <returns>The canonical existing path, or <see langword="null"/>.</returns>
    private static string? TryResolveMigratedSingletonDocumentPath(
            string legacyPath)
    {
        string fullPath =
            Path.GetFullPath(
                legacyPath);

        if (File.Exists(
                fullPath))
        {
            return null;
        }

        string? legacyDirectory =
            Path.GetDirectoryName(
                fullPath);

        if (string.IsNullOrWhiteSpace(
                legacyDirectory))
        {
            return null;
        }

        string roleName =
            Path.GetFileName(
                legacyDirectory);
        string fileNameWithoutExtension =
            Path.GetFileNameWithoutExtension(
                fullPath);

        bool singletonRole =
            string.Equals(
                roleName,
                "Jalons",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                roleName,
                "Glossaire",
                StringComparison.OrdinalIgnoreCase);

        if (!singletonRole ||
            !string.Equals(
                roleName,
                fileNameWithoutExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? projectDirectory =
            Directory.GetParent(
                legacyDirectory)?.FullName;

        if (string.IsNullOrWhiteSpace(
                projectDirectory))
        {
            return null;
        }

        string canonicalPath =
            Path.GetFullPath(
                Path.Combine(
                    projectDirectory,
                    roleName + ".md"));

        return File.Exists(
                canonicalPath)
            ? canonicalPath
            : null;
    }

    /// <summary>
    /// Recreates each tab autosave controller when the configured delay changes.
    /// </summary>
    /// <param name="delayMilliseconds">The new autosave delay.</param>
    /// <returns>A task representing the controller replacement.</returns>
    private async Task RecreateDocumentTabAutosavesAsync(
            int delayMilliseconds)
    {
        StoreActiveDocumentTabState();

        foreach (DocumentTabViewModel tab in _documentTabs)
        {
            DocumentAutosaveController oldAutosave =
                tab.Autosave;

            await oldAutosave.FlushAsync();

            UnregisterDocumentTabAutosave(
                oldAutosave);

            await oldAutosave.DisposeAsync();

            DocumentAutosaveController newAutosave =
                new DocumentAutosaveController(
                    tab.Session,
                    TimeSpan.FromMilliseconds(
                        delayMilliseconds));

            tab.Autosave = newAutosave;

            RegisterDocumentTabAutosave(
                tab,
                newAutosave);
        }

        if (_activeDocumentTab is not null)
        {
            _autosave =
                _activeDocumentTab.Autosave;
        }
    }

    /// <summary>
    /// Refreshes one tab after its autosave controller reports a successful save.
    /// </summary>
    /// <param name="sender">The autosave controller that raised the event.</param>
    /// <returns>A task representing contextual refresh work.</returns>
    private async Task HandleDocumentTabAutosaveSavedAsync(
            object? sender)
    {
        if (sender is not DocumentAutosaveController autosave ||
            !_autosaveTabs.TryGetValue(
                autosave,
                out DocumentTabViewModel? tab))
        {
            return;
        }

        tab.IsDirty =
            !string.Equals(
                tab.Content,
                tab.Session.Content,
                StringComparison.Ordinal);

        tab.HasConflict = false;

        if (ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            UpdateSecondarySaveState(
                tab);
        }

        if (!ReferenceEquals(
                tab,
                _activeDocumentTab))
        {
            if (!tab.IsDirty)
            {
                await RefreshLinkIndexAsync();
            }

            return;
        }

        _documentDirty =
            tab.IsDirty;

        SaveStateText.Text =
            tab.IsDirty
                ? "Modification…"
                : "Enregistré";

        if (tab.IsDirty)
        {
            return;
        }

        StatusText.Text =
            "Enregistré localement";

        try
        {
            await RefreshLinkIndexAndContextAsync();
            await RefreshGlossaryContextAsync(
                tab.FullPath);
            UpdateGlossaryAnnotations();
            await RefreshDashboardTasksAsync();
            await RefreshDashboardMilestonesAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            StatusText.Text =
                $"Enregistré · index liens indisponible : {exception.Message}";
        }
    }

    /// <summary>
    /// Marks the owning tab when autosave detects an external modification conflict.
    /// </summary>
    /// <param name="sender">The autosave controller that raised the event.</param>
    private void HandleDocumentTabAutosaveConflict(
            object? sender)
    {
        if (sender is not DocumentAutosaveController autosave ||
            !_autosaveTabs.TryGetValue(
                autosave,
                out DocumentTabViewModel? tab))
        {
            return;
        }

        tab.IsDirty = true;
        tab.HasConflict = true;

        if (ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            UpdateSecondarySaveState(
                tab);
        }

        if (!ReferenceEquals(
                tab,
                _activeDocumentTab))
        {
            return;
        }

        _documentDirty = true;
        SaveStateText.Text =
            "Conflit externe";

        StatusText.Text =
            "Le fichier a été modifié en dehors de Nodalis.";

        if (tab.ConflictWarningShown)
        {
            return;
        }

        tab.ConflictWarningShown = true;
        _conflictWarningShown = true;

        MessageBox.Show(
            this,
            "Ce fichier a été modifié par un autre programme depuis son ouverture. " +
            "Nodalis n'écrasera pas cette version automatiquement.\n\n" +
            "Vos modifications restent conservées dans leur onglet.",
            "Conflit de modification",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Marks the owning tab when an autosave attempt fails.
    /// </summary>
    /// <param name="sender">The autosave controller that raised the event.</param>
    /// <param name="exception">The save failure.</param>
    private void HandleDocumentTabAutosaveFailure(
            object? sender,
            Exception exception)
    {
        if (sender is not DocumentAutosaveController autosave ||
            !_autosaveTabs.TryGetValue(
                autosave,
                out DocumentTabViewModel? tab))
        {
            ReportRecoverableUiError(
                "Autosave document",
                exception,
                showDialog: false);
            return;
        }

        string errorId =
            _diagnosticsService.LogRecoverableException(
                "Autosave · " +
                tab.TabTitle,
                exception);

        tab.IsDirty = true;
        tab.IsMissing = !File.Exists(
            tab.FullPath);

        if (ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            SecondaryMarkdownEditorTextBox.IsReadOnly =
                tab.IsMissing;

            UpdateSecondarySaveState(
                tab);
        }

        if (!ReferenceEquals(
                tab,
                _activeDocumentTab))
        {
            return;
        }

        _documentDirty = true;
        MarkdownEditorTextBox.IsReadOnly =
            tab.IsMissing;

        SaveStateText.Text =
            tab.IsMissing
                ? "Fichier introuvable"
                : "Erreur d'enregistrement";

        StatusText.Text =
            "Autosave en échec · " +
            errorId +
            " · " +
            RecoverableExceptionPolicy.GetUserMessage(
                exception);
    }

    /// <summary>
    /// Synchronizes legacy active-document fields after an explicit flush.
    /// </summary>
    private void SyncActiveDocumentDirtyState()
    {
        if (_activeDocumentTab is null)
        {
            _documentDirty = false;
            return;
        }

        _activeDocumentTab.IsDirty =
            !string.Equals(
                _activeDocumentTab.Content,
                _activeDocumentTab.Session.Content,
                StringComparison.Ordinal);

        _documentDirty =
            _activeDocumentTab.IsDirty;
    }

    /// <summary>
    /// Selects the next or previous document tab using keyboard navigation.
    /// </summary>
    /// <param name="delta">Positive for next, negative for previous.</param>
    /// <returns>A task representing tab activation.</returns>
    private async Task CycleDocumentTabAsync(
            int delta)
    {
        DocumentTabViewModel[] candidates =
            _documentTabs
                .Where(tab =>
                    !ReferenceEquals(
                        tab,
                        _secondaryDocumentTab))
                .ToArray();

        if (candidates.Length == 0)
        {
            return;
        }

        int currentIndex = _activeDocumentTab is null
            ? 0
            : Array.IndexOf(
                candidates,
                _activeDocumentTab);

        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int nextIndex =
            (currentIndex + delta) %
            candidates.Length;

        if (nextIndex < 0)
        {
            nextIndex +=
                candidates.Length;
        }

        await ActivateDocumentTabAsync(
            candidates[nextIndex],
            synchronizeNavigation: true);
    }

    /// <summary>
    /// Records the drag origin for tab reordering.
    /// </summary>
    /// <param name="sender">The tab list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void DocumentTabsList_PreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        _documentTabDragStart =
            e.GetPosition(
                DocumentTabsList);

        ListBoxItem? item = FindVisualParent<ListBoxItem>(
            e.OriginalSource as DependencyObject);

        _draggedDocumentTab =
            item?.DataContext as
                DocumentTabViewModel;
    }

    /// <summary>
    /// Starts a tab reorder drag after the normal drag threshold is crossed.
    /// </summary>
    /// <param name="sender">The tab list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void DocumentTabsList_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            _draggedDocumentTab is null)
        {
            return;
        }

        Point current =
            e.GetPosition(
                DocumentTabsList);

        if (Math.Abs(
                current.X - _documentTabDragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(
                current.Y - _documentTabDragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            DocumentTabsList,
            _draggedDocumentTab,
            DragDropEffects.Move);

        _draggedDocumentTab = null;
    }

    /// <summary>
    /// Reorders a dragged tab at the drop target position.
    /// </summary>
    /// <param name="sender">The tab list.</param>
    /// <param name="e">The drag event arguments.</param>
    private void DocumentTabsList_Drop(
            object sender,
            DragEventArgs e)
    {
        if (e.Data.GetData(
                typeof(DocumentTabViewModel)) is not
            DocumentTabViewModel source)
        {
            return;
        }

        ListBoxItem? item = FindVisualParent<ListBoxItem>(
            e.OriginalSource as DependencyObject);

        if (item?.DataContext is not
            DocumentTabViewModel target ||
            ReferenceEquals(
                source,
                target))
        {
            return;
        }

        int sourceIndex =
            _documentTabs.IndexOf(source);

        int targetIndex =
            _documentTabs.IndexOf(target);

        if (sourceIndex < 0 ||
            targetIndex < 0)
        {
            return;
        }

        _documentTabs.Move(
            sourceIndex,
            targetIndex);
    }

    /// <summary>
    /// Selects the tab under the pointer before opening its context menu.
    /// </summary>
    /// <param name="sender">The tab list.</param>
    /// <param name="e">The mouse event arguments.</param>
    private void DocumentTabsList_PreviewMouseRightButtonDown(
            object sender,
            MouseButtonEventArgs e)
    {
        ListBoxItem? item = FindVisualParent<ListBoxItem>(
            e.OriginalSource as DependencyObject);

        if (item?.DataContext is
            DocumentTabViewModel tab)
        {
            _suppressDocumentTabSelection = true;
            DocumentTabsList.SelectedItem = tab;
            _suppressDocumentTabSelection = false;
        }
    }

    /// <summary>
    /// Builds tab-specific close actions for the tab-strip context menu.
    /// </summary>
    /// <param name="sender">The tab list.</param>
    /// <param name="e">The context menu event arguments.</param>
    private void DocumentTabsList_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e)
    {
        if (DocumentTabsList.SelectedItem is not
            DocumentTabViewModel tab)
        {
            e.Handled = true;
            return;
        }

        ContextMenu menu =
            new ContextMenu();

        MenuItem close =
            new MenuItem
            {
                Header = "Fermer"
            };

        close.Click += async (_, _) =>
            await CloseDocumentTabAsync(
                tab,
                "fermer cet onglet",
                activateNeighbor: true);

        menu.Items.Add(close);

        MenuItem closeOthers =
            new MenuItem
            {
                Header = "Fermer les autres",
                IsEnabled = _documentTabs.Count > 1
            };

        closeOthers.Click += async (_, _) =>
            await CloseOtherDocumentTabsAsync(
                tab);

        menu.Items.Add(closeOthers);

        int tabIndex =
            _documentTabs.IndexOf(tab);

        MenuItem closeRight =
            new MenuItem
            {
                Header = "Fermer les onglets à droite",
                IsEnabled = tabIndex >= 0 &&
                    tabIndex <
                    _documentTabs.Count - 1
            };

        closeRight.Click += async (_, _) =>
            await CloseDocumentTabsToRightAsync(
                tab);

        menu.Items.Add(closeRight);
        DocumentTabsList.ContextMenu = menu;
    }

    /// <summary>
    /// Closes every tab except the requested survivor.
    /// </summary>
    /// <param name="survivor">The tab to keep.</param>
    /// <returns>A task representing close operations.</returns>
    private async Task CloseOtherDocumentTabsAsync(
            DocumentTabViewModel survivor)
    {
        foreach (DocumentTabViewModel tab in _documentTabs
                     .Where(candidate =>
                         !ReferenceEquals(
                             candidate,
                             survivor))
                     .ToArray())
        {
            if (!await CloseDocumentTabAsync(
                    tab,
                    "fermer cet onglet",
                    activateNeighbor: false))
            {
                return;
            }
        }

        await ActivateDocumentTabAsync(
            survivor,
            synchronizeNavigation: true);
    }

    /// <summary>
    /// Closes every tab positioned to the right of the requested survivor.
    /// </summary>
    /// <param name="survivor">The reference tab.</param>
    /// <returns>A task representing close operations.</returns>
    private async Task CloseDocumentTabsToRightAsync(
            DocumentTabViewModel survivor)
    {
        int index =
            _documentTabs.IndexOf(survivor);

        if (index < 0)
        {
            return;
        }

        DocumentTabViewModel[] tabs = _documentTabs
            .Skip(index + 1)
            .ToArray();

        foreach (DocumentTabViewModel tab in tabs)
        {
            if (!await CloseDocumentTabAsync(
                    tab,
                    "fermer cet onglet",
                    activateNeighbor: false))
            {
                return;
            }
        }

        await ActivateDocumentTabAsync(
            survivor,
            synchronizeNavigation: true);
    }

    /// <summary>
    /// Updates the tab-strip visibility after opening or closing tabs.
    /// </summary>
    private void UpdateDocumentTabsVisibility()
    {
        DocumentTabsHost.Visibility =
            _documentTabs.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    /// <summary>
    /// Refreshes tab display metadata after rebuilding the workspace navigation tree.
    /// </summary>
    private void RefreshDocumentTabsFromNavigation()
    {
        foreach (DocumentTabViewModel tab in _documentTabs)
        {
            NavigationNodeViewModel? node =
                _root
                    .DescendantsAndSelf()
                    .FirstOrDefault(candidate =>
                        candidate.Kind == WorkspaceNodeKind.Document &&
                        candidate.Id == tab.DocumentId);

            if (node is null)
            {
                tab.IsMissing = true;
                continue;
            }

            if (string.Equals(
                    Path.GetFullPath(
                        tab.Session.Path),
                    Path.GetFullPath(
                        node.FullPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                tab.UpdateNavigationIdentity(
                    node.DisplayName,
                    node.FullPath);
                tab.IsMissing =
                    !File.Exists(
                        node.FullPath);
            }
            else
            {
                tab.IsMissing = true;
            }
        }
    }
}
