using Nodalis.Core.Projects;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable exact-file rollback data for a single approved task handover.
/// Files under the transaction directory are byte-exact original and proposed
/// snapshots; only Phase changes after this record has been prepared.
/// </summary>
public sealed record ProjectHandoverTaskJournal
{
    /// <summary>Permanent transaction identity.</summary>
    public required Guid TransactionId { get; init; }

    /// <summary>Immutable source-owned human approval identity.</summary>
    public required Guid IntentId { get; init; }

    /// <summary>Owner of the old task, including during a B succession.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Proposed next owner or successor owner.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Human-reviewed source task.</summary>
    public required Guid TaskId { get; init; }

    /// <summary>Preserved A identity or independently allocated B identity.</summary>
    public required Guid TargetTaskId { get; init; }

    /// <summary>Explicit user-selected A/B strategy.</summary>
    public required ProjectHandoverMethod Method { get; init; }

    /// <summary>Canonical source project relative to one workspace.</summary>
    public required string SourceProjectRelativePath { get; init; }

    /// <summary>Canonical target project relative to one workspace.</summary>
    public required string TargetProjectRelativePath { get; init; }

    /// <summary>Exact canonical source task Markdown file.</summary>
    public required string SourceDocumentRelativePath { get; init; }

    /// <summary>Exact canonical target task Markdown file.</summary>
    public required string TargetDocumentRelativePath { get; init; }

    /// <summary>Original source byte SHA-256.</summary>
    public required string OriginalSourceHash { get; init; }

    /// <summary>Original target byte SHA-256.</summary>
    public required string OriginalTargetHash { get; init; }

    /// <summary>Staged source byte SHA-256.</summary>
    public required string PublishedSourceHash { get; init; }

    /// <summary>Staged target byte SHA-256.</summary>
    public required string PublishedTargetHash { get; init; }

    /// <summary>
    /// Prepared, first-published, completed, rollback-prepared or rolled-back.
    /// Never use this phase alone to infer actual file state after a crash.
    /// </summary>
    public required string Phase { get; init; }

    /// <summary>First durable transaction creation time.</summary>
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Exact read-only readiness/links status before physical mutation.</summary>
public sealed record ProjectHandoverTaskExecutionPlan
{
    /// <summary>Approved and source-checked handover event.</summary>
    public required ProjectHandoverIntent Intent { get; init; }

    /// <summary>Task source that will be changed.</summary>
    public required string SourceDocumentRelativePath { get; init; }

    /// <summary>Task target that will be changed.</summary>
    public required string TargetDocumentRelativePath { get; init; }

    /// <summary>Whole pre-transaction source content SHA-256.</summary>
    public required string SourceHash { get; init; }

    /// <summary>Whole pre-transaction target content SHA-256.</summary>
    public required string TargetHash { get; init; }

    /// <summary>Exact source replacement text, generated purely.</summary>
    public required string ProposedSource { get; init; }

    /// <summary>Exact target replacement text, generated purely.</summary>
    public required string ProposedTarget { get; init; }

    /// <summary>Full source-specific audit warnings, not automatic approvals.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when no discovered reference requires a manual rewrite.</summary>
    public bool CanExecute { get; init; } = true;
}
