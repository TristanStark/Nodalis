namespace Nodalis.Core.Importing;

public sealed record DocxImportTargetOption
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required string QualifiedName { get; init; }

    public required string RelativePath { get; init; }

    public Guid? ApplicationId { get; init; }

    /// <summary>Named sections already present in this project.</summary>
    public List<string> ExistingSections { get; init; } = [];
}
