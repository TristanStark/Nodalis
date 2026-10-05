using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Decisions;
using Nodalis.Infrastructure.Decisions;

namespace Nodalis.App.Dialogs;

public partial class DecisionListDialog : Window
{
    private readonly WorkspaceDecisionService _decisions;
    private readonly string? _contextPath;

    /// <summary>
    /// Initializes a new instance of <see cref="DecisionListDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="scopeLabel">The <c>scopeLabel</c> value.</param>
    public DecisionListDialog(
            string workspaceRoot,
            string? contextPath,
            string scopeLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _decisions =
            new WorkspaceDecisionService(
                workspaceRoot);
        _contextPath =
            contextPath;

        InitializeComponent();
        ScopeText.Text =
            scopeLabel;
        StatusFilterComboBox.SelectedIndex =
            0;

        Loaded += async (_, _) =>
            await RefreshAsync();
    }

    public DecisionRecord? SelectedDecision { get; private set; }

    /// <summary>
    /// Refreshes the decision list when the search text changes.
    /// </summary>
    /// <param name="sender">The search box.</param>
    /// <param name="e">The text-change event.</param>
    private async void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
    {
        await RefreshAsync();
    }

    /// <summary>
    /// Refreshes the decision list when the lifecycle filter changes.
    /// </summary>
    /// <param name="sender">The status filter.</param>
    /// <param name="e">The selection-change event.</param>
    private async void StatusFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await RefreshAsync();
    }

    /// <summary>
    /// Selects one Decision Record for navigation in the main window.
    /// </summary>
    /// <param name="sender">The decisions list.</param>
    /// <param name="e">The mouse event.</param>
    private void DecisionsList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (DecisionsList.SelectedItem is not DecisionRecord decision)
        {
            return;
        }

        SelectedDecision =
            decision;
        DialogResult =
            true;
    }

    /// <summary>
    /// Marks the selected Decision Record as Active.
    /// </summary>
    /// <param name="sender">The action button.</param>
    /// <param name="e">The routed event.</param>
    private async void MarkActive_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (DecisionsList.SelectedItem is not DecisionRecord decision)
        {
            ShowSelectionRequired();
            return;
        }

        await SetLifecycleStatusAsync(
            decision,
            WorkspaceDecisionService.ActiveStatus);
    }

    /// <summary>
    /// Marks the selected Decision Record as Deprecated.
    /// </summary>
    /// <param name="sender">The action button.</param>
    /// <param name="e">The routed event.</param>
    private async void MarkDeprecated_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (DecisionsList.SelectedItem is not DecisionRecord decision)
        {
            ShowSelectionRequired();
            return;
        }

        await SetLifecycleStatusAsync(
            decision,
            WorkspaceDecisionService.DeprecatedStatus);
    }

    /// <summary>
    /// Links the selected Decision Record to an existing replacement from the same scope.
    /// </summary>
    /// <param name="sender">The replacement button.</param>
    /// <param name="e">The routed event.</param>
    private async void Supersede_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (DecisionsList.SelectedItem is not DecisionRecord previous)
        {
            ShowSelectionRequired();
            return;
        }

        try
        {
            IReadOnlyList<DecisionRecord> all =
                await _decisions.GetDecisionsAsync(
                    _contextPath);

            DecisionRecord[] candidates = all
                .Where(candidate =>
                    !string.Equals(
                        candidate.SourceRelativePath,
                        previous.SourceRelativePath,
                        StringComparison.OrdinalIgnoreCase) &&
                    candidate.LifecycleState != DecisionLifecycleState.Superseded &&
                    string.IsNullOrWhiteSpace(
                        candidate.SupersededByReference))
                .OrderByDescending(candidate =>
                    candidate.Date ?? DateOnly.MinValue)
                .ThenBy(
                    candidate => candidate.Title,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                MessageBox.Show(
                    this,
                    "Aucune autre décision utilisable comme remplaçante n'existe dans ce contexte. Créez d'abord le nouveau Decision Record.",
                    "Remplacer une décision",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            DecisionReplacementDialog dialog =
                new DecisionReplacementDialog(
                    previous,
                    candidates)
                {
                    Owner =
                        this
                };

            if (dialog.ShowDialog() != true ||
                dialog.SelectedReplacement is null)
            {
                return;
            }

            await _decisions.SupersedeAsync(
                previous,
                dialog.SelectedReplacement);

            StatusText.Text =
                $"« {previous.Title} » est remplacée par « {dialog.SelectedReplacement.Title} ».";

            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Remplacer une décision",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Applies one lifecycle status to the selected Decision Record and refreshes the list.
    /// </summary>
    /// <param name="decision">The selected Decision Record.</param>
    /// <param name="status">The canonical status to write.</param>
    /// <returns>A task representing the update.</returns>
    private async Task SetLifecycleStatusAsync(
            DecisionRecord decision,
            string status)
    {
        try
        {
            await _decisions.SetLifecycleStatusAsync(
                decision,
                status);

            StatusText.Text =
                $"Statut mis à jour : {status}.";

            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Cycle de vie de la décision",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Shows the common message used when no Decision Record is selected.
    /// </summary>
    private void ShowSelectionRequired()
    {
        MessageBox.Show(
            this,
            "Sélectionnez d'abord une décision.",
            "Décisions",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Loads, searches and filters Decision Records for the current scope.
    /// </summary>
    /// <returns>A task representing the local refresh.</returns>
    private async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<DecisionRecord> decisions =
                await _decisions.SearchAsync(
                    _contextPath,
                    SearchTextBox.Text);

            IEnumerable<DecisionRecord> filtered =
                ApplyLifecycleFilter(
                    decisions);

            DecisionRecord[] displayed =
                filtered.ToArray();

            DecisionsList.ItemsSource =
                displayed;

            int warningCount =
                displayed.Count(decision =>
                    decision.HasLifecycleWarning);

            StatusText.Text =
                displayed.Length == 0
                    ? "Aucune décision dans ce filtre."
                    : warningCount == 0
                        ? $"{displayed.Length} décision(s)."
                        : $"{displayed.Length} décision(s) · {warningCount} chaîne(s) à vérifier.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            DecisionsList.ItemsSource =
                null;
            StatusText.Text =
                exception.Message;
        }
    }

    /// <summary>
    /// Applies the currently selected lifecycle filter without changing the underlying Decision Records.
    /// </summary>
    /// <param name="decisions">The searched Decision Records.</param>
    /// <returns>The filtered sequence.</returns>
    private IEnumerable<DecisionRecord> ApplyLifecycleFilter(
            IReadOnlyList<DecisionRecord> decisions)
    {
        string filter =
            StatusFilterComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag
                ? tag
                : "All";

        return filter switch
        {
            "Active" =>
                decisions.Where(decision =>
                    decision.LifecycleState == DecisionLifecycleState.Active),
            "Superseded" =>
                decisions.Where(decision =>
                    decision.LifecycleState == DecisionLifecycleState.Superseded),
            "Deprecated" =>
                decisions.Where(decision =>
                    decision.LifecycleState == DecisionLifecycleState.Deprecated),
            "Warning" =>
                decisions.Where(decision =>
                    decision.HasLifecycleWarning),
            _ =>
                decisions
        };
    }
}
