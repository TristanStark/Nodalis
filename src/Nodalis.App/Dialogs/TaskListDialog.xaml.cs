using System.IO;
using System.Windows;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Tasks;

namespace Nodalis.App.Dialogs;

public partial class TaskListDialog : Window
{
    private readonly WorkspaceTaskService _tasks;
    private readonly string? _contextPath;
    private readonly Func<TaskItem, bool, Task> _toggleTaskAsync;

    /// <summary>
    /// Initializes a new instance of <see cref="TaskListDialog"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="scopeLabel">The <c>scopeLabel</c> value.</param>
    /// <param name="toggleTaskAsync">The <c>toggleTaskAsync</c> value.</param>
    public TaskListDialog(
            string workspaceRoot,
            string? contextPath,
            string scopeLabel,
            Func<TaskItem, bool, Task> toggleTaskAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(toggleTaskAsync);

        _tasks = new WorkspaceTaskService(
            workspaceRoot);
        _contextPath = contextPath;
        _toggleTaskAsync = toggleTaskAsync;

        InitializeComponent();

        ScopeText.Text = scopeLabel;

        Loaded += async (_, _) =>
            await RefreshAsync();
    }

    public TaskItem? SelectedTask { get; private set; }

    /// <summary>
    /// Performs the <c>IncludeCompletedCheckBox_Changed</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async void IncludeCompletedCheckBox_Changed(
            object sender,
            RoutedEventArgs e) =>
            await RefreshAsync();

    /// <summary>
    /// Performs the <c>TaskCheckBox_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async void TaskCheckBox_Click(
            object sender,
            RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox checkBox ||
            checkBox.DataContext is not TaskItem task)
        {
            return;
        }

        try
        {
            bool completed = checkBox.IsChecked == true;

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
            StatusText.Text = exception.Message;
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Performs the <c>TasksList_MouseDoubleClick</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private void TasksList_MouseDoubleClick(
            object sender,
            System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TasksList.SelectedItem is not TaskItem task)
        {
            return;
        }

        SelectedTask = task;
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
            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> tasks = await _tasks.GetTasksAsync(
                _contextPath,
                IncludeCompletedCheckBox.IsChecked == true);

            TasksList.ItemsSource = tasks;
            StatusText.Text =
                tasks.Count == 0
                    ? "Aucune tâche dans ce contexte."
                    : $"{tasks.Count} tâche(s).";
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            TasksList.ItemsSource = null;
            StatusText.Text = exception.Message;
        }
    }
}
