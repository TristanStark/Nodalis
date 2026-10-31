namespace Nodalis.Core.Importing;

public sealed record DocxImportPreview
{
    /// <summary>Stable identifier shared by the preview, plan and final manifest.</summary>
    public Guid ImportId { get; init; } = Guid.NewGuid();

    public required DocxStagedImport StagedImport { get; init; }

    public required DocxImportAnalysis Analysis { get; init; }

    public List<DocxImportTargetOption> Applications { get; init; } = [];

    public List<DocxImportTargetOption> Projects { get; init; } = [];

    public Guid? SuggestedApplicationId { get; init; }

    public Guid? SuggestedProjectId { get; init; }

    public string? SuggestedNewProjectName { get; init; }

    public List<DocxImportSectionPreview> Sections { get; init; } = [];

    public List<string> Conflicts { get; init; } = [];
}
