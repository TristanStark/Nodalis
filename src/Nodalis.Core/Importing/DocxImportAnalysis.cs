namespace Nodalis.Core.Importing;

public sealed record DocxImportAnalysis
{
    public List<DocxDetectedTarget> ApplicationCandidates { get; init; } = [];

    public List<DocxDetectedTarget> ProjectCandidates { get; init; } = [];

    public string? ProposedApplicationName { get; init; }

    public string? ProposedProjectName { get; init; }

    public List<string> DetectionNotes { get; init; } = [];

    public List<DocxMappedSection> MappedSections { get; init; } = [];

    public List<DocxBlock> UnmappedBlocks { get; init; } = [];

    public bool HasAmbiguousApplication =>
        ApplicationCandidates.Count > 1;

    public bool HasAmbiguousProject =>
        ProjectCandidates.Count > 1;

    public bool RequiresProjectCreation =>
        !string.IsNullOrWhiteSpace(ProposedProjectName) &&
        ProjectCandidates.Count == 0 &&
        ApplicationCandidates.Count == 1;
}
