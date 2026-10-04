namespace Nodalis.Core.Importing;

public sealed record DocxImportRuleCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DocxEntityDetectionRules Detection { get; init; } = new();

    public List<DocxSectionMappingRule> SectionMappings { get; init; } = [];
}

public sealed record DocxEntityDetectionRules
{
    public List<string> ApplicationLabels { get; init; } =
        ["Application", "Application cible", "Appli"];

    public List<string> ProjectLabels { get; init; } =
        ["Projet", "Projet cible", "Project"];

    public int MaxContentParagraphs { get; init; } = 80;
}
