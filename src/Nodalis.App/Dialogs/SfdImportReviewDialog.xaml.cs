using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit review of external SFD JSON. Raw responses stay volatile.
/// Any publication or immutable base capture requires a separate, human-
/// confirmed action. No AI network calls, silent mapping or DOCX import.
/// </summary>
public partial class SfdImportReviewDialog : Window
{
    private sealed record ReviewRow(SfdImportReviewItem Item, string Label);

    private readonly SfdImportReviewService _service;
    private readonly SfdApprovedProcessImportService _publisher;
    private readonly SfdApprovedActivityImportService _activityPublisher;
    private readonly SfdApprovedRequirementImportService _requirementPublisher;
    private readonly SfdApprovedRuleImportService _rulePublisher;
    private readonly SfdApprovedFeatureImportService _featurePublisher;
    private readonly SfdDocumentBaselineService _baselineService;
    private readonly SfdApprovedFieldUpdateService _fieldUpdateService;
    private readonly string _projectDirectory;
    private readonly bool _readOnly;
    private long _revision;
    private bool _busy;
    private SfdImportReviewSnapshot? _snapshot;
    private string? _analyzedResponse;

    /// <summary>Initializes one project-scoped, local-only proposal review window.</summary>
    /// <param name="workspaceRoot">Authorized workspace root.</param>
    /// <param name="projectDirectory">Canonical selected SFD project.</param>
    /// <param name="readOnly">Whether project or workspace is read-only.</param>
    public SfdImportReviewDialog(string workspaceRoot, string projectDirectory,
        bool readOnly = false)
    {
        _service = new SfdImportReviewService(workspaceRoot);
        _publisher = new SfdApprovedProcessImportService(workspaceRoot);
        _activityPublisher = new SfdApprovedActivityImportService(workspaceRoot);
        _requirementPublisher = new SfdApprovedRequirementImportService(workspaceRoot);
        _rulePublisher = new SfdApprovedRuleImportService(workspaceRoot);
        _featurePublisher = new SfdApprovedFeatureImportService(workspaceRoot);
        _baselineService = new SfdDocumentBaselineService(workspaceRoot);
        _fieldUpdateService = new SfdApprovedFieldUpdateService(workspaceRoot);
        _projectDirectory = projectDirectory;
        _readOnly = readOnly;
        InitializeComponent();
        UpdateButtons();
    }

