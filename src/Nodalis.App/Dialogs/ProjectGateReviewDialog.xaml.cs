using System.Windows;
using Nodalis.Core.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Human-only gate verdict editor. No decision is written until its parent
/// registry verifies the source hashes and the user confirms the change.
/// </summary>
public partial class ProjectGateReviewDialog : Window
{
    private readonly ProjectGateDefinition _gate;
    private readonly List<CriterionRow> _criteria;
    private readonly List<ProofRow> _proofs;
    private readonly GateVerdictChoice[] _verdicts =
    [
        new(ProjectGateVerdict.Deferred, "Reporté"),
        new(ProjectGateVerdict.NoGo, "NO-GO"),
        new(ProjectGateVerdict.GoWithReservations, "GO sous réserves"),
        new(ProjectGateVerdict.Go, "GO")
    ];

    /// <summary>Creates a gate review from the current definition.</summary>
    /// <param name="gate">Existing gate.</param>
    /// <param name="sourcePaths">Existing operation proof references.</param>
    public ProjectGateReviewDialog(ProjectGateDefinition gate,
        IReadOnlyList<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        _gate = gate;
        InitializeComponent();
        GateNameText.Text = "Décider · " + gate.Name;
        VerdictCombo.ItemsSource = _verdicts;
        VerdictCombo.SelectedIndex = 0;
        _criteria = gate.Criteria.Select(item => new CriterionRow(item)).ToList();
        _proofs = sourcePaths.Where(ProjectJourneySourcePath.IsSafe)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ProofRow(path)).ToList();
        CriteriaList.ItemsSource = _criteria;
        EvidenceList.ItemsSource = _proofs;
        StatusText.Text = _proofs.Count == 0
            ? "Aucune preuve proposée. Pour un GO, liez d'abord un document à l'opération."
            : "Les preuves cochées seront vérifiées et figées à la validation.";
    }

    /// <summary>The reviewed human choice after successful confirmation.</summary>
    public ProjectGateReview? Review { get; private set; }

    /// <summary>Selected evidence references to fingerprint before saving.</summary>
    public IReadOnlyList<string> SelectedSourcePaths { get; private set; } = [];

    /// <summary>Builds a complete review without persisting it.</summary>
    /// <param name="sender">Accept button.</param>
    /// <param name="e">Click event.</param>
    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        GateVerdictChoice? choice = VerdictCombo.SelectedItem as GateVerdictChoice;
        if (choice is null || string.IsNullOrWhiteSpace(ActorBox.Text) ||
            string.IsNullOrWhiteSpace(ScopeBox.Text) ||
            RationaleBox.Text.Trim().Length < 10)
        {
            StatusText.Text = "Auteur, périmètre et justification " +
                "(10 caractères minimum) obligatoires.";
            return;
        }

        if (choice.Verdict == ProjectGateVerdict.GoWithReservations &&
            string.IsNullOrWhiteSpace(ReservationBox.Text))
        {
            StatusText.Text = "Renseignez la réserve du GO sous réserves.";
            return;
        }

        if (choice.Verdict != ProjectGateVerdict.GoWithReservations &&
            !string.IsNullOrWhiteSpace(ReservationBox.Text))
        {
            StatusText.Text = "La réserve est utilisable seulement avec GO sous réserves.";
            return;
        }

        SelectedSourcePaths = _proofs.Where(item => item.Selected)
            .Select(item => item.RelativePath).ToArray();
        if ((choice.Verdict is ProjectGateVerdict.Go or
                ProjectGateVerdict.GoWithReservations) &&
            SelectedSourcePaths.Count == 0)
        {
            StatusText.Text = "Un GO nécessite au moins une preuve sélectionnée.";
            return;
        }

        ProjectGateReview draft = new()
        {
            GateId = _gate.Id,
            Verdict = choice.Verdict,
            Actor = ActorBox.Text.Trim(),
            Scope = ScopeBox.Text.Trim(),
            Rationale = RationaleBox.Text.Trim(),
            Version = string.IsNullOrWhiteSpace(VersionBox.Text)
                ? null : VersionBox.Text.Trim(),
            Environment = string.IsNullOrWhiteSpace(EnvironmentBox.Text)
                ? null : EnvironmentBox.Text.Trim(),
            Assessments = _criteria.Select(item =>
                new ProjectGateCriterionAssessment
                {
                    CriterionId = item.Id,
                    Name = item.PersistedName,
                    Status = item.Status,
                    Comment = item.Comment
                }).ToList(),
            Reservations = choice.Verdict ==
                ProjectGateVerdict.GoWithReservations
                ? [new ProjectGateReservation
                {
                    Id = Guid.NewGuid(),
                    Description = ReservationBox.Text.Trim()
                }]
                : []
        };
        try
        {
            // Pre-validate human input before the registry dialog fingerprints
            // evidence, confirms the decision and performs the SHA-checked write.
            ProjectGateRegistry fake = new()
            {
                ProjectId = Guid.NewGuid(),
                Gates = [_gate]
            };
            ProjectGateReview withPlaceholderEvidence = draft with
            {
                Evidence = SelectedSourcePaths.Select(path =>
                    new ProjectGateEvidenceSnapshot
                    {
                        RelativePath = path,
                        Sha256 = new string('0', 64)
                    }).ToList()
            };
            ProjectGateEditor.AppendDecision(fake, withPlaceholderEvidence);
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidDataException or InvalidOperationException)
        {
            StatusText.Text = exception.Message;
            return;
        }

        Review = draft;
        DialogResult = true;
    }

    /// <summary>Closes without proposing any decision.</summary>
    /// <param name="sender">Cancel button.</param>
    /// <param name="e">Click event.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record GateVerdictChoice(ProjectGateVerdict Verdict, string Label);

    /// <summary>Mutable, modal-only WPF evaluation state.</summary>
    private sealed class CriterionRow
    {
        /// <summary>Creates one unreviewed criterion.</summary>
        public CriterionRow(ProjectGateCriterion criterion)
        {
            Id = criterion.Id;
            Name = criterion.Name + (criterion.Optional ? " (facultatif)" : "");
            // The persisted label must not include the display-only suffix.
            PersistedName = criterion.Name;
        }

        public Guid Id { get; }

        public string Name { get; }

        public string PersistedName { get; }

        public ProjectGateCriterionStatus Status { get; set; } =
            ProjectGateCriterionStatus.Unknown;

        public string Comment { get; set; } = string.Empty;

        public ProjectGateCriterionStatus[] Options { get; } =
            Enum.GetValues<ProjectGateCriterionStatus>();
    }

    /// <summary>Explicit, initially unchecked source selection.</summary>
    private sealed class ProofRow
    {
        public ProofRow(string relativePath) => RelativePath = relativePath;

        public string RelativePath { get; }

        public bool Selected { get; set; }
    }
}
