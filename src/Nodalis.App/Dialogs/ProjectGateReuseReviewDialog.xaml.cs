using System.IO;
using System.Windows;
using Nodalis.App.Reliability;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// On-demand, read-only inspection of an existing historical gate decision.
/// Current file hashes are recaptured, and no decision or MEP is approved.
/// </summary>
public partial class ProjectGateReuseReviewDialog : Window
{
    private readonly ProjectGateRegistryService _service;
    private readonly string _projectDirectory;
    private readonly Guid _decisionId;
    private readonly string? _expectedSha;
    private bool _busy;

    /// <summary>Creates a non-authorizing review of one frozen decision.</summary>
    /// <param name="workspaceRoot">Active workspace root.</param>
    /// <param name="projectDirectory">Exact project containing the gate.</param>
    /// <param name="decision">Selected historical decision.</param>
    /// <param name="registrySha">Registry revision shown in the parent dialog.</param>
    public ProjectGateReuseReviewDialog(string workspaceRoot,
        string projectDirectory, ProjectGateDecision decision, string? registrySha)
    {
        ArgumentNullException.ThrowIfNull(decision);
        _service = new ProjectGateRegistryService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _decisionId = decision.Id;
        _expectedSha = registrySha;
        InitializeComponent();
        VersionBox.Text = decision.Version ?? string.Empty;
        EnvironmentBox.Text = decision.Environment ?? string.Empty;
        ScopeBox.Text = decision.Scope;
        HistoricalText.Text = "Gate : " + decision.GateName +
            "\nDécision : " + decision.Id.ToString("D") +
            " · " + decision.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm") +
            "\nVerdict historique : " + decision.Verdict +
            "\nVersion : " + (decision.Version ?? "non précisée") +
            " · environnement : " + (decision.Environment ?? "non précisé") +
            "\nPérimètre : " + decision.Scope;
    }

    /// <summary>Closes the review without changing anything.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">WPF routed click.</param>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Captures current evidence only after the user's click, verifies the
    /// registry SHA before and after I/O, then displays advisory differences.
    /// </summary>
    /// <param name="sender">Compare button.</param>
    /// <param name="e">WPF routed click.</param>
    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        CompareButton.IsEnabled = false;
        VersionBox.IsEnabled = false;
        EnvironmentBox.IsEnabled = false;
        ScopeBox.IsEnabled = false;
        try
        {
            await UiActionGuard.RunAsync(this, "Gates · requalification",
                async () =>
                {
                    ProjectGateRegistrySnapshot snapshot =
                        await _service.ReadAsync(_projectDirectory);
                    if (!string.Equals(snapshot.Sha256, _expectedSha,
                        StringComparison.Ordinal))
                    {
                        ReportBox.Text = "Le registre a changé depuis l'ouverture. " +
                            "Fermez ce contrôle et actualisez l'historique des gates.";
                        StatusText.Text = "Révision périmée · aucune conclusion.";
                        return;
                    }

                    ProjectGateDecision decision =
                        snapshot.Registry.Decisions.SingleOrDefault(item =>
                            item.Id == _decisionId) ??
                        throw new InvalidDataException(
                            "Décision historique absente du registre.");
                    List<ProjectGateEvidenceSnapshot> current = [];
                    int inaccessible = 0;
                    foreach (ProjectGateEvidenceSnapshot proof in decision.Evidence)
                    {
                        try
                        {
                            current.Add(await _service.CaptureEvidenceAsync(
                                proof.RelativePath));
                        }
                        catch (Exception exception) when (
                            exception is IOException or InvalidDataException or
                                UnauthorizedAccessException or ArgumentException)
                        {
                            // A missing/unreadable proof is NOT treated as a match.
                            inaccessible++;
                        }
                    }

                    ProjectGateRegistrySnapshot afterRead =
                        await _service.ReadAsync(_projectDirectory);
                    if (!string.Equals(afterRead.Sha256, snapshot.Sha256,
                        StringComparison.Ordinal))
                    {
                        ReportBox.Text = "Le registre a changé pendant la lecture. " +
                            "Recommencez après actualisation.";
                        StatusText.Text = "Révision concurrente · contrôle refusé.";
                        return;
                    }

                    ProjectGateReuseAssessment review =
                        ProjectGateReuseReview.Assess(snapshot.Registry,
                            _decisionId, VersionBox.Text, EnvironmentBox.Text,
                            ScopeBox.Text, current);
                    List<string> lines =
                    [
                        review.WithinHistoricalConditions
                            ? "Contexte vérifié : aucune différence dans les conditions connues."
                            : "Conditions historiques incompatibles : nouvelle décision requise.",
                        "Autorisation de MEP : NON.",
                        "Validation humaine explicite : TOUJOURS OBLIGATOIRE.",
                        "Preuves actuellement non accessibles : " + inaccessible,
                        string.Empty,
                        "Constats :"
                    ];
                    if (review.Findings.Count == 0)
                    {
                        lines.Add("  - Aucun écart dans les éléments vérifiés.");
                    }
                    else
                    {
                        lines.AddRange(review.Findings.Select(item =>
                            "  - " + (item.RequiresNewDecision
                                ? "[Nouvelle décision] " : "[Revue humaine] ") +
                            item.Detail));
                    }

                    lines.Add(string.Empty);
                    lines.Add("Hors de ce contrôle : procédure de déploiement, " +
                        "rollback, état réel, incidents, risques, durée de validité.");
                    lines.Add("Aucune décision nouvelle n'a été enregistrée.");
                    ReportBox.Text = string.Join(Environment.NewLine, lines);
                    StatusText.Text = "Consultation terminée · aucun fichier modifié.";
                });
        }
        finally
        {
            _busy = false;
            CompareButton.IsEnabled = true;
            VersionBox.IsEnabled = true;
            EnvironmentBox.IsEnabled = true;
            ScopeBox.IsEnabled = true;
        }
    }
}
