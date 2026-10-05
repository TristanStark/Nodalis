using System.Windows;
using Nodalis.Core.Milestones;

namespace Nodalis.App.Dialogs;

public partial class MilestoneEditorDialog : Window
{
    /// <summary>
    /// Initializes a new instance of <see cref="MilestoneEditorDialog"/>.
    /// </summary>
    /// <param name="existing">The existing milestone, when editing.</param>
    /// <param name="availableMilestones">The milestones that can be selected as prerequisites.</param>
    public MilestoneEditorDialog(
            MilestoneItem? existing = null,
            IReadOnlyList<MilestoneItem>? availableMilestones = null)
    {
        InitializeComponent();

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> candidates =
            (availableMilestones ?? Array.Empty<MilestoneItem>())
                .Where(candidate =>
                    existing is null ||
                    candidate.Id != existing.Id)
                .OrderBy(candidate =>
                    candidate.TargetDate ?? DateOnly.MaxValue)
                .ThenBy(
                    candidate => candidate.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        DependenciesListBox.ItemsSource = candidates;

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

            foreach (MilestoneItem candidate in candidates)
            {
                if (existing.DependencyIds.Contains(
                        candidate.Id))
                {
                    DependenciesListBox.SelectedItems.Add(
                        candidate);
                }
            }
        }

        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    public MilestoneDraft? Draft { get; private set; }

    /// <summary>
    /// Saves the edited milestone draft after validating the required fields.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The routed event.</param>
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

        global::System.Collections.Generic.IReadOnlyList<global::System.Guid> dependencyIds =
            DependenciesListBox.SelectedItems
                .Cast<MilestoneItem>()
                .Select(candidate =>
                    candidate.Id)
                .Distinct()
                .ToArray();

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
                : LinkTextBox.Text.Trim(),
            DependencyIds = dependencyIds
        };

        DialogResult = true;
    }
}
