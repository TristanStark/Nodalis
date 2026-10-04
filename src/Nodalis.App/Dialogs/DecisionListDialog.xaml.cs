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

    private async void SearchTextBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        await RefreshAsync();
    }

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
