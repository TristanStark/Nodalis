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
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or ArgumentException or IOException or
            UnauthorizedAccessException)
        {
            if (revision == _inputRevision)
            {
                _comparisons = null;
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
    }

    /// <summary>Discards volatile proposals without any persistence.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
