namespace Nodalis.Core.Domain;

public sealed record ProjectManifest
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required Guid ApplicationId { get; init; }

    public Guid? ModuleId { get; init; }

    public Guid? ParentProjectId { get; init; }

    public required ProjectComplexity InitialComplexity { get; init; }

    public List<SectionManifest> Sections { get; init; } = [];

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC closure timestamp, or null for an active project.</summary>
    public DateTimeOffset? ArchivedUtc { get; init; }

    /// <summary>Original workspace-relative path used for a safe, reversible reopening.</summary>
    public string? ArchiveOriginalRelativePath { get; init; }
}
