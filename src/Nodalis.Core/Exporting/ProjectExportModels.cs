namespace Nodalis.Core.Exporting;

/// <summary>
/// Identifies one project export representation.
/// </summary>
public enum ProjectExportFormat
{
    PortableMarkdown,
    StandaloneHtml,
    NativeDocx
}

/// <summary>
/// Describes one project section that can be included in an export.
/// </summary>
public sealed record ProjectExportSectionOption
{
    public required Guid SectionId { get; init; }

    public required string Name { get; init; }

    public required int SourceOrder { get; init; }

    public required int DocumentCount { get; init; }

    public required string RelativeDirectory { get; init; }
}

/// <summary>
/// Contains the read-only project analysis shown before export.
/// </summary>
public sealed record ProjectExportPreview
{
    public required Guid ProjectId { get; init; }

    public required string ProjectName { get; init; }

    public required string ProjectDirectory { get; init; }

    public required string ProjectRelativePath { get; init; }

    public List<ProjectExportSectionOption> Sections { get; init; } = [];

    public int MarkdownDocumentCount { get; init; }

    public int AttachmentCount { get; init; }

    public string SuggestedBaseName { get; init; } = "Export";
}

/// <summary>
/// Describes one selected section and its desired position in the consolidated export.
/// </summary>
public sealed record ProjectExportSectionSelection
{
    public required Guid SectionId { get; init; }

    public required int Order { get; init; }
}

/// <summary>
/// Describes an explicit, user-validated project export request.
/// </summary>
public sealed record ProjectExportRequest
{
    public required string DestinationDirectory { get; init; }

    public required string BaseName { get; init; }

    public required bool IncludeAttachments { get; init; }

    public List<ProjectExportFormat> Formats { get; init; } = [];

    public List<ProjectExportSectionSelection> Sections { get; init; } = [];
}

/// <summary>
/// Contains the paths produced by one completed project export.
/// </summary>
public sealed record ProjectExportResult
{
    public List<string> OutputPaths { get; init; } = [];

    public int ExportedDocumentCount { get; init; }

    public int ExportedAttachmentCount { get; init; }
}
