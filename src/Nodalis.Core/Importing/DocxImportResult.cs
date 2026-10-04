namespace Nodalis.Core.Importing;

public sealed record DocxImportResult
{
    public required string SourceCopyPath { get; init; }

    public required ParsedDocxDocument Document { get; init; }
}
