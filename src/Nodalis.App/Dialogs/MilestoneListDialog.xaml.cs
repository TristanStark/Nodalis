using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Milestones;
using Nodalis.Infrastructure.Milestones;

namespace Nodalis.App.Dialogs;

public partial class MilestoneListDialog : Window
{
    private readonly WorkspaceMilestoneService _milestones;
    private readonly string? _contextPath;
    private string? _projectDirectory;

    /// <summary>
    /// Initializes a new instance of <see cref="MilestoneListDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    public MilestoneListDialog(
            string workspaceRoot,
            string? contextPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _milestones = new WorkspaceMilestoneService(
            workspaceRoot);
        _contextPath = contextPath;

        InitializeComponent();

        Loaded += async (_, _) =>
            await RefreshAsync();
    }

    public MilestoneItem? SelectedMilestone { get; private set; }

    /// <summary>
    /// Performs the <c>Add_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async void Add_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (!await EnsureProjectDirectoryAsync())
        {
            return;
        }

        global::Nodalis.App.Dialogs.MilestoneEditorDialog dialog = new MilestoneEditorDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.Draft is null)
        {
            return;
        }

        try
        {
            await _milestones.AddAsync(
                _projectDirectory!,
                dialog.Draft);

            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            ShowError(
                "Ajouter le jalon",
                exception.Message);
        }
    }

    /// <summary>
    /// Performs the <c>Edit_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async void Edit_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Nodalis.Core.Milestones.MilestoneItem? milestone = GetSelectedMilestone();

        if (milestone is null)
        {
            ShowError(
                "Modifier le jalon",
                "Sélectionnez d'abord un jalon.");
            return;
        }

        global::Nodalis.App.Dialogs.MilestoneEditorDialog dialog = new MilestoneEditorDialog(
            milestone)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true ||
            dialog.Draft is null)
        {
            return;
        }

        try
        {
            await _milestones.UpdateAsync(
                milestone,
                dialog.Draft);

            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            MilestoneSourceConflictException)
        {
            ShowError(
                "Modifier le jalon",
                exception.Message);
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Performs the <c>Delete_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async void Delete_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Nodalis.Core.Milestones.MilestoneItem? milestone = GetSelectedMilestone();

        if (milestone is null)
        {
            ShowError(
                "Supprimer le jalon",
                "Sélectionnez d'abord un jalon.");
            return;
        }

        global::System.Windows.MessageBoxResult answer = MessageBox.Show(
            this,
            $"Supprimer le jalon « {milestone.Name} » ?",
            "Supprimer le jalon",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _milestones.DeleteAsync(
                milestone);

            await RefreshAsync();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            MilestoneSourceConflictException)
        {
            ShowError(
                "Supprimer le jalon",
                exception.Message);
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Performs the <c>MilestonesList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void MilestonesList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            OpenSelectedFrom(
                MilestonesList);

    /// <summary>
    /// Performs the <c>TimelineList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void TimelineList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            OpenSelectedFrom(
                TimelineList);

    /// <summary>
    /// Performs the <c>OpenSelectedFrom</c> operation.
    /// </summary>
    /// <param name="list">The <c>list</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void OpenSelectedFrom(ListBox list)
    {
        if (list.SelectedItem is not MilestoneItem milestone)
        {
            return;
        }

        SelectedMilestone = milestone;
        DialogResult = true;
    }

    /// <summary>
    /// Performs the <c>GetSelectedMilestone</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private MilestoneItem? GetSelectedMilestone() =>
            TimelineList.SelectedItem as MilestoneItem ??
            MilestonesList.SelectedItem as MilestoneItem;

    /// <summary>
    /// Performs the <c>EnsureProjectDirectoryAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task<bool> EnsureProjectDirectoryAsync()
    {
        if (_projectDirectory is not null)
        {
            return true;
        }

        _projectDirectory =
            await _milestones.GetProjectDirectoryForContextAsync(
                _contextPath);

        if (_projectDirectory is not null)
        {
            return true;
        }

        StatusText.Text =
            "Sélectionnez un projet pour gérer ses jalons.";
        return false;
    }

    /// <summary>
    /// Performs the <c>RefreshAsync</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private async Task RefreshAsync()
    {
        try
        {
            if (!await EnsureProjectDirectoryAsync())
            {
                MilestonesList.ItemsSource = null;
                TimelineList.ItemsSource = null;
                return;
            }

            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> milestones = await _milestones.GetMilestonesAsync(
                _projectDirectory);

            MilestonesList.ItemsSource = milestones;
            TimelineList.ItemsSource = milestones
                .Where(item => item.TargetDate is not null)
                .OrderBy(item => item.TargetDate)
                .ThenBy(
                    item => item.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            ScopeText.Text =
                Path.GetFileName(_projectDirectory);

            StatusText.Text = milestones.Count == 0
                ? "Aucun jalon pour ce projet."
                : $"{milestones.Count} jalon(s).";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            MilestonesList.ItemsSource = null;
            TimelineList.ItemsSource = null;
            StatusText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Performs the <c>ShowError</c> operation.
    /// </summary>
    /// <param name="title">The <c>title</c> value.</param>
    /// <param name="message">The <c>message</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void ShowError(
            string title,
            string message)
    {
        MessageBox.Show(
            this,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
