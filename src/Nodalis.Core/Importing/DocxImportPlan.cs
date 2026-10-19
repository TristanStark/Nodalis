namespace Nodalis.Core.Importing;

public sealed record DocxImportPlan
{
    public required string TargetProjectDisplayName { get; init; }

    public required string TargetProjectRelativePath { get; init; }

    public bool CreatesProject { get; init; }

    public List<DocxImportPlannedChange> Changes { get; init; } = [];

    public List<string> Warnings { get; init; } = [];

    /// <summary>SHA256 of the projected section Markdown used to detect source changes.</summary>
    public string ProjectedContentSha256 { get; init; } = string.Empty;

    /// <summary>Deterministic fingerprint of the exact dry-run, displayed only after computing it.</summary>
    public string Fingerprint { get; init; } = string.Empty;
}
