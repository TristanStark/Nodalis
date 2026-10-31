namespace Nodalis.Core.Importing;

public sealed record DocxStagedImport
{
    public required string OriginalSourcePath { get; init; }

    public required string StagedCopyPath { get; init; }

    /// <summary>SHA-256 of the exact staged bytes used to build the Word preview.</summary>
    public string StagedCopySha256 { get; init; } = string.Empty;

    public required ParsedDocxDocument Document { get; init; }
}
