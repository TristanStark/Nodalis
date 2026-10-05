using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Nodalis.App.Editor;
using Nodalis.App.Markdown;
using Nodalis.App.Navigation;
using Nodalis.Core.Navigation;
using Nodalis.Core.Settings;

namespace Nodalis.App;

/// <summary>
/// Identifies the primary or secondary editing surface.
/// </summary>
internal enum EditorPaneSlot
{
    Primary,
    Secondary
}

public partial class MainWindow
{
    private const double MinimumEditorSplitRatio = 0.2;
    private const double MaximumEditorSplitRatio = 0.8;

    private EditorPaneSlot _focusedEditorPane = EditorPaneSlot.Primary;
    private EditorSplitMode _editorSplitMode = EditorSplitMode.None;
    private double _editorSplitRatio = 0.5;
    private DocumentTabViewModel? _secondaryDocumentTab;
    private DispatcherTimer? _secondaryPreviewTimer;
    private bool _suppressSecondaryEditorChanges;
    private bool _suppressSecondaryDocumentSelection;

    /// <summary>
    /// Initializes the second editor pane and its independent preview debounce.
    /// </summary>
    private void InitializeEditorSplit()
    {
        _editorSplitRatio = Math.Clamp(
            _preferences.Editor.SplitRatio,
            MinimumEditorSplitRatio,
            MaximumEditorSplitRatio);

        SecondaryDocumentPicker.ItemsSource =
            _documentTabs;

        SecondaryMarkdownEditorTextBox.FontSize =
            _preferences.Editor.FontSize;

        SecondaryMarkdownEditorTextBox.TextWrapping =
            _preferences.Editor.WordWrap
                ? TextWrapping.Wrap
                : TextWrapping.NoWrap;

        _secondaryPreviewTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.Background,
            SecondaryPreviewTimer_Tick,
            Dispatcher)
        {
            IsEnabled = false
        };

