using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Captures one deliberate progress declaration for a known treatment version.
/// Tasks have position-derived IDs today, so this form does not invent links.
/// </summary>
public partial class ProjectRiskMeasureDialog : Window
{
    private readonly ProjectRiskEntry _entry;

    /// <summary>Initializes a read-only snapshot of the current treatment.</summary>
    /// <param name="entry">Existing risk with at least one treatment plan.</param>
    public ProjectRiskMeasureDialog(ProjectRiskEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.CurrentTreatment is null)
        {
            throw new InvalidOperationException(
                "Un plan de traitement est requis pour suivre ses mesures.");
        }

        _entry = entry;
        InitializeComponent();
        HeadingText.Text = "Avancement des mesures · " + entry.Card.Reference;
        IntroText.Text = entry.Card.Title + Environment.NewLine +
            "Le plan " + entry.Treatments[^1].Id.ToString("D") +
            " possède trois rubriques indépendantes. Une nouvelle version " +
            "de plan possède ses propres statuts ; aucun ancien état " +
            "terminé n'est automatiquement reconduit.";
        MeasureCombo.ItemsSource = new[]
        {
            "Prévention",
            "Réduction / transfert",
            "Contingence"
        };
        StatusCombo.ItemsSource = new[]
        {
            "En attente",
            "En cours",
            "Bloquée",
            "Terminée (preuve requise)"
        };
        MeasureCombo.SelectedIndex = 0;
        Loaded += (_, _) => MeasureCombo.Focus();
    }

    /// <summary>Selected measure slot.</summary>
    public ProjectRiskMeasureKind? Kind { get; private set; }

    /// <summary>Human-confirmed new progress status.</summary>
    public ProjectRiskMeasureStatus? Status { get; private set; }

    /// <summary>Person confirming execution or blocking reason.</summary>
    public string? Actor { get; private set; }

    /// <summary>Explanation of the human transition.</summary>
    public string? Rationale { get; private set; }

    /// <summary>Textual proof declaration when the measure is completed.</summary>
    public string? Evidence { get; private set; }

    /// <summary>Updates the visible measure text and current human status.</summary>
    /// <param name="sender">Measure selection control.</param>
    /// <param name="e">WPF selection change.</param>
    private void Measure_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MeasureText is null || PreviousText is null ||
            MeasureCombo.SelectedIndex is < 0 or > 2)
        {
            return;
        }

        ProjectRiskMeasureKind kind =
            (ProjectRiskMeasureKind)MeasureCombo.SelectedIndex;
        ProjectRiskTreatmentPlan plan = _entry.CurrentTreatment!;
        MeasureText.Text = kind switch
        {
            ProjectRiskMeasureKind.Prevention => plan.Prevention,
            ProjectRiskMeasureKind.Mitigation => plan.Mitigation,
            _ => plan.Contingency
        };
        PreviousText.Text = "Statut actuel : " + _entry.MeasureStatus(kind);
        if (StatusCombo is not null)
        {
            StatusCombo.SelectedIndex = -1;
        }
    }

    /// <summary>Highlights when a documented proof is compulsory.</summary>
    /// <param name="sender">Progress status combo.</param>
    /// <param name="e">Selection changed.</param>
    private void Status_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EvidenceLabel is not null)
        {
            EvidenceLabel.Text = StatusCombo.SelectedIndex == 3
                ? "Preuve de réalisation obligatoire (10 caractères minimum)"
                : "Preuve ou commentaire complémentaire (facultatif)";
        }
    }

    /// <summary>Returns a validated proposal for separate human confirmation.</summary>
    /// <param name="sender">Submit button.</param>
    /// <param name="e">WPF click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (MeasureCombo.SelectedIndex is < 0 or > 2 ||
            StatusCombo.SelectedIndex is < 0 or > 3 ||
            string.IsNullOrWhiteSpace(ActorBox.Text) ||
            ActorBox.Text.Any(char.IsControl) ||
            !IsHumanText(RationaleBox.Text, 4000, 10) ||
            EvidenceBox.Text.Length > 0 &&
                !IsHumanText(EvidenceBox.Text, 2000, 1))
        {
            MessageBox.Show(this,
                "Choisissez une mesure et un nouveau statut, puis " +
                "renseignez l'auteur et une justification (10 caractères).",
                "Déclaration de mesure incomplète",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProjectRiskMeasureKind kind =
            (ProjectRiskMeasureKind)MeasureCombo.SelectedIndex;
        ProjectRiskMeasureStatus newStatus =
            (ProjectRiskMeasureStatus)StatusCombo.SelectedIndex;
        if (newStatus == _entry.MeasureStatus(kind) ||
            newStatus == ProjectRiskMeasureStatus.Completed &&
                !IsHumanText(EvidenceBox.Text, 2000, 10))
        {
            MessageBox.Show(this,
                "Le nouveau statut doit être différent de l'état actuel. " +
                "Une déclaration « Terminée » exige une preuve de " +
                "réalisation motivée (10 caractères minimum).",
                "Changement ou preuve requis",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Kind = kind;
        Status = newStatus;
        Actor = ActorBox.Text.Trim();
        Rationale = RationaleBox.Text.Trim();
        Evidence = string.IsNullOrWhiteSpace(EvidenceBox.Text)
            ? null : EvidenceBox.Text.Trim();
        DialogResult = true;
    }

    /// <summary>Checks bounded human prose and excludes control injection.</summary>
    /// <param name="text">User supplied text.</param>
    /// <param name="max">Maximum length.</param>
    /// <param name="min">Minimum trimmed length.</param>
    /// <returns>Whether the text can be recorded.</returns>
    private static bool IsHumanText(string text, int max, int min) =>
        text.Length <= max && text.Trim().Length >= min &&
        !text.Any(character => char.IsControl(character) &&
            character is not ('\r' or '\n'));

    /// <summary>Closes without changing a source file.</summary>
    /// <param name="sender">Cancel command.</param>
    /// <param name="e">WPF event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
