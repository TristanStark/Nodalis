namespace Nodalis.Core.AI;

/// <summary>
/// Identifies the semantic role of one proposal produced from a meeting by the local assistant.
/// </summary>
public enum MeetingAiProposalKind
{
    /// <summary>A candidate decision.</summary>
    Decision,

    /// <summary>A candidate action.</summary>
    Action,

    /// <summary>An unresolved question.</summary>
    OpenQuestion,

    /// <summary>A risk or point requiring attention.</summary>
    Risk
}

/// <summary>
/// Represents one editable proposal produced from a meeting.
/// </summary>
public sealed record MeetingAiProposal
{
    /// <summary>Gets the proposal category.</summary>
    public required MeetingAiProposalKind Kind { get; init; }

    /// <summary>Gets the proposal text.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// Represents a structured meeting summary returned by the local assistant or approved by the user.
/// </summary>
public sealed record MeetingAiDraft
{
    /// <summary>Gets the meeting summary.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets the structured proposals.</summary>
    public IReadOnlyList<MeetingAiProposal> Proposals { get; init; } = [];
}
