using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit, project-local post-MEP REC/incident review. Only produces an
/// in-memory human record, never changes the REC or maintenance sources.
/// </summary>
public partial class ProjectDeploymentPostCheckDialog : Window
{
    private readonly ProjectDeploymentOperation _operation;
    private readonly ProjectDeploymentAttempt _attempt;
    private readonly List<IncidentOption> _incidents;

    /// <summary>Collects matching project REC passages and canonical INC IDs.</summary>
    /// <param name="operation">Source-verified MEP before closure.</param>
    /// <param name="catalog">Existing campaigns belonging to this project.</param>
    /// <param name="incidents">Project-local incident candidates.</param>
    public ProjectDeploymentPostCheckDialog(
        ProjectDeploymentOperation operation, ProjectTestCampaignCatalog catalog,
        ProjectGateIncidentCatalog incidents)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(incidents);
        ProjectDeploymentMarkdownCodec.Validate(operation);
        if (operation.Closure is not null || operation.Attempts.Count == 0)
        {
            throw new InvalidDataException(
                "Aucune tentative ouverte à une revue post-déploiement.");
        }

        _operation = operation;
        _attempt = operation.Attempts[^1];
        ProjectDeploymentAttemptState attemptState =
            ProjectDeploymentMarkdownCodec.GetAttemptState(
                operation, _attempt.Id);
        if (!attemptState.IsFinished)
        {
            throw new InvalidDataException(
                "Terminez la tentative avant le contrôle post-déploiement.");
        }

        List<PassageOption> choices = [];
        foreach (ProjectTestCampaignEntry campaign in catalog.Campaigns)
        {
            foreach (ProjectTestPassage passage in campaign.Campaign.Passages)
            {
                if (string.Equals(passage.Environment, _attempt.Environment,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(passage.ApplicationVersion,
                        _attempt.TargetVersion, StringComparison.Ordinal))
                {
                    choices.Add(new PassageOption(campaign, passage,
                        campaign.Campaign.Reference + " · passage " +
                        passage.Number + " · " + campaign.Campaign.Title));
                }
            }
        }

        _incidents = incidents.Candidates.Select(item =>
            new IncidentOption(item.Id, item.Label)).ToList();
        InitializeComponent();
        CampaignCombo.ItemsSource = choices.OrderBy(item => item.Label,
            StringComparer.CurrentCultureIgnoreCase).ToArray();
        CampaignCombo.SelectedIndex = choices.Count > 0 ? 0 : -1;
        IncidentList.ItemsSource = _incidents;
        List<VerdictOption> verdicts =
        [
            new(ProjectDeploymentPostCheckVerdict.ToReview, "À requalifier"),
            new(ProjectDeploymentPostCheckVerdict.NonConform, "Non conforme")
        ];
        if (attemptState.LastEventKind ==
            ProjectDeploymentEventKind.FinishedSucceeded)
        {
            verdicts.Add(new VerdictOption(
                ProjectDeploymentPostCheckVerdict.Conform,
                "Conforme · validation humaine"));
            verdicts.Add(new VerdictOption(
                ProjectDeploymentPostCheckVerdict.ConformWithReservations,
                "Conforme avec réserves"));
        }

        VerdictCombo.ItemsSource = verdicts;
        VerdictCombo.SelectedIndex = 0;
        AttemptText.Text = operation.Reference + " · tentative " +
            _attempt.Number + " · " + _attempt.Environment + " · " +
            _attempt.TargetVersion;
        UpdateSummary();
    }

    /// <summary>Number of REC passages matching the exact MEP context.</summary>
    public int EligiblePassages => CampaignCombo.Items.Count;

    /// <summary>One human-confirmed, unsaved historical REC assessment.</summary>
    public ProjectDeploymentPostCheck? ConfirmedCheck { get; private set; }

