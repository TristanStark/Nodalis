namespace Nodalis.Core.Importing;

public sealed record DocxStagedImport
{
    public required string OriginalSourcePath { get; init; }

    public required string StagedCopyPath { get; init; }

    public required ParsedDocxDocument Document { get; init; }
}
