using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Pasted external AI response preview, with NO workspace service or
/// persistence dependency. Input and projections stay in volatile memory.
/// </summary>
public partial class ProjectAiResponsePreviewDialog : Window
{
    private ProjectAiResponsePreview? _preview;
    private bool _busy;
    private long _inputRevision;
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private IReadOnlyDictionary<Guid, ProjectAiRequirementReview>? _comparisons;
    private readonly Dictionary<(Guid ProposalId, string Field),
        ProjectAiFieldDecision> _decisions = [];
    private bool _settingReviewInput;

    /// <summary>Initializes project-scoped, read-only external response review.</summary>
    /// <param name="workspaceRoot">The active single workspace.</param>
    /// <param name="projectDirectory">Project selected in the journey tab.</param>
    public ProjectAiResponsePreviewDialog(
        string workspaceRoot, string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        _workspaceRoot = workspaceRoot;
        _projectDirectory = projectDirectory;
        InitializeComponent();
        UpdateButtons();
    }

    /// <summary>Invalidates previously analyzed rows after any input edit.</summary>
    /// <param name="sender">Raw input.</param>
    /// <param name="e">Text changed event.</param>
    private void RawResponseBox_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        if (ProposalsList is null)
        {
            return;
        }

        _inputRevision++;
        _preview = null;
        _comparisons = null;
        ClearDecisions();
        ProposalsList.ItemsSource = null;
        DetailsBox.Text = string.Empty;
        SummaryText.Text = "Réponse modifiée : analyse à relancer.";
        UpdateButtons();
    }

    /// <summary>Parses a bounded response off the WPF Dispatcher.</summary>
    /// <param name="sender">Explicit preview button.</param>
    /// <param name="e">Click event.</param>
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(RawResponseBox.Text))
        {
            return;
        }

        string raw = RawResponseBox.Text;
        long revision = _inputRevision;
        _busy = true;
        UpdateButtons();
        try
        {
            ProjectAiResponsePreview preview = await Task.Run(() =>
                ProjectAiResponseImport.Parse(raw));
            if (revision != _inputRevision)
            {
                // Text changed during analysis: discard obsolete external data.
                return;
            }

            _preview = preview;
            _comparisons = null;
            ClearDecisions();
            ProposalsList.ItemsSource = preview.Document.Proposals;
            ProposalsList.SelectedIndex = 0;
            SummaryText.Text = preview.Document.Proposals.Count +
                " proposition(s) non fiables, format " +
                preview.Document.Schema + ". Empreinte : " +
                preview.RawSha256[..12] + "…";
            StatusText.Text = "Analyse locale réussie. Aucune proposition " +
                "n'est acceptée, écrite ou transmise à une IA.";
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException)
        {
            if (revision != _inputRevision)
            {
                return;
            }

            _preview = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = "Réponse invalide : " + exception.Message;
            StatusText.Text = "Aucun objet Nodalis n'a été créé ou modifié.";
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Resolves only current-project requirements from canonical Markdown.
    /// Work runs outside the WPF Dispatcher and never commits a proposal.
    /// </summary>
    /// <param name="sender">Explicit comparison button.</param>
    /// <param name="e">Click event.</param>
    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _preview is null)
        {
            return;
        }

        ProjectAiResponsePreview preview = _preview;
        long revision = _inputRevision;
        _busy = true;
        UpdateButtons();
        try
        {
            (ProjectRequirementCatalog catalog,
                IReadOnlyList<ProjectAiRequirementReview> reviews) = await
                Task.Run(async () =>
                {
                    ProjectRequirementRegistryService service = new(
                        _workspaceRoot);
                    ProjectRequirementCatalog current = await
                        service.ListAsync(_projectDirectory);
                    IReadOnlyList<ProjectAiRequirementReview> compared =
                        ProjectAiRequirementReviewService.Compare(
                            preview, current);
                    return (current, compared);
                });
            if (revision != _inputRevision || !ReferenceEquals(preview, _preview))
            {
                return;
            }

            _comparisons = reviews.ToDictionary(
                item => item.Candidate.ProposalId);
            ClearDecisions();
            int matched = reviews.Count(item => item.Status is
                ProjectAiRequirementMatchStatus.GuidMatch or
                ProjectAiRequirementMatchStatus.ReferenceMatch);
            SummaryText.Text = reviews.Count +
                " proposition(s) inspectée(s) · " + matched +
                " exigence(s) identifiée(s) · " +
                catalog.Diagnostics.Count + " diagnostic(s) du registre.";
            StatusText.Text = "Comparaison au registre du projet terminée. " +
                "Les propositions et bases IA restent non fiables. " +
                "Aucune écriture autorisée.";
            ShowSelectedDetails();
            UpdateButtons();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or ArgumentException or IOException or
            UnauthorizedAccessException)
        {
            if (revision == _inputRevision)
            {
                _comparisons = null;
                ClearDecisions();
                StatusText.Text = "Comparaison refusée : " + exception.Message +
                    " · aucune modification du workspace.";
                ShowSelectedDetails();
            }
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Shows only the selected candidate's untrusted text fields.</summary>
    /// <param name="sender">Candidate list.</param>
    /// <param name="e">Selection change.</param>
    private void ProposalsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e) => ShowSelectedDetails();

    /// <summary>Refreshes the selected proposal and its optional local comparison.</summary>
    private void ShowSelectedDetails()
    {
        if (_preview is null ||
            ProposalsList.SelectedItem is not ProjectAiImportCandidate item)
        {
            DetailsBox.Text = string.Empty;
            RenderFieldChoices();
            return;
        }

        StringBuilder builder = new();
        builder.AppendLine("PROPOSITION NON QUALIFIÉE : " +
            item.ProposalId.ToString("D"));
        builder.AppendLine("Type : " + item.Kind);
        builder.AppendLine("Action suggérée : " + item.Operation);
        builder.AppendLine("Certitude déclarée par l'IA : " +
            item.Certainty + " (non vérifiée)");
        builder.AppendLine("Cible indicative : " +
            (item.TargetId?.ToString("D") ??
                item.CandidateReference ?? "aucune"));
        builder.AppendLine("Source indicative : " +
            (item.SourcePath ?? "non fournie"));
        builder.AppendLine();
        builder.AppendLine("CHAMPS PROPOSÉS (AUCUNE ÉCRITURE)");
        foreach (KeyValuePair<string, string> field in item.ProposedFields)
        {
            builder.AppendLine(field.Key + " : " + field.Value);
        }

        builder.AppendLine();
        builder.AppendLine("BASE DÉCLARÉE PAR L'IA (NON VALIDÉE)");
        foreach (KeyValuePair<string, string> field in item.BaseFields)
        {
            builder.AppendLine(field.Key + " : " + field.Value);
        }

        builder.AppendLine();
        if (_comparisons is not null &&
            _comparisons.TryGetValue(item.ProposalId,
                out ProjectAiRequirementReview? review))
        {
            builder.AppendLine("LECTURE CANONIQUE DU PROJET (SANS ÉCRITURE)");
            builder.AppendLine("Résolution : " + review.Status);
            if (review.Requirement is ProjectRequirementEntry found)
            {
                builder.AppendLine("Exigence locale : " + found.Card.Reference +
                    " · " + found.Card.Id.ToString("D"));
                builder.AppendLine("Fichier : " + found.RelativePath);
                builder.AppendLine("Empreinte source : " +
                    found.SourceSha256[..Math.Min(16, found.SourceSha256.Length)] +
                    "… (instantané, non autorisation)");
            }

            foreach (ProjectAiFieldDifference field in review.Fields)
            {
                builder.AppendLine();
                builder.AppendLine("Champ : " + field.Field + " · " + field.Kind);
                builder.AppendLine("  Base IA déclarée : " +
                    (field.Base ?? "(inconnue)"));
                builder.AppendLine("  Valeur actuelle : " +
                    (field.Current ?? "(absente)"));
                builder.AppendLine("  Proposition : " + field.Suggested);
            }

            foreach (string warning in review.Warnings)
            {
                builder.AppendLine("ATTENTION : " + warning);
            }
        }
        else
        {
            builder.AppendLine("Comparer au registre du projet pour afficher " +
                "les correspondances et conflits, en lecture seule.");
        }

        builder.AppendLine();
        builder.AppendLine("Aucun champ ne peut être accepté ni importé ici. " +
            "Revalidation des sources et décision humaine restent obligatoires.");
        DetailsBox.Text = builder.ToString();
        RenderFieldChoices();
    }

    /// <summary>Clears all volatile choices and an obsolete dry-run.</summary>
    private void ClearDecisions()
    {
        _decisions.Clear();
        if (PlanTextBox is not null)
        {
            PlanTextBox.Text = "Plan invalidé : comparaison et choix à refaire.";
        }

        if (ReviewStatusText is not null)
        {
            ReviewStatusText.Text = "Aucun choix enregistré en mémoire";
        }
    }

    /// <summary>Lists only fields in the currently selected external proposal.</summary>
    private void RenderFieldChoices()
    {
        if (FieldPicker is null)
        {
            return;
        }

        string? selectedField = FieldPicker.SelectedItem as string;
        _settingReviewInput = true;
        try
        {
            if (ProposalsList.SelectedItem is not ProjectAiImportCandidate candidate)
            {
                FieldPicker.ItemsSource = null;
                FieldPicker.SelectedItem = null;
            }
            else
            {
                string[] fields = candidate.ProposedFields.Keys.OrderBy(
                    item => item, StringComparer.Ordinal).ToArray();
                FieldPicker.ItemsSource = fields;
                FieldPicker.SelectedItem = fields.Contains(selectedField,
                    StringComparer.Ordinal) ? selectedField :
                    fields.FirstOrDefault();
            }
        }
        finally
        {
            _settingReviewInput = false;
        }

        RenderFieldInputs();
    }

    /// <summary>Shows the proposed or human-edited value and stored choice.</summary>
    private void RenderFieldInputs()
    {
        if (ReviewedValueBox is null || JustificationBox is null ||
            ReviewStatusText is null)
        {
            return;
        }

        _settingReviewInput = true;
        try
        {
            ProjectAiImportCandidate? candidate =
                ProposalsList.SelectedItem as ProjectAiImportCandidate;
            string? field = FieldPicker.SelectedItem as string;
            if (candidate is not null && field is not null &&
                candidate.ProposedFields.TryGetValue(field, out string? value))
            {
                _decisions.TryGetValue((candidate.ProposalId, field),
                    out ProjectAiFieldDecision? decision);
                ReviewedValueBox.Text =
                    decision?.EditedValue ?? value;
                JustificationBox.Text = decision?.Reason ?? string.Empty;
            }
            else
            {
                ReviewedValueBox.Text = string.Empty;
                JustificationBox.Text = string.Empty;
            }

            ReviewStatusText.Text = _decisions.Count + " choix humain(s) " +
                "en mémoire · champ : " +
                (candidate is not null && field is not null &&
                 _decisions.TryGetValue((candidate.ProposalId, field),
                     out ProjectAiFieldDecision? current)
                    ? current.Kind.ToString()
                    : "sans décision");
        }
        finally
        {
            _settingReviewInput = false;
        }

        UpdateButtons();
    }

    /// <summary>Switches one proposal field, without implicitly accepting it.</summary>
    /// <param name="sender">The selected field.</param>
    /// <param name="e">Selection event.</param>
    private void FieldPicker_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_settingReviewInput)
        {
            RenderFieldInputs();
        }
    }

    /// <summary>Invalidates an earlier decision if the human edits its input.</summary>
    /// <param name="sender">Editable reviewed value or reason.</param>
    /// <param name="e">Text changed event.</param>
    private void DecisionInput_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        if (_settingReviewInput || FieldPicker is null ||
            ProposalsList is null)
        {
            return;
        }

        if (ProposalsList.SelectedItem is ProjectAiImportCandidate candidate &&
            FieldPicker.SelectedItem is string field)
        {
            _decisions.Remove((candidate.ProposalId, field));
            if (PlanTextBox is not null)
            {
                PlanTextBox.Text = "Plan périmé : préparer à nouveau après validation.";
            }

            if (ReviewStatusText is not null)
            {
                ReviewStatusText.Text = _decisions.Count +
                    " choix humain(s) en mémoire · valeur modifiée, non approuvée";
            }
        }

        UpdateButtons();
    }

    /// <summary>Stores one explicit decision only after pure plan validation.</summary>
    /// <param name="kind">Explicit UI action.</param>
    private void RecordDecision(ProjectAiFieldDecisionKind kind)
    {
        if (_preview is null || _comparisons is null ||
            ProposalsList.SelectedItem is not ProjectAiImportCandidate candidate ||
            FieldPicker.SelectedItem is not string field)
        {
            return;
        }

        ProjectAiFieldDecision decision = new(
            candidate.ProposalId, field, kind,
            kind == ProjectAiFieldDecisionKind.Edit
                ? ReviewedValueBox.Text : null,
            string.IsNullOrWhiteSpace(JustificationBox.Text)
                ? null : JustificationBox.Text.Trim());
        Dictionary<(Guid ProposalId, string Field), ProjectAiFieldDecision>
            next = new(_decisions)
            {
                [(candidate.ProposalId, field)] = decision
            };

        try
        {
            ProjectAiImportDecisionPlanner.Prepare(
                _preview, _comparisons.Values.ToArray(), next.Values.ToArray());
            _decisions[(candidate.ProposalId, field)] = decision;
            PlanTextBox.Text = "Choix modifiés : préparer à nouveau le plan.";
            StatusText.Text = "Décision « " + kind +
                " » conservée temporairement. Aucun document modifié.";
            RenderFieldInputs();
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = "Décision refusée : " + exception.Message;
            MessageBox.Show(this, exception.Message,
                "Import IA · qualification requise",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Accepts one already compared, supported field explicitly.</summary>
    private void AcceptField_Click(object sender, RoutedEventArgs e) =>
        RecordDecision(ProjectAiFieldDecisionKind.Accept);

    /// <summary>Accepts a human-edited value, not the AI original.</summary>
    private void EditField_Click(object sender, RoutedEventArgs e) =>
        RecordDecision(ProjectAiFieldDecisionKind.Edit);

    /// <summary>Defers the selected field for human requalification.</summary>
    private void ReviewField_Click(object sender, RoutedEventArgs e) =>
        RecordDecision(ProjectAiFieldDecisionKind.Review);

    /// <summary>Explicitly ignores the selected field without affecting others.</summary>
    private void IgnoreField_Click(object sender, RoutedEventArgs e) =>
        RecordDecision(ProjectAiFieldDecisionKind.Ignore);

    /// <summary>Builds a human-readable, non-executable review plan.</summary>
    private void BuildReviewPlan_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is null || _comparisons is null)
        {
            return;
        }

        try
        {
            ProjectAiPreparedReviewPlan plan =
                ProjectAiImportDecisionPlanner.Prepare(_preview,
                    _comparisons.Values.ToArray(), _decisions.Values.ToArray());
            StringBuilder builder = new();
            builder.AppendLine("PLAN DE QUALIFICATION — LECTURE SEULE");
            builder.AppendLine("AUCUNE TRANSACTION NI ÉCRITURE N'EST POSSIBLE ICI.");
            builder.AppendLine("Réponse SHA-256 : " + plan.RawResponseSha256);
            builder.AppendLine("Champs explicitement traités : " +
                plan.ReviewedFields);
            builder.AppendLine("Champs ignorés : " + plan.IgnoredFields);
            builder.AppendLine("Champs restant à revoir : " + plan.PendingFields);
            builder.AppendLine("Modifications proposées : " + plan.Changes.Count);
            foreach (ProjectAiPreparedTitleChange change in plan.Changes)
            {
                builder.AppendLine();
                builder.AppendLine("Exigence " + change.Reference +
                    " · " + change.RequirementId.ToString("D"));
                builder.AppendLine("Source : " + change.RelativePath);
                builder.AppendLine("SHA actuel à revalider : " +
                    change.ExpectedSha256);
                builder.AppendLine("Titre actuel : " + change.CurrentTitle);
                builder.AppendLine("Titre retenu : " + change.ReviewedTitle);
                if (!string.IsNullOrWhiteSpace(change.Reason))
                {
                    builder.AppendLine("Justification : " + change.Reason);
                }
            }

            builder.AppendLine();
            builder.AppendLine("Même avec accord humain, il faudra " +
                "recontrôler les fichiers avant un futur commit sécurisé.");
            PlanTextBox.Text = builder.ToString();
            StatusText.Text = "Plan préparé en mémoire : " +
                plan.Changes.Count + " modification(s), zéro écriture.";
        }
        catch (InvalidDataException exception)
        {
            PlanTextBox.Text = "Plan impossible : " + exception.Message;
            StatusText.Text = "Aucune transaction effectuée.";
        }
    }

    /// <summary>Prevents double launch and parsing empty input.</summary>
    private void UpdateButtons()
    {
        if (AnalyzeButton is not null)
        {
            AnalyzeButton.IsEnabled = !_busy &&
                !string.IsNullOrWhiteSpace(RawResponseBox?.Text);
        }

        if (CompareButton is not null)
        {
            CompareButton.IsEnabled = !_busy && _preview is not null;
        }

        if (AcceptFieldButton is null || FieldPicker is null)
        {
            return;
        }

        bool selected = ProposalsList.SelectedItem is
            ProjectAiImportCandidate proposal &&
            FieldPicker.SelectedItem is string field;
        bool compared = !_busy && _comparisons is not null &&
            selected && _comparisons.ContainsKey(proposal!.ProposalId);
        bool supported = compared && field == "title" &&
            proposal!.Kind == "requirement" &&
            proposal.Operation == "update" &&
            _comparisons![proposal.ProposalId].Status is
                ProjectAiRequirementMatchStatus.GuidMatch or
                ProjectAiRequirementMatchStatus.ReferenceMatch;
        AcceptFieldButton.IsEnabled = supported;
        EditFieldButton.IsEnabled = supported;
        ReviewedValueBox.IsEnabled = supported;
        JustificationBox.IsEnabled = supported;
        ReviewFieldButton.IsEnabled = compared;
        IgnoreFieldButton.IsEnabled = compared;
        FieldPicker.IsEnabled = !_busy && _preview is not null;
        BuildReviewPlanButton.IsEnabled = compared && _decisions.Count > 0;
    }

    /// <summary>Discards volatile proposals without any persistence.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
