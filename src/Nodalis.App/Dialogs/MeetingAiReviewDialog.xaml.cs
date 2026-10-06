using System.IO;
using System.Windows;
using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Lets the user edit, accept, or reject each local-AI meeting proposal before persistence.
/// </summary>
public partial class MeetingAiReviewDialog : Window
{
    private readonly List<MeetingAiProposalViewModel> _proposals;

    /// <summary>
    /// Initializes one review dialog from a parsed local-AI response.
    /// </summary>
    /// <param name="draft">The parsed assistant draft.</param>
    /// <param name="sourcePath">The source meeting path displayed for provenance.</param>
    public MeetingAiReviewDialog(
            MeetingAiDraft draft,
            string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(
            draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourcePath);

        InitializeComponent();

        _proposals =
            draft.Proposals
                .Select(proposal =>
                    new MeetingAiProposalViewModel(
                        proposal))
                .ToList();

        SummaryTextBox.Text =
            draft.Summary;
        AcceptSummaryCheckBox.IsChecked =
            !string.IsNullOrWhiteSpace(
                draft.Summary);
        ProposalsItemsControl.ItemsSource =
            _proposals;
        SourceText.Text =
            "Source : " +
            Path.GetFileName(
                sourcePath);
    }

    /// <summary>Gets the final user-approved result after the dialog succeeds.</summary>
    public MeetingAiDraft? ApprovedDraft { get; private set; }

    /// <summary>
    /// Validates the reviewed values and returns only explicitly accepted proposals.
    /// </summary>
    /// <param name="sender">The accept button.</param>
    /// <param name="e">The routed event arguments.</param>
    private void Accept_Click(
            object sender,
            RoutedEventArgs e)
    {
        string summary =
            AcceptSummaryCheckBox.IsChecked ==
                    true
                ? SummaryTextBox.Text.Trim()
                : string.Empty;

        MeetingAiProposal[] accepted =
            _proposals
                .Where(proposal =>
                    proposal.IsAccepted &&
                    !string.IsNullOrWhiteSpace(
                        proposal.Text))
                .Select(proposal =>
                    new MeetingAiProposal
                    {
                        Kind =
                            proposal.Kind,
                        Text =
                            proposal.Text.Trim()
                    })
                .ToArray();

        if (string.IsNullOrWhiteSpace(
                summary) &&
            accepted.Length ==
                0)
        {
            MessageBox.Show(
                this,
                "Conservez au moins le résumé ou une proposition avant de créer la synthèse.",
                "Revue du résumé IA",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ApprovedDraft =
            new MeetingAiDraft
            {
                Summary =
                    summary,
                Proposals =
                    accepted
            };

        DialogResult =
            true;
    }
}
