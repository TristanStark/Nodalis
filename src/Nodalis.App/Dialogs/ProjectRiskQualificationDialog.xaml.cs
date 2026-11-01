using System.Windows;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Collects a deliberate human business-state decision, independent from
/// the risk matrix, with no implicit creation, evaluation or source write.
/// </summary>
public partial class ProjectRiskQualificationDialog : Window
{
    private readonly ProjectRiskQualification _previous;

    /// <summary>Builds a decision form for one verified risk source.</summary>
    /// <param name="entry">Previously displayed canonical risk.</param>
    public ProjectRiskQualificationDialog(ProjectRiskEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _previous = entry.Card.Qualification;
        InitializeComponent();
        HeadingText.Text = "Décision métier · " + entry.Card.Reference;
        ContextText.Text = entry.Card.Title + Environment.NewLine +
            "Statut actuel : " + _previous + ". Choisissez une nouvelle " +
            "qualification, indépendamment de la criticité initiale ou résiduelle.";
        StatusCombo.ItemsSource = new[]
        {
            "À qualifier",
            "Validé",
            "Rejeté"
        };
        Loaded += (_, _) => StatusCombo.Focus();
    }

    /// <summary>New deliberately selected business state.</summary>
    public ProjectRiskQualification? Qualification { get; private set; }

    /// <summary>Name of the person making this business decision.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human explanation kept in the immutable decision log.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Proposes validated text for a second explicit confirmation.</summary>
    /// <param name="sender">Confirmation command.</param>
    /// <param name="e">Click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (StatusCombo.SelectedIndex is < 0 or > 2 ||
            StatusCombo.SelectedIndex == (int)_previous ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            ActorBox.Text.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(RationaleBox.Text) ||
            RationaleBox.Text.Trim().Length < 10 ||
            RationaleBox.Text.Any(character => char.IsControl(character) &&
                character is not ('\r' or '\n')))
        {
            MessageBox.Show(this,
                "Sélectionnez un statut différent du statut actuel, " +
                "puis renseignez un décideur et une justification " +
                "motivée (10 caractères minimum).",
                "Décision métier incomplète",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Qualification = (ProjectRiskQualification)StatusCombo.SelectedIndex;
        Actor = ActorBox.Text.Trim();
        Rationale = RationaleBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>Dismisses without creating any historical decision.</summary>
    /// <param name="sender">Cancel command.</param>
    /// <param name="e">Click event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
