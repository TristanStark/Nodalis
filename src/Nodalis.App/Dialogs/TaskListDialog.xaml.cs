using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Tasks;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays, filters, sorts and edits Markdown checkbox tasks in one local view.
/// </summary>
public partial class TaskListDialog : Window
{
    private const string AllPrioritiesLabel = "Toutes priorités";
    private const string AllStatusesLabel = "Tous statuts";
    private const string AllOwnersLabel = "Tous responsables";

    private readonly WorkspaceTaskService _tasks;
    private readonly string? _contextPath;
    private readonly Func<string, TaskMetadataUpdate, Task<TaskItem>> _createTaskAsync;
    private readonly Func<TaskItem, bool, Task> _toggleTaskAsync;
    private readonly Func<TaskItem, TaskMetadataUpdate, Task> _updateTaskMetadataAsync;
    private readonly Func<TaskItem, TaskMetadataUpdate, Task<MeetingActionPromotionResult>> _promoteMeetingActionAsync;

    private IReadOnlyList<TaskItem> _loadedTasks = [];
    private bool _updatingFilters;
    private bool _isInitialized;

    /// <summary>
    /// Initializes a new instance of <see cref="TaskListDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    /// <param name="contextPath">The current task scope path.</param>
    /// <param name="scopeLabel">The localized scope label.</param>
    /// <param name="createTaskAsync">The callback used to create a project task.</param>
    /// <param name="toggleTaskAsync">The callback used to change checkbox completion.</param>
    /// <param name="updateTaskMetadataAsync">The callback used to rewrite task metadata.</param>
    /// <param name="promoteMeetingActionAsync">The callback used to promote a meeting action into project task tracking.</param>
    public TaskListDialog(
            string workspaceRoot,
            string? contextPath,
            string scopeLabel,
            Func<string, TaskMetadataUpdate, Task<TaskItem>> createTaskAsync,
            Func<TaskItem, bool, Task> toggleTaskAsync,
            Func<TaskItem, TaskMetadataUpdate, Task> updateTaskMetadataAsync,
            Func<TaskItem, TaskMetadataUpdate, Task<MeetingActionPromotionResult>> promoteMeetingActionAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);
        ArgumentNullException.ThrowIfNull(
            createTaskAsync);
        ArgumentNullException.ThrowIfNull(
            toggleTaskAsync);
        ArgumentNullException.ThrowIfNull(
            updateTaskMetadataAsync);
        ArgumentNullException.ThrowIfNull(
            promoteMeetingActionAsync);

        _tasks = new WorkspaceTaskService(
            workspaceRoot);
        _contextPath =
            contextPath;
        _createTaskAsync =
            createTaskAsync;
        _toggleTaskAsync =
            toggleTaskAsync;
        _updateTaskMetadataAsync =
            updateTaskMetadataAsync;
        _promoteMeetingActionAsync =
            promoteMeetingActionAsync;

        InitializeComponent();
        _isInitialized =
            true;

        ScopeText.Text =
            scopeLabel;

