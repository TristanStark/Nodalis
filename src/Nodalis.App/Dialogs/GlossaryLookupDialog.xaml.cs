using System.Windows;
using System.Windows.Input;
using Nodalis.Core.Glossary;
using Nodalis.Infrastructure.Glossary;

namespace Nodalis.App.Dialogs;

public partial class GlossaryLookupDialog : Window
{
    private readonly GlossaryService _glossary = new();
    private readonly string _workspaceRoot;
    private readonly string? _contextPath;
    private CancellationTokenSource? _lookupCancellation;
    private GlossaryEntry? _primary;

    public GlossaryLookupDialog(
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
            _lookupCancellation?.Cancel();
            _lookupCancellation?.Dispose();
        };
    }

    public GlossaryEntry? SelectedEntry { get; private set; }

    private async void SearchTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _lookupCancellation?.Cancel();
        _lookupCancellation?.Dispose();

        var query = SearchTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            ClearResult(
                "Saisissez un terme, synonyme ou acronyme.");
            return;
        }

        var cancellation = new CancellationTokenSource();
        _lookupCancellation = cancellation;

        try
        {
            await Task.Delay(
                150,
                cancellation.Token);

            var resolution = await _glossary.ResolveAsync(
                _workspaceRoot,
                _contextPath,
                query,
                cancellation.Token);

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (!resolution.Found)
            {
                ClearResult(
                    $"Aucune définition pour « {query} ».");
                return;
            }

            _primary = resolution.Primary;
            PrimaryCard.Visibility = Visibility.Visible;
            NotFoundText.Visibility = Visibility.Collapsed;

            PrimaryScopeText.Text =
                resolution.Primary!.Scope.DisplayName;
            PrimaryTermText.Text =
                resolution.Primary.Term;
            PrimaryDefinitionText.Text =
                resolution.Primary.Definition;

            var aliases = resolution.Primary.Synonyms
                .Select(value => $"synonyme: {value}")
                .Concat(
                    resolution.Primary.Acronyms.Select(
                        value => $"acronyme: {value}"))
                .ToArray();

            PrimaryAliasesText.Text =
                aliases.Length == 0
                    ? "Aucun synonyme ou acronyme."
                    : string.Join(" · ", aliases);

            PrimaryLinksText.Text =
                resolution.Primary.Links.Count == 0
                    ? string.Empty
                    : "Liens : " +
                      string.Join(
                          " · ",
                          resolution.Primary.Links);

            AlternativesList.ItemsSource =
                resolution.Alternatives;

            AlternativesHeader.Visibility =
                resolution.Alternatives.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ClearResult(
                exception.Message);
        }
    }

    private void OpenPrimary_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_primary is null)
        {
            return;
        }

        SelectedEntry = _primary;
        DialogResult = true;
    }

    private void AlternativesList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (AlternativesList.SelectedItem is not GlossaryEntry entry)
        {
            return;
        }

        SelectedEntry = entry;
        DialogResult = true;
    }

    private void Window_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void ClearResult(string message)
    {
        _primary = null;
        PrimaryCard.Visibility = Visibility.Collapsed;
        AlternativesHeader.Visibility = Visibility.Collapsed;
        AlternativesList.ItemsSource = null;
        NotFoundText.Text = message;
        NotFoundText.Visibility = Visibility.Visible;
    }
}
