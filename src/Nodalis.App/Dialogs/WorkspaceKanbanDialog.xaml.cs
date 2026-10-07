using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Kanban;
using Nodalis.App.Reliability;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Displays a four-column Kanban projection of Markdown tasks.
/// </summary>
public partial class WorkspaceKanbanDialog : Window
{
    private readonly WorkspaceKanbanService _service;
    private IReadOnlyList<TaskItem> _tasks =
        [];
    private Point _dragStart;

    /// <summary>
    /// Initializes a new workspace Kanban dialog.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceKanbanDialog(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _service =
            new WorkspaceKanbanService(
                workspaceRoot);

        InitializeComponent();

        Loaded +=
            WorkspaceKanbanDialog_Loaded;
    }

    /// <summary>
    /// Gets the task selected for source navigation.
    /// </summary>
    public TaskItem? SelectedTask { get; private set; }

    /// <summary>
    /// Loads the board after the dialog becomes visible.
    /// </summary>
    /// <param name="sender">The dialog.</param>
    /// <param name="e">The event arguments.</param>
    private async void WorkspaceKanbanDialog_Loaded(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Kanban · chargement",
            async () =>
            {
            Loaded -=
                WorkspaceKanbanDialog_Loaded;
    
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Refreshes the board from Markdown sources.
    /// </summary>
    /// <param name="sender">The refresh button.</param>
    /// <param name="e">The event arguments.</param>
    private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Kanban · actualiser",
            async () =>
            {
            await RefreshAsync();
            });
    }

    /// <summary>
    /// Applies filters after one selection changes.
    /// </summary>
    /// <param name="sender">The changed filter.</param>
    /// <param name="e">The selection arguments.</param>
    private void Filter_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RebuildBoard();
        }
    }

    /// <summary>
    /// Starts a task-card drag after the pointer exceeds the system drag threshold.
    /// </summary>
    /// <param name="sender">The source list.</param>
    /// <param name="e">The mouse arguments.</param>
    private void TaskList_PreviewMouseMove(
            object sender,
            MouseEventArgs e)
    {
        if (e.LeftButton !=
                MouseButtonState.Pressed)
        {
            _dragStart =
                e.GetPosition(
                    this);
            return;
        }

        Point current =
            e.GetPosition(
                this);

        if (Math.Abs(
                current.X -
                _dragStart.X) <
                SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(
                current.Y -
                _dragStart.Y) <
                SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        if (sender is not ListBox listBox ||
            listBox.SelectedItem is not TaskItem task)
        {
            return;
        }

        DragDrop.DoDragDrop(
            listBox,
            task,
            DragDropEffects.Move);
    }

    /// <summary>
    /// Opens the Markdown source of a double-clicked task.
    /// </summary>
    /// <param name="sender">The source list.</param>
    /// <param name="e">The mouse arguments.</param>
    private void TaskList_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox ||
            listBox.SelectedItem is not TaskItem task)
        {
            return;
        }

        SelectedTask =
            task;
        DialogResult =
            true;
    }

    /// <summary>
    /// Advertises task-card move semantics over a board column.
    /// </summary>
    /// <param name="sender">The target list.</param>
    /// <param name="e">The drag arguments.</param>
    private void Column_DragOver(
            object sender,
            DragEventArgs e)
    {
        e.Effects =
            e.Data.GetDataPresent(
                typeof(TaskItem))
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled =
            true;
    }

    /// <summary>
    /// Persists a board move by rewriting the source task's status metadata and completion checkbox.
    /// </summary>
    /// <param name="sender">The target list.</param>
    /// <param name="e">The drag arguments.</param>
    private async void Column_Drop(
            object sender,
            DragEventArgs e)
    {
        await UiActionGuard.RunAsync(
            this,
            "Kanban · déplacer une tâche",
            async () =>
            {
            if (sender is not ListBox listBox ||
                listBox.Tag is not string targetStatus ||
                e.Data.GetData(
                    typeof(TaskItem)) is not TaskItem task)
            {
                return;
            }
    
            try
            {
                await _service.MoveAsync(
                    task,
                    targetStatus);
    
                StatusText.Text =
                    "Tâche déplacée vers « " +
                    targetStatus +
                    " » · source Markdown mise à jour.";
    
                await RefreshAsync();
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                InvalidOperationException or
                ArgumentOutOfRangeException)
            {
                StatusText.Text =
                    "Déplacement refusé : " +
                    exception.Message;
            }
            });
    }

    /// <summary>
    /// Reloads task sources and refreshes all filters and columns.
    /// </summary>
    /// <returns>A task representing the refresh.</returns>
    private async Task RefreshAsync()
    {
        StatusText.Text =
            "Lecture des tâches Markdown…";

        try
        {
            _tasks =
                await _service.RefreshAsync();

            RebuildFilters();
            RebuildBoard();

            StatusText.Text =
                _tasks.Count +
                " tâche(s) · aucune donnée Kanban secondaire.";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException)
        {
            _tasks =
                [];
            ClearBoard();
            StatusText.Text =
                exception.Message;
        }
    }

    /// <summary>
    /// Rebuilds project, owner, and priority filters while preserving previous selections.
    /// </summary>
    private void RebuildFilters()
    {
        RebuildFilter(
            ProjectFilter,
            "Tous les projets",
            _tasks
                .Where(task =>
                    task.ProjectId is not null)
                .Select(task =>
                    new FilterOption(
                        task.ProjectId!.Value.ToString(
                            "D"),
                        task.ProjectName ??
                        task.ProjectId.Value.ToString(
                            "D"))));

        RebuildFilter(
            OwnerFilter,
            "Tous les responsables",
            _tasks
                .Where(task =>
                    !string.IsNullOrWhiteSpace(
                        task.Owner))
                .Select(task =>
                    new FilterOption(
                        task.Owner!,
                        task.Owner!)));

        RebuildFilter(
            PriorityFilter,
            "Toutes les priorités",
            _tasks
                .Where(task =>
                    !string.IsNullOrWhiteSpace(
                        task.Priority))
                .Select(task =>
                    new FilterOption(
                        task.Priority!,
                        task.Priority!)));
    }

    /// <summary>
    /// Rebuilds one simple text filter.
    /// </summary>
    /// <param name="comboBox">The filter control.</param>
    /// <param name="allLabel">The all-values label.</param>
    /// <param name="values">Available values.</param>
    private static void RebuildFilter(
            ComboBox comboBox,
            string allLabel,
            IEnumerable<FilterOption> values)
    {
        string? selected =
            (comboBox.SelectedItem as FilterOption)?.Key;

        List<FilterOption> options =
            new List<FilterOption>
            {
                new FilterOption(
                    null,
                    allLabel)
            };

        options.AddRange(
            values
                .GroupBy(option =>
                    option.Key,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(group =>
                    group.First())
                .OrderBy(option =>
                    option.Label,
                    StringComparer.CurrentCultureIgnoreCase));

        comboBox.ItemsSource =
            options;
        comboBox.SelectedItem =
            options.FirstOrDefault(option =>
                string.Equals(
                    option.Key,
                    selected,
                    StringComparison.CurrentCultureIgnoreCase)) ??
            options[0];
    }

    /// <summary>
    /// Applies all active filters and assigns tasks to the four canonical workflow columns.
    /// </summary>
    private void RebuildBoard()
    {
        string? projectKey =
            (ProjectFilter.SelectedItem as FilterOption)?.Key;
        string? owner =
            (OwnerFilter.SelectedItem as FilterOption)?.Key;
        string? priority =
            (PriorityFilter.SelectedItem as FilterOption)?.Key;

        IReadOnlyList<TaskItem> filtered =
            _tasks
                .Where(task =>
                    projectKey is null ||
                    string.Equals(
                        task.ProjectId?.ToString(
                            "D"),
                        projectKey,
                        StringComparison.OrdinalIgnoreCase))
                .Where(task =>
                    owner is null ||
                    string.Equals(
                        task.Owner,
                        owner,
                        StringComparison.CurrentCultureIgnoreCase))
                .Where(task =>
                    priority is null ||
                    string.Equals(
                        task.Priority,
                        priority,
                        StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        TodoList.ItemsSource =
            filtered.Where(task =>
                    WorkspaceKanbanService.GetColumnStatus(
                        task) ==
                    WorkspaceKanbanService.TodoStatus)
                .ToArray();

        InProgressList.ItemsSource =
            filtered.Where(task =>
                    WorkspaceKanbanService.GetColumnStatus(
                        task) ==
                    WorkspaceKanbanService.InProgressStatus)
                .ToArray();

        BlockedList.ItemsSource =
            filtered.Where(task =>
                    WorkspaceKanbanService.GetColumnStatus(
                        task) ==
                    WorkspaceKanbanService.BlockedStatus)
                .ToArray();

        DoneList.ItemsSource =
            filtered.Where(task =>
                    WorkspaceKanbanService.GetColumnStatus(
                        task) ==
                    WorkspaceKanbanService.DoneStatus)
                .ToArray();
    }

    /// <summary>
    /// Clears every board column.
    /// </summary>
    private void ClearBoard()
    {
        TodoList.ItemsSource =
            null;
        InProgressList.ItemsSource =
            null;
        BlockedList.ItemsSource =
            null;
        DoneList.ItemsSource =
            null;
    }

    /// <summary>
    /// Represents one reusable filter option.
    /// </summary>
    private sealed record FilterOption
    {
        /// <summary>
        /// Initializes a filter option.
        /// </summary>
        /// <param name="key">The filter key, or null for all values.</param>
        /// <param name="label">The display label.</param>
        public FilterOption(
                string? key,
                string label)
        {
            Key =
                key;
            Label =
                label;
        }

        /// <summary>Gets the optional filter key.</summary>
        public string? Key { get; }

        /// <summary>Gets the display label.</summary>
        public string Label { get; }
    }
}
