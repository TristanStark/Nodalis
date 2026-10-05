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
    private IReadOnlyList<MilestoneItem> _currentMilestones = Array.Empty<MilestoneItem>();

    /// <summary>
    /// Initializes a new instance of <see cref="MilestoneListDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="contextPath">The current project context path.</param>
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
    /// Creates a milestone in the current project.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
    private async void Add_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (!await EnsureProjectDirectoryAsync())
        {
            return;
        }

        global::Nodalis.App.Dialogs.MilestoneEditorDialog dialog = new MilestoneEditorDialog(
            availableMilestones: _currentMilestones)
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
    /// Edits the selected milestone.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
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
            milestone,
            _currentMilestones)
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
    /// Navigates to the first prerequisite of the selected milestone.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
    private void NavigateDependency_Click(
            object sender,
            RoutedEventArgs e)
    {
        global::Nodalis.Core.Milestones.MilestoneItem? milestone = GetSelectedMilestone();

        if (milestone is null)
        {
            ShowError(
                "Prérequis",
                "Sélectionnez d'abord un jalon.");
            return;
        }

        global::System.Guid dependencyId =
            milestone.DependencyIds.FirstOrDefault();

        if (dependencyId == Guid.Empty)
        {
            ShowError(
                "Prérequis",
                "Ce jalon n'a aucun prérequis.");
            return;
        }

        global::Nodalis.Core.Milestones.MilestoneItem? dependency =
            _currentMilestones.FirstOrDefault(candidate =>
                candidate.Id == dependencyId);

        if (dependency is null)
        {
            ShowError(
                "Prérequis",
                "Le jalon prérequis est introuvable. La référence est signalée comme incohérente.");
            return;
        }

        MilestonesList.SelectedItem = dependency;
        MilestonesList.ScrollIntoView(
            dependency);

        if (dependency.TargetDate is not null)
        {
            TimelineList.SelectedItem = dependency;
            TimelineList.ScrollIntoView(
                dependency);
        }

        StatusText.Text =
            $"Prérequis ouvert : {dependency.Name}";
    }

    /// <summary>
    /// Deletes the selected milestone when it is not referenced by another milestone.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
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
    /// Opens the selected milestone from the list view.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The mouse event.</param>
    private void MilestonesList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            OpenSelectedFrom(
                MilestonesList);

    /// <summary>
    /// Opens the selected milestone from the timeline view.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The mouse event.</param>
    private void TimelineList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e) =>
            OpenSelectedFrom(
                TimelineList);

    /// <summary>
    /// Opens the selected milestone from the supplied list.
    /// </summary>
    /// <param name="list">The source list.</param>
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
    /// Gets the milestone selected in either list.
    /// </summary>
    /// <returns>The selected milestone.</returns>
    private MilestoneItem? GetSelectedMilestone() =>
            TimelineList.SelectedItem as MilestoneItem ??
            MilestonesList.SelectedItem as MilestoneItem;

    /// <summary>
    /// Resolves the project directory for the current context.
    /// </summary>
    /// <returns><see langword="true"/> when a project context is available.</returns>
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
    /// Refreshes the milestone list, timeline and dependency warnings.
    /// </summary>
    private async Task RefreshAsync()
    {
        try
        {
            if (!await EnsureProjectDirectoryAsync())
            {
                _currentMilestones = Array.Empty<MilestoneItem>();
                MilestonesList.ItemsSource = null;
                TimelineList.ItemsSource = null;
                return;
            }

            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> milestones =
                await _milestones.GetMilestonesAsync(
                    _projectDirectory);

            _currentMilestones = milestones;
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

            int warningCount = milestones.Count(item =>
                item.DependencyWarnings.Count > 0);

            StatusText.Text = milestones.Count == 0
                ? "Aucun jalon pour ce projet."
                : warningCount == 0
                    ? $"{milestones.Count} jalon(s)."
                    : $"{milestones.Count} jalon(s), {warningCount} avec alerte de dépendance.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _currentMilestones = Array.Empty<MilestoneItem>();
            MilestonesList.ItemsSource = null;
            TimelineList.ItemsSource = null;
            StatusText.Text = exception.Message;
        }
    }

    /// <summary>
    /// Displays a warning message owned by the dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The warning text.</param>
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
