namespace Nodalis.Core.Migrations;

public enum WorkspaceMigrationRisk
{
    Low,
    Medium,
    High
}

public enum WorkspaceMigrationBackupPolicy
{
    None,
    Recommended,
    Required
}

public sealed record WorkspaceMigrationStepInfo
{
    public required string Id { get; init; }

    public required int FromVersion { get; init; }

    public required int ToVersion { get; init; }

    public required string Description { get; init; }

    public required WorkspaceMigrationRisk Risk { get; init; }

    public required WorkspaceMigrationBackupPolicy BackupPolicy { get; init; }
}

public sealed record WorkspaceMigrationPreflight
{
    public required int DetectedSchemaVersion { get; init; }

    public required int TargetSchemaVersion { get; init; }

    public required bool MigrationRequired { get; init; }

    public required bool CanMigrate { get; init; }

    public required bool BackupRequired { get; init; }

    public required IReadOnlyList<WorkspaceMigrationStepInfo> Steps { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}

public sealed record WorkspaceMigrationChange
{
    public required string StepId { get; init; }

    public required string RelativePath { get; init; }

    public required string Description { get; init; }
}

public sealed record WorkspaceMigrationReport
{
    public required DateTimeOffset StartedUtc { get; init; }

    public required DateTimeOffset CompletedUtc { get; init; }

    public required int InitialSchemaVersion { get; init; }

    public required int FinalSchemaVersion { get; init; }

    public required bool MigrationPerformed { get; init; }

    public required bool Succeeded { get; init; }

    public required IReadOnlyList<WorkspaceMigrationStepInfo> AppliedSteps { get; init; }

    public required IReadOnlyList<WorkspaceMigrationChange> Changes { get; init; }

    public string? BackupArchivePath { get; init; }

    public string? ReportPath { get; init; }

    public string? ErrorMessage { get; init; }
}
