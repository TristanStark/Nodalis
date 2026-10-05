namespace Nodalis.Core.Importing;

public sealed record DocxImportSectionPreview
{
    public required int Index { get; init; }

    public required string SourceHeading { get; init; }

    public required string SuggestedTargetSection { get; init; }

    public required int BlockCount { get; init; }

    public int? HeadingBlockIndex { get; init; }

    public List<DocxImportBlockPreview> Blocks { get; init; } = [];

    public required string MarkdownPreview { get; init; }
}
