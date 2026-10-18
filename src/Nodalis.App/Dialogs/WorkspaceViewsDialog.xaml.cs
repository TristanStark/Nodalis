using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Views;
using Nodalis.Infrastructure.Views;
using Nodalis.Infrastructure.Reliability;
using System.Windows.Threading;

namespace Nodalis.App.Dialogs;

/// <summary>A compact, offline and keyboard-accessible first UI for dynamic workspace views.</summary>
public partial class WorkspaceViewsDialog : Window
{
    private WorkspaceViewProjection _projection;
    private readonly Func<CancellationToken, Task<WorkspaceViewProjection>>? _reloadProjection;
    private readonly WorkspaceFileChangeMonitor? _monitor;
    private readonly DispatcherTimer _refreshTimer;
    private CancellationTokenSource? _reloadCancellation;
    private CancellationTokenSource? _queryCancellation;
    private int _querySequence;
    private int _pageIndex;
    private bool _closed;
    private readonly FileSystemWorkspaceViewStore _store;
    private readonly bool _readOnly;
    private Guid? _preferredSavedId;
    private WorkspaceViewVisibility? _preferredVisibility;
    private WorkspaceViewDefinitionSnapshot? _workspaceSaved;
    private WorkspaceViewDefinitionSnapshot? _privateSaved;
    private WorkspaceViewQuery? _loadedQuery;
    private bool _queryEdited;
    private bool _loading;
    private WorkspaceViewItem? _selectedItem;

    private sealed record SavedOption(
        string Label, WorkspaceViewDefinition? Definition,
        WorkspaceViewVisibility Visibility);

    private sealed record ResultRow(
        WorkspaceViewItem Item, string Title, string Kind,
        string DateLabel, string Status, string Group, string Subtitle);

    /// <summary>Builds a browsing dialog from existing read-only projections.</summary>
    /// <param name="projection">Current immutable workspace projection.</param>
    /// <param name="store">Portable/private saved view definitions store.</param>
    /// <param name="readOnly">Whether writes to the workspace are disabled.</param>
    /// <param name="initialViewId">Optional saved view selected from dashboard or palette.</param>
    /// <param name="initialVisibility">Visibility of that saved view.</param>
    /// <param name="reloadProjection">Optional cancellable canonical projection refresh.</param>
    /// <param name="monitor">Optional shared workspace change monitor.</param>
    public WorkspaceViewsDialog(WorkspaceViewProjection projection,
        FileSystemWorkspaceViewStore store, bool readOnly = false,
        Guid? initialViewId = null, WorkspaceViewVisibility? initialVisibility = null,
        Func<CancellationToken, Task<WorkspaceViewProjection>>? reloadProjection = null,
        WorkspaceFileChangeMonitor? monitor = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(store);
        _projection = projection;
        _store = store;
        _readOnly = readOnly;
        _reloadProjection = reloadProjection;
        _monitor = monitor;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        if (_monitor is not null)
        {
            _monitor.FileChanged += Monitor_FileChanged;
        }

        Closed += Window_Closed;
        _preferredSavedId = initialViewId;
        _preferredVisibility = initialVisibility;
        InitializeComponent();
        _loading = true;
        KindComboBox.ItemsSource = new[]
        {
            "Tous", "Document", "Task", "Milestone", "Decision", "Incident", "Intervention"
        };
        KindComboBox.SelectedIndex = 0;
        GroupComboBox.ItemsSource = new[] { "Aucun", "project", "kind", "status" };
        GroupComboBox.SelectedIndex = 0;
        SortComboBox.ItemsSource = new[] { "title", "date", "priority", "status" };
        SortComboBox.SelectedIndex = 0;
        VisibilityComboBox.ItemsSource = new[] { "Workspace", "Privée" };
        VisibilityComboBox.SelectedIndex = 0;
        ViewNameTextBox.Text = "Ma vue";
        _loading = false;
    }

    /// <summary>Selected canonical item to navigate to after closing.</summary>
    public WorkspaceViewItem? SelectedSource { get; private set; }

