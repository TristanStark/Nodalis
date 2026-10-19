namespace Nodalis.Core.Importing;

/// <summary>
/// A portable collection of explicitly selected DOCX mapping profiles.
/// No rule is executed automatically when a workspace is opened.
/// </summary>
public sealed record DocxMappingProfileCatalog
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<DocxMappingProfile> Profiles { get; init; } = [];
}

/// <summary>
/// An ordered, human-editable profile independent of source document paths.
/// </summary>
public sealed record DocxMappingProfile
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public List<DocxMappingProfileRule> Rules { get; init; } = [];

    public List<DocxMappingManualOverride> ManualOverrides { get; init; } = [];
}

/// <summary>
/// A deterministic action. NewSection means suggest a new target, never create it during evaluation.
/// </summary>
public enum DocxMappingAction
{
    Map,
    Ignore,
    NewSection
}

/// <summary>
/// AND-combined predicates, evaluated in list order with a fixed regex timeout.
/// A completely empty predicate is invalid.
/// </summary>
public sealed record DocxMappingProfileRule
{
    public required Guid Id { get; init; }

    public bool Enabled { get; init; } = true;

    public string? HeadingText { get; init; }

    public string? WordStyleId { get; init; }

    public int? HeadingLevel { get; init; }

    public string? ParentPathContains { get; init; }

    public string? TextPattern { get; init; }

    public required DocxMappingAction Action { get; init; }

    public string? TargetSection { get; init; }
}

/// <summary>
/// An explicitly saved decision identified by source heading and parent context.
/// </summary>
public sealed record DocxMappingManualOverride
{
    public required string HeadingText { get; init; }

    public string ParentPath { get; init; } = string.Empty;

    public required DocxMappingAction Action { get; init; }

    public string? TargetSection { get; init; }
}

/// <summary>
/// Heading metadata extracted from the parsed DOCX, without workspace writes.
/// </summary>
public sealed record DocxMappingSourceSection
{
    public required string Heading { get; init; }

    public string? WordStyleId { get; init; }

    public int? HeadingLevel { get; init; }

    public string ParentPath { get; init; } = string.Empty;
}

/// <summary>
/// Explainable proposal. Conflicts have no action until the user resolves them.
/// </summary>
public sealed record DocxMappingDecision
{
    public bool Matched { get; init; }

    public bool IsManualOverride { get; init; }

    public bool HasConflict { get; init; }

    public DocxMappingAction? Action { get; init; }

    public string? TargetSection { get; init; }

    public List<Guid> MatchingRuleIds { get; init; } = [];

    public List<string> Reasons { get; init; } = [];
}
