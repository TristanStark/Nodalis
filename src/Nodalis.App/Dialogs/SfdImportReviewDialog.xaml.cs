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
    private readonly string _projectDirectory;
    private long _revision;
    private bool _busy;
    private SfdImportReviewSnapshot? _snapshot;

    /// <summary>Initializes one project-scoped, local-only proposal review window.</summary>
    /// <param name="workspaceRoot">Authorized workspace root.</param>
    /// <param name="projectDirectory">Canonical selected SFD project.</param>
    public SfdImportReviewDialog(string workspaceRoot, string projectDirectory)
    {
        _service = new SfdImportReviewService(workspaceRoot);
        _projectDirectory = projectDirectory;
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
        DetailsBox.Text =
            "Étape : " + _snapshot.Step +
            "\nLot : " + _snapshot.RunId +
            "\nClé externe provisoire : " + item.ClientKey +
            "\nType / opération : " + item.Kind + " / " + item.Operation +
            "\nStatut d'analyse : " + item.Status +
            "\nTitre proposé : " + item.Title +
            "\nIdentité canonique existante : " +
                (item.ExistingId?.ToString("D") ?? "Aucune") +
            "\nRéférence existante : " +
                (item.ExistingReference ?? "Aucune") +
            "\nTitre existant : " + (item.ExistingTitle ?? "Aucun") +
            "\nSHA source : " + (item.ExistingSha256 ?? "Non applicable") +
            "\nDécision métier : NON VALIDÉE" +
            "\n\n" + item.Message;
    }

    /// <summary>Disables repeated analysis while the read-only task is running.</summary>
    private void UpdateButtons()
    {
        if (AnalyzeButton is not null)
        {
            AnalyzeButton.IsEnabled = !_busy &&
                !string.IsNullOrWhiteSpace(ResponseTextBox.Text);
        }
    }
}
