using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Requirements;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit individual human acknowledgement of every stable CA identity.
/// A cancelled or incomplete review cannot authorize a filesystem mutation.
/// </summary>
public partial class ProjectRequirementCriteriaReviewDialog : Window
{
    private readonly IReadOnlyList<ProjectRequirementCriterion> _criteria;
    private readonly List<(Guid Id, CheckBox Box)> _checks = [];

    /// <summary>Displays source-owned CA identities for mandatory review.</summary>
    /// <param name="criteria">Unmodified SHA-bound preview criteria.</param>
    public ProjectRequirementCriteriaReviewDialog(
        IReadOnlyList<ProjectRequirementCriterion> criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.Count == 0 || criteria.Count > 99 ||
            criteria.Any(item =>
                item.Id is null || item.Id == Guid.Empty ||
                !item.LocallyIdentified ||
                !ProjectRequirementCriterionCodec.IsReference(item.Reference)) ||
            criteria.Select(item => item.Id).Distinct().Count() != criteria.Count)
        {
            throw new InvalidDataException(
                "La revue CA exige des identités toutes stables et distinctes.");
        }

        _criteria = criteria.ToArray();
        InitializeComponent();
        foreach (ProjectRequirementCriterion criterion in _criteria)
        {
            Guid id = criterion.Id!.Value;
            CheckBox box = new()
            {
                Margin = new Thickness(0, 6, 12, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = criterion.Reference + " · " + criterion.Title +
                        "\nGUID stable : " + id.ToString("D"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 625
                }
            };
            box.Checked += ReviewChanged;
            box.Unchecked += ReviewChanged;
            CriteriaPanel.Children.Add(box);
            _checks.Add((id, box));
        }

        RefreshApproval();
    }

    /// <summary>Only human-confirmed CA GUIDs, empty on cancellation.</summary>
    public IReadOnlyList<Guid> ApprovedCriteriaIds { get; private set; } = [];

    /// <summary>Required human-written justification of the reviewed transfer.</summary>
    public string Rationale { get; private set; } = string.Empty;

    /// <summary>Updates validity as the user approves each criterion.</summary>
    /// <param name="sender">Checkbox or reason text.</param>
    /// <param name="e">Routed control event.</param>
    private void ReviewChanged(object sender, RoutedEventArgs e) =>
        RefreshApproval();

    /// <summary>Allows confirmation only with every CA and a real reason.</summary>
    private void RefreshApproval()
    {
        if (ApproveButton is null || CountText is null ||
            RationaleBox is null)
        {
            return;
        }

        int checkedCount = _checks.Count(item => item.Box.IsChecked == true);
        CountText.Text = checkedCount + " / " + _criteria.Count +
            " critères vérifiés individuellement.";
        ApproveButton.IsEnabled = checkedCount == _criteria.Count &&
            !string.IsNullOrWhiteSpace(RationaleBox.Text) &&
            RationaleBox.Text.Trim().Length <= 2000;
    }

    /// <summary>Returns only the identities the user explicitly checked.</summary>
    /// <param name="sender">Confirmation button.</param>
    /// <param name="e">Click event.</param>
    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        RefreshApproval();
        if (!ApproveButton.IsEnabled)
        {
            return;
        }

        ApprovedCriteriaIds = _checks.Select(item => item.Id).ToArray();
        Rationale = RationaleBox.Text.Trim();
        DialogResult = true;
    }
}