        Loaded += async (_, _) =>
            await RefreshAsync();
    }

    /// <summary>
    /// Gets the task selected for navigation when the dialog is accepted.
    /// </summary>
    public TaskItem? SelectedTask { get; private set; }

    /// <summary>
    /// Reloads tasks when the completed-task option changes.
    /// </summary>
    /// <param name="sender">The completed-task checkbox.</param>
    /// <param name="e">The routed event.</param>
    private async void IncludeCompletedCheckBox_Changed(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Tâches · filtrer",
            async () =>
            {
            if (!_isInitialized)
            {
                return;
            }
    
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Reapplies in-memory filters or sorting when one filter changes.
    /// </summary>
    /// <param name="sender">The changed filter control.</param>
    /// <param name="e">The routed event.</param>
    private void Filters_Changed(
            object sender,
            RoutedEventArgs e)
    {
        if (!_isInitialized ||
            _updatingFilters)
        {
            return;
        }

        ApplyFiltersAndSort();
    }


    /// <summary>
    /// Creates a new Markdown task in the task document of the current project.
    /// </summary>
    /// <param name="sender">The create button.</param>
    /// <param name="e">The routed event.</param>
    private async void CreateTask_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Tâches · créer",
            async () =>
            {
            TaskCreationDialog dialog =
                new TaskCreationDialog
                {
                    Owner =
                        this
                };
    
            if (dialog.ShowDialog() != true ||
                dialog.Metadata is null)
            {
                return;
            }
    
            try
            {
                TaskItem created = await _createTaskAsync(
                    dialog.TaskText,
                    dialog.Metadata);
    
                StatusText.Text =
                    $"Tâche créée · {created.Text}";
    
                await RefreshAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                InvalidOperationException or
                TaskSourceConflictException)
            {
                StatusText.Text =
                    exception.Message;
            }
            });
    }

    /// <summary>
    /// Toggles the Markdown checkbox for the clicked task.
    /// </summary>
    /// <param name="sender">The task checkbox.</param>
    /// <param name="e">The routed event.</param>
    private async void TaskCheckBox_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Tâches · changer le statut",
            async () =>
            {
            if (sender is not CheckBox checkBox ||
                checkBox.DataContext is not TaskItem task)
            {
                return;
            }
    
            try
            {
                bool completed =
                    checkBox.IsChecked ==
                    true;
    
                await _toggleTaskAsync(
                    task,
                    completed);
    
                await RefreshAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                TaskSourceConflictException)
            {
                StatusText.Text =
                    exception.Message;
                await RefreshAsync();
            }
            });
    }

    /// <summary>
    /// Promotes one meeting action to project task tracking after allowing its metadata to be reviewed.
    /// </summary>
    /// <param name="sender">The promote button.</param>
    /// <param name="e">The routed event.</param>
    private async void PromoteTask_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Tâches · promouvoir",
            async () =>
            {
            if (sender is not Button button ||
                button.Tag is not TaskItem task ||
                !task.CanPromoteMeetingAction)
            {
                return;
            }
    
            TaskMetadataDialog dialog =
                new TaskMetadataDialog(
                    task)
                {
                    Owner =
                        this
                };
    
            if (dialog.ShowDialog() != true ||
                dialog.Metadata is null)
            {
                return;
            }
    
            try
            {
                MeetingActionPromotionResult result = await _promoteMeetingActionAsync(
                    task,
                    dialog.Metadata);
    
                StatusText.Text = result.Created
                    ? $"Action promue dans {result.ProjectTaskRelativePath}."
                    : $"Action déjà promue ; liaison mise à jour dans {result.ProjectTaskRelativePath}.";
    
                await RefreshAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                InvalidOperationException or
                TaskSourceConflictException)
            {
                StatusText.Text =
                    exception.Message;
                await RefreshAsync();
            }
            });
    }

    /// <summary>
    /// Opens the metadata editor for one task and writes the accepted values to Markdown.
    /// </summary>
    /// <param name="sender">The edit button.</param>
    /// <param name="e">The routed event.</param>
    private async void EditTask_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Tâches · modifier",
            async () =>
            {
            if (sender is not Button button ||
                button.Tag is not TaskItem task)
            {
                return;
            }
    
            TaskMetadataDialog dialog =
                new TaskMetadataDialog(
                    task)
                {
                    Owner =
                        this
                };
    
            if (dialog.ShowDialog() != true ||
                dialog.Metadata is null)
            {
                return;
            }
    
            try
            {
                await _updateTaskMetadataAsync(
                    task,
                    dialog.Metadata);
    
                await RefreshAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                TaskSourceConflictException)
            {
                StatusText.Text =
                    exception.Message;
                await RefreshAsync();
            }
            });
    }

    /// <summary>
    /// Accepts the selected task for source navigation on double-click.
    /// </summary>
    /// <param name="sender">The task list.</param>
    /// <param name="e">The mouse event.</param>
    private void TasksList_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TasksList.SelectedItem is not TaskItem task)
        {
            return;
        }

        SelectedTask =
            task;
        DialogResult =
            true;
    }

    /// <summary>
    /// Reloads tasks from the workspace and refreshes filter choices.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        try
        {
            _loadedTasks = await _tasks.GetTasksAsync(
                _contextPath,
                IncludeCompletedCheckBox.IsChecked ==
                true);

            RefreshFilterChoices();
            ApplyFiltersAndSort();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            _loadedTasks =
                [];
            TasksList.ItemsSource =
                null;
            StatusText.Text =
                exception.Message;
        }
    }

    /// <summary>
    /// Rebuilds the available priority, status and owner filter values.
    /// </summary>
    private void RefreshFilterChoices()
    {
        _updatingFilters =
            true;

        try
        {
            SetFilterChoices(
                PriorityFilterComboBox,
                AllPrioritiesLabel,
                _loadedTasks.Select(task =>
                    task.Priority));

            SetFilterChoices(
                StatusFilterComboBox,
                AllStatusesLabel,
                _loadedTasks.Select(task =>
                    task.Status));

            SetFilterChoices(
                OwnerFilterComboBox,
                AllOwnersLabel,
                _loadedTasks.Select(task =>
                    task.Owner));
        }
        finally
        {
            _updatingFilters =
                false;
        }
    }

    /// <summary>
    /// Replaces a combo box filter catalog while preserving its current choice when possible.
    /// </summary>
    /// <param name="comboBox">The filter combo box.</param>
    /// <param name="allLabel">The first no-filter label.</param>
    /// <param name="values">The task values to expose.</param>
    private static void SetFilterChoices(
            ComboBox comboBox,
            string allLabel,
            IEnumerable<string?> values)
    {
        string? selected =
            comboBox.SelectedItem as
            string;

        string[] choices =
            new[] { allLabel }
                .Concat(
                    values
                        .Where(value =>
                            !string.IsNullOrWhiteSpace(
                                value))
                        .Select(value =>
                            value!.Trim())
                        .Distinct(
                            StringComparer.CurrentCultureIgnoreCase)
                        .OrderBy(
                            value => value,
                            StringComparer.CurrentCultureIgnoreCase))
                .ToArray();

        comboBox.ItemsSource =
            choices;

        comboBox.SelectedItem =
            selected is not null &&
            choices.Contains(
                selected,
                StringComparer.CurrentCultureIgnoreCase)
                ? choices.First(choice =>
                    string.Equals(
                        choice,
                        selected,
                        StringComparison.CurrentCultureIgnoreCase))
                : allLabel;
    }

    /// <summary>
    /// Applies the current filters and selected sort mode without rereading files.
    /// </summary>
    private void ApplyFiltersAndSort()
    {
        IEnumerable<TaskItem> filtered =
            _loadedTasks;

        string? priority =
            PriorityFilterComboBox.SelectedItem as
            string;
        string? status =
            StatusFilterComboBox.SelectedItem as
            string;
        string? owner =
            OwnerFilterComboBox.SelectedItem as
            string;

        if (!string.IsNullOrWhiteSpace(
                priority) &&
            !string.Equals(
                priority,
                AllPrioritiesLabel,
                StringComparison.Ordinal))
        {
            filtered = filtered.Where(task =>
                string.Equals(
                    task.Priority,
                    priority,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(
                status) &&
            !string.Equals(
                status,
                AllStatusesLabel,
                StringComparison.Ordinal))
        {
            filtered = filtered.Where(task =>
                string.Equals(
                    task.Status,
                    status,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(
                owner) &&
            !string.Equals(
                owner,
                AllOwnersLabel,
                StringComparison.Ordinal))
        {
            filtered = filtered.Where(task =>
                string.Equals(
                    task.Owner,
                    owner,
                    StringComparison.CurrentCultureIgnoreCase));
        }

        if (OverdueOnlyCheckBox.IsChecked ==
            true)
        {
            filtered = filtered.Where(task =>
                task.IsOverdue);
        }

        IOrderedEnumerable<TaskItem> ordered =
            SortComboBox.SelectedIndex switch
            {
                1 => filtered
                    .OrderBy(task =>
                        WorkspaceTaskService.GetPriorityRank(
                            task.Priority))
                    .ThenBy(task =>
                        task.DueDate ??
                        DateOnly.MaxValue)
                    .ThenBy(
                        task => task.Text,
                        StringComparer.CurrentCultureIgnoreCase),
                2 => filtered
                    .OrderBy(
                        task => task.Owner ??
                        string.Empty,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(
                        task => task.Text,
                        StringComparer.CurrentCultureIgnoreCase),
                3 => filtered
                    .OrderBy(
                        task => task.Text,
                        StringComparer.CurrentCultureIgnoreCase),
                _ => filtered
                    .OrderBy(task =>
                        task.DueDate ??
                        DateOnly.MaxValue)
                    .ThenBy(task =>
                        WorkspaceTaskService.GetPriorityRank(
                            task.Priority))
                    .ThenBy(
                        task => task.Text,
                        StringComparer.CurrentCultureIgnoreCase)
            };

        TaskItem[] visible =
            ordered.ToArray();

        TasksList.ItemsSource =
            visible;

        int overdueCount =
            visible.Count(task =>
                task.IsOverdue);

        StatusText.Text =
            visible.Length == 0
                ? "Aucune tâche ne correspond aux filtres."
                : $"{visible.Length} tâche(s) · {overdueCount} en retard";
    }
}
