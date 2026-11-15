using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Projects;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Explicit review of external SFD JSON. All imported data is volatile;
/// there is no commit action, silent mapping, or DOCX import pathway.
/// </summary>
public partial class SfdImportReviewDialog : Window
{
    private sealed record ReviewRow(SfdImportReviewItem Item, string Label);

    private readonly SfdImportReviewService _service;
    private readonly SfdApprovedProcessImportService _publisher;
    private readonly SfdApprovedActivityImportService _activityPublisher;
    private readonly SfdApprovedRequirementImportService _requirementPublisher;
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
        }
    }
}
