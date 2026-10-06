using Nodalis.Core.AI;

namespace Nodalis.App.Dialogs;

/// <summary>
/// Exposes one editable and individually selectable local-AI meeting proposal.
/// </summary>
public sealed class MeetingAiProposalViewModel
{
    /// <summary>
    /// Initializes one proposal view model.
    /// </summary>
    /// <param name="proposal">The parsed proposal.</param>
    public MeetingAiProposalViewModel(
            MeetingAiProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(
            proposal);

        Kind =
            proposal.Kind;
        Category =
            GetCategoryLabel(
                proposal.Kind);
        Text =
            proposal.Text;
        IsAccepted =
            true;
    }

    /// <summary>Gets the proposal kind.</summary>
    public MeetingAiProposalKind Kind { get; }

    /// <summary>Gets the human-readable category.</summary>
    public string Category { get; }

    /// <summary>Gets or sets whether the proposal will be kept.</summary>
    public bool IsAccepted { get; set; }

    /// <summary>Gets or sets the editable proposal text.</summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets the localized label for one proposal category.
    /// </summary>
    /// <param name="kind">The proposal kind.</param>
    /// <returns>The category label.</returns>
    private static string GetCategoryLabel(
            MeetingAiProposalKind kind) =>
        kind switch
        {
            MeetingAiProposalKind.Decision => "Décision",
            MeetingAiProposalKind.Action => "Action",
            MeetingAiProposalKind.OpenQuestion => "Question ouverte",
            MeetingAiProposalKind.Risk => "Risque / attention",
            _ => "Proposition"
        };
}
