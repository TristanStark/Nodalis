namespace Nodalis.Core.Importing;

public sealed record DocxImportCommitResult
{
    public required Guid ProjectId { get; init; }

    public required string ProjectDirectory { get; init; }

    public required string SourceCopyPath { get; init; }

    public List<string> GeneratedFiles { get; init; } = [];
}
