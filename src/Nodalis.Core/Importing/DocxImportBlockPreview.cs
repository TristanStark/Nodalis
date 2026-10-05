namespace Nodalis.Core.Importing;

public sealed record DocxImportBlockPreview
{
    public required int BlockIndex { get; init; }

    public required DocxBlock Block { get; init; }

    public required string KindLabel { get; init; }

    public required string DisplayText { get; init; }

    public required string MarkdownPreview { get; init; }

    public int? ParentBlockIndex { get; init; }

    public bool IsSectionHeading { get; init; }
}
