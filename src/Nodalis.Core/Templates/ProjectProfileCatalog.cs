namespace Nodalis.Core.Templates;

public sealed record ProjectProfileCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<ProjectProfileDefinition> Profiles { get; init; } = [];
}