    /// <summary>
    /// Invalidates a previous comparison whenever the user edits a single
    /// character: stale approvals or row decisions must never survive edits.
    /// </summary>
    /// <param name="sender">Raw pasted response.</param>
    /// <param name="e">Change notification.</param>
    private void Response_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AnalyzeButton is null)
        {
            return;
        }

        _revision++;
        _snapshot = null;
        _analyzedResponse = null;
        ProposalsList.ItemsSource = null;
        DetailsBox.Text = string.Empty;
        SummaryText.Text = "Texte modifié : l'analyse précédente n'est plus valide.";
        StatusText.Text = "Aucune modification du projet.";
        UpdateButtons();
    }

    /// <summary>Performs a bounded local review, rejecting stale UI responses.</summary>
    /// <param name="sender">Analyze button.</param>
    /// <param name="e">Click event.</param>
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(ResponseTextBox.Text))
        {
            return;
        }

        long revision = _revision;
        string input = ResponseTextBox.Text;
        _busy = true;
        UpdateButtons();
        try
        {
            SfdImportReviewSnapshot snapshot = await Task.Run(() =>
                _service.ReviewAsync(_projectDirectory, input));
            if (revision != _revision)
            {
                return;
            }

            _snapshot = snapshot;
            _analyzedResponse = input;
            ReviewRow[] rows = snapshot.Items.Select(item => new ReviewRow(
                item, item.Status + " · " + item.Kind +
                    " · " + item.Title)).ToArray();
            ProposalsList.ItemsSource = rows;
            ProposalsList.SelectedIndex = rows.Length > 0 ? 0 : -1;
            SummaryText.Text = rows.Length + " proposition(s), " +
                snapshot.Incomplete + " brouillon(s) incomplet(s), " +
                snapshot.Blocked + " en attente d'arbitrage/refus. " +
                "Plan : " + snapshot.PlanSha256[..12] + "…";
            StatusText.Text = "Comparaison terminée · aucune publication.";
        }
        catch (Exception exception) when (exception is InvalidDataException or
            IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (revision != _revision)
            {
                return;
            }

            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = "Réponse non exploitable : " + exception.Message;
            StatusText.Text = "Aucun objet créé, modifié ou validé.";
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Whether one user-approved PM was actually published.</summary>
    public bool Changed { get; private set; }

    /// <summary>Publishes a single reviewed new PM through the durable journal.</summary>
    /// <param name="sender">Publish selected PM button.</param>
    /// <param name="e">Click event.</param>
    private async void Publish_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            _analyzedResponse is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind != "businessProcess" ||
            selected.Item.Operation != "create" ||
            selected.Item.Status != SfdImportReviewStatus.NewCandidate)
        {
            return;
        }

        SfdImportReviewSnapshot snapshot = _snapshot;
        string response = _analyzedResponse;
        string clientKey = selected.Item.ClientKey;
        string description = selected.Item.ProposedDescription;
        MessageBoxResult confirmed = MessageBox.Show(this,
            "Créer uniquement ce PM à partir de la proposition examinée ?\n\n" +
            selected.Item.Title + "\n\nDescription :\n" +
            (description.Length > 700 ? description[..700] + "…" : description) +
            "\n\nUn journal permettra la reprise après interruption. " +
            "La fiche restera À CONFIRMER ; aucune autre proposition ne sera publiée.",
            "Confirmer la publication du PM", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            SfdApprovedProcessResult result = await Task.Run(() =>
                _publisher.PublishAsync(_projectDirectory, response,
                    snapshot, clientKey));
            Changed = Changed || !result.AlreadyApplied;
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = result.AlreadyApplied
                ? "PM déjà présent, même identité : " + result.Reference
                : "PM créé avec journal : " + result.Reference;
            StatusText.Text = "Ré-analysez les autres propositions avant publication.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Publication PM suspendue", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Explicit publication of ONE activity with a GUID-resolved PM parent.
    /// User confirmation is separate from JSON analysis and plan comparison.
    /// </summary>
    /// <param name="sender">Reviewed ACT publish button.</param>
    /// <param name="e">Click event.</param>
    private async void PublishActivity_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            _analyzedResponse is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind != "businessActivity" ||
            selected.Item.Operation != "create" ||
            selected.Item.Status != SfdImportReviewStatus.NewCandidate ||
            selected.Item.ResolvedParentId is not Guid parent)
        {
            return;
        }

        string response = _analyzedResponse;
        SfdImportReviewSnapshot snapshot = _snapshot;
        string key = selected.Item.ClientKey;
        string description = selected.Item.ProposedDescription;
        MessageBoxResult confirmed = MessageBox.Show(this,
            "Publier uniquement cette ACT sous le PM identifié ?\n\n" +
            selected.Item.Title + "\n\n" +
            "PM parent (GUID) : " + parent.ToString("D") + "\n\n" +
            "Description proposée :\n" +
            (description.Length > 600 ? description[..600] + "…" : description) +
            "\n\nLe parent doit être inchangé depuis la revue. " +
            "La nouvelle ACT restera À CONFIRMER ; aucune autre proposition " +
            "ne sera publiée.",
            "Confirmer la création de l'ACT",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            SfdApprovedActivityResult result = await Task.Run(() =>
                _activityPublisher.PublishAsync(_projectDirectory,
                    response, snapshot, key));
            Changed = Changed || !result.AlreadyApplied;
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = result.AlreadyApplied
                ? "ACT déjà publiée avec le même GUID : " + result.Reference
                : "ACT publiée après confirmation : " + result.Reference;
            StatusText.Text =
                "Réanalysez la réponse pour revoir les autres propositions.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Publication ACT suspendue", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Confirms one EXU/EXS/EXR creation including authored C/SC and canonical
    /// RM GUID references; no automatic source validation or bulk approval.
    /// </summary>
    /// <param name="sender">Explicitly selected requirement publish command.</param>
    /// <param name="e">Click event.</param>
    private async void PublishRequirement_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            _analyzedResponse is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind is not ("userRequirement" or
                "systemRequirement" or "regulatoryRequirement") ||
            selected.Item.Operation != "create" ||
            selected.Item.Status is not (SfdImportReviewStatus.NewCandidate or
                SfdImportReviewStatus.IncompleteDraft) ||
            selected.Item.ResolvedParentId is not Guid parent)
        {
            return;
        }

        string response = _analyzedResponse;
        SfdImportReviewSnapshot snapshot = _snapshot;
        string key = selected.Item.ClientKey;
        string kind = selected.Item.Kind;
        string warning = selected.Item.Status == SfdImportReviewStatus.IncompleteDraft
            ? "\n\nATTENTION : cette exigence sera créée comme BROUILLON INCOMPLET ; " +
                "aucune C/SC/RM manquante ne sera fabriquée."
            : string.Empty;
        string structured = FormatStructuredBlocks(selected.Item.ProposedConditions);
        MessageBoxResult approval = MessageBox.Show(this,
            "Créer uniquement cette " + kind + " après vérification ?\n\n" +
            selected.Item.Title + "\n\n" +
            "ACT parente : " + parent.ToString("D") +
            "\nBlocs proposés : " + selected.Item.ConditionCount + " C, " +
                selected.Item.SubconditionCount + " SC, " +
                selected.Item.RuleLinkCount + " références RM" +
            "\nSource EXR fournie : " +
                (selected.Item.ProposedRegulatorySource ?? "Aucune (à confirmer)") +
            "\n\nC/SC/RM proposées :\n" +
            (structured.Length > 1100 ? structured[..1100] +
                "\n… relisez le détail complet avant d'approuver." : structured) +
            warning + "\n\nLa fiche restera À CONFIRMER. " +
            "Toute référence RM devra déjà exister dans le catalogue ; " +
            "aucune autre fiche ne sera créée.",
            "Confirmer la création d'exigence SFD",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (approval != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            SfdApprovedRequirementResult result = await Task.Run(() =>
                _requirementPublisher.PublishAsync(_projectDirectory,
                    response, snapshot, key));
            Changed = Changed || !result.AlreadyApplied;
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = result.AlreadyApplied
                ? "Exigence déjà publiée, GUID conservé : " + result.Reference
                : "Exigence publiée après confirmation : " + result.Reference;
            StatusText.Text = "Aucune autre proposition appliquée. " +
                "Réanalysez le lot avant une nouvelle publication.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Publication EXU/EXS/EXR suspendue",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Publishes one RM in the canonical shared catalog, with explicit
    /// confirmation of the exact statement and no invented provenance.
    /// </summary>
    /// <param name="sender">Reviewed RM approval button.</param>
    /// <param name="e">Click event.</param>
    private async void PublishRule_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            _analyzedResponse is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind != "businessRule" ||
            selected.Item.Operation != "create" ||
            selected.Item.Status != SfdImportReviewStatus.NewCandidate)
        {
            return;
        }

        string response = _analyzedResponse;
        SfdImportReviewSnapshot snapshot = _snapshot;
        string key = selected.Item.ClientKey;
        MessageBoxResult approval = MessageBox.Show(this,
            "Ajouter UNE règle au catalogue RM canonique ?\n\n" +
            selected.Item.Title + "\n\n" +
            "Description proposée (non probante) :\n" +
            (selected.Item.ProposedDescription.Length > 500
                ? selected.Item.ProposedDescription[..500] + "…"
                : selected.Item.ProposedDescription) +
            "\n\nLa règle aura un GUID attribué par Nodalis et restera " +
            "À CONFIRMER sans source fictive. Aucune exigence ni SC n'est " +
            "modifiée automatiquement ; il faudra réanalyser la réponse " +
            "pour utiliser sa clé dans les SC.",
            "Valider la création d'une RM",
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (approval != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            SfdApprovedRuleResult result = await Task.Run(() =>
                _rulePublisher.PublishAsync(_projectDirectory,
                    response, snapshot, key));
            Changed = Changed || !result.AlreadyApplied;
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = result.AlreadyApplied
                ? "RM déjà publiée, même GUID : " + result.Reference
                : "RM ajoutée au catalogue : " + result.Reference;
            StatusText.Text =
                "Réanalysez le lot pour relier cette RM aux sous-conditions.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Publication RM suspendue", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Confirms and publishes one FCT with a verified with/sans-IHM mode and
    /// its exact ACT links; missing links remain an incomplete draft.
    /// </summary>
    /// <param name="sender">Explicit FCT approval button.</param>
    /// <param name="e">Click event.</param>
    private async void PublishFeature_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            _analyzedResponse is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind != "feature" ||
            selected.Item.Operation != "create" ||
            selected.Item.Status is not (SfdImportReviewStatus.NewCandidate or
                SfdImportReviewStatus.IncompleteDraft) ||
            selected.Item.ProposedUiMode is not ("withUi" or "withoutUi") ||
            selected.Item.ActivityRevisions.Count != selected.Item.ActivityKeys.Count)
        {
            return;
        }

        string response = _analyzedResponse;
        SfdImportReviewSnapshot snapshot = _snapshot;
        string key = selected.Item.ClientKey;
        string linked = string.Join("\n",
            selected.Item.ActivityRevisions.OrderBy(pair => pair.Key)
                .Select(pair => "- ACT GUID " + pair.Key.ToString("D") +
                    " · SHA " + pair.Value[..12] + "…"));
        string warning = selected.Item.ActivityKeys.Count == 0
            ? "\n\nATTENTION : FCT sans ACT liée = brouillon incomplet ; " +
                "aucun lien ne sera créé automatiquement."
            : string.Empty;
        MessageBoxResult answer = MessageBox.Show(this,
            "Créer exactement cette FCT et ses liens ACT ?\n\n" +
            selected.Item.Title + "\nMode : " +
                selected.Item.ProposedUiMode +
            "\nActivités proposées : " +
                selected.Item.ActivityKeys.Count + "\n" +
            (linked.Length > 1200
                ? linked[..1200] + "\n… consultez le détail avant de confirmer."
                : linked) +
            warning +
            "\n\nLa FCT restera À CONFIRMER. Un journal permettra la " +
            "reprise entre la création du Markdown et les relations N:N.",
            "Confirmer la publication de la FCT",
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            SfdApprovedFeatureResult result = await Task.Run(() =>
                _featurePublisher.PublishAsync(_projectDirectory,
                    response, snapshot, key));
            Changed = Changed || !result.AlreadyApplied;
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = result.AlreadyApplied
                ? "FCT déjà publiée avec ses liens : " + result.Reference
                : "FCT publiée : " + result.Reference + " ; ACT liées : " +
                    result.LinkedActivityCount;
            StatusText.Text = "Réanalysez les autres propositions avant publication.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Publication FCT suspendue", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Freezes an exact current source as the one immutable base of an update.
    /// A confirmation is required because the saved source contains local data.
    /// </summary>
    /// <param name="sender">Base capture button.</param>
    /// <param name="e">Click event.</param>
    private async void CaptureBaseline_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind is not ("businessProcess" or "businessActivity") ||
            selected.Item.Operation != "update" ||
            selected.Item.ExistingId is not Guid targetId ||
            selected.Item.ExistingSha256 is not string sourceHash ||
            selected.Item.ThreeWay is not { HasBaseline: false })
        {
            return;
        }

        MessageBoxResult decision = MessageBox.Show(this,
            "Figer la version de référence de ce document SFD ?\n\n" +
            selected.Item.Title + "\nGUID : " + targetId.ToString("D") +
            "\nSHA : " + sourceHash[..12] + "…" +
            "\n\nUne copie locale immuable du Markdown courant sera créée " +
            "dans le journal privé du projet. Aucune fiche métier ne sera " +
            "modifiée ni transmise à une IA.",
            "Figer la base de comparaison", MessageBoxButton.YesNo,
            MessageBoxImage.Question, MessageBoxResult.No);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            await Task.Run(() => _baselineService.CaptureAsync(
                _projectDirectory, targetId, sourceHash));
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = "Base immuable enregistrée pour ce GUID. " +
                "Cliquez sur « Analyser et comparer » pour voir les différences.";
            StatusText.Text = "Aucune modification de fiche SFD.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or
            ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Base non capturée", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>
    /// Opens actual base/current/proposed values and applies ONLY chosen safe
    /// PM/ACT fields with a durable journal and optimistic source revision.
    /// </summary>
    /// <param name="sender">Field approval action.</param>
    /// <param name="e">Click event.</param>
    private async void UpdateFields_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _readOnly || _snapshot is null ||
            _analyzedResponse is null ||
            ProposalsList.SelectedItem is not ReviewRow selected ||
            selected.Item.Kind is not ("businessProcess" or "businessActivity") ||
            selected.Item.Operation != "update" ||
            selected.Item.Status != SfdImportReviewStatus.UpdateCandidate ||
            selected.Item.ThreeWay is not
            { HasBaseline: true, HasConflict: false, HasProposedChange: true })
        {
            return;
        }

        SfdThreeWayApprovalDialog approval = new(selected.Item)
        {
            Owner = this
        };
        if (approval.ShowDialog() != true)
        {
            return;
        }

        string response = _analyzedResponse;
        SfdImportReviewSnapshot snapshot = _snapshot;
        string key = selected.Item.ClientKey;
        bool title = approval.AcceptTitle;
        bool description = approval.AcceptDescription;
        _busy = true;
        UpdateButtons();
        try
        {
            SfdApprovedFieldUpdateResult result = await Task.Run(() =>
                _fieldUpdateService.PublishAsync(_projectDirectory,
                    response, snapshot, key, title, description));
            Changed = Changed || !result.AlreadyApplied;
            _revision++;
            _snapshot = null;
            _analyzedResponse = null;
            ProposalsList.ItemsSource = null;
            DetailsBox.Text = string.Empty;
            SummaryText.Text = result.AlreadyApplied
                ? "Champs déjà appliqués sur la même fiche GUID."
                : "Champs approuvés appliqués sans recréer le document.";
            StatusText.Text =
                "Aucune autre proposition appliquée. Nouvelle revue requise.";
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or UnauthorizedAccessException or
            ArgumentException)
        {
            MessageBox.Show(this, exception.Message,
                "Mise à jour SFD suspendue", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    /// <summary>Displays explicit provenance, comparison and blocking cause.</summary>
    /// <param name="sender">Selected candidate.</param>
    /// <param name="e">Selection event.</param>
    private void Proposal_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_snapshot is null ||
            ProposalsList.SelectedItem is not ReviewRow selected)
        {
            DetailsBox.Text = string.Empty;
            return;
        }

        SfdImportReviewItem item = selected.Item;
        UpdateButtons();
        DetailsBox.Text =
            "Étape : " + _snapshot.Step +
            "\nLot : " + _snapshot.RunId +
            "\nClé externe provisoire : " + item.ClientKey +
            "\nType / opération : " + item.Kind + " / " + item.Operation +
            "\nStatut d'analyse : " + item.Status +
            "\nTitre proposé : " + item.Title +
            "\nDescription proposée : " + item.ProposedDescription +
            "\nParent proposé : " + (item.ProposedParentKey ?? "Aucun") +
            "\nPM parent résolu : " +
                (item.ResolvedParentId?.ToString("D") ?? "À publier/résoudre") +
            "\nSHA du PM au moment de la revue : " +
                (item.ResolvedParentSha256 ?? "Non disponible") +
            "\nMode IHM : " + (item.ProposedUiMode ?? "Sans objet") +
            "\nSource EXR proposée (NON vérifiée) : " +
                (item.ProposedRegulatorySource ?? "Non renseignée") +
            "\nSources déclarées : " + string.Join(", ", item.SourceKeys) +
            "\nACT liées : " + string.Join(", ", item.ActivityKeys) +
            "\nSHA ACT liées : " + string.Join(", ",
                item.ActivityRevisions.OrderBy(pair => pair.Key)
                    .Select(pair => pair.Key.ToString("D") + "=" + pair.Value)) +
            "\nExigences sources : " + string.Join(", ", item.RequirementKeys) +
            "\nBlocs C/SC/RM : " + item.ConditionCount + " / " +
                item.SubconditionCount + " / " + item.RuleLinkCount +
            "\nIdentité canonique existante : " +
                (item.ExistingId?.ToString("D") ?? "Aucune") +
            "\nRéférence existante : " +
                (item.ExistingReference ?? "Aucune") +
            "\nTitre existant : " + (item.ExistingTitle ?? "Aucun") +
            "\nSHA source : " + (item.ExistingSha256 ?? "Non applicable") +
            "\nDécision métier : NON VALIDÉE" +
            "\n\n## Comparaison Base / Courant / Proposé\n" +
                FormatThreeWay(selected.Item.ThreeWay) +
            "\n\n## C/SC/RM proposées (texte intégral à relire)\n" +
                FormatStructuredBlocks(item.ProposedConditions) +
            "\n\n" + item.Message;
    }

    /// <summary>
    /// Formats all exact model-proposed conditions, subconditions and RM
    /// GUID references for informed, pre-publication human review.
    /// No part of this text is executed or imported by simply opening it.
    /// </summary>
    /// <param name="conditions">Untrusted, parsed proposal blocks.</param>
    /// <returns>Plain readable text without invented blocks.</returns>
    private static string FormatStructuredBlocks(
        IReadOnlyList<SfdImportCondition> conditions)
    {
        if (conditions.Count == 0)
        {
            return "Aucune C proposée — brouillon incomplet.";
        }

        System.Text.StringBuilder text = new();
        foreach (SfdImportCondition condition in conditions)
        {
            text.Append("C [").Append(condition.ClientKey)
                .Append("] ").AppendLine(condition.Statement);
            if (condition.Subconditions.Count == 0)
            {
                text.AppendLine("  Aucune SC proposée — brouillon incomplet.");
            }

            foreach (SfdImportSubcondition subcondition in condition.Subconditions)
            {
                text.Append("  SC [").Append(subcondition.ClientKey)
                    .Append("] ").AppendLine(subcondition.Statement);
                text.Append("    Références RM (GUID) : ")
                    .AppendLine(subcondition.BusinessRuleKeys.Count == 0
                        ? "Aucune — brouillon incomplet."
                        : string.Join(", ", subcondition.BusinessRuleKeys));
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Shows every complete base/current/proposed field value, without
    /// guessing conflicting contents or treating a difference as approval.
    /// </summary>
    /// <param name="comparison">Frozen optional 3-way projection.</param>
    /// <returns>Readable multiline explanation.</returns>
    private static string FormatThreeWay(SfdThreeWayPreview? comparison)
    {
        if (comparison is null)
        {
            return "Pas de comparaison à trois voies pour cette opération.";
        }

        if (!comparison.HasBaseline)
        {
            return "Base absente : utiliser « Figer la base… » avant de " +
                "comparer les champs existants.";
        }

        System.Text.StringBuilder details = new();
        details.Append("SHA base : ").AppendLine(comparison.BaselineSha256)
            .Append("SHA actuel : ").AppendLine(comparison.CurrentSha256);
        foreach (SfdThreeWayField entry in comparison.Fields)
        {
            details.AppendLine()
                .Append("Champ ").Append(entry.Name).Append(" : ")
                .AppendLine(entry.Status.ToString())
                .AppendLine("Base :").AppendLine(entry.BaseValue)
                .AppendLine("Actuel :").AppendLine(entry.CurrentValue)
                .AppendLine("Proposé :").AppendLine(entry.ProposedValue);
        }

        if (comparison.HasConflict)
        {
            details.AppendLine()
                .AppendLine("CONFLIT : les modifications concurrentes " +
                    "nécessitent un arbitrage humain. Publication bloquée.");
        }

        return details.ToString();
    }

    /// <summary>Disables repeated analysis while the read-only task is running.</summary>
    private void UpdateButtons()
    {
        if (AnalyzeButton is not null)
        {
            AnalyzeButton.IsEnabled = !_busy &&
                !string.IsNullOrWhiteSpace(ResponseTextBox.Text);
            PublishButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null && _analyzedResponse is not null &&
                ProposalsList.SelectedItem is ReviewRow row &&
                row.Item.Kind == "businessProcess" &&
                row.Item.Operation == "create" &&
                row.Item.Status == SfdImportReviewStatus.NewCandidate;
            PublishActivityButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null && _analyzedResponse is not null &&
                ProposalsList.SelectedItem is ReviewRow activity &&
                activity.Item.Kind == "businessActivity" &&
                activity.Item.Operation == "create" &&
                activity.Item.Status == SfdImportReviewStatus.NewCandidate &&
                activity.Item.ResolvedParentId.HasValue &&
                activity.Item.ResolvedParentSha256 is not null;
            PublishRequirementButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null && _analyzedResponse is not null &&
                ProposalsList.SelectedItem is ReviewRow requirement &&
                requirement.Item.Kind is ("userRequirement" or
                    "systemRequirement" or "regulatoryRequirement") &&
                requirement.Item.Operation == "create" &&
                requirement.Item.Status is (SfdImportReviewStatus.NewCandidate or
                    SfdImportReviewStatus.IncompleteDraft) &&
                requirement.Item.ResolvedParentId.HasValue &&
                requirement.Item.ResolvedParentSha256 is not null;
            PublishRuleButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null && _analyzedResponse is not null &&
                ProposalsList.SelectedItem is ReviewRow rule &&
                rule.Item.Kind == "businessRule" &&
                rule.Item.Operation == "create" &&
                rule.Item.Status == SfdImportReviewStatus.NewCandidate;
            PublishFeatureButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null && _analyzedResponse is not null &&
                ProposalsList.SelectedItem is ReviewRow feature &&
                feature.Item.Kind == "feature" &&
                feature.Item.Operation == "create" &&
                feature.Item.ProposedUiMode is ("withUi" or "withoutUi") &&
                feature.Item.Status is (SfdImportReviewStatus.NewCandidate or
                    SfdImportReviewStatus.IncompleteDraft) &&
                feature.Item.ActivityRevisions.Count ==
                    feature.Item.ActivityKeys.Count;

            // Only one publication role can be selected at once. Collapsing
            // unrelated actions prevents WPF footer clipping on small screens
            // and DPI-scaled workstations.
            PublishButton.Visibility = PublishButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            PublishActivityButton.Visibility = PublishActivityButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            PublishRequirementButton.Visibility = PublishRequirementButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            PublishRuleButton.Visibility = PublishRuleButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            PublishFeatureButton.Visibility = PublishFeatureButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            CaptureBaselineButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null &&
                ProposalsList.SelectedItem is ReviewRow baseRow &&
                baseRow.Item.Kind is ("businessProcess" or "businessActivity") &&
                baseRow.Item.Operation == "update" &&
                baseRow.Item.ExistingId.HasValue &&
                baseRow.Item.ExistingSha256 is not null &&
                baseRow.Item.ThreeWay is { HasBaseline: false };
            UpdateFieldsButton.IsEnabled = !_busy && !_readOnly &&
                _snapshot is not null && _analyzedResponse is not null &&
                ProposalsList.SelectedItem is ReviewRow updateRow &&
                updateRow.Item.Kind is ("businessProcess" or "businessActivity") &&
                updateRow.Item.Operation == "update" &&
                updateRow.Item.Status == SfdImportReviewStatus.UpdateCandidate &&
                updateRow.Item.ThreeWay is
                { HasBaseline: true, HasConflict: false, HasProposedChange: true };
            CaptureBaselineButton.Visibility = CaptureBaselineButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateFieldsButton.Visibility = UpdateFieldsButton.IsEnabled
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
