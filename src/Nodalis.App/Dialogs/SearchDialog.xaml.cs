using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Search;
using Nodalis.Infrastructure.Search;

namespace Nodalis.App.Dialogs;

public partial class SearchDialog : Window
{
    private readonly WorkspaceSearchService _search = new();
    private readonly string _workspaceRoot;
    private readonly string? _contextPath;
    private CancellationTokenSource? _searchCancellation;

    public SearchDialog(
        string workspaceRoot,
        string? contextPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = workspaceRoot;
        _contextPath = contextPath;

        InitializeComponent();

        Loaded += (_, _) => SearchTextBox.Focus();
        Closed += (_, _) =>
        {
            _searchCancellation?.Cancel();
            _searchCancellation?.Dispose();
        };
    }

    public SearchResult? SelectedResult { get; private set; }

    private async void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();

        var query = SearchTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            ClearResults();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;

        try
        {
            await Task.Delay(
                180,
                cancellation.Token);

            var results = await _search.SearchAsync(
                _workspaceRoot,
                _contextPath,
                query,
                cancellation.Token);

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ProjectResults.ItemsSource = results.Project;
            ApplicationResults.ItemsSource = results.Application;
            GlobalResults.ItemsSource = results.Global;

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

            StatusText.Text =
                $"{results.TotalCount} résultat(s) pour « {query} »";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            StatusText.Text = exception.Message;
        }
    }

    private void Results_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (sender is not ListBox selected ||
            selected.SelectedItem is null)
        {
            return;
        }

        foreach (var list in new[]
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
                list.SelectedItem = null;
            }
        }
    }

    private void Results_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        AcceptSelectedResult();

    private void Window_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            AcceptSelectedResult();
        }
    }

    private void AcceptSelectedResult()
    {
        SelectedResult =
            ProjectResults.SelectedItem as SearchResult ??
            ApplicationResults.SelectedItem as SearchResult ??
            GlobalResults.SelectedItem as SearchResult;

        if (SelectedResult is not null)
        {
            DialogResult = true;
        }
    }

    private void ClearResults()
    {
        ProjectResults.ItemsSource = null;
        ApplicationResults.ItemsSource = null;
        GlobalResults.ItemsSource = null;

        ProjectHeader.Text = "PROJET · 0 résultat";
        ApplicationHeader.Text = "APPLICATION · 0 résultat";
        GlobalHeader.Text = "GLOBAL · 0 résultat";

        ProjectExpander.Visibility = Visibility.Visible;
        ApplicationExpander.Visibility = Visibility.Visible;
        GlobalExpander.Visibility = Visibility.Visible;

        StatusText.Text = "Saisissez un terme exact.";
    }

    private static string FormatCount(int count) =>
        count == 1
            ? "1 résultat"
            : $"{count} résultats";
}