    /// <summary>Loads saved definitions without creating any file or changing Markdown.</summary>
    /// <param name="sender">WPF window.</param>
    /// <param name="e">Loaded event.</param>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadSavedAsync();
        RefreshResults();
        SearchTextBox.Focus();
    }

    /// <summary>Refreshes the saved view picker after a read or explicit write.</summary>
    /// <returns>Completion of local JSON reads.</returns>
    private async Task LoadSavedAsync()
    {
        try
        {
            _workspaceSaved = await _store.LoadAsync(WorkspaceViewVisibility.Workspace);
            _privateSaved = await _store.LoadAsync(WorkspaceViewVisibility.Private);
            List<SavedOption> choices =
            [
                new SavedOption("(Nouvelle vue)", null, WorkspaceViewVisibility.Workspace)
            ];
            choices.AddRange(_workspaceSaved.Views.OrderBy(item => item.Order)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new SavedOption(item.Name + " · Workspace", item,
                    WorkspaceViewVisibility.Workspace)));
            choices.AddRange(_privateSaved.Views.OrderBy(item => item.Order)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(item => new SavedOption(item.Name + " · Privée", item,
                    WorkspaceViewVisibility.Private)));
            _loading = true;
            SavedViewsComboBox.ItemsSource = choices;
            SavedOption? selected = choices.FirstOrDefault(item =>
                item.Definition?.Id == _preferredSavedId &&
                item.Visibility == _preferredVisibility);
            SavedViewsComboBox.SelectedItem = selected ?? choices[0];
            _loading = false;
            ApplySavedSelection();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or
            System.Text.Json.JsonException)
        {
            WarningText.Text = "Erreur de chargement des vues : " + exception.Message;
        }
    }

    /// <summary>Builds a typed query from simple visual filters.</summary>
    /// <returns>Bounded query to evaluate without code execution.</returns>
    private WorkspaceViewQuery BuildVisualQuery()
    {
        List<WorkspaceViewPredicate> predicates = [];
        if (!string.IsNullOrWhiteSpace(SearchTextBox.Text))
        {
            predicates.Add(new WorkspaceViewPredicate
            {
                Field = "title",
                Operator = WorkspaceViewOperator.Contains,
                Text = SearchTextBox.Text.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(StatusTextBox.Text))
        {
            predicates.Add(new WorkspaceViewPredicate
            {
                Field = "status",
                Operator = WorkspaceViewOperator.Contains,
                Text = StatusTextBox.Text.Trim()
            });
        }

        string kind = KindComboBox.SelectedItem as string ?? "Tous";
        List<WorkspaceViewItemKind> kinds = [];
        if (Enum.TryParse(kind, ignoreCase: true, out WorkspaceViewItemKind chosen))
        {
            kinds.Add(chosen);
        }

        return new WorkspaceViewQuery
        {
            Kinds = kinds,
            Filter = predicates.Count == 0 ? null :
                new WorkspaceViewFilter { Predicates = predicates },
            SortBy = SortComboBox.SelectedItem as string ?? "title",
            GroupBy = GroupComboBox.SelectedItem is string group &&
                group != "Aucun" ? group : null,
            Limit = 200,
            MaxScannedItems = 25000
        };
    }

    /// <summary>Returns the saved advanced query until the simple visual form is edited.</summary>
    /// <returns>The query currently presented to the user.</returns>
    private WorkspaceViewQuery CurrentQuery() =>
        !_queryEdited && _loadedQuery is not null ? _loadedQuery : BuildVisualQuery();

    /// <summary>Starts a cancelable query; only the most recent request can render.</summary>
    private void RefreshResults()
    {
        _ = RefreshResultsAsync();
    }

    /// <summary>Runs bounded filtering off the WPF UI thread and discards stale results.</summary>
    /// <returns>Completion of the latest query or a silent cancellation.</returns>
    private async Task RefreshResultsAsync()
    {
        if (_loading || !IsLoaded || _closed)
        {
            return;
        }

        _queryCancellation?.Cancel();
        CancellationTokenSource request = new();
        _queryCancellation = request;
        int sequence = ++_querySequence;
        try
        {
            WorkspaceViewQuery query = CurrentQuery() with
            {
                Offset = _pageIndex * CurrentQuery().Limit
            };
            WorkspaceViewProjection projection = _projection;
            WorkspaceViewQueryResult result = await Task.Run(() =>
                WorkspaceViewQueryEngine.Execute(projection.Items, query, request.Token),
                request.Token);
            if (request.IsCancellationRequested || sequence != _querySequence || _closed)
            {
                return;
            }

            ResultRow[] rows = result.Items.Select(item =>
            {
                string date = item.Fields.TryGetValue("date", out WorkspaceViewField? value)
                    ? value.Date?.ToString("dd/MM/yyyy") ?? "" : "";
                string status = item.Fields.TryGetValue("status", out WorkspaceViewField? field)
                    ? field.Text ?? "" : "";
                string group = CurrentQuery().GroupBy switch
                {
                    "kind" => item.Kind.ToString(),
                    "status" => string.IsNullOrWhiteSpace(status) ? "(Non renseigné)" : status,
                    "project" => item.Fields.TryGetValue("project",
                        out WorkspaceViewField? project) ? project.Text ?? "(Non renseigné)"
                        : "(Non renseigné)",
                    _ => item.ScopeIdentity
                };
                return new ResultRow(item, item.Title, item.Kind.ToString(), date,
                    status, group, item.ScopeIdentity + " · " +
                        item.Source.RelativePath +
                        (item.Source.LineNumber is int line ? " · ligne " + line : ""));
            }).ToArray();

            _selectedItem = null;
            OpenSourceButton.IsEnabled = false;
            ListResults.ItemsSource = rows;
            TableResults.ItemsSource = rows;
            CardResults.ItemsSource = rows;
            PreviousPageButton.IsEnabled = _pageIndex > 0;
            NextPageButton.IsEnabled = (_pageIndex + 1) * query.Limit < result.TotalMatches;
            SummaryText.Text = rows.Length + " résultat(s) affiché(s) sur " +
                result.TotalMatches + " correspondance(s) · page " +
                (_pageIndex + 1) +
                (result.IsTruncated ? " · ANALYSE LIMITÉE" : "") + ".";
            WarningText.Text = _projection.Diagnostics.Count + " référence(s) non projetée(s)" +
                (_readOnly ? " · workspace en lecture seule" : "") +
                (!_queryEdited && _loadedQuery?.Filter is not null
                    ? " · filtres de la vue sauvegardée appliqués" : "") +
                (query.ScopeIdentity is string scope &&
                 !projection.Items.Any(item => string.Equals(
                     item.ScopeIdentity, scope, StringComparison.OrdinalIgnoreCase))
                    ? " · périmètre introuvable : " + scope + " (aucune redirection)" : "");

        }
        catch (OperationCanceledException)
        {
            // Expected when a user edits a filter or closes the window.
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            if (sequence == _querySequence && !_closed)
            {
                SummaryText.Text = "Filtre invalide : " + exception.Message;
                ListResults.ItemsSource = null;
                TableResults.ItemsSource = null;
                CardResults.ItemsSource = null;
                PreviousPageButton.IsEnabled = false;
                NextPageButton.IsEnabled = false;
                OpenSourceButton.IsEnabled = false;
            }
        }
        finally
        {
            if (ReferenceEquals(_queryCancellation, request))
            {
                _queryCancellation = null;
            }

            request.Dispose();
        }
    }

    /// <summary>Browses the previous bounded page without rescanning canonical sources.</summary>
    /// <param name="sender">Navigation button.</param>
    /// <param name="e">Click.</param>
    private void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        _pageIndex = Math.Max(0, _pageIndex - 1);
        RefreshResults();
    }

    /// <summary>Browses the next bounded page with stable query ordering.</summary>
    /// <param name="sender">Navigation button.</param>
    /// <param name="e">Click.</param>
    private void NextPage_Click(object sender, RoutedEventArgs e)
    {
        _pageIndex++;
        RefreshResults();
    }

    /// <summary>Coalesces filesystem bursts onto the UI dispatcher.</summary>
    /// <param name="sender">Shared monitor.</param>
    /// <param name="change">Filesystem change notification.</param>
    private void Monitor_FileChanged(object? sender, WorkspaceFileChangedEventArgs change)
    {
        string name = Path.GetFileName(change.FullPath);
        if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            name is not ".project.json" and not ".application.json" and not ".workspace.json" &&
            Path.HasExtension(change.FullPath))
        {
            return;
        }

        if (_closed || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed)
            {
                return;
            }

            _refreshTimer.Stop();
            _refreshTimer.Start();
        }));
    }

    /// <summary>Rebuilds the changed read projection, never edits the source itself.</summary>
    /// <param name="sender">Refresh timer.</param>
    /// <param name="e">Timer event.</param>
    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        if (_reloadProjection is null || _closed)
        {
            return;
        }

        _reloadCancellation?.Cancel();
        CancellationTokenSource request = new();
        _reloadCancellation = request;
        try
        {
            WarningText.Text = "Actualisation des sources…";
            WorkspaceViewProjection updated = await _reloadProjection(request.Token);
            if (_closed || request.IsCancellationRequested ||
                !ReferenceEquals(_reloadCancellation, request))
            {
                return;
            }

            _projection = updated;
            _pageIndex = 0;
            RefreshResults();
            WarningText.Text = updated.Diagnostics.Count + " référence(s) non projetée(s)";
        }
        catch (OperationCanceledException)
        {
            // Superseded request, including when the dialog closes.
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                WarningText.Text = "Actualisation impossible : " + exception.Message;
            }
        }
        finally
        {
            if (ReferenceEquals(_reloadCancellation, request))
            {
                _reloadCancellation = null;
            }

            request.Dispose();
        }
    }

    /// <summary>Detaches event listeners and cancels pending work on closing.</summary>
    /// <param name="sender">Window.</param>
    /// <param name="e">Closed event.</param>
    private void Window_Closed(object? sender, EventArgs e)
    {
        _closed = true;
        _monitor?.FileChanged -= Monitor_FileChanged;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        _queryCancellation?.Cancel();
        _reloadCancellation?.Cancel();
    }

    /// <summary>Marks the simple form as edited and reruns its projection.</summary>
    /// <param name="sender">Selector.</param>
    /// <param name="e">Selection event.</param>
    private void Filters_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            _queryEdited = true;
            _pageIndex = 0;
            RefreshResults();
        }
    }

    /// <summary>Handles live search/status changes without disk writes.</summary>
    /// <param name="sender">Text box.</param>
    /// <param name="e">Text event.</param>
    private void Filters_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
        {
            _queryEdited = true;
            _pageIndex = 0;
            RefreshResults();
        }
    }

    /// <summary>Loads a persisted view and preserves advanced query predicates until edited.</summary>
    /// <param name="sender">Saved selector.</param>
    /// <param name="e">Selection event.</param>
    private void SavedView_Changed(object sender, SelectionChangedEventArgs e)
    {
        ApplySavedSelection();
    }

    /// <summary>Loads the selected view even when the combo box was populated programmatically.</summary>
    private void ApplySavedSelection()
    {
        if (_loading || SavedViewsComboBox.SelectedItem is not SavedOption chosen)
        {
            return;
        }

        _pageIndex = 0;
        _preferredSavedId = chosen.Definition?.Id;
        _preferredVisibility = chosen.Visibility;
        _loading = true;
        WorkspaceViewDefinition? saved = chosen.Definition;
        _loadedQuery = saved?.Query;
        _queryEdited = saved is null;
        ViewNameTextBox.Text = saved?.Name ?? "Ma vue";
        PinViewCheckBox.IsChecked = saved?.Pinned ?? false;
        VisibilityComboBox.SelectedIndex = chosen.Visibility == WorkspaceViewVisibility.Workspace ? 0 : 1;
        KindComboBox.SelectedItem = saved?.Query.Kinds.Count == 1
            ? saved.Query.Kinds[0].ToString() : "Tous";
        GroupComboBox.SelectedItem = saved?.Query.GroupBy ?? "Aucun";
        SortComboBox.SelectedItem = saved?.Query.SortBy ?? "title";
        SearchTextBox.Text = string.Empty;
        StatusTextBox.Text = string.Empty;
        LayoutTabs.SelectedIndex = saved?.Layout switch
        {
            WorkspaceViewLayout.Table => 1,
            WorkspaceViewLayout.Cards => 2,
            _ => 0
        };
        _loading = false;
        RefreshResults();
    }

    /// <summary>Preserves a saved layout choice when the user changes tabs.</summary>
    /// <param name="sender">Tab control.</param>
    /// <param name="e">Selection event.</param>
    private void Layout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ReferenceEquals(e.OriginalSource, LayoutTabs))
        {
            RefreshResults();
        }
    }

    /// <summary>Updates navigation selection from one of three synchronized layouts.</summary>
    /// <param name="sender">Selected view.</param>
    /// <param name="e">Selection event.</param>
    private void Result_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedItem = sender switch
        {
            ListBox list => (list.SelectedItem as ResultRow)?.Item,
            DataGrid grid => (grid.SelectedItem as ResultRow)?.Item,
            _ => null
        };
        OpenSourceButton.IsEnabled = _selectedItem is not null;
    }

    /// <summary>Navigates by double-click if a real source was selected.</summary>
    /// <param name="sender">Result layout.</param>
    /// <param name="e">Mouse event.</param>
    private void Result_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSource_Click(sender, e);
    }

    /// <summary>Accepts the canonical GUID and original source position.</summary>
    /// <param name="sender">Open button.</param>
    /// <param name="e">Click event.</param>
    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        SelectedSource = _selectedItem;
        DialogResult = true;
    }

    /// <summary>Reads storage choice from the explicit user selector.</summary>
    /// <returns>Private or workspace target.</returns>
    private WorkspaceViewVisibility ChosenVisibility() =>
        VisibilityComboBox.SelectedIndex == 1
            ? WorkspaceViewVisibility.Private : WorkspaceViewVisibility.Workspace;

    /// <summary>Returns current validated layout selection.</summary>
    /// <returns>List, table or cards.</returns>
    private WorkspaceViewLayout ChosenLayout() => LayoutTabs.SelectedIndex switch
    {
        1 => WorkspaceViewLayout.Table,
        2 => WorkspaceViewLayout.Cards,
        _ => WorkspaceViewLayout.List
    };

    /// <summary>Persists or replaces one definition after explicit user action.</summary>
    /// <param name="duplicate">Create a new GUID rather than overwriting selected view.</param>
    /// <returns>Completion and fresh saved definition snapshot.</returns>
    private async Task SaveCurrentAsync(bool duplicate)
    {
        if (_readOnly)
        {
            WarningText.Text = "Enregistrement désactivé : workspace en lecture seule.";
            return;
        }

        string name = ViewNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            WarningText.Text = "Indiquez un nom de vue.";
            return;
        }

        WorkspaceViewVisibility visibility = ChosenVisibility();
        WorkspaceViewDefinitionSnapshot? snapshot = visibility ==
            WorkspaceViewVisibility.Workspace ? _workspaceSaved : _privateSaved;
        if (snapshot is null || snapshot.IsReadOnly)
        {
            WarningText.Text = "Les vues de cette catégorie ne peuvent pas être modifiées.";
            return;
        }

        WorkspaceViewDefinition? original = (SavedViewsComboBox.SelectedItem as SavedOption)?.Definition;
        Guid id = !duplicate && original is not null &&
            (SavedViewsComboBox.SelectedItem as SavedOption)?.Visibility == visibility
            ? original.Id : Guid.NewGuid();
        WorkspaceViewDefinition proposed = new()
        {
            Id = id,
            Name = name,
            Query = CurrentQuery(),
            Layout = ChosenLayout(),
            Columns = original?.Columns ?? ["title", "kind", "date", "status"],
            Pinned = PinViewCheckBox.IsChecked == true,
            Order = original?.Order ?? snapshot.Views.Count
        };
        List<WorkspaceViewDefinition> revised = snapshot.Views
            .Where(item => item.Id != proposed.Id).ToList();
        revised.Add(proposed);
        try
        {
            await _store.SaveAsync(visibility, revised, snapshot.Revision);
            _preferredSavedId = proposed.Id;
            _preferredVisibility = visibility;
            await LoadSavedAsync();
            WarningText.Text = "Vue enregistrée : " + proposed.Name;
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            WarningText.Text = "Enregistrement impossible : " + exception.Message;
        }
    }

    /// <summary>Saves the existing selected view or creates one.</summary>
    /// <param name="sender">Save button.</param>
    /// <param name="e">Click.</param>
    private async void SaveView_Click(object sender, RoutedEventArgs e)
    {
        await SaveCurrentAsync(duplicate: false);
    }

    /// <summary>Duplicates the definition with a new stable GUID.</summary>
    /// <param name="sender">Duplicate button.</param>
    /// <param name="e">Click.</param>
    private async void DuplicateView_Click(object sender, RoutedEventArgs e)
    {
        await SaveCurrentAsync(duplicate: true);
    }

    /// <summary>Deletes a saved definition, never its source documents or query results.</summary>
    /// <param name="sender">Delete button.</param>
    /// <param name="e">Click.</param>
    private async void DeleteView_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly || SavedViewsComboBox.SelectedItem is not SavedOption chosen ||
            chosen.Definition is null)
        {
            return;
        }

        WorkspaceViewDefinitionSnapshot? snapshot = chosen.Visibility ==
            WorkspaceViewVisibility.Workspace ? _workspaceSaved : _privateSaved;
        if (snapshot is null || snapshot.IsReadOnly)
        {
            return;
        }

        if (MessageBox.Show(this,
                "Supprimer seulement la définition de la vue « " + chosen.Definition.Name +
                " » ? Aucun document source ne sera modifié.",
                "Supprimer la vue", MessageBoxButton.YesNo, MessageBoxImage.Question) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _store.SaveAsync(chosen.Visibility, snapshot.Views
                .Where(item => item.Id != chosen.Definition.Id).ToArray(),
                snapshot.Revision);
            _preferredSavedId = null;
            _preferredVisibility = null;
            await LoadSavedAsync();
            RefreshResults();
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or InvalidDataException)
        {
            WarningText.Text = "Suppression impossible : " + exception.Message;
        }
    }
}
