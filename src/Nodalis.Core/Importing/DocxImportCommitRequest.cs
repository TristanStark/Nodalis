using Nodalis.Core.Domain;

namespace Nodalis.Core.Importing;

public sealed record DocxImportCommitRequest
{
    public required Guid ApplicationId { get; init; }

    public Guid? ProjectId { get; init; }

    public string? NewProjectName { get; init; }

    public ProjectComplexity NewProjectComplexity { get; init; } =
        ProjectComplexity.Medium;

    public List<DocxImportSectionSelection> Sections { get; init; } = [];

    /// <summary>Frozen profile actually applied to this preview, if any.</summary>
    public DocxMappingProfile? AppliedMappingProfile { get; init; }

    /// <summary>Optional fingerprint of the user's last explicitly approved dry-run.</summary>
    public string? ExpectedPlanFingerprint { get; init; }
}
