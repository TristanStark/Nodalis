namespace Nodalis.Core.Updates;

public enum UpdateTransactionCommand
{
    Apply,
    Rollback
}

public enum UpdateTransactionStage
{
    Prepared,
    ExistingPreviousRemoved,
    CurrentMovedToPrevious,
    CandidateMovedToCurrent,
    CandidateFilesValidated,
    CandidateStartupValidated,
    RollbackCurrentMovedToSwap,
    RollbackPreviousMovedToCurrent,
    RollbackStartupValidated,
    RollbackSwapMovedToPrevious,
    Completed,
    Failed,
    Recovered
}

public sealed record UpdateOperationJournal
{
    public const int CurrentJournalSchemaVersion = 1;

    public int JournalSchemaVersion { get; init; } =
        CurrentJournalSchemaVersion;

    public required Guid OperationId { get; init; }

    public required UpdateTransactionCommand Command { get; init; }

    public required UpdateTransactionStage Stage { get; init; }

    public required string InstallationDirectory { get; init; }

    public string? StagingDirectory { get; init; }

    public required string PreviousDirectory { get; init; }

    public string? SwapDirectory { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset UpdatedUtc { get; init; }

    public required string Message { get; init; }
}
