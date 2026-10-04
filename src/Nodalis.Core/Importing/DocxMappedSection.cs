namespace Nodalis.Core.Importing;

public sealed record DocxMappedSection
{
    public required string TargetSection { get; init; }

    public required string SourceHeading { get; init; }

    public required int HeadingBlockIndex { get; init; }

    public List<DocxBlock> Blocks { get; init; } = [];
}
