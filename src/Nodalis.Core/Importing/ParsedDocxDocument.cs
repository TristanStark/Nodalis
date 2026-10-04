namespace Nodalis.Core.Importing;

public sealed record ParsedDocxDocument
{
    public DocxDocumentMetadata Metadata { get; init; } = new();

    public List<DocxBlock> Blocks { get; init; } = [];

    public List<DocxRelationship> Relationships { get; init; } = [];
}
