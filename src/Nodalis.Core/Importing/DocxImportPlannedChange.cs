namespace Nodalis.Core.Importing;

public sealed record DocxImportPlannedChange
{
    public required string Action { get; init; }

    public required string RelativePath { get; init; }

    public required string Description { get; init; }
}
