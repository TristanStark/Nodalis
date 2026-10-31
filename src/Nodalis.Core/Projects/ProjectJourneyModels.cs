using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

/// <summary>Persisted lifecycle of one project journey step, distinct from task status.</summary>
public enum ProjectJourneyStepStatus
{
    Todo,
    InProgress,
    Completed,
    NotApplicable
}

/// <summary>Immutable built-in defaults with optional workspace overrides.</summary>
public sealed record ProjectJourneyTemplateCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<ProjectJourneyTemplate> Templates { get; init; } = [];
}

/// <summary>One journey template bound to an initial project complexity.</summary>
public sealed record ProjectJourneyTemplate
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public required ProjectComplexity Complexity { get; init; }

    public List<ProjectJourneyStepTemplate> Steps { get; init; } = [];
}

/// <summary>Named stage in a template, with stable logical key and display order.</summary>
public sealed record ProjectJourneyStepTemplate
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    public int Order { get; init; }

    public List<ProjectJourneyOperationTemplate> Operations { get; init; } = [];
}

/// <summary>Non-executing operation proposed by a stage template.</summary>
public sealed record ProjectJourneyOperationTemplate
{
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>Optional named prompt for later preparation, never executed automatically.</summary>
    public string? PromptKey { get; init; }
}

/// <summary>Portable project-local journey file, independent of the document tree.</summary>
public sealed record ProjectJourneyDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ProjectId { get; init; }

    /// <summary>Original template key is provenance, not an active link to its defaults.</summary>
    public required string TemplateKey { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<ProjectJourneyStep> Steps { get; init; } = [];
}

/// <summary>Stable editable stage instance; reordering never changes its GUID.</summary>
public sealed record ProjectJourneyStep
{
    public required Guid Id { get; init; }

    public string? TemplateKey { get; init; }

    public required string Name { get; init; }

    public int Order { get; init; }

    public ProjectJourneyStepStatus Status { get; init; } = ProjectJourneyStepStatus.Todo;

    public List<ProjectJourneyOperation> Operations { get; init; } = [];
}

/// <summary>Stable operation instance with non-owning references to project evidence.</summary>
public sealed record ProjectJourneyOperation
{
    public required Guid Id { get; init; }

    public string? TemplateKey { get; init; }

    public required string Name { get; init; }

    public string? PromptKey { get; init; }

    /// <summary>Workspace-relative source references; no source file is created here.</summary>
    public List<string> SourcePaths { get; init; } = [];

    /// <summary>Related gate identifiers, never implicitly approved by this model.</summary>
    public List<Guid> GateIds { get; init; } = [];

    /// <summary>Referenced milestone identifiers, not duplicate milestone entities.</summary>
    public List<Guid> MilestoneIds { get; init; } = [];
}

/// <summary>Read-only snapshot with a byte-level revision for optimistic writes.</summary>
public sealed record ProjectJourneySnapshot(
    ProjectJourneyDocument Document,
    string Sha256);


/// <summary>Workspace template catalog with its optional on-disk revision.</summary>
/// <param name="Catalog">Deterministic built-ins or an explicit workspace override.</param>
/// <param name="Sha256">Exact file hash, null when no override exists.</param>
public sealed record ProjectJourneyTemplateSnapshot(
    ProjectJourneyTemplateCatalog Catalog,
    string? Sha256);
