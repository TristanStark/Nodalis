using System.Windows;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit human plan editor. Previous content may be prefilled for a
/// revision, but no new plan is accepted or persisted without confirmation.
/// </summary>
public partial class ProjectRiskTreatmentDialog : Window
{
    private readonly ProjectRiskTreatmentPlan? _previous;

    /// <summary>Creates a strategy-and-measures proposal for a risk.</summary>
    /// <param name="entry">Previously displayed project-local risk.</param>
    public ProjectRiskTreatmentDialog(ProjectRiskEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _previous = entry.CurrentTreatment;
        InitializeComponent();
        HeadingText.Text = (_previous is null
            ? "Premier plan de traitement · "
            : "Réviser le plan de traitement · ") + entry.Card.Reference;
        ExplanationText.Text = entry.Card.Title + Environment.NewLine +
            "Éviter, réduire, transférer ou accepter le risque. " +
            "Documentez explicitement les mesures, leur responsable et la " +
            "réaction prévue en cas de réalisation. Pour une mesure sans objet, " +
            "expliquez pourquoi dans le champ correspondant. " +
            "Aucune action ni décision n'est automatiquement créée.";
        StrategyCombo.ItemsSource = new[]
        {
            "Éviter",
            "Réduire",
            "Transférer",
            "Accepter"
        };
        if (_previous is not null)
        {
            StrategyCombo.SelectedIndex = (int)_previous.Strategy;
            OwnerBox.Text = _previous.Owner;
            PreventionBox.Text = _previous.Prevention;
            MitigationBox.Text = _previous.Mitigation;
            ContingencyBox.Text = _previous.Contingency;
        }

        Loaded += (_, _) => StrategyCombo.Focus();
    }

    /// <summary>Complete strategy and human-owned measures.</summary>
    public ProjectRiskTreatmentPlan? Plan { get; private set; }

    /// <summary>Human author of the initial treatment or revision.</summary>
    public string? Actor { get; private set; }

    /// <summary>Human rationale for this version.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Validates all measures and returns an unpersisted proposal.</summary>
    /// <param name="sender">Submit button.</param>
    /// <param name="e">WPF click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (StrategyCombo.SelectedIndex is < 0 or > 3 ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            ActorBox.Text.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(RationaleBox.Text) ||
            RationaleBox.Text.Trim().Length < 10 ||
            RationaleBox.Text.Any(character => char.IsControl(character) &&
                character is not ('\r' or '\n')))
        {
            MessageBox.Show(this,
                "Sélectionnez une stratégie, un auteur et une justification " +
                "motivée d'au moins 10 caractères.",
                "Plan de traitement incomplet",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectRiskTreatmentPlan proposal = new(
            (ProjectRiskTreatmentStrategy)StrategyCombo.SelectedIndex,
            OwnerBox.Text.Trim(),
            PreventionBox.Text.Trim(),
            MitigationBox.Text.Trim(),
            ContingencyBox.Text.Trim());
        if (!ProjectRiskTreatmentMarkdownCodec.IsValidPlan(proposal) ||
            proposal == _previous)
        {
            MessageBox.Show(this,
                "Chaque mesure et le plan de contingence doivent être " +
                "explicitement renseignés (8 caractères minimum), avec " +
                "un responsable. Une révision identique n'est pas historisée.",
                "Mesures ou version manquantes",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Plan = proposal;
        Actor = ActorBox.Text.Trim();
        Rationale = RationaleBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>Dismisses this proposal without any disk modification.</summary>
    /// <param name="sender">Cancel command.</param>
    /// <param name="e">WPF event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
