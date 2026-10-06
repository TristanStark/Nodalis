using System.Globalization;
using System.Windows;
using Nodalis.Core.Tasks;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Collects the text and metadata required to create one project task.
/// </summary>
public partial class TaskCreationDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="TaskCreationDialog"/>.
    /// </summary>
    public TaskCreationDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            TaskTextBox.Focus();
            TaskTextBox.SelectAll();
        };
    }

    /// <summary>
    /// Gets the validated task text after the dialog is accepted.
    /// </summary>
    public string TaskText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the validated metadata after the dialog is accepted.
    /// </summary>
    public TaskMetadataUpdate? Metadata { get; private set; }

    /// <summary>
    /// Validates the new task fields and accepts the dialog.
    /// </summary>
    /// <param name="sender">The create button.</param>
    /// <param name="e">The routed event.</param>
    private void Create_Click(
            object sender,
            RoutedEventArgs e)
    {
        string taskText =
            TaskTextBox.Text.Trim();

        if (taskText.Length == 0)
        {
            MessageBox.Show(
                this,
                "Le texte de la tâche est requis.",
                "Nouvelle tâche",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!ValidateSingleLine(
                taskText) ||
            taskText.Contains(
                '|'))
        {
            MessageBox.Show(
                this,
                "Le texte de la tâche doit tenir sur une ligne et ne peut pas contenir « | ».",
                "Nouvelle tâche",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

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
                    out DateOnly parsed))
            {
                MessageBox.Show(
                    this,
                    "L'échéance doit utiliser le format AAAA-MM-JJ.",
                    "Nouvelle tâche",
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
                OwnerTextBox.Text) ||
            !ValidateSingleLine(
                PriorityComboBox.Text) ||
            !ValidateSingleLine(
                StatusComboBox.Text) ||
            OwnerTextBox.Text.Contains(
                '|') ||
            PriorityComboBox.Text.Contains(
                '|') ||
            StatusComboBox.Text.Contains(
                '|') ||
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
                "Nouvelle tâche",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        TaskText =
            taskText;
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
    /// Checks that a value does not contain a line break.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <returns><see langword="true"/> when the value fits on one line.</returns>
    private static bool ValidateSingleLine(
            string value) =>
        !value.Contains(
            '\r') &&
        !value.Contains(
            '\n');

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
