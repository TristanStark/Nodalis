namespace Nodalis.Core.Importing;

public sealed record DocxSectionMappingRule
{
    public required string TargetSection { get; init; }

    public List<string> HeadingAliases { get; init; } = [];

    public int MaximumHeadingLevel { get; init; } = 3;

    public bool AllowPrefixMatch { get; init; } = true;
}
