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

    private async void Add_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!await EnsureProjectDirectoryAsync())
        {
            return;
        }

        var dialog = new MilestoneEditorDialog
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

    private async void Edit_Click(
        object sender,
        RoutedEventArgs e)
    {
        var milestone = GetSelectedMilestone();

        if (milestone is null)
        {
            ShowError(
                "Modifier le jalon",
                "Sélectionnez d'abord un jalon.");
            return;
        }

        var dialog = new MilestoneEditorDialog(
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

    private async void Delete_Click(
        object sender,
        RoutedEventArgs e)
    {
        var milestone = GetSelectedMilestone();

        if (milestone is null)
        {
            ShowError(
                "Supprimer le jalon",
                "Sélectionnez d'abord un jalon.");
            return;
        }

        var answer = MessageBox.Show(
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

    private void MilestonesList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        OpenSelectedFrom(
            MilestonesList);

    private void TimelineList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e) =>
        OpenSelectedFrom(
            TimelineList);

    private void OpenSelectedFrom(ListBox list)
    {
        if (list.SelectedItem is not MilestoneItem milestone)
        {
            return;
        }

        SelectedMilestone = milestone;
        DialogResult = true;
    }

    private MilestoneItem? GetSelectedMilestone() =>
        TimelineList.SelectedItem as MilestoneItem ??
        MilestonesList.SelectedItem as MilestoneItem;

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

            var milestones = await _milestones.GetMilestonesAsync(
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
