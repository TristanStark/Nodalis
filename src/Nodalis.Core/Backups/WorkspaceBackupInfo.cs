namespace Nodalis.Core.Backups;

public sealed record WorkspaceBackupInfo
{
    public required string ArchivePath { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required long SizeBytes { get; init; }

    public required BackupValidationStatus Status { get; init; }

    public string? StatusMessage { get; init; }

    public Guid? WorkspaceId { get; init; }

    public string? WorkspaceName { get; init; }

    public string FileName => Path.GetFileName(ArchivePath);

    public string DisplaySize =>
        SizeBytes >= 1024L * 1024L * 1024L
            ? $"{SizeBytes / (1024d * 1024d * 1024d):0.##} Go"
            : SizeBytes >= 1024L * 1024L
                ? $"{SizeBytes / (1024d * 1024d):0.##} Mo"
                : SizeBytes >= 1024L
                    ? $"{SizeBytes / 1024d:0.##} Ko"
                    : $"{SizeBytes} o";
}
