namespace Nodalis.Core.Importing;

public sealed record DocxBlock
{
    public required DocxBlockKind Kind { get; init; }

    public DocxParagraph? Paragraph { get; init; }

    public DocxTable? Table { get; init; }
}
