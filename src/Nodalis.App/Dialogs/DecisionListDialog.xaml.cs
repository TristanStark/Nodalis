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
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _decisions = new WorkspaceDecisionService(workspaceRoot);
        _contextPath = contextPath;

        InitializeComponent();
        ScopeText.Text = scopeLabel;

        Loaded += async (_, _) => await RefreshAsync();
    }

    public DecisionRecord? SelectedDecision { get; private set; }

    /// <summary>
    /// Performs the <c>SearchTextBox_TextChanged</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
private async void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        await RefreshAsync();
    }

    /// <summary>
    /// Performs the <c>DecisionsList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
private void DecisionsList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DecisionsList.SelectedItem is not DecisionRecord decision)
        {
            return;
        }

        SelectedDecision = decision;
        DialogResult = true;
    }

    /// <summary>
    /// Performs the <c>RefreshAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
private async Task RefreshAsync()
    {
        try
        {
            var decisions = await _decisions.SearchAsync(
                _contextPath,
                SearchTextBox.Text);

            DecisionsList.ItemsSource = decisions;
            StatusText.Text = decisions.Count == 0
                ? "Aucune décision dans ce contexte."
                : $"{decisions.Count} décision(s).";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            DecisionsList.ItemsSource = null;
            StatusText.Text = exception.Message;
        }
    }
}
