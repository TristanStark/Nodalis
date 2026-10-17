using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Search;
using Nodalis.Infrastructure.Search;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Provides ranked local workspace search with fuzzy and exact modes.
/// </summary>
public partial class SearchDialog : Window
{
    private readonly WorkspaceSearchService _search =
        new WorkspaceSearchService();
    private readonly string _workspaceRoot;
    private readonly string? _contextPath;
    private readonly string? _initialQuery;
    private CancellationTokenSource? _searchCancellation;

    /// <summary>
    /// Initializes a new instance of <see cref="SearchDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="contextPath">The current context path.</param>
    /// <param name="initialQuery">The optional query to restore.</param>
    public SearchDialog(
            string workspaceRoot,
            string? contextPath,
            string? initialQuery = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            workspaceRoot;
        _contextPath =
            contextPath;
        _initialQuery =
            initialQuery;

        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(
                    _initialQuery))
            {
                SearchTextBox.Text =
                    _initialQuery;
                SearchTextBox.SelectAll();
            }

            SearchTextBox.Focus();
        };

        Closed += (_, _) =>
        {
            CancelActiveSearch();
        };
    }

    /// <summary>
    /// Gets the result accepted by the user.
    /// </summary>
    public SearchResult? SelectedResult { get; private set; }

    /// <summary>
    /// Gets the query currently entered by the user.
    /// </summary>
    public string Query =>
        SearchTextBox.Text.Trim();

    /// <summary>
    /// Schedules a debounced search after the query changes.
    /// </summary>
    /// <param name="sender">The search box.</param>
    /// <param name="e">The text event.</param>
    private async void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await UiActionGuard.RunAsync(
            this,
            "Recherche · saisir",
            SearchAsync,
            _workspaceRoot);
    }

    /// <summary>
    /// Reruns the current query when exact mode changes.
    /// </summary>
    /// <param name="sender">The exact-mode checkbox.</param>
    /// <param name="e">The routed event.</param>
    private async void ExactModeCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Recherche · saisir",
            async () =>
            {
                if (!IsLoaded)
                {
                    return;
                }

                await SearchAsync();
            });
    }

    /// <summary>
    /// Cancels the previous request, debounces input and refreshes all result scopes.
    /// </summary>
    /// <returns>A task representing the search refresh.</returns>
    private async Task SearchAsync()
    {
        CancelActiveSearch();

        string query =
            SearchTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(
                query))
        {
            ClearResults();
            return;
        }

        CancellationTokenSource cancellation =
            new CancellationTokenSource();
        _searchCancellation =
            cancellation;

        try
        {
            await Task.Delay(
                180,
                cancellation.Token);

            SearchMatchMode mode =
                ExactModeCheckBox.IsChecked ==
                true
                    ? SearchMatchMode.Exact
                    : SearchMatchMode.Fuzzy;

            SearchResultSet results =
                await _search.SearchAsync(
                    _workspaceRoot,
                    _contextPath,
                    query,
                    mode,
                    cancellation.Token);

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ProjectResults.ItemsSource =
                results.Project;
            ApplicationResults.ItemsSource =
                results.Application;
            GlobalResults.ItemsSource =
                results.Global;

            ProjectHeader.Text =
                $"PROJET · {FormatCount(results.Project.Count)}";
            ApplicationHeader.Text =
                $"APPLICATION · {FormatCount(results.Application.Count)}";
            GlobalHeader.Text =
                $"GLOBAL · {FormatCount(results.Global.Count)}";

            ProjectExpander.Visibility =
                results.Project.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ApplicationExpander.Visibility =
                results.Application.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            GlobalExpander.Visibility =
                results.Global.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            string modeLabel =
                mode == SearchMatchMode.Exact
                    ? "exact"
                    : "fuzzy";

            StatusText.Text =
                $"{results.TotalCount} résultat(s) · mode {modeLabel} · « {query} »";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            StatusText.Text =
                exception.Message;
        }
        finally
        {
            if (ReferenceEquals(
                    _searchCancellation,
                    cancellation))
            {
                _searchCancellation =
                    null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Cancels the active search without disposing a token source that may still be in use.
    /// The owning search disposes its token source when its asynchronous work has completed.
    /// </summary>
    private void CancelActiveSearch()
    {
        CancellationTokenSource? cancellation =
            _searchCancellation;
        _searchCancellation =
            null;

        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Cancellation is idempotent from the UI perspective.
        }
    }

    /// <summary>
    /// Keeps a single selected result across the three scope lists.
    /// </summary>
    /// <param name="sender">The selected list.</param>
    /// <param name="e">The selection event.</param>
    private void Results_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (sender is not ListBox selected ||
            selected.SelectedItem is null)
        {
            return;
        }

        foreach (ListBox list in new[]
                 {
                     ProjectResults,
                     ApplicationResults,
                     GlobalResults
                 })
        {
            if (!ReferenceEquals(
                    list,
                    selected))
            {
                list.SelectedItem =
                    null;
            }
        }
    }

    /// <summary>
    /// Accepts the selected result on double-click.
    /// </summary>
    /// <param name="sender">The result list.</param>
    /// <param name="e">The mouse event.</param>
    private void Results_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
        AcceptSelectedResult();

    /// <summary>
    /// Handles escape and enter navigation shortcuts.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The key event.</param>
    private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
    {
        if (e.Key ==
            Key.Escape)
        {
            e.Handled =
                true;
            DialogResult =
                false;
            return;
        }

        if (e.Key ==
            Key.Enter)
        {
            e.Handled =
                true;
            AcceptSelectedResult();
        }
    }

    /// <summary>
    /// Accepts whichever scope currently owns the selected result.
    /// </summary>
    private void AcceptSelectedResult()
    {
        SelectedResult =
            ProjectResults.SelectedItem as SearchResult ??
            ApplicationResults.SelectedItem as SearchResult ??
            GlobalResults.SelectedItem as SearchResult;

        if (SelectedResult is not null)
        {
            DialogResult =
                true;
        }
    }

    /// <summary>
    /// Clears all search result collections.
    /// </summary>
    private void ClearResults()
    {
        ProjectResults.ItemsSource =
            null;
        ApplicationResults.ItemsSource =
            null;
        GlobalResults.ItemsSource =
            null;

        ProjectHeader.Text =
            "PROJET · 0 résultat";
        ApplicationHeader.Text =
            "APPLICATION · 0 résultat";
        GlobalHeader.Text =
            "GLOBAL · 0 résultat";

        ProjectExpander.Visibility =
            Visibility.Visible;
        ApplicationExpander.Visibility =
            Visibility.Visible;
        GlobalExpander.Visibility =
            Visibility.Visible;

        StatusText.Text =
            ExactModeCheckBox.IsChecked ==
            true
                ? "Saisissez un terme. Mode exact activé."
                : "Saisissez un terme. Recherche fuzzy activée.";
    }

    /// <summary>
    /// Formats a localized result count.
    /// </summary>
    /// <param name="count">The result count.</param>
    /// <returns>The display label.</returns>
    private static string FormatCount(
            int count) =>
        count == 1
            ? "1 résultat"
            : $"{count} résultats";
}
