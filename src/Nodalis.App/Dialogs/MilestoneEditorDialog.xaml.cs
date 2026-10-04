using System.Windows;
using Nodalis.Core.Milestones;

namespace Nodalis.App.Dialogs;

public partial class MilestoneEditorDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="MilestoneEditorDialog"/>.
    /// </summary>
    /// <param name="existing">The <c>existing</c> value.</param>
    public MilestoneEditorDialog(
            MilestoneItem? existing = null)
    {
        InitializeComponent();

        if (existing is null)
        {
            Title = "Nouveau jalon";
            StatusTextBox.Text = "À faire";
        }
        else
        {
            Title = "Modifier le jalon";
            NameTextBox.Text = existing.Name;
            TargetDatePicker.SelectedDate =
                existing.TargetDate?.ToDateTime(TimeOnly.MinValue);
            StatusTextBox.Text = existing.Status;
            DescriptionTextBox.Text = existing.Description;
            LinkTextBox.Text = existing.Link ?? string.Empty;
        }

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public MilestoneDraft? Draft { get; private set; }

    /// <summary>
    /// Performs the <c>Save_Click</c> operation.
    /// </summary>
    /// <param name="sender">The <c>sender</c> value.</param>
    /// <param name="e">The <c>e</c> value.</param>
    private void Save_Click(
            object sender,
            RoutedEventArgs e)
    {
        string name = NameTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(
                this,
                "Le nom du jalon est requis.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            NameTextBox.Focus();
            return;
        }

        Draft = new MilestoneDraft
        {
            Name = name,
            TargetDate = TargetDatePicker.SelectedDate is DateTime date
                ? DateOnly.FromDateTime(date)
                : null,
            Status = StatusTextBox.Text.Trim(),
            Description = DescriptionTextBox.Text.Trim(),
            Link = string.IsNullOrWhiteSpace(LinkTextBox.Text)
                ? null
                : LinkTextBox.Text.Trim()
        };

        DialogResult = true;
    }
}
