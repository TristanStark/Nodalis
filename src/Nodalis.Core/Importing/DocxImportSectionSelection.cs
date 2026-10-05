namespace Nodalis.Core.Importing;

public sealed record DocxImportSectionSelection
{
    public required int SectionIndex { get; init; }

    public bool Include { get; init; } = true;

    public required string TargetSection { get; init; }

    public List<int>? SelectedBlockIndexes { get; init; }
}
