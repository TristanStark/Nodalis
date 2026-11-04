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
        builder.AppendLine("Toute écriture exige une décision humaine, " +
            "un plan unitaire, une confirmation explicite et un contrôle " +
            "des fichiers au dernier instant.");
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
            _preparedPlan = (plan.Changes.Count is >= 1 and <= 20 &&
                    plan.Creations.Count == 0) ||
                (plan.Changes.Count == 0 && plan.Creations.Count == 1) ||
                (plan.Changes.Count >= 1 && plan.Creations.Count >= 1 &&
                    plan.Changes.Count + plan.Creations.Count <= 20)
                ? plan : null;
            StringBuilder builder = new();
            builder.AppendLine("PLAN DE QUALIFICATION — REVUE HUMAINE");
            builder.AppendLine("Les titres EXI existants peuvent être publiés " +
                "en lot (2 à 20), les créations seules restent unitaires. " +
                "Un lot mixte (1 à 20 créations + titres) est aussi possible " +
                "après validation explicite et relecture complète.");
            builder.AppendLine("Réponse SHA-256 : " + plan.RawResponseSha256);
            builder.AppendLine("Champs explicitement traités : " +
                plan.ReviewedFields);
            builder.AppendLine("Champs ignorés : " + plan.IgnoredFields);
            builder.AppendLine("Champs restant à revoir : " + plan.PendingFields);
            builder.AppendLine("Modifications proposées : " + plan.Changes.Count);
            builder.AppendLine("Créations proposées : " + plan.Creations.Count);
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

            foreach (ProjectAiPreparedRequirementCreation creation in
                plan.Creations)
            {
                builder.AppendLine();
                builder.AppendLine("Nouvelle exigence du projet " +
                    creation.ProjectId.ToString("D"));
                builder.AppendLine("Proposition : " +
                    creation.ProposalId.ToString("D"));
                builder.AppendLine("Titre approuvé : " + creation.ReviewedTitle);
                builder.AppendLine("Référence EXI / GUID : Nodalis les attribuera " +
                    "après relecture du registre et confirmation humaine.");
                if (!string.IsNullOrWhiteSpace(creation.Reason))
                {
                    builder.AppendLine("Justification : " + creation.Reason);
                }
            }

            builder.AppendLine();
            builder.AppendLine(plan.Changes.Count == 1 &&
                plan.Creations.Count == 0
                ? "Un seul titre peut être enregistré. Le service recontrôlera " +
                  "les identités et SHA-256 puis journalisera les deux versions. " +
                  "Fermez tout éditeur de la fiche concernée avant publication."
                : plan.Changes.Count is >= 2 and <= 20 &&
                    plan.Creations.Count == 0
                    ? "Lot de titres éligible à une transaction reprise par journal. " +
                      "Toutes les sources sont prévalidées, puis chaque fichier " +
                      "est publié atomiquement : une interruption peut laisser " +
                      "un lot partiel qui doit être explicitement repris."
                    : plan.Changes.Count >= 1 && plan.Creations.Count >= 1 &&
                        plan.Changes.Count + plan.Creations.Count <= 20
                        ? "Lot mixte éligible : toutes les créations seront " +
                          "attribuées par Nodalis après contrôle de tous les " +
                          "fichiers. Une interruption peut laisser un lot " +
                          "partiel : reprise volontaire depuis le journal."
                        : plan.Changes.Count == 0 && plan.Creations.Count == 1
                            ? "Un seul brouillon EXI peut être créé. Nodalis choisira " +
                              "une référence libre, un GUID stable et conservera un journal."
                            : "Aucune publication : limite de 20 modifications, " +
                              "ou plusieurs créations sans changement existant.");
            PlanTextBox.Text = builder.ToString();
            StatusText.Text = "Plan préparé en mémoire : " +
                plan.Changes.Count + " modification(s), " +
                plan.Creations.Count + " création(s), zéro écriture.";
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
    /// Publishes two to twenty explicitly reviewed EXI title updates, only
    /// after an additional human confirmation of every affected file.
    /// Multiple Windows file replacements are replayable, not all-or-nothing.
    /// </summary>
    /// <param name="sender">Explicit batch publication command.</param>
    /// <param name="e">Human click.</param>
    private async void PublishReviewedBatch_Click(
        object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _preview is null || _comparisons is null ||
            _preparedPlan is null || _preparedPlan.Creations.Count != 0 ||
            _preparedPlan.Changes.Count is < 2 or > 20)
        {
            return;
        }

        ProjectAiPreparedReviewPlan expected = _preparedPlan;
        ProjectAiPreparedReviewPlan current;
        try
        {
            current = ProjectAiImportDecisionPlanner.Prepare(_preview,
                _comparisons.Values.ToArray(), _decisions.Values.ToArray());
            if (current.Changes.Count != expected.Changes.Count ||
                current.Creations.Count != 0 ||
                !current.Changes.SequenceEqual(expected.Changes) ||
                current.RawResponseSha256 != expected.RawResponseSha256 ||
                current.ReviewedFields != expected.ReviewedFields ||
                current.IgnoredFields != expected.IgnoredFields ||
                current.PendingFields != expected.PendingFields)
            {
                _preparedPlan = null;
                StatusText.Text = "Lot modifié : préparer un nouveau plan " +
                    "et reconfirmer toutes les décisions.";
                UpdateButtons();
                return;
            }

            if (Application.Current.MainWindow is not Nodalis.App.MainWindow main)
            {
                throw new InvalidDataException(
                    "Contrôle des éditeurs ouverts indisponible : lot refusé.");
            }

            ProjectJourneySourceService resolver = new(_workspaceRoot);
            foreach (ProjectAiPreparedTitleChange change in current.Changes)
            {
                string target = resolver.ResolveExistingFile(change.RelativePath);
                if (main.IsDocumentOpenForAiImport(target))
                {
                    throw new InvalidDataException(
                        "Fermez les onglets Markdown correspondant à " +
                        change.Reference +
                        " avant l'import groupé (risque d'autosave obsolète).");
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Lot refusé : " + exception.Message;
            MessageBox.Show(this, StatusText.Text,
                "Import IA · validation du lot",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StringBuilder confirmation = new();
        confirmation.AppendLine("PUBLICATION GROUPÉE — " +
            current.Changes.Count + " titres d'exigences existantes");
        confirmation.AppendLine();
        foreach (ProjectAiPreparedTitleChange change in current.Changes
            .OrderBy(item => item.Reference, StringComparer.Ordinal))
        {
            confirmation.AppendLine(change.Reference + " : " +
                change.CurrentTitle + " → " + change.ReviewedTitle);
        }

        confirmation.AppendLine();
        confirmation.AppendLine("Tous les fichiers sont vérifiés avant " +
            "la première écriture, avec des copies intégrales conservées " +
            "localement dans le projet.");
        confirmation.AppendLine("IMPORTANT : une interruption peut laisser " +
            "une publication partielle. Une reprise volontaire, avec le même " +
            "plan, vérifiera chaque fichier avant d'écrire la suite.");
        confirmation.AppendLine("Aucune création, validation d'exigence, " +
            "tâche ou modification de critères ne sera effectuée.");
        confirmation.AppendLine();
        confirmation.AppendLine("Confirmer la publication de ce lot ?");
        if (MessageBox.Show(this, confirmation.ToString(),
            "Import IA · confirmer le lot", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            (ProjectAiBatchTitleCommitStatus result, string? auditWarning) =
                await PublishWithAuditAsync(current, "title-batch",
                    "batch-" + ProjectAiBatchTitleImportTransactionService
                        .PlanFingerprint(current),
                    () => new ProjectAiBatchTitleImportTransactionService(
                        _workspaceRoot).CommitAsync(_projectDirectory, current));

            _comparisons = null;
            ClearDecisions();
            ShowSelectedDetails();
            PlanTextBox.Text = "Lot " + result + " · " +
                current.Changes.Count + " titre(s) concernés. " +
                "Recharger les documents et comparer à nouveau avant " +
                "toute nouvelle décision.";
            StatusText.Text = result == ProjectAiBatchTitleCommitStatus.AlreadyApplied
                ? "Lot déjà publié (empreintes conformes) : aucune réécriture."
                : "Lot de titres EXI publié et journalisé.";
            if (auditWarning is not null)
            {
                StatusText.Text += "\\n" + auditWarning;
            }
            MessageBox.Show(this, StatusText.Text,
                "Import IA · résultat du lot", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Lot bloqué : " + exception.Message;
            PlanTextBox.Text = "Un lot peut avoir été partiellement publié. " +
                "Ne pas modifier ni supprimer le journal .nodalis-ai-import. " +
                "Vérifier les changements externes puis relancer " +
                "explicitement le même plan pour reprendre, sans écrasement.";
            MessageBox.Show(this, StatusText.Text +
                "\n\nConsulter le journal et vérifier les fiches. " +
                "Aucune reprise automatique n'est effectuée.",
                "Import IA · lot en attente de récupération",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _publishing = false;
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Applies one explicitly approved mixed EXI batch after showing the
    /// newly allocated references and every existing title before/after.
    /// A mixed batch can be partially published but safely resumed later.
    /// </summary>
    /// <param name="sender">User-initiated publication button.</param>
    /// <param name="e">Mouse or keyboard click.</param>
    private async void PublishMixedBatch_Click(
        object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _preview is null || _comparisons is null ||
            _preparedPlan is null || _preparedPlan.Changes.Count == 0 ||
            _preparedPlan.Creations.Count == 0 ||
            _preparedPlan.Changes.Count + _preparedPlan.Creations.Count > 20)
        {
            return;
        }

        ProjectAiPreparedReviewPlan previous = _preparedPlan;
        ProjectAiPreparedReviewPlan current;
        try
        {
            current = ProjectAiImportDecisionPlanner.Prepare(_preview,
                _comparisons.Values.ToArray(), _decisions.Values.ToArray());
            if (current.Changes.Count != previous.Changes.Count ||
                current.Creations.Count != previous.Creations.Count ||
                !current.Changes.SequenceEqual(previous.Changes) ||
                !current.Creations.SequenceEqual(previous.Creations) ||
                current.RawResponseSha256 != previous.RawResponseSha256 ||
                current.ReviewedFields != previous.ReviewedFields ||
                current.IgnoredFields != previous.IgnoredFields ||
                current.PendingFields != previous.PendingFields)
            {
                _preparedPlan = null;
                StatusText.Text = "Les décisions ont changé : " +
                    "repréparer et confirmer le nouveau lot.";
                UpdateButtons();
                return;
            }

            if (Application.Current.MainWindow is not Nodalis.App.MainWindow main)
            {
                throw new InvalidDataException(
                    "Éditeur principal indisponible : publication interdite.");
            }

            ProjectJourneySourceService resolver = new(_workspaceRoot);
            foreach (ProjectAiPreparedTitleChange update in current.Changes)
            {
                string source = resolver.ResolveExistingFile(update.RelativePath);
                if (main.IsDocumentOpenForAiImport(source))
                {
                    throw new InvalidDataException(
                        "Fermez d'abord l'onglet " + update.Reference +
                        " pour éviter une sauvegarde obsolète.");
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Plan mixte refusé : " + exception.Message;
            MessageBox.Show(this, StatusText.Text,
                "Import IA · prévalidation du lot mixte",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        long revision = _inputRevision;
        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            ProjectAiMixedImportTransactionService service = new(_workspaceRoot);
            IReadOnlyList<string> assigned = await Task.Run(() =>
                service.ProposeReferencesAsync(_projectDirectory, current));
            if (revision != _inputRevision ||
                !ReferenceEquals(previous, _preparedPlan))
            {
                StatusText.Text = "Réponse modifiée pendant la revue : " +
                    "requalification obligatoire.";
                return;
            }

            ProjectAiPreparedRequirementCreation[] creations = current.Creations
                .OrderBy(item => item.ProposalId).ToArray();
            StringBuilder message = new();
            message.AppendLine("CONFIRMATION DU LOT MIXTE — " +
                (current.Changes.Count + current.Creations.Count) +
                " opération(s)");
            message.AppendLine();
            foreach (ProjectAiPreparedTitleChange update in current.Changes
                .OrderBy(item => item.Reference, StringComparer.Ordinal))
            {
                message.AppendLine("MODIFIER " + update.Reference + " : " +
                    update.CurrentTitle + " → " + update.ReviewedTitle);
            }

            for (int i = 0; i < creations.Length; i++)
            {
                message.AppendLine("CRÉER " + assigned[i] + " : " +
                    creations[i].ReviewedTitle + " (statut brouillon)");
            }

            message.AppendLine();
            message.AppendLine("Les nouveaux GUID sont attribués par Nodalis, " +
                "jamais par l'IA. Aucun statut, critère, test ou lien ne change.");
            message.AppendLine("Toutes les sources sont vérifiées avant " +
                "la première écriture. Chaque fichier est publié atomiquement, " +
                "mais le lot complet n'est pas atomique.");
            message.AppendLine("Une panne peut laisser un lot partiel, " +
                "récupérable depuis le journal local uniquement sur demande.");
            message.AppendLine("Le journal inclut des copies Markdown " +
                "potentiellement confidentielles.");
            message.AppendLine();
            message.AppendLine("Confirmer ce lot et ces références EXI ?");
            if (MessageBox.Show(this, message.ToString(),
                "Import IA · confirmer modifications et créations",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            (ProjectAiMixedCommitStatus result, string? auditWarning) =
                await PublishWithAuditAsync(current, "mixed",
                    "mixed-" + ProjectAiMixedImportTransactionService
                        .PlanFingerprint(current),
                    () => service.CommitAsync(_projectDirectory, current,
                        assigned));
            _comparisons = null;
            ClearDecisions();
            ShowSelectedDetails();
            PlanTextBox.Text = "Lot mixte " + result + " : " +
                current.Changes.Count + " modification(s), " +
                current.Creations.Count + " création(s).\n" +
                "Rouvrir les fiches et rafraîchir le Worktree.";
            StatusText.Text = result == ProjectAiMixedCommitStatus.AlreadyApplied
                ? "Lot déjà publié intégralement : aucun doublon."
                : "Lot mixte publié : nouvelles exigences en brouillon, " +
                  "titres existants actualisés.";
            if (auditWarning is not null)
            {
                StatusText.Text += "\\n" + auditWarning;
            }
            MessageBox.Show(this, StatusText.Text,
                "Import IA · résultat mixte",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Lot mixte bloqué : " + exception.Message;
            PlanTextBox.Text = "Le journal peut contenir une publication partielle. " +
                "Ne pas supprimer les instantanés. " +
                "Rechercher les lots interrompus, résoudre les divergences, " +
                "puis confirmer une reprise volontaire.";
            MessageBox.Show(this, StatusText.Text,
                "Import IA · récupération nécessaire",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _publishing = false;
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Discovers recoverable, incomplete EXI title batches without writing
    /// or interpreting any pasted AI response as an action.
    /// </summary>
    /// <param name="sender">Read-only discovery button.</param>
    /// <param name="e">User click.</param>
    private async void FindPendingBatches_Click(
        object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            (IReadOnlyList<ProjectAiBatchRecoveryPreview> titleBatches,
             IReadOnlyList<ProjectAiMixedRecoveryPreview> mixedBatches) =
                await Task.Run(async () =>
                {
                    ProjectAiBatchTitleImportTransactionService titles = new(
                        _workspaceRoot);
                    ProjectAiMixedImportTransactionService mixed = new(
                        _workspaceRoot);
                    IReadOnlyList<ProjectAiBatchRecoveryPreview> titleResults =
                        await titles.ListPendingBatchesAsync(_projectDirectory);
                    IReadOnlyList<ProjectAiMixedRecoveryPreview> mixedResults =
                        await mixed.ListPendingAsync(_projectDirectory);
                    return (titleResults, mixedResults);
                });

            PendingBatchPicker.Items.Clear();
            foreach (ProjectAiBatchRecoveryPreview batch in titleBatches)
            {
                PendingBatchPicker.Items.Add(new ComboBoxItem
                {
                    Content = "Titres · " + batch.Published + "/" +
                        batch.Files.Count + " · " +
                        batch.PlanSha256[..12] + "…",
                    Tag = "title:" + batch.PlanSha256
                });
            }

            foreach (ProjectAiMixedRecoveryPreview batch in mixedBatches)
            {
                PendingBatchPicker.Items.Add(new ComboBoxItem
                {
                    Content = "Mixte · " + batch.Published + "/" +
                        batch.Entries.Count + " · " +
                        batch.PlanSha256[..12] + "…",
                    Tag = "mixed:" + batch.PlanSha256
                });
            }

            int count = titleBatches.Count + mixedBatches.Count;
            PendingBatchPicker.SelectedIndex = count > 0 ? 0 : -1;
            StatusText.Text = count + " lot(s) récupérable(s) : " +
                titleBatches.Count + " lot(s) de titres, " +
                mixedBatches.Count + " lot(s) mixtes. Les journaux " +
                "corrompus et les cibles divergentes sont exclus.";
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            PendingBatchPicker.Items.Clear();
            StatusText.Text = "Recherche des journaux refusée : " +
                exception.Message;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Revalidates the selected retained batch, enumerates all proposed titles
    /// for explicit human confirmation, then safely resumes publication.
    /// </summary>
    /// <param name="sender">Recovery command.</param>
    /// <param name="e">User click.</param>
    private async void ResumePendingBatch_Click(
        object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly ||
            PendingBatchPicker.SelectedItem is not ComboBoxItem selected ||
            selected.Tag is not string key)
        {
            return;
        }

        if (key.StartsWith("mixed:", StringComparison.Ordinal))
        {
            await ResumeMixedBatchAsync(key[6..], selected);
            return;
        }

        key = key.StartsWith("title:", StringComparison.Ordinal)
            ? key[6..]
            : key;
        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            ProjectAiBatchTitleImportTransactionService service = new(
                _workspaceRoot);
            ProjectAiBatchRecoveryPreview preview = await Task.Run(() =>
                service.ReviewRecoveryAsync(_projectDirectory, key));
            if (preview.Pending == 0)
            {
                StatusText.Text = "Lot déjà achevé, aucune reprise nécessaire.";
                return;
            }

            if (Application.Current.MainWindow is not Nodalis.App.MainWindow main)
            {
                throw new InvalidDataException(
                    "Éditeur principal indisponible : reprise refusée.");
            }

            ProjectJourneySourceService resolver = new(_workspaceRoot);
            foreach (ProjectAiBatchTitleFile file in preview.Files)
            {
                string path = resolver.ResolveExistingFile(file.RelativePath);
                if (main.IsDocumentOpenForAiImport(path))
                {
                    throw new InvalidDataException(
                        "La fiche " + file.Reference + " est ouverte en édition. " +
                        "Fermez l'onglet avant de reprendre le lot.");
                }
            }

            StringBuilder message = new();
            message.AppendLine("REPRISE EXPLICITE D'UN LOT LOCAL");
            message.AppendLine("Journal : " + preview.PlanSha256);
            message.AppendLine("Projet : " + preview.ProjectId.ToString("D"));
            message.AppendLine(preview.Published + " déjà publié(s) ; " +
                preview.Pending + " restant(s) sur " + preview.Files.Count);
            message.AppendLine();
            foreach (ProjectAiBatchTitleFile file in preview.Files)
            {
                message.AppendLine(file.Reference + " : " +
                    file.OriginalTitle + " → " + file.ReviewedTitle);
            }

            message.AppendLine();
            message.AppendLine("Tous les documents et instantanés seront " +
                "revérifiés. Aucune divergence ne sera écrasée.");
            message.AppendLine("Cette reprise n'efface pas les journaux.");
            message.AppendLine();
            message.AppendLine("Confirmer la reprise des titres restants ?");
            if (MessageBox.Show(this, message.ToString(),
                "Import IA · reprise depuis le journal",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectAiBatchTitleCommitStatus result = await Task.Run(() =>
                service.ResumeAsync(_projectDirectory, key));
            PendingBatchPicker.Items.Remove(selected);
            StatusText.Text = result ==
                ProjectAiBatchTitleCommitStatus.AlreadyApplied
                ? "Lot déjà finalisé, aucun fichier modifié."
                : "Lot repris depuis son journal local. " +
                  "Recharger les fiches mises à jour.";
            PlanTextBox.Text = "Récupération du lot " + key +
                " terminée : " + result + ".\n" +
                "Requalifier les éventuelles autres propositions séparément.";
            MessageBox.Show(this, StatusText.Text,
                "Import IA · résultat de récupération",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or UnauthorizedAccessException or
            ArgumentException)
        {
            StatusText.Text = "Reprise interrompue/refusée : " +
                exception.Message;
            MessageBox.Show(this, StatusText.Text +
                "\n\nLes copies du journal demeurent disponibles ; " +
                "résoudre toute divergence avant de réessayer.",
                "Import IA · récupération bloquée",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            _publishing = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Resumes a mixed batch from its durable local journal after a new
    /// human confirmation. No original AI text or past dialog is required.
    /// </summary>
    /// <param name="key">Stable 64-hex reviewed plan fingerprint.</param>
    /// <param name="selected">Recovery selector item to clear on success.</param>
    private async Task ResumeMixedBatchAsync(
        string key, ComboBoxItem selected)
    {
        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            ProjectAiMixedImportTransactionService service = new(_workspaceRoot);
            ProjectAiMixedRecoveryPreview preview = await Task.Run(() =>
                service.ReviewRecoveryAsync(_projectDirectory, key));
            if (preview.Pending == 0)
            {
                StatusText.Text = "Lot mixte déjà achevé, aucune reprise utile.";
                PendingBatchPicker.Items.Remove(selected);
                return;
            }

            if (Application.Current.MainWindow is not Nodalis.App.MainWindow main)
            {
                throw new InvalidDataException(
                    "Éditeur principal indisponible : récupération interdite.");
            }

            ProjectJourneySourceService resolver = new(_workspaceRoot);
            foreach (ProjectAiMixedEntry item in preview.Entries)
            {
                string target = Path.GetFullPath(Path.Combine(
                    _workspaceRoot, item.RelativePath.Replace(
                        '/', Path.DirectorySeparatorChar)));
                if (File.Exists(target))
                {
                    string verified = resolver.ResolveExistingFile(
                        item.RelativePath);
                    if (main.IsDocumentOpenForAiImport(verified))
                    {
                        throw new InvalidDataException(
                            "Fiche " + item.Reference + " encore ouverte " +
                            "dans un éditeur : fermez l'onglet.");
                    }
                }
            }

            StringBuilder message = new();
            message.AppendLine("REPRISE DU LOT IA MIXTE");
            message.AppendLine("Journal : " + preview.PlanSha256);
            message.AppendLine("Projet : " + preview.ProjectId.ToString("D"));
            message.AppendLine(preview.Published + " déjà publié(s), " +
                preview.Pending + " restant(s) sur " +
                preview.Entries.Count);
            message.AppendLine();
            foreach (ProjectAiMixedEntry item in preview.Entries)
            {
                message.AppendLine((item.Operation == "create"
                        ? "CRÉER "
                        : "MODIFIER ") +
                    item.Reference + " : " +
                    (item.OriginalTitle ?? "(nouvelle exigence)") +
                    " → " + item.ReviewedTitle);
            }

            message.AppendLine();
            message.AppendLine("Chaque fichier sera recontrôlé par SHA-256. " +
                "Les identifiants créés seront réutilisés depuis le journal.");
            message.AppendLine("Aucun fichier divergent ne sera écrasé. " +
                "Une interruption peut laisser un lot partiel.");
            message.AppendLine();
            message.AppendLine("Confirmer la reprise de ce lot mixte ?");
            if (MessageBox.Show(this, message.ToString(),
                "Import IA · reprise mixte",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            ProjectAiMixedCommitStatus result = await Task.Run(() =>
                service.ResumeAsync(_projectDirectory, key));
            PendingBatchPicker.Items.Remove(selected);
            StatusText.Text = result == ProjectAiMixedCommitStatus.AlreadyApplied
                ? "Lot mixte déjà publié : aucune nouvelle écriture."
                : "Lot mixte repris : recharger les exigences et le Worktree.";
            PlanTextBox.Text = "Récupération du lot mixte " + key +
                " : " + result + ".\n" +
                "Les créations restent en brouillon.";
            MessageBox.Show(this, StatusText.Text,
                "Import IA · récupération mixte",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or
            UnauthorizedAccessException or ArgumentException)
        {
            StatusText.Text = "Reprise mixte refusée : " + exception.Message;
            MessageBox.Show(this, StatusText.Text +
                "\n\nLe journal local est conservé pour inspection " +
                "et résolution manuelle des divergences.",
                "Import IA · récupération mixte bloquée",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            _publishing = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Creates exactly one user-confirmed, unapproved requirement draft.
    /// The fresh EXI code and stable GUID are assigned by Nodalis, not by AI.
    /// </summary>
    /// <param name="sender">Explicit creation command.</param>
    /// <param name="e">User click.</param>
    private async void CreateReviewedRequirement_Click(
        object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _preview is null ||
            _comparisons is null || _preparedPlan is null ||
            _preparedPlan.Changes.Count != 0 ||
            _preparedPlan.Creations.Count != 1)
        {
            return;
        }

        ProjectAiPreparedReviewPlan expected = _preparedPlan;
        ProjectAiPreparedReviewPlan current;
        try
        {
            current = ProjectAiImportDecisionPlanner.Prepare(_preview,
                _comparisons.Values.ToArray(), _decisions.Values.ToArray());
            if (current.Changes.Count != 0 || current.Creations.Count != 1 ||
                current.Creations[0] != expected.Creations[0] ||
                current.RawResponseSha256 != expected.RawResponseSha256 ||
                current.ReviewedFields != expected.ReviewedFields ||
                current.IgnoredFields != expected.IgnoredFields ||
                current.PendingFields != expected.PendingFields)
            {
                _preparedPlan = null;
                StatusText.Text = "Plan de création modifié : requalifier le choix.";
                UpdateButtons();
                return;
            }
        }
        catch (InvalidDataException exception)
        {
            _preparedPlan = null;
            StatusText.Text = "Plan de création invalide : " + exception.Message;
            UpdateButtons();
            return;
        }

        long revision = _inputRevision;
        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            ProjectAiRequirementCreateTransactionService service = new(
                _workspaceRoot);
            (ProjectRequirementCatalog catalog, string? journaled) =
                await Task.Run(async () =>
                {
                    ProjectRequirementRegistryService registry = new(
                        _workspaceRoot);
                    ProjectRequirementCatalog local =
                        await registry.ListAsync(_projectDirectory);
                    string? previous = await service.GetJournaledReferenceAsync(
                        _projectDirectory, current);
                    return (local, previous);
                });
            if (revision != _inputRevision ||
                !ReferenceEquals(expected, _preparedPlan))
            {
                StatusText.Text = "Réponse ou plan modifié : création annulée.";
                return;
            }

            ProjectAiPreparedRequirementCreation creation =
                current.Creations.Single();
            if (creation.ProjectId != catalog.ProjectId)
            {
                throw new InvalidDataException(
                    "Le projet choisi ne correspond pas à la qualification.");
            }

            string code = journaled ?? catalog.NextReference ??
                throw new InvalidDataException(
                    "Aucune référence EXI n'est disponible.");
            string question = "Projet : " +
                creation.ProjectId.ToString("D") +
                "\nRéférence attribuée par Nodalis : " + code +
                "\nTitre retenu : " + creation.ReviewedTitle +
                "\n\nL'exigence sera créée comme BROUILLON : " +
                "aucune validation ni vérification automatique." +
                "\nLes autres propositions IA ne seront pas enregistrées." +
                "\nLe journal local conservera une copie de la fiche créée." +
                (journaled is null
                    ? "\nUn nouveau GUID stable sera généré par Nodalis."
                    : "\nUne transaction existante sera vérifiée : " +
                      "aucun doublon ne sera créé.") +
                "\n\nConfirmer cette création ?";
            if (MessageBox.Show(this, question,
                "Import IA · créer une seule exigence",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
                MessageBoxResult.Yes)
            {
                return;
            }

            (ProjectAiRequirementCreateResult result, string? auditWarning) =
                await PublishWithAuditAsync(current, "create",
                    creation.ProposalId.ToString("N"),
                    () => service.CommitAsync(_projectDirectory, current, code));
            _comparisons = null;
            ClearDecisions();
            ShowSelectedDetails();
            PlanTextBox.Text = "Exigence " + result.Entry.Card.Reference +
                " · " + result.Entry.Card.Id.ToString("D") +
                "\nFichier : " + result.Entry.RelativePath +
                "\nÉtat : " + result.Status +
                "\nBrouillon non approuvé. Recharger le Worktree si nécessaire.";
            StatusText.Text = result.Status ==
                ProjectAiRequirementCreateStatus.AlreadyCreated
                ? "La proposition a déjà créé cette exigence : aucun doublon."
                : "Nouvelle exigence EXI créée en brouillon, avec journal.";
            if (auditWarning is not null)
            {
                StatusText.Text += "\\n" + auditWarning;
            }
            MessageBox.Show(this, StatusText.Text +
                "\n\n" + result.Entry.RelativePath,
                "Import IA · résultat",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException or
            IOException or ArgumentException or UnauthorizedAccessException)
        {
            StatusText.Text = "Création refusée : " + exception.Message;
            PlanTextBox.Text = "Aucun écrasement automatique permis. " +
                "Si un journal est incomplet, examiner les instantanés locaux " +
                "avant toute tentative de reprise.";
            MessageBox.Show(this, StatusText.Text,
                "Import IA · refus ou récupération",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _publishing = false;
            _busy = false;
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
            (ProjectAiTitleCommitStatus result, string? auditWarning) =
                await PublishWithAuditAsync(current, "title",
                    change.ProposalId.ToString("N"), () =>
                        new ProjectAiTitleImportTransactionService(_workspaceRoot)
                            .CommitAsync(_projectDirectory, current));
            _comparisons = null;
            ClearDecisions();
            ShowSelectedDetails();
            PlanTextBox.Text = "Publication " + result +
                " : recharger la fiche EXI et relancer une comparaison " +
                "avant une nouvelle modification.";
            StatusText.Text = result == ProjectAiTitleCommitStatus.AlreadyApplied
                ? "Proposition déjà publiée (journal concordant) : aucun doublon."
                : "Titre EXI publié avec remplacement atomique et journal durable.";
            if (auditWarning is not null)
            {
                StatusText.Text += "\\n" + auditWarning;
            }
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

    /// <summary>
    /// Archives a human's full review decision map independently of publishing
    /// EXI changes. Raw external text is stored only with affirmative opt-in.
    /// </summary>
    /// <param name="sender">Explicit archive button.</param>
    /// <param name="e">User click.</param>
    private async void SaveReviewAudit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _preview is null || _comparisons is null)
        {
            return;
        }

        ProjectAiPreparedReviewPlan plan;
        try
        {
            plan = ProjectAiImportDecisionPlanner.Prepare(_preview,
                _comparisons.Values.ToArray(), _decisions.Values.ToArray());
        }
        catch (InvalidDataException exception)
        {
            StatusText.Text = "Revue invalide : " + exception.Message;
            return;
        }

        bool includeRaw = ArchiveRawResponseCheck.IsChecked == true;
        string confirmation = "Archiver la revue de " +
            _preview.Document.Proposals.Count + " proposition(s), " +
            plan.ReviewedFields + " choix explicites et " +
            plan.PendingFields + " champ(s) à revoir ?" +
            "\n\nL'historique conservera les décisions, les valeurs " +
            "explicitement acceptées et les justifications, avec les " +
            "empreintes des suggestions non retenues." +
            (includeRaw
                ? "\n\nATTENTION : vous avez demandé de conserver " +
                  "également la réponse brute intégrale dans le projet."
                : "\n\nLa réponse brute intégrale ne sera PAS enregistrée.") +
            "\n\nAucune exigence ne sera créée ou modifiée.";
        if (MessageBox.Show(this, confirmation,
            "Import IA · archiver la revue",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) !=
            MessageBoxResult.Yes)
        {
            return;
        }

        ProjectAiResponsePreview preview = _preview;
        ProjectAiRequirementReview[] reviews = _comparisons.Values.ToArray();
        ProjectAiFieldDecision[] decisions = _decisions.Values.ToArray();
        string? raw = includeRaw ? RawResponseBox.Text : null;
        _busy = true;
        _publishing = true;
        UpdateButtons();
        try
        {
            Guid id = await Task.Run(() =>
                new ProjectAiReviewHistoryService(_workspaceRoot).SaveAsync(
                    _projectDirectory, preview, reviews, decisions, plan,
                    includeRaw, raw, "review-only", null));
            StatusText.Text = "Revue enregistrée dans le projet : " +
                id.ToString("D") + " · réponse brute " +
                (includeRaw ? "conservée sur demande." : "non enregistrée.");
            PlanTextBox.Text = StatusText.Text +
                "\n\nCette archive n'autorise aucune écriture métier.";
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or ArgumentException or
            UnauthorizedAccessException)
        {
            StatusText.Text = "Archive refusée : " + exception.Message;
        }
        finally
        {
            _busy = false;
            _publishing = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Lists the current project's archived human reviews, without importing
    /// any data or displaying optional raw model responses.
    /// </summary>
    /// <param name="sender">Explicit history listing.</param>
    /// <param name="e">Click event.</param>
    private async void ListReviewAudits_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            IReadOnlyList<ProjectAiReviewAudit> audits =
                await Task.Run(() =>
                    new ProjectAiReviewHistoryService(_workspaceRoot).ListAsync(
                        _projectDirectory));
            ReviewAuditPicker.Items.Clear();
            foreach (ProjectAiReviewAudit audit in audits)
            {
                ReviewAuditPicker.Items.Add(new ComboBoxItem
                {
                    Content = audit.RecordedUtc.ToLocalTime()
                        .ToString("dd/MM/yyyy HH:mm") + " · " +
                        audit.Action + " · " +
                        audit.ReviewId.ToString("N")[..8],
                    Tag = audit.ReviewId
                });
            }

            ReviewAuditPicker.SelectedIndex = audits.Count > 0 ? 0 : -1;
            StatusText.Text = audits.Count + " revue(s) archivée(s) " +
                "accessibles. Les réponses brutes ne sont pas affichées.";
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or ArgumentException or
            UnauthorizedAccessException)
        {
            ReviewAuditPicker.Items.Clear();
            StatusText.Text = "Historique indisponible : " + exception.Message;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Displays a read-only field-by-field record and its optional outcome.
    /// The opt-in raw archive itself is never opened implicitly.
    /// </summary>
    /// <param name="sender">Explicit review selection button.</param>
    /// <param name="e">Click event.</param>
    private async void ShowReviewAudit_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly ||
            ReviewAuditPicker.SelectedItem is not ComboBoxItem selected ||
            selected.Tag is not Guid reviewId)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            (ProjectAiReviewAudit audit, ProjectAiReviewOutcome? outcome) =
                await Task.Run(async () =>
                {
                    ProjectAiReviewHistoryService service = new(_workspaceRoot);
                    ProjectAiReviewAudit record = await service.ReadAsync(
                        _projectDirectory, reviewId);
                    ProjectAiReviewOutcome? result =
                        await service.ReadOutcomeAsync(_projectDirectory, reviewId);
                    return (record, result);
                });
            StringBuilder builder = new();
            builder.AppendLine("HISTORIQUE DE QUALIFICATION HUMAINE");
            builder.AppendLine("Revue : " + audit.ReviewId.ToString("D"));
            builder.AppendLine("Date : " +
                audit.RecordedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"));
            builder.AppendLine("Projet : " + audit.ProjectId.ToString("D"));
            builder.AppendLine("Réponse IA SHA-256 : " + audit.RawResponseSha256);
            builder.AppendLine("Prompt : " +
                (audit.PromptKey ?? "(non indiqué)"));
            builder.AppendLine("Réponse brute : " +
                (audit.RawArchived ? "archivée avec consentement" : "non archivée"));
            builder.AppendLine("Action : " + audit.Action);
            builder.AppendLine("Transaction : " +
                (audit.TransactionKey ?? "(revue seule)"));
            builder.AppendLine("Résultat métier : " +
                (outcome?.Result ?? "non confirmé dans le journal d'audit"));
            builder.AppendLine("Décisions : " + audit.ReviewedFields +
                ", ignorées : " + audit.IgnoredFields +
                ", à revoir : " + audit.PendingFields);
            foreach (ProjectAiAuditField field in audit.Fields)
            {
                builder.AppendLine();
                builder.AppendLine(field.ProposalId.ToString("D") +
                    " · " + field.Kind + "/" + field.Operation +
                    " · " + field.Field + " → " + field.Decision);
                builder.AppendLine("  Résolution : " + field.MatchStatus);
                builder.AppendLine("  Valeur proposée SHA-256 : " +
                    field.SuggestedSha256);
                if (field.ApprovedValue is not null)
                {
                    builder.AppendLine("  Valeur retenue : " + field.ApprovedValue);
                }

                if (!string.IsNullOrWhiteSpace(field.Reason))
                {
                    builder.AppendLine("  Justification : " + field.Reason);
                }
            }

            PlanTextBox.Text = builder.ToString();
            StatusText.Text = "Revue archivée chargée en lecture seule : " +
                reviewId.ToString("D");
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or ArgumentException or
            UnauthorizedAccessException)
        {
            StatusText.Text = "Lecture de revue refusée : " + exception.Message;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Archives a confirmed publication's human decisions BEFORE its first
    /// business file write. A second, immutable outcome confirms the result.
    /// Publication is never claimed successful if only its review exists.
    /// </summary>
    /// <param name="plan">Exact rechecked human plan.</param>
    /// <param name="action">One recognized action bound to its transaction.</param>
    /// <param name="key">Canonical project-local import journal identifier.</param>
    /// <param name="publish">Business publication after audit staging.</param>
    /// <returns>Business result and any non-fatal audit-outcome warning.</returns>
    private async Task<(T Result, string? Warning)> PublishWithAuditAsync<T>(
        ProjectAiPreparedReviewPlan plan, string action, string key,
        Func<Task<T>> publish)
    {
        if (_preview is null || _comparisons is null)
        {
            throw new InvalidDataException(
                "Réponse ou comparaison manquante : publication interdite.");
        }

        ProjectAiResponsePreview preview = _preview;
        ProjectAiRequirementReview[] reviews = _comparisons.Values.ToArray();
        ProjectAiFieldDecision[] decisions = _decisions.Values.ToArray();
        bool includeRaw = ArchiveRawResponseCheck.IsChecked == true;
        string? raw = includeRaw ? RawResponseBox.Text : null;
        ProjectAiReviewHistoryService history = new(_workspaceRoot);
        Guid reviewId = await Task.Run(() => history.SaveAsync(
            _projectDirectory, preview, reviews, decisions, plan,
            includeRaw, raw, action, key));
        try
        {
            T result = await Task.Run(publish);
            string outcome = result switch
            {
                ProjectAiRequirementCreateResult created =>
                    created.Status.ToString(),
                null => "Failed",
                _ => result.ToString() ?? "Failed"
            };
            try
            {
                await Task.Run(() => history.RecordOutcomeAsync(
                    _projectDirectory, reviewId, outcome));
                return (result, null);
            }
            catch (Exception exception) when (exception is InvalidDataException or
                InvalidOperationException or IOException or ArgumentException or
                UnauthorizedAccessException)
            {
                // Business publication may already have succeeded. Never
                // claim failure or attempt a second publish on audit I/O error.
                return (result, "Import publié, mais résultat d'audit " +
                    "non enregistré : " + exception.Message);
            }
        }
        catch
        {
            try
            {
                // Failed includes interrupted/partially published batches;
                // it NEVER means that every individual file is unchanged.
                await Task.Run(() => history.RecordOutcomeAsync(
                    _projectDirectory, reviewId, "Failed"));
            }
            catch (Exception exception) when (exception is InvalidDataException or
                InvalidOperationException or IOException or ArgumentException or
                UnauthorizedAccessException)
            {
                // Keep original business exception and durable review.
            }

            throw;
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

        if (SaveReviewAuditButton is not null)
        {
            SaveReviewAuditButton.IsEnabled = !_busy && !_readOnly &&
                _preview is not null && _comparisons is not null;
        }

        if (ListReviewAuditsButton is not null)
        {
            ListReviewAuditsButton.IsEnabled = !_busy && !_readOnly;
        }

        if (ReviewAuditPicker is not null)
        {
            ReviewAuditPicker.IsEnabled = !_busy && !_readOnly;
        }

        if (ShowReviewAuditButton is not null)
        {
            ShowReviewAuditButton.IsEnabled = !_busy && !_readOnly &&
                ReviewAuditPicker?.SelectedItem is ComboBoxItem;
        }

        if (ArchiveRawResponseCheck is not null)
        {
            ArchiveRawResponseCheck.IsEnabled = !_busy && !_readOnly;
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
                _preparedPlan.Creations.Count == 0 &&
                _comparisons is not null && _preview is not null;
        }

        if (CreateReviewedRequirementButton is not null)
        {
            CreateReviewedRequirementButton.IsEnabled = !_busy && !_readOnly &&
                _preparedPlan is not null &&
                _preparedPlan.Changes.Count == 0 &&
                _preparedPlan.Creations.Count == 1 &&
                _comparisons is not null && _preview is not null;
        }

        if (PublishReviewedBatchButton is not null)
        {
            PublishReviewedBatchButton.IsEnabled = !_busy && !_readOnly &&
                _preparedPlan is not null &&
                _preparedPlan.Changes.Count is >= 2 and <= 20 &&
                _preparedPlan.Creations.Count == 0 &&
                _comparisons is not null && _preview is not null;
        }

        if (PublishMixedBatchButton is not null)
        {
            PublishMixedBatchButton.IsEnabled = !_busy && !_readOnly &&
                _preparedPlan is not null &&
                _preparedPlan.Changes.Count >= 1 &&
                _preparedPlan.Creations.Count >= 1 &&
                _preparedPlan.Changes.Count + _preparedPlan.Creations.Count <= 20 &&
                _comparisons is not null && _preview is not null;
        }

        if (FindPendingBatchesButton is not null)
        {
            FindPendingBatchesButton.IsEnabled = !_busy && !_readOnly;
        }

        if (PendingBatchPicker is not null)
        {
            PendingBatchPicker.IsEnabled = !_busy && !_readOnly;
        }

        if (ResumePendingBatchButton is not null)
        {
            ResumePendingBatchButton.IsEnabled = !_busy && !_readOnly &&
                PendingBatchPicker?.SelectedItem is ComboBoxItem;
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
            (proposal.Operation == "update" &&
                _comparisons![proposal.ProposalId].Status is
                    ProjectAiRequirementMatchStatus.GuidMatch or
                    ProjectAiRequirementMatchStatus.ReferenceMatch ||
             proposal.Operation == "create" &&
                _comparisons![proposal.ProposalId].Status ==
                    ProjectAiRequirementMatchStatus.CreationDraft);
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
