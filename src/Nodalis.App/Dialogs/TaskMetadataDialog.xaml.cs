using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Tasks;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Edits the readable metadata attached to one Markdown checkbox task.
/// </summary>
public partial class TaskMetadataDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="TaskMetadataDialog"/>.
    /// </summary>
    /// <param name="task">The task to edit.</param>
    public TaskMetadataDialog(
            TaskItem task)
    {
        ArgumentNullException.ThrowIfNull(
            task);

        InitializeComponent();

        TaskTextBlock.Text =
            task.Text;
        OwnerTextBox.Text =
            task.Owner ??
            string.Empty;
        DueDateTextBox.Text =
            task.DueDate?.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture) ??
            string.Empty;
        PriorityComboBox.Text =
            task.Priority ??
            string.Empty;
        StatusComboBox.Text =
            task.Status ??
            string.Empty;
        TagsTextBox.Text =
            string.Join(
                ", ",
                task.Tags);
    }

    /// <summary>
    /// Gets the validated metadata after the dialog is accepted.
    /// </summary>
    public TaskMetadataUpdate? Metadata { get; private set; }

    /// <summary>
    /// Validates the fields and accepts the dialog.
    /// </summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The routed event.</param>
    private void Save_Click(
            object sender,
            RoutedEventArgs e)
    {
        string dueText =
            DueDateTextBox.Text.Trim();

        DateOnly? dueDate =
            null;

        if (dueText.Length > 0)
        {
            if (!DateOnly.TryParseExact(
                    dueText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out global::System.DateOnly parsed))
            {
                MessageBox.Show(
                    this,
                    "L'échéance doit utiliser le format AAAA-MM-JJ.",
                    "Métadonnées de la tâche",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            dueDate =
                parsed;
        }

        string[] tags = TagsTextBox.Text
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(tag =>
                tag.Trim()
                    .TrimStart(
                        '#'))
            .Where(tag =>
                tag.Length > 0)
            .Distinct(
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (!ValidateSingleLine(
                OwnerTextBox.Text,
                "responsable") ||
            !ValidateSingleLine(
                PriorityComboBox.Text,
                "priorité") ||
            !ValidateSingleLine(
                StatusComboBox.Text,
                "statut") ||
            tags.Any(tag =>
                tag.Contains(
                    '|') ||
                tag.Contains(
                    '\r') ||
                tag.Contains(
                    '\n')))
        {
            MessageBox.Show(
                this,
                "Les métadonnées doivent tenir sur une ligne et ne peuvent pas contenir « | ».",
                "Métadonnées de la tâche",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Metadata =
            new TaskMetadataUpdate
            {
                Owner =
                    NormalizeOptional(
                        OwnerTextBox.Text),
                DueDate =
                    dueDate,
                Priority =
                    NormalizeOptional(
                        PriorityComboBox.Text),
                Status =
                    NormalizeOptional(
                        StatusComboBox.Text),
                Tags =
                    tags
            };

        DialogResult =
            true;
    }

    /// <summary>
    /// Validates one compact metadata field.
    /// </summary>
    /// <param name="value">The field value.</param>
    /// <param name="displayName">The field name.</param>
    /// <returns><see langword="true"/> when the value is safe to serialize.</returns>
    private static bool ValidateSingleLine(
            string value,
            string displayName)
    {
        _ =
            displayName;

        return !value.Contains(
                   '|') &&
               !value.Contains(
                   '\r') &&
               !value.Contains(
                   '\n');
    }

    /// <summary>
    /// Converts an empty edited value to <see langword="null"/>.
    /// </summary>
    /// <param name="value">The edited value.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    private static string? NormalizeOptional(
            string value)
    {
        string normalized =
            value.Trim();

        return normalized.Length == 0
            ? null
            : normalized;
    }
}
