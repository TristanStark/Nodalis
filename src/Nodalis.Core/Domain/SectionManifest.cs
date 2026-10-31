namespace Nodalis.Core.Domain;

public sealed record SectionManifest
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public int Order { get; init; }

    public bool IsSingleton { get; init; }

    public string? TemplateKey { get; init; }

    /// <summary>Optional explicit project-relative section directory; null uses legacy name-based layout.</summary>
    public string? RelativeDirectory { get; init; }
}
