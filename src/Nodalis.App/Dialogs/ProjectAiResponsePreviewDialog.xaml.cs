using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Nodalis.Core.Importing;

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

    /// <summary>Initializes the read-only, no-network analysis dialog.</summary>
    public ProjectAiResponsePreviewDialog()
    {
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

    /// <summary>Shows only the selected candidate's untrusted text fields.</summary>
    /// <param name="sender">Candidate list.</param>
    /// <param name="e">Selection change.</param>
    private void ProposalsList_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
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
        builder.AppendLine(
            "Comparaison à trois voies / validation humaine à intégrer " +
            "après résolution de la source et des identités réelles.");
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
    }

    /// <summary>Discards volatile proposals without any persistence.</summary>
    /// <param name="sender">Close button.</param>
    /// <param name="e">Click event.</param>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
