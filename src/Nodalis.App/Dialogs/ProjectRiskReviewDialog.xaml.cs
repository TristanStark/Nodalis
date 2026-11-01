using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Captures one deliberate human initial or residual assessment without
/// reading files, persisting data, or inferring any score from linked tasks.
/// </summary>
public partial class ProjectRiskReviewDialog : Window
{
    /// <summary>Builds an explicit 3x3 risk review for a verified selection.</summary>
    /// <param name="entry">Source card with frozen previous human reviews.</param>
    /// <param name="kind">Initial or residual assessment.</param>
    public ProjectRiskReviewDialog(
        ProjectRiskEntry entry, ProjectRiskReviewKind kind)
    {
        ArgumentNullException.ThrowIfNull(entry);
        InitializeComponent();
        HeadingText.Text = (kind == ProjectRiskReviewKind.Initial
            ? "Qualification initiale · " : "Réévaluation résiduelle · ") +
            entry.Card.Reference;
        DescriptionText.Text = entry.Card.Title + Environment.NewLine +
            (kind == ProjectRiskReviewKind.Initial
                ? "Cette première évaluation sera figée : elle ne pourra " +
                  "pas être corrigée par une réévaluation résiduelle."
                : "L'évaluation initiale reste intangible. Vous consignez " +
                  "un nouvel avis humain sans effacer le précédent.");
        ProbabilityCombo.ItemsSource = new[] { 1, 2, 3 };
        ImpactCombo.ItemsSource = new[] { 1, 2, 3 };

        // The user explicitly chooses numbers on each review. No rating is
        // silently pre-populated from linked objects or an earlier event.
        SeverityText.Text = "Criticité : non évaluée";
        Loaded += (_, _) => ProbabilityCombo.Focus();
    }

    /// <summary>Complete explicitly selected probability and impact.</summary>
    public ProjectRiskRating? Rating { get; private set; }

    /// <summary>Human reviewer accepting responsibility for the assessment.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human explanation permanently retained with the event.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Recomputes a preview only after both user choices exist.</summary>
    /// <param name="sender">Score combo.</param>
    /// <param name="e">Selection event.</param>
    private void Score_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SeverityText is null)
        {
            return;
        }

        if (ProbabilityCombo.SelectedItem is int probability &&
            ImpactCombo.SelectedItem is int impact)
        {
            ProjectRiskRating rating = new(probability, impact);
            SeverityText.Text = "Criticité proposée : " +
                ProjectRiskMatrix.Evaluate(rating) +
                " (" + (probability * impact) + "/9)";
        }
        else
        {
            SeverityText.Text = "Criticité : non évaluée";
        }
    }

    /// <summary>Validates manual inputs then returns an unpersisted proposal.</summary>
    /// <param name="sender">Accept button.</param>
    /// <param name="e">Click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (ProbabilityCombo.SelectedItem is not int probability ||
            ImpactCombo.SelectedItem is not int impact ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            ActorBox.Text.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(RationaleBox.Text) ||
            RationaleBox.Text.Trim().Length < 10 ||
            RationaleBox.Text.Any(character => char.IsControl(character) &&
                character is not ('\r' or '\n')))
        {
            MessageBox.Show(this,
                "Choisissez une probabilité et un impact entre 1 et 3, " +
                "puis précisez un auteur et une justification d'au moins " +
                "10 caractères. Le nom doit tenir sur une ligne.",
                "Avis humain requis", MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Rating = new ProjectRiskRating(probability, impact);
        Actor = ActorBox.Text.Trim();
        Rationale = RationaleBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>Dismisses without creating any audit entry.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">Click event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
