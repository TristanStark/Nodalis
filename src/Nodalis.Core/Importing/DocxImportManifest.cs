namespace Nodalis.Core.Importing;

/// <summary>
/// Portable provenance for a completed DOCX import. Paths are relative to the
/// workspace so that a moved workspace retains its history. The base snapshots
/// belong to the workspace and are never sent to a remote service.
/// </summary>
public sealed record DocxImportManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid ImportId { get; init; }

    public required Guid ProjectId { get; init; }

    /// <summary>Previous immutable DOCX history, if this is a reimport.</summary>
    public Guid? PreviousImportId { get; init; }

    /// <summary>Durable reimport operation ID for idempotent lineage.</summary>
    public Guid? ReimportOperationId { get; init; }

    public required DateTimeOffset ImportedAtUtc { get; init; }

    /// <summary>The leaf name only; the original external absolute path is not stored.</summary>
    public required string SourceFileName { get; init; }

    /// <summary>SHA-256 of the exact imported DOCX bytes, independent of its file name.</summary>
    public required string SourceSha256 { get; init; }

    /// <summary>Workspace-relative location of the preserved DOCX copy.</summary>
    public required string SourceCopyRelativePath { get; init; }

    /// <summary>Optional fingerprint of the user's approved preview plan.</summary>
    public string? ApprovedPlanFingerprint { get; init; }

    /// <summary>Snapshot of the applied mapping profile and all its original rules.</summary>
    public DocxMappingProfile? AppliedMappingProfile { get; init; }

    /// <summary>Section indexes explicitly omitted from the import.</summary>
    public List<int> ExcludedSectionIndexes { get; init; } = [];

    /// <summary>One entry per imported Word section, including merged destinations.</summary>
    public List<DocxImportManifestFragment> Fragments { get; init; } = [];
}

/// <summary>
/// Immutable reference snapshot used as the base for a later three-way reimport.
/// Two identically titled Word sections remain separate by their source indexes,
/// heading block positions, individual fragment identifiers and content hashes.
/// </summary>
public sealed record DocxImportManifestFragment
{
    public required Guid FragmentId { get; init; }

    public required int SourceSectionIndex { get; init; }

    public int? HeadingBlockIndex { get; init; }

    public required string SourceHeading { get; init; }

    /// <summary>Word heading hierarchy at import; null for older v1 provenance records.</summary>
    public string? SourceParentPath { get; init; }

    /// <summary>Original Word heading style; null when unavailable.</summary>
    public string? SourceWordStyleId { get; init; }

    /// <summary>Original Word heading level; null when unavailable.</summary>
    public int? SourceHeadingLevel { get; init; }

    public required string TargetSection { get; init; }

    /// <summary>Canonical Nodalis section GUID, independent of subsequent renames.</summary>
    public Guid? TargetSectionId { get; init; }

    /// <summary>The exact Markdown file written to Nodalis, workspace-relative.</summary>
    public required string DestinationRelativePath { get; init; }

    /// <summary>Original source block indexes selected in this fragment.</summary>
    public List<int> SelectedBlockIndexes { get; init; } = [];

    /// <summary>
    /// Total block count in the source section at original import. Null in old
    /// provenance; enables detecting partial imports before a reimport.
    /// </summary>
    public int? OriginalSourceBlockCount { get; init; }

    /// <summary>
    /// The Word text observed at reimport, even when the user retained Nodalis
    /// or supplied a manual merge. Missing for historical v1 imports.
    /// </summary>
    public string? LatestWordMarkdownSnapshot { get; init; }

    /// <summary>
    /// SHA-256 of the latest observed Word snapshot. Nullable for legacy
    /// manifests created before versioned reimport provenance existed.
    /// </summary>
    public string? LatestWordSnapshotSha256 { get; init; }

    /// <summary>
    /// Explicit action used to construct the new baseline: Word, Manual or
    /// Nodalis. Missing for original imports.
    /// </summary>
    public string? ReimportResolution { get; init; }

    /// <summary>SHA-256 of this fragment's Markdown snapshot.</summary>
    public required string FragmentSha256 { get; init; }

    /// <summary>Markdown at import time; never automatically overwritten on reimport.</summary>
    public required string BaseMarkdown { get; init; }

    /// <summary>SHA-256 of the complete destination file just after import.</summary>
    public required string DestinationSha256 { get; init; }
}

/// <summary>
/// Candidate association for a future import. Matching by file name alone is
/// informational and must never authorize automatic replacement of user data.
/// </summary>
public sealed record DocxImportManifestMatch
{
    public required DocxImportManifest Manifest { get; init; }

    public required bool ExactSourceHash { get; init; }

    public required bool SameSourceFileName { get; init; }
}