        ApplyEditorSplitMode(
            EditorSplitMode.None);
    }

    /// <summary>
    /// Restores the locally persisted split orientation and secondary document after tabs are reopened.
    /// </summary>
    /// <returns>A task representing secondary-pane restoration.</returns>
    private async Task RestoreEditorSplitAsync()
    {
        EditorSplitMode preferredMode =
            _preferences.Editor.SplitMode;

        if (preferredMode == EditorSplitMode.None)
        {
            return;
        }

        ApplyEditorSplitMode(
            preferredMode);

        Guid? preferredDocumentId =
            _preferences.Editor.SecondaryDocumentTabId;

        DocumentTabViewModel? secondary =
            preferredDocumentId.HasValue
                ? _documentTabs.FirstOrDefault(tab =>
                    tab.DocumentId ==
                    preferredDocumentId.Value)
                : null;

        if (secondary is null ||
            ReferenceEquals(
                secondary,
                _activeDocumentTab))
        {
            secondary = _documentTabs.FirstOrDefault(tab =>
                !ReferenceEquals(
                    tab,
                    _activeDocumentTab));
        }

        if (secondary is not null)
        {
            await ActivateSecondaryDocumentTabAsync(
                secondary,
                synchronizeNavigation: false,
                focusEditor: false);
        }
    }

    /// <summary>
    /// Handles the vertical split toolbar command.
    /// </summary>
    /// <param name="sender">The split button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void SplitVertical_Click(
            object sender,
            RoutedEventArgs e)
    {
        await EnableEditorSplitAsync(
            EditorSplitMode.Vertical);
    }

    /// <summary>
    /// Handles the horizontal split toolbar command.
    /// </summary>
    /// <param name="sender">The split button.</param>
    /// <param name="e">The routed event arguments.</param>
    private async void SplitHorizontal_Click(
            object sender,
            RoutedEventArgs e)
    {
        await EnableEditorSplitAsync(
            EditorSplitMode.Horizontal);
    }

    /// <summary>
    /// Enables or reorients the split editor and picks a distinct open tab for the second pane when possible.
    /// </summary>
    /// <param name="mode">The requested split orientation.</param>
    /// <returns>A task representing optional secondary-tab activation.</returns>
    private async Task EnableEditorSplitAsync(
            EditorSplitMode mode)
    {
        if (mode == EditorSplitMode.None)
        {
            return;
        }

        ApplyEditorSplitMode(
            mode);

        if (_secondaryDocumentTab is not null)
        {
            return;
        }

        DocumentTabViewModel? candidate =
            _documentTabs.FirstOrDefault(tab =>
                !ReferenceEquals(
                    tab,
                    _activeDocumentTab));

        if (candidate is not null)
        {
            await ActivateSecondaryDocumentTabAsync(
                candidate,
                synchronizeNavigation: false,
                focusEditor: false);
        }
        else
        {
            StatusText.Text =
                "Split actif · ouvrez un second document puis sélectionnez-le dans la pane 2.";
        }
    }

    /// <summary>
    /// Closes only the secondary pane while keeping every document tab and autosave session open.
    /// </summary>
    /// <param name="sender">The close-pane button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void CloseSecondaryPane_Click(
            object sender,
            RoutedEventArgs e)
    {
        StoreSecondaryDocumentTabState();
        DetachSecondaryDocumentTabView();
        ApplyEditorSplitMode(
            EditorSplitMode.None);

        SetFocusedEditorPane(
            EditorPaneSlot.Primary);

        MarkdownEditorTextBox.Focus();
    }

    /// <summary>
    /// Applies one split orientation without creating or duplicating document sessions.
    /// </summary>
    /// <param name="mode">The requested editor layout.</param>
    private void ApplyEditorSplitMode(
            EditorSplitMode mode)
    {
        if (_editorSplitMode != EditorSplitMode.None)
        {
            CaptureEditorSplitRatio();
        }

        _editorSplitMode = mode;

        Grid.SetRow(
            PrimaryEditorPane,
            0);
        Grid.SetColumn(
            PrimaryEditorPane,
            0);

        if (mode == EditorSplitMode.None)
        {
            PrimaryEditorRow.Height =
                new GridLength(
                    1,
                    GridUnitType.Star);

            SplitEditorHorizontalDividerRow.Height =
                new GridLength(0);

            SecondaryEditorRow.Height =
                new GridLength(0);

            PrimaryEditorColumn.Width =
                new GridLength(
                    1,
                    GridUnitType.Star);

            SplitEditorVerticalDividerColumn.Width =
                new GridLength(0);

            SecondaryEditorColumn.Width =
                new GridLength(0);

            SplitEditorDivider.Visibility =
                Visibility.Collapsed;

            SecondaryEditorPane.Visibility =
                Visibility.Collapsed;

            return;
        }

        SecondaryEditorPane.Visibility =
            Visibility.Visible;

        SplitEditorDivider.Visibility =
            Visibility.Visible;

        if (mode == EditorSplitMode.Vertical)
        {
            Grid.SetRow(
                SplitEditorDivider,
                0);
            Grid.SetColumn(
                SplitEditorDivider,
                1);

            Grid.SetRow(
                SecondaryEditorPane,
                0);
            Grid.SetColumn(
                SecondaryEditorPane,
                2);

            SplitEditorDivider.Width = 5;
            SplitEditorDivider.Height = double.NaN;
            SplitEditorDivider.HorizontalAlignment =
                HorizontalAlignment.Stretch;
            SplitEditorDivider.VerticalAlignment =
                VerticalAlignment.Stretch;
            SplitEditorDivider.ResizeDirection =
                GridResizeDirection.Columns;

            PrimaryEditorRow.Height =
                new GridLength(
                    1,
                    GridUnitType.Star);
            SplitEditorHorizontalDividerRow.Height =
                new GridLength(0);
            SecondaryEditorRow.Height =
                new GridLength(0);

            PrimaryEditorColumn.Width =
                new GridLength(
                    _editorSplitRatio,
                    GridUnitType.Star);
            SplitEditorVerticalDividerColumn.Width =
                new GridLength(5);
            SecondaryEditorColumn.Width =
                new GridLength(
                    1 - _editorSplitRatio,
                    GridUnitType.Star);
        }
        else
        {
            Grid.SetRow(
                SplitEditorDivider,
                1);
            Grid.SetColumn(
                SplitEditorDivider,
                0);

            Grid.SetRow(
                SecondaryEditorPane,
                2);
            Grid.SetColumn(
                SecondaryEditorPane,
                0);

            SplitEditorDivider.Width = double.NaN;
            SplitEditorDivider.Height = 5;
            SplitEditorDivider.HorizontalAlignment =
                HorizontalAlignment.Stretch;
            SplitEditorDivider.VerticalAlignment =
                VerticalAlignment.Stretch;
            SplitEditorDivider.ResizeDirection =
                GridResizeDirection.Rows;

            PrimaryEditorColumn.Width =
                new GridLength(
                    1,
                    GridUnitType.Star);
            SplitEditorVerticalDividerColumn.Width =
                new GridLength(0);
            SecondaryEditorColumn.Width =
                new GridLength(0);

            PrimaryEditorRow.Height =
                new GridLength(
                    _editorSplitRatio,
                    GridUnitType.Star);
            SplitEditorHorizontalDividerRow.Height =
                new GridLength(5);
            SecondaryEditorRow.Height =
                new GridLength(
                    1 - _editorSplitRatio,
                    GridUnitType.Star);
        }

        ApplySecondaryPreviewState();
    }

    /// <summary>
    /// Captures the user-adjusted split ratio for local preference persistence.
    /// </summary>
    /// <returns>The normalized primary-pane share of the available editor space.</returns>
    private double CaptureEditorSplitRatio()
    {
        double ratio =
            _editorSplitRatio;

        if (_editorSplitMode == EditorSplitMode.Vertical)
        {
            double total =
                PrimaryEditorColumn.ActualWidth +
                SecondaryEditorColumn.ActualWidth;

            if (total > 0)
            {
                ratio =
                    PrimaryEditorColumn.ActualWidth /
                    total;
            }
        }
        else if (_editorSplitMode == EditorSplitMode.Horizontal)
        {
            double total =
                PrimaryEditorRow.ActualHeight +
                SecondaryEditorRow.ActualHeight;

            if (total > 0)
            {
                ratio =
                    PrimaryEditorRow.ActualHeight /
                    total;
            }
        }

        _editorSplitRatio = Math.Clamp(
            ratio,
            MinimumEditorSplitRatio,
            MaximumEditorSplitRatio);

        return _editorSplitRatio;
    }

    /// <summary>
    /// Records the latest ratio after the user drags the split divider.
    /// </summary>
    /// <param name="sender">The split divider.</param>
    /// <param name="e">The drag-completed event arguments.</param>
    private void SplitEditorDivider_DragCompleted(
            object sender,
            DragCompletedEventArgs e)
    {
        CaptureEditorSplitRatio();
    }

    /// <summary>
    /// Assigns a tab to either pane while enforcing one visual pane per document session.
    /// </summary>
    /// <param name="tab">The tab to display.</param>
    /// <param name="pane">The target pane.</param>
    /// <param name="synchronizeNavigation">Whether to select the corresponding navigation node.</param>
    /// <returns>A task representing activation and context refresh.</returns>
    private async Task ActivateDocumentTabForPaneAsync(
            DocumentTabViewModel tab,
            EditorPaneSlot pane,
            bool synchronizeNavigation)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (pane == EditorPaneSlot.Secondary &&
            _editorSplitMode != EditorSplitMode.None)
        {
            if (_activeDocumentTab is null)
            {
                SetFocusedEditorPane(
                    EditorPaneSlot.Primary);

                await ActivateDocumentTabAsync(
                    tab,
                    synchronizeNavigation);
                return;
            }

            if (ReferenceEquals(
                    tab,
                    _activeDocumentTab))
            {
                if (_secondaryDocumentTab is null)
                {
                    StatusText.Text =
                        "Un même document ne peut pas être affiché dans les deux panes.";
                    return;
                }

                await SwapEditorPaneDocumentsAsync(
                    EditorPaneSlot.Secondary);
                return;
            }

            await ActivateSecondaryDocumentTabAsync(
                tab,
                synchronizeNavigation,
                focusEditor: true);
            return;
        }

        if (ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            if (_activeDocumentTab is not null)
            {
                await SwapEditorPaneDocumentsAsync(
                    EditorPaneSlot.Primary);
                return;
            }

            DetachSecondaryDocumentTabView();
        }

        SetFocusedEditorPane(
            EditorPaneSlot.Primary);

        await ActivateDocumentTabAsync(
            tab,
            synchronizeNavigation);
    }

    /// <summary>
    /// Displays an already-open document tab in the second pane without allocating a second session or autosave controller.
    /// </summary>
    /// <param name="tab">The tab to display.</param>
    /// <param name="synchronizeNavigation">Whether to select the matching navigation item.</param>
    /// <param name="focusEditor">Whether keyboard focus should move to the second editor.</param>
    /// <returns>A task representing contextual refresh work.</returns>
    private async Task ActivateSecondaryDocumentTabAsync(
            DocumentTabViewModel tab,
            bool synchronizeNavigation,
            bool focusEditor)
    {
        ArgumentNullException.ThrowIfNull(tab);

        if (_activeDocumentTab is null)
        {
            SetFocusedEditorPane(
                EditorPaneSlot.Primary);

            await ActivateDocumentTabAsync(
                tab,
                synchronizeNavigation);
            return;
        }

        if (_editorSplitMode == EditorSplitMode.None)
        {
            ApplyEditorSplitMode(
                EditorSplitMode.Vertical);
        }

        if (ReferenceEquals(
                tab,
                _activeDocumentTab))
        {
            StatusText.Text =
                "Un même document ne peut pas être affiché dans les deux panes.";
            return;
        }

        if (!ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            StoreSecondaryDocumentTabState();
        }

        tab.IsMissing =
            !File.Exists(
                tab.FullPath);

        _secondaryDocumentTab = tab;

        _suppressSecondaryDocumentSelection = true;
        SecondaryDocumentPicker.SelectedItem = tab;
        _suppressSecondaryDocumentSelection = false;

        _suppressSecondaryEditorChanges = true;
        SecondaryMarkdownEditorTextBox.Text =
            tab.Content;

        int selectionStart = Math.Clamp(
            tab.SelectionStart,
            0,
            SecondaryMarkdownEditorTextBox.Text.Length);

        int selectionLength = Math.Clamp(
            tab.SelectionLength,
            0,
            SecondaryMarkdownEditorTextBox.Text.Length -
            selectionStart);

        SecondaryMarkdownEditorTextBox.Select(
            selectionStart,
            selectionLength);

        SecondaryMarkdownEditorTextBox.CaretIndex =
            Math.Clamp(
                tab.CaretIndex,
                0,
                SecondaryMarkdownEditorTextBox.Text.Length);

        SecondaryMarkdownEditorTextBox.IsReadOnly =
            tab.IsMissing;

        _suppressSecondaryEditorChanges = false;

        DashboardHost.Visibility =
            Visibility.Collapsed;
        NodeSummaryHost.Visibility =
            Visibility.Collapsed;
        EditorToolbar.Visibility =
            Visibility.Visible;
        DocumentEditorHost.Visibility =
            Visibility.Visible;

        UpdateSecondarySaveState(
            tab);

        NavigationNodeViewModel? node =
            FindDocumentNavigationNode(
                tab);

        if (node is not null)
        {
            tab.UpdateNavigationIdentity(
                node.DisplayName,
                node.FullPath);

            if (synchronizeNavigation)
            {
                _selectedNode = node;
                _restoringSelection = true;
                node.IsSelected = true;
                _restoringSelection = false;
            }

            await TrackRecentDocumentTabAsync(
                node);
        }

        if (focusEditor)
        {
            SetFocusedEditorPane(
                EditorPaneSlot.Secondary);

            SecondaryMarkdownEditorTextBox.Focus();
            UpdateFocusedDocumentChrome(
                tab);
        }

        RenderSecondaryPreview();
        ScheduleDocumentOutlineRefresh();
    }

    /// <summary>
    /// Copies the second editor's content and cursor state back to its shared document tab.
    /// </summary>
    private void StoreSecondaryDocumentTabState()
    {
        if (_secondaryDocumentTab is null)
        {
            return;
        }

        _secondaryDocumentTab.Content =
            SecondaryMarkdownEditorTextBox.Text;

        _secondaryDocumentTab.CaretIndex =
            SecondaryMarkdownEditorTextBox.CaretIndex;

        _secondaryDocumentTab.SelectionStart =
            SecondaryMarkdownEditorTextBox.SelectionStart;

        _secondaryDocumentTab.SelectionLength =
            SecondaryMarkdownEditorTextBox.SelectionLength;
    }

    /// <summary>
    /// Clears the secondary visual assignment without closing or disposing the underlying tab.
    /// </summary>
    private void DetachSecondaryDocumentTabView()
    {
        _secondaryPreviewTimer?.Stop();

        _secondaryDocumentTab = null;

        _suppressSecondaryDocumentSelection = true;
        SecondaryDocumentPicker.SelectedItem = null;
        _suppressSecondaryDocumentSelection = false;

        _suppressSecondaryEditorChanges = true;
        SecondaryMarkdownEditorTextBox.Text =
            string.Empty;
        SecondaryMarkdownEditorTextBox.IsReadOnly =
            false;
        _suppressSecondaryEditorChanges = false;

        SecondarySaveStateText.Text = "—";
        SecondaryMarkdownPreview.Document = null;

        if (_focusedEditorPane == EditorPaneSlot.Secondary)
        {
            _focusedEditorPane =
                EditorPaneSlot.Primary;
        }

        ScheduleDocumentOutlineRefresh();
    }

    /// <summary>
    /// Handles choosing an already-open tab from the secondary-pane selector.
    /// </summary>
    /// <param name="sender">The document picker.</param>
    /// <param name="e">The selection change arguments.</param>
    private async void SecondaryDocumentPicker_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (_suppressSecondaryDocumentSelection ||
            SecondaryDocumentPicker.SelectedItem is not
                DocumentTabViewModel tab)
        {
            return;
        }

        if (ReferenceEquals(
                tab,
                _activeDocumentTab))
        {
            _suppressSecondaryDocumentSelection = true;
            SecondaryDocumentPicker.SelectedItem =
                _secondaryDocumentTab;
            _suppressSecondaryDocumentSelection = false;

            StatusText.Text =
                "Choisissez un document différent de celui de la pane principale.";
            return;
        }

        await ActivateSecondaryDocumentTabAsync(
            tab,
            synchronizeNavigation: true,
            focusEditor: true);
    }

    /// <summary>
    /// Tracks edits in the second pane and schedules the tab's single shared autosave controller.
    /// </summary>
    /// <param name="sender">The secondary editor.</param>
    /// <param name="e">The text change arguments.</param>
    private void SecondaryMarkdownEditorTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        if (_suppressSecondaryEditorChanges ||
            _secondaryDocumentTab is null)
        {
            return;
        }

        _secondaryDocumentTab.Content =
            SecondaryMarkdownEditorTextBox.Text;

        _secondaryDocumentTab.IsDirty = true;

        UpdateSecondarySaveState(
            _secondaryDocumentTab);

        _secondaryDocumentTab.Autosave.Schedule(
            SecondaryMarkdownEditorTextBox.Text);

        _secondaryPreviewTimer?.Stop();
        _secondaryPreviewTimer?.Start();

        if (_focusedEditorPane ==
            EditorPaneSlot.Secondary)
        {
            ScheduleDocumentOutlineRefresh();
        }
    }

    /// <summary>
    /// Keeps the document outline synchronized with caret motion in the second pane.
    /// </summary>
    /// <param name="sender">The secondary editor.</param>
    /// <param name="e">The selection event arguments.</param>
    private void SecondaryMarkdownEditorTextBox_SelectionChanged(
            object sender,
            RoutedEventArgs e)
    {
        if (_focusedEditorPane ==
            EditorPaneSlot.Secondary)
        {
            UpdateCurrentDocumentOutlineSection();
        }
    }

    /// <summary>
    /// Marks the primary editor as the pane targeted by navigation and editor commands.
    /// </summary>
    /// <param name="sender">The primary editor.</param>
    /// <param name="e">The keyboard focus event arguments.</param>
    private void PrimaryMarkdownEditorTextBox_GotKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
    {
        SetFocusedEditorPane(
            EditorPaneSlot.Primary);
    }

    /// <summary>
    /// Marks the secondary editor as the pane targeted by navigation and editor commands.
    /// </summary>
    /// <param name="sender">The secondary editor.</param>
    /// <param name="e">The keyboard focus event arguments.</param>
    private void SecondaryMarkdownEditorTextBox_GotKeyboardFocus(
            object sender,
            KeyboardFocusChangedEventArgs e)
    {
        SetFocusedEditorPane(
            EditorPaneSlot.Secondary);
    }

    /// <summary>
    /// Changes the focused pane and refreshes context chrome without changing document sessions.
    /// </summary>
    /// <param name="pane">The newly focused pane.</param>
    private void SetFocusedEditorPane(
            EditorPaneSlot pane)
    {
        if (pane == EditorPaneSlot.Secondary &&
            (_editorSplitMode == EditorSplitMode.None ||
             _secondaryDocumentTab is null))
        {
            pane = EditorPaneSlot.Primary;
        }

        _focusedEditorPane = pane;

        DocumentTabViewModel? tab =
            GetFocusedDocumentTab();

        if (tab is not null)
        {
            UpdateFocusedDocumentChrome(
                tab);
        }

        ScheduleDocumentOutlineRefresh();
    }

    /// <summary>
    /// Updates shared title and context labels for the document owning the focused pane.
    /// </summary>
    /// <param name="tab">The focused document tab.</param>
    private void UpdateFocusedDocumentChrome(
            DocumentTabViewModel tab)
    {
        DocumentTitleText.Text =
            tab.DisplayName;

        DocumentPathText.Text =
            Path.GetRelativePath(
                _root.FullPath,
                tab.FullPath);

        NavigationNodeViewModel? node =
            FindDocumentNavigationNode(
                tab);

        if (node is not null)
        {
            _selectedNode = node;
            ContextTitleText.Text =
                node.DisplayName;
            ContextKindText.Text =
                GetKindLabel(
                    node.Kind);
            ContextPathText.Text =
                node.FullPath;
        }

        UpdateLinkContext(
            tab.FullPath);
    }

    /// <summary>
    /// Returns the editor control targeted by formatting, outline and save commands.
    /// </summary>
    /// <returns>The focused editor text box.</returns>
    private TextBox GetFocusedEditorTextBox() =>
        _focusedEditorPane == EditorPaneSlot.Secondary &&
        _secondaryDocumentTab is not null
            ? SecondaryMarkdownEditorTextBox
            : MarkdownEditorTextBox;

    /// <summary>
    /// Returns the document tab targeted by the currently focused editor pane.
    /// </summary>
    /// <returns>The focused document tab, or null when no document is displayed.</returns>
    private DocumentTabViewModel? GetFocusedDocumentTab() =>
        _focusedEditorPane == EditorPaneSlot.Secondary
            ? _secondaryDocumentTab
            : _activeDocumentTab;

    /// <summary>
    /// Saves the tab displayed by the focused pane using its existing single autosave controller.
    /// </summary>
    /// <returns>A task representing the explicit flush.</returns>
    private async Task SaveFocusedDocumentAsync()
    {
        DocumentTabViewModel? tab =
            GetFocusedDocumentTab();

        if (tab is null)
        {
            return;
        }

        if (ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            StoreSecondaryDocumentTabState();
        }
        else
        {
            StoreActiveDocumentTabState();
        }

        await tab.Autosave.FlushAsync();

        tab.IsDirty =
            !string.Equals(
                tab.Content,
                tab.Session.Content,
                StringComparison.Ordinal);

        if (ReferenceEquals(
                tab,
                _secondaryDocumentTab))
        {
            UpdateSecondarySaveState(
                tab);
        }
        else
        {
            _documentDirty =
                tab.IsDirty;

            SaveStateText.Text =
                tab.IsDirty
                    ? "Non enregistré"
                    : "Enregistré";
        }
    }

    /// <summary>
    /// Updates the compact persistence state shown in the second pane header.
    /// </summary>
    /// <param name="tab">The secondary document tab.</param>
    private void UpdateSecondarySaveState(
            DocumentTabViewModel tab)
    {
        SecondarySaveStateText.Text =
            tab.IsMissing
                ? "Introuvable"
                : tab.HasConflict
                    ? "Conflit"
                    : tab.IsDirty
                        ? "Modification…"
                        : "Enregistré";
    }

    /// <summary>
    /// Applies the global preview preference to the secondary pane.
    /// </summary>
    private void ApplySecondaryPreviewState()
    {
        bool show =
            _previewVisible &&
            _editorSplitMode != EditorSplitMode.None;

        SecondaryPreviewColumn.Width =
            show
                ? new GridLength(
                    1,
                    GridUnitType.Star)
                : new GridLength(0);

        SecondaryPreviewSplitterColumn.Width =
            show
                ? new GridLength(5)
                : new GridLength(0);

        SecondaryMarkdownPreview.Visibility =
            show
                ? Visibility.Visible
                : Visibility.Collapsed;

        SecondaryPreviewSplitter.Visibility =
            show
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// Renders the second pane preview from its own text and document-relative path.
    /// </summary>
    private void RenderSecondaryPreview()
    {
        if (!_previewVisible ||
            _editorSplitMode == EditorSplitMode.None ||
            _secondaryDocumentTab is null)
        {
            return;
        }

        string? baseDirectory =
            Path.GetDirectoryName(
                _secondaryDocumentTab.FullPath);

        SecondaryMarkdownPreview.Document =
            MarkdownFlowDocumentRenderer.Render(
                SecondaryMarkdownEditorTextBox.Text,
                baseDirectory,
                OnSecondaryInternalLinkClicked,
                OnMarkdownLinkClicked);
    }

    /// <summary>
    /// Executes the debounced preview render for edits made in the second pane.
    /// </summary>
    /// <param name="sender">The secondary preview timer.</param>
    /// <param name="e">The timer event arguments.</param>
    private void SecondaryPreviewTimer_Tick(
            object? sender,
            EventArgs e)
    {
        _secondaryPreviewTimer?.Stop();

        try
        {
            RenderSecondaryPreview();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException)
        {
            StatusText.Text =
                $"Aperçu secondaire temporairement indisponible : {exception.Message}";
        }
    }

    /// <summary>
    /// Routes internal links clicked in the secondary preview back through the normal navigation pipeline.
    /// </summary>
    /// <param name="target">The internal link target.</param>
    private void OnSecondaryInternalLinkClicked(
            string target)
    {
        SetFocusedEditorPane(
            EditorPaneSlot.Secondary);

        OnInternalLinkClicked(
            target);
    }

    /// <summary>
    /// Moves the focused tab toward a requested pane, swapping documents when both panes are occupied.
    /// </summary>
    /// <param name="targetPane">The destination pane.</param>
    /// <returns>A task representing the move or swap.</returns>
    private async Task MoveFocusedTabToPaneAsync(
            EditorPaneSlot targetPane)
    {
        if (targetPane == _focusedEditorPane)
        {
            return;
        }

        if (targetPane == EditorPaneSlot.Secondary)
        {
            if (_editorSplitMode == EditorSplitMode.None)
            {
                ApplyEditorSplitMode(
                    EditorSplitMode.Vertical);
            }

            if (_activeDocumentTab is null)
            {
                return;
            }

            if (_secondaryDocumentTab is not null)
            {
                await SwapEditorPaneDocumentsAsync(
                    EditorPaneSlot.Secondary);
                return;
            }

            DocumentTabViewModel? replacement =
                _documentTabs.FirstOrDefault(tab =>
                    !ReferenceEquals(
                        tab,
                        _activeDocumentTab));

            if (replacement is null)
            {
                StatusText.Text =
                    "Ouvrez un second document avant de déplacer l'onglet vers la pane 2.";
                return;
            }

            DocumentTabViewModel moving =
                _activeDocumentTab;

            StoreActiveDocumentTabState();

            await ActivateDocumentTabAsync(
                replacement,
                synchronizeNavigation: false);

            await ActivateSecondaryDocumentTabAsync(
                moving,
                synchronizeNavigation: true,
                focusEditor: true);

            return;
        }

        if (_secondaryDocumentTab is null)
        {
            return;
        }

        if (_activeDocumentTab is not null)
        {
            await SwapEditorPaneDocumentsAsync(
                EditorPaneSlot.Primary);
            return;
        }

        DocumentTabViewModel promoted =
            _secondaryDocumentTab;

        StoreSecondaryDocumentTabState();
        DetachSecondaryDocumentTabView();

        await ActivateDocumentTabAsync(
            promoted,
            synchronizeNavigation: true);

        SetFocusedEditorPane(
            EditorPaneSlot.Primary);
    }

    /// <summary>
    /// Swaps the two currently displayed document tabs without duplicating their sessions or autosave controllers.
    /// </summary>
    /// <param name="focusPane">The pane that should receive keyboard focus after the swap.</param>
    /// <returns>A task representing both visual activations.</returns>
    private async Task SwapEditorPaneDocumentsAsync(
            EditorPaneSlot focusPane)
    {
        if (_activeDocumentTab is null ||
            _secondaryDocumentTab is null)
        {
            return;
        }

        DocumentTabViewModel previousPrimary =
            _activeDocumentTab;

        DocumentTabViewModel previousSecondary =
            _secondaryDocumentTab;

        StoreActiveDocumentTabState();
        StoreSecondaryDocumentTabState();

        _secondaryDocumentTab = null;

        await ActivateDocumentTabAsync(
            previousSecondary,
            synchronizeNavigation: false);

        await ActivateSecondaryDocumentTabAsync(
            previousPrimary,
            synchronizeNavigation: false,
            focusEditor: false);

        SetFocusedEditorPane(
            focusPane);

        DocumentTabViewModel? focused =
            GetFocusedDocumentTab();

        if (focused is not null)
        {
            SynchronizeNavigationToTab(
                focused);

            if (focusPane == EditorPaneSlot.Secondary)
            {
                SecondaryMarkdownEditorTextBox.Focus();
            }
            else
            {
                MarkdownEditorTextBox.Focus();
            }
        }
    }

    /// <summary>
    /// Selects the navigation item corresponding to a displayed tab without recursively reopening it.
    /// </summary>
    /// <param name="tab">The tab whose navigation node should be selected.</param>
    private void SynchronizeNavigationToTab(
            DocumentTabViewModel tab)
    {
        NavigationNodeViewModel? node =
            FindDocumentNavigationNode(
                tab);

        if (node is null)
        {
            return;
        }

        _selectedNode = node;
        _restoringSelection = true;
        node.IsSelected = true;
        _restoringSelection = false;

        UpdateFocusedDocumentChrome(
            tab);
    }

    /// <summary>
    /// Cycles tabs inside the pane that most recently held editor focus.
    /// </summary>
    /// <param name="delta">Positive for next and negative for previous.</param>
    /// <returns>A task representing pane activation.</returns>
    private async Task CycleFocusedDocumentTabAsync(
            int delta)
    {
        if (_focusedEditorPane != EditorPaneSlot.Secondary ||
            _editorSplitMode == EditorSplitMode.None)
        {
            await CycleDocumentTabAsync(
                delta);
            return;
        }

        DocumentTabViewModel[] candidates =
            _documentTabs
                .Where(tab =>
                    !ReferenceEquals(
                        tab,
                        _activeDocumentTab))
                .ToArray();

        if (candidates.Length == 0)
        {
            return;
        }

        int currentIndex =
            _secondaryDocumentTab is null
                ? 0
                : Array.IndexOf(
                    candidates,
                    _secondaryDocumentTab);

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

        await ActivateSecondaryDocumentTabAsync(
            candidates[nextIndex],
            synchronizeNavigation: true,
            focusEditor: true);
    }

    /// <summary>
    /// Finds the closest tab that can replace a closed primary tab without duplicating the secondary document.
    /// </summary>
    /// <param name="oldIndex">The former primary tab index.</param>
    /// <returns>A distinct primary replacement, or null.</returns>
    private DocumentTabViewModel? FindPrimaryReplacementAfterClose(
            int oldIndex)
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
            return null;
        }

        int index = Math.Clamp(
            oldIndex,
            0,
            candidates.Length - 1);

        return candidates[index];
    }

    /// <summary>
    /// Refreshes secondary-pane metadata after the navigation tree is rebuilt or a file moves.
    /// </summary>
    private void RefreshSecondaryDocumentChrome()
    {
        if (_secondaryDocumentTab is null)
        {
            return;
        }

        UpdateSecondarySaveState(
            _secondaryDocumentTab);

        if (_focusedEditorPane ==
            EditorPaneSlot.Secondary)
        {
            UpdateFocusedDocumentChrome(
                _secondaryDocumentTab);
        }
    }
}
