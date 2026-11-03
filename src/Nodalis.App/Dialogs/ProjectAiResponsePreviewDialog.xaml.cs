using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Pasted external AI response review. Analysis and field decisions remain
/// volatile; precisely one explicitly confirmed EXI title can be journaled
/// and atomically published through a separate, conservative service.
/// </summary>
public partial class ProjectAiResponsePreviewDialog : Window
{
    private ProjectAiResponsePreview? _preview;
    private bool _busy;
    private long _inputRevision;
    private readonly string _workspaceRoot;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private IReadOnlyDictionary<Guid, ProjectAiRequirementReview>? _comparisons;
    private readonly Dictionary<(Guid ProposalId, string Field),
        ProjectAiFieldDecision> _decisions = [];
    private bool _settingReviewInput;
    private bool _publishing;
    private ProjectAiPreparedReviewPlan? _preparedPlan;

    /// <summary>Initializes project-scoped, read-only external response review.</summary>
    /// <param name="workspaceRoot">The active single workspace.</param>
    /// <param name="projectDirectory">Project selected in the journey tab.</param>
    /// <param name="readOnly">Read-only workspace or archived project.</param>
    public ProjectAiResponsePreviewDialog(
        string workspaceRoot, string projectDirectory, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        _workspaceRoot = workspaceRoot;
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
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
        _preparedPlan = null;
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
            _preparedPlan = null;
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
            _preparedPlan = null;
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
            _preparedPlan = plan.Changes.Count == 1 ? plan : null;
            StringBuilder builder = new();
            builder.AppendLine("PLAN DE QUALIFICATION — REVUE HUMAINE");
            builder.AppendLine("Une seule modification EXI est publiable " +
                "après confirmation explicite.");
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
            builder.AppendLine(plan.Changes.Count == 1
                ? "Un seul titre peut être enregistré. Le service recontrôlera " +
                  "les identités et SHA-256 puis journalisera les deux versions. " +
                  "Fermez tout éditeur de la fiche concernée avant publication."
                : "Aucune publication : le lot doit contenir exactement " +
                  "une modification approuvée et non déjà présente.");
            PlanTextBox.Text = builder.ToString();
            StatusText.Text = "Plan préparé en mémoire : " +
                plan.Changes.Count + " modification(s), zéro écriture.";
            UpdateButtons();
        }
        catch (InvalidDataException exception)
        {
            _preparedPlan = null;
            PlanTextBox.Text = "Plan impossible : " + exception.Message;
            StatusText.Text = "Aucune transaction effectuée.";
            UpdateButtons();
        }
    }

    /// <summary>
    /// Applies exactly one previously confirmed EXI title. An import is never
    /// auto-approved or run in response to AI text, comparison or selection.
    /// </summary>
    /// <param name="sender">Explicit publish button.</param>
    /// <param name="e">User click.</param>
    private async void PublishReviewedTitle_Click(
        object sender, RoutedEventArgs e)
    {
        if (_busy || _preview is null || _comparisons is null ||
            _preparedPlan is null || _preparedPlan.Changes.Count != 1)
        {
            return;
        }

        ProjectAiPreparedReviewPlan expected = _preparedPlan;
        ProjectAiPreparedReviewPlan current;
        try
        {
            current = ProjectAiImportDecisionPlanner.Prepare(_preview,
                _comparisons.Values.ToArray(), _decisions.Values.ToArray());
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = "Plan devenu invalide : " + exception.Message;
            _preparedPlan = null;
            UpdateButtons();
            return;
        }

        if (current.Changes.Count != 1 ||
            current.Changes[0] != expected.Changes[0] ||
            current.RawResponseSha256 != expected.RawResponseSha256 ||
            current.ReviewedFields != expected.ReviewedFields ||
            current.IgnoredFields != expected.IgnoredFields ||
            current.PendingFields != expected.PendingFields)
        {
            _preparedPlan = null;
            StatusText.Text = "Plan modifié : renouveler la validation.";
            UpdateButtons();
            return;
        }

        ProjectAiPreparedTitleChange change = current.Changes[0];
        if (_readOnly)
        {
            StatusText.Text = "Workspace ou projet en lecture seule : " +
                "import IA interdit.";
            return;
        }

        try
        {
            string resolved = new ProjectJourneySourceService(_workspaceRoot)
                .ResolveExistingFile(change.RelativePath);
            if (Application.Current.MainWindow is not Nodalis.App.MainWindow main)
            {
                StatusText.Text = "Contrôle des onglets impossible : " +
                    "publication refusée.";
                return;
            }

            if (main.IsDocumentOpenForAiImport(resolved))
            {
                StatusText.Text = "Fiche EXI actuellement ouverte dans un " +
                    "onglet Markdown : fermez cet onglet (et sauvegardez les " +
                    "modifications éventuelles) avant l'import.";
                MessageBox.Show(this, StatusText.Text,
                    "Import IA · éditeur ouvert",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or
            IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Cible non disponible : " + exception.Message;
            return;
        }

        string confirmation = "Exigence : " + change.Reference +
            "\nDocument : " + change.RelativePath +
            "\nTitre actuel : " + change.CurrentTitle +
            "\nTitre approuvé : " + change.ReviewedTitle +
            "\n\nSeul le titre de cette fiche EXI sera modifié ; les statuts, " +
            "critères et autres propositions ne le seront pas." +
            "\n\nLe journal conservera les versions originale et approuvée " +
            "(potentiellement confidentielles) dans le projet." +
            "\n\nFermez d'abord toute édition ouverte de ce document " +
            "pour éviter un écrasement ultérieur depuis un onglet non rechargé." +
            "\n\nConfirmer la publication locale ?";
        if (MessageBox.Show(this, confirmation,
            "Import IA · confirmer une seule modification",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            ProjectAiTitleCommitStatus result = await Task.Run(async () =>
            {
                ProjectAiTitleImportTransactionService service = new(
                    _workspaceRoot);
                return await service.CommitAsync(_projectDirectory, current);
            });
            _comparisons = null;
            ClearDecisions();
            ShowSelectedDetails();
            PlanTextBox.Text = "Publication " + result +
                " : recharger la fiche EXI et relancer une comparaison " +
                "avant une nouvelle modification.";
            StatusText.Text = result == ProjectAiTitleCommitStatus.AlreadyApplied
                ? "Proposition déjà publiée (journal concordant) : aucun doublon."
                : "Titre EXI publié avec remplacement atomique et journal durable.";
            MessageBox.Show(this, StatusText.Text +
                "\n\nRouvrez la fiche pour actualiser l'éditeur.",
                "Import IA · résultat", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            StatusText.Text = "Publication refusée : " + exception.Message;
            PlanTextBox.Text = "Aucune réécriture concurrente autorisée. " +
                "Un journal préparé peut nécessiter une récupération " +
                "après interruption. Vérifiez les fichiers avant requalification.";
            MessageBox.Show(this, StatusText.Text,
                "Import IA · vérification ou récupération",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _publishing = false;
            _busy = false;
            UpdateButtons();
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

        if (RawResponseBox is not null)
        {
            RawResponseBox.IsReadOnly = _publishing;
        }

        if (PublishReviewedTitleButton is not null)
        {
            PublishReviewedTitleButton.IsEnabled = !_busy && !_readOnly &&
                _preparedPlan is not null &&
                _preparedPlan.Changes.Count == 1 &&
                _comparisons is not null && _preview is not null;
        }

        if (AcceptFieldButton is null || FieldPicker is null)
        {
            return;
        }

        ProjectAiImportCandidate? proposal =
            ProposalsList.SelectedItem as ProjectAiImportCandidate;
        string? field = FieldPicker.SelectedItem as string;
        bool compared = !_busy && _comparisons is not null &&
            proposal is not null && field is not null &&
            _comparisons.ContainsKey(proposal.ProposalId);
        bool supported = compared && proposal is not null &&
            field == "title" && proposal.Kind == "requirement" &&
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
