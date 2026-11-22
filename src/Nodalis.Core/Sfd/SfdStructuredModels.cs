namespace Nodalis.Core.Sfd;

/// <summary>A durable C block nested inside one requirement or future EMO.</summary>
public sealed record SfdConditionBlock
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required string Statement { get; init; }

    public List<SfdSubconditionBlock> Subconditions { get; init; } = [];
}

/// <summary>
/// A durable SC block, nested in a C. RM are referenced only by stable GUID;
/// their authoritative wording remains in the project-wide RM catalog.
/// </summary>
public sealed record SfdSubconditionBlock
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required string Statement { get; init; }

    public List<Guid> BusinessRuleIds { get; init; } = [];
}

/// <summary>A canonical, reusable RM declaration in the single shared Markdown catalog.</summary>
public sealed record SfdBusinessRule
{
    public required Guid Id { get; init; }

    public required string Reference { get; init; }

    public required string Statement { get; init; }

    public string Source { get; init; } = string.Empty;

    public SfdAssertionCertainty Certainty { get; init; } =
        SfdAssertionCertainty.ToConfirm;
}

/// <summary>Read-only SFD completion diagnostic with explicit severity.</summary>
public enum SfdCompletionSeverity
{
    Information,
    Incomplete,
    Invalid
}

/// <summary>Non-destructive assessment; no status or metadata is changed automatically.</summary>
public sealed record SfdCompletionFinding
{
    public required SfdCompletionSeverity Severity { get; init; }

    public required Guid DocumentId { get; init; }

    public required string Reference { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }
}
