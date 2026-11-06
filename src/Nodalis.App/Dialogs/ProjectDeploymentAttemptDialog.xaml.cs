using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Collects an explicit human attempt, exact target context, two source
/// documents and the GO review. It never writes or initiates a deployment.
/// </summary>
public partial class ProjectDeploymentAttemptDialog : Window
{
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Prepares a human form with already recorded GO decisions.</summary>
    /// <param name="workspaceRoot">Workspace for local source selection.</param>
    /// <param name="eligibleDecisions">Latest human approving gate decisions.</param>
    public ProjectDeploymentAttemptDialog(string workspaceRoot,
        IReadOnlyList<ProjectGateDecision> eligibleDecisions)
    {
        ArgumentNullException.ThrowIfNull(eligibleDecisions);
        _sources = new ProjectJourneySourceService(workspaceRoot);
        InitializeComponent();
        GateDecisionCombo.ItemsSource = eligibleDecisions.Select(item =>
            new DecisionChoice(item,
                item.GateName + " · " + item.Verdict + " · " +
                (item.Version ?? "version non renseignée") + " / " +
                (item.Environment ?? "environnement non renseigné")))
            .ToArray();
        GateDecisionCombo.SelectedIndex =
            eligibleDecisions.Count == 0 ? -1 : 0;
    }

    /// <summary>Reference chosen by the human to capture as procedure.</summary>
    public string ProcedurePath => ProcedureBox.Text.Trim();

    /// <summary>Reference chosen by the human to capture as rollback plan.</summary>
    public string RollbackPath => RollbackBox.Text.Trim();

    /// <summary>
    /// Constructs an immutable user-submitted attempt using fingerprints
    /// captured by the caller immediately before a conditional commit.
    /// </summary>
    /// <param name="number">Next attempt number from current ledger.</param>
    /// <param name="procedure">Current verified procedure SHA.</param>
    /// <param name="rollback">Current verified rollback SHA.</param>
    /// <returns>Declared attempt, not permission to execute.</returns>
    public ProjectDeploymentAttempt BuildAttempt(int number,
        ProjectGateEvidenceSnapshot procedure,
        ProjectGateEvidenceSnapshot rollback)
    {
        if (GateDecisionCombo.SelectedItem is not DecisionChoice selected)
        {
            throw new InvalidDataException("Choisissez une décision GO.");
        }

        return new ProjectDeploymentAttempt
        {
            Id = Guid.NewGuid(),
            Number = number,
            StartedUtc = DateTimeOffset.UtcNow,
            StartedBy = OperatorBox.Text.Trim(),
            Rationale = ReasonBox.Text.Trim(),
            ObservedVersionBefore = ObservedBox.Text.Trim(),
            TargetVersion = TargetBox.Text.Trim(),
            Environment = EnvironmentBox.Text.Trim(),
            Scope = ScopeBox.Text.Trim(),
            Procedure = procedure,
            RollbackProcedure = rollback,
            GateDecisionId = selected.Decision.Id,
            AuthorizationReviewedBy = ReviewerBox.Text.Trim(),
            AuthorizationRationale = ReviewReasonBox.Text.Trim()
        };
    }

    /// <summary>Prefills only gate context; never the actual observed version.</summary>
    /// <param name="sender">Gate dropdown.</param>
    /// <param name="e">Selection event.</param>
    private void GateDecisionCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (GateDecisionCombo.SelectedItem is DecisionChoice selected)
        {
            TargetBox.Text = selected.Decision.Version ?? string.Empty;
            EnvironmentBox.Text = selected.Decision.Environment ?? string.Empty;
            ScopeBox.Text = selected.Decision.Scope;
        }
    }

    /// <summary>Opens the safe workspace picker for the deployment procedure.</summary>
    /// <param name="sender">Picker button.</param>
    /// <param name="e">WPF click event.</param>
    private void ChooseProcedure_Click(object sender, RoutedEventArgs e) =>
        ChooseSource(ProcedureBox);

    /// <summary>Opens the safe workspace picker for the rollback procedure.</summary>
    /// <param name="sender">Picker button.</param>
    /// <param name="e">WPF click event.</param>
    private void ChooseRollback_Click(object sender, RoutedEventArgs e) =>
        ChooseSource(RollbackBox);

    /// <summary>
    /// Picks one existing, workspace-confined file without following
    /// symbolic links or permitting an external source.
    /// </summary>
    /// <param name="target">Reference field to fill.</param>
    private void ChooseSource(TextBox target)
    {
        OpenFileDialog picker = new()
        {
            Title = "Choisir une procédure existante du workspace",
            Filter = "Documents|*.md;*.txt|Tous les fichiers|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            target.Text = _sources.GetReferenceForExistingFile(
                picker.FileName);
        }
        catch (Exception exception) when (exception is ArgumentException or
            IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, exception.Message,
                "Source MEP non sûre", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>Validates the form and requests explicit human approval.</summary>
    /// <param name="sender">Review button.</param>
    /// <param name="e">WPF click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (GateDecisionCombo.SelectedItem is not DecisionChoice ||
            !HasText(ObservedBox) || !HasText(TargetBox) ||
            !HasText(EnvironmentBox) || !HasText(ScopeBox) ||
            !HasText(OperatorBox) || !HasText(ReasonBox) ||
            !HasText(ReviewerBox) || !HasText(ReviewReasonBox) ||
            !ProjectJourneySourcePath.IsSafe(ProcedurePath) ||
            !ProjectJourneySourcePath.IsSafe(RollbackPath))
        {
            MessageBox.Show(this,
                "Choisissez un GO et deux sources du workspace, puis " +
                "renseignez toutes les versions, le périmètre, les auteurs " +
                "et les motifs. La version actuelle doit être constatée " +
                "et non déduite automatiquement.",
                "Tentative incomplète", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this,
            "Consigner une nouvelle tentative avec l'état observé « " +
            ObservedBox.Text.Trim() + " », la version cible « " +
            TargetBox.Text.Trim() + " » et le périmètre « " +
            ScopeBox.Text.Trim() + " » ?\n\n" +
            "Les preuves et l'ancien GO seront recontrôlés avant écriture. " +
            "Il restera nécessaire d'examiner humainement incidents, risques " +
            "et conditions réelles. Aucune MEP n'est exécutée.",
            "Nouvelle tentative · confirmation",
            MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes)
        {
            DialogResult = true;
        }
    }

    /// <summary>Checks one human-entered nonblank field.</summary>
    /// <param name="field">Text input.</param>
    /// <returns>True if nonblank.</returns>
    private static bool HasText(TextBox field) =>
        !string.IsNullOrWhiteSpace(field.Text);

    /// <summary>One historical gate candidate, never inferred authorization.</summary>
    /// <param name="Decision">Historical approving gate.</param>
    /// <param name="Label">Readable selection.</param>
    private sealed record DecisionChoice(
        ProjectGateDecision Decision, string Label);
}