    /// <summary>Builds an exact immutable snapshot from the chosen REC entry.</summary>
    /// <returns>Explicit human assessment to be rechecked on persistence.</returns>
    private ProjectDeploymentPostCheck BuildCheck()
    {
        if (CampaignCombo.SelectedItem is not PassageOption selected ||
            VerdictCombo.SelectedItem is not VerdictOption verdict)
        {
            throw new InvalidDataException(
                "Choisissez un passage REC et un verdict explicite.");
        }

        ProjectTestPassage passage = selected.Passage;
        return new ProjectDeploymentPostCheck
        {
            Id = Guid.NewGuid(),
            AttemptId = _attempt.Id,
            CampaignId = selected.Campaign.Campaign.Id,
            PassageId = passage.Id,
            CampaignSha256 = selected.Campaign.SourceSha256,
            FrozenTests = passage.Tests.Count,
            Passed = passage.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.Passed),
            Failed = passage.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.Failed),
            Blocked = passage.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.Blocked),
            NotApplicable = passage.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.NotApplicable),
            IncidentIds = _incidents.Where(item => item.IsSelected)
                .Select(item => item.Id).ToArray(),
            RecordedUtc = DateTimeOffset.UtcNow,
            Actor = ActorBox.Text.Trim(),
            Rationale = RationaleBox.Text.Trim(),
            Verdict = verdict.Value,
            Reservations = string.IsNullOrWhiteSpace(ReservationsBox.Text)
                ? null : ReservationsBox.Text.Trim()
        };
    }

    /// <summary>Updates read-only exact passage counts, never an inferred GO.</summary>
    private void UpdateSummary()
    {
        if (CampaignCombo.SelectedItem is not PassageOption choice)
        {
            PassageSummaryText.Text = "Aucun passage du projet ne correspond " +
                "à la version et à l'environnement de cette tentative.";
            return;
        }

        ProjectTestPassage passage = choice.Passage;
        int passed = passage.Results.Count(item =>
            item.Outcome == ProjectTestOutcome.Passed);
        int failed = passage.Results.Count(item =>
            item.Outcome == ProjectTestOutcome.Failed);
        int blocked = passage.Results.Count(item =>
            item.Outcome == ProjectTestOutcome.Blocked);
        int corrections = choice.Campaign.Campaign.ResultCorrections?
            .Count(item => item.PassageId == passage.Id) ?? 0;
        PassageSummaryText.Text = passage.Tests.Count + " test(s) · " +
            passed + " conforme(s) · " + failed + " non conforme(s) · " +
            blocked + " bloqué(s) · " +
            (passage.Tests.Count - passage.Results.Count) +
            " non exécuté(s) · résultats d'origine · " +
            corrections + " rectification(s) administrative(s) à relire · " +
            "révision SHA-256 conservée";
    }

    /// <summary>Updates the count for the explicitly chosen frozen passage.</summary>
    /// <param name="sender">Campaign selector.</param>
    /// <param name="e">Selection change.</param>
    private void CampaignCombo_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        UpdateSummary();
    }

    /// <summary>Asks the person to confirm all warnings before append-only save.</summary>
    /// <param name="sender">Review button.</param>
    /// <param name="e">WPF routed click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        ProjectDeploymentPostCheck check;
        try
        {
            check = BuildCheck();
            ProjectDeploymentEditor.RecordPostCheck(_operation, check);
        }
        catch (InvalidDataException exception)
        {
            MessageBox.Show(this, exception.Message,
                "Contrôle post-MEP invalide", MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<string> warnings =
            ProjectDeploymentPostCheckPolicy.Warnings(check);
        string description = string.Join("\n", warnings.Take(5));
        if (CampaignCombo.SelectedItem is PassageOption selected)
        {
            int corrections = selected.Campaign.Campaign.ResultCorrections?
                .Count(item => item.PassageId == selected.Passage.Id) ?? 0;
            if (corrections > 0)
            {
                description += "\n" + corrections +
                    " rectification(s) administrative(s) : lire le " +
                    "journal REC avant de statuer.";
            }
        }
        if (MessageBox.Show(this,
            "Enregistrer un lien post-déploiement à ce passage REC ?\n\n" +
            description + "\n\n" +
            "Votre verdict : " + check.Verdict + ". " +
            "Les preuves et IDs seront revérifiés lors de la sauvegarde. " +
            "Nodalis ne modifiera aucun résultat REC, incident ou GO.",
            "Confirmer la revue humaine",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) ==
            MessageBoxResult.Yes)
        {
            ConfirmedCheck = check;
            DialogResult = true;
        }
    }

    /// <summary>One stable project-local passage candidate.</summary>
    /// <param name="Campaign">Exact source entry.</param>
    /// <param name="Passage">Frozen execution context.</param>
    /// <param name="Label">Readable dropdown label.</param>
    private sealed record PassageOption(
        ProjectTestCampaignEntry Campaign, ProjectTestPassage Passage, string Label);

    /// <summary>Human verdict option without inferred execution success.</summary>
    /// <param name="Value">Persisted assessment.</param>
    /// <param name="Label">Readable label.</param>
    private sealed record VerdictOption(
        ProjectDeploymentPostCheckVerdict Value, string Label);

    /// <summary>Checkbox state for one canonical maintenance incident.</summary>
    private sealed class IncidentOption
    {
        /// <summary>Stable INC ID.</summary>
        public string Id { get; }

        /// <summary>Current incident description and ID.</summary>
        public string Label { get; }

        /// <summary>Whether the human explicitly included the incident.</summary>
        public bool IsSelected { get; set; }

        /// <summary>Creates one independently selectable incident.</summary>
        /// <param name="id">Canonical INC ID.</param>
        /// <param name="label">Readable label.</param>
        public IncidentOption(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }
}
