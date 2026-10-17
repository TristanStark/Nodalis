namespace Nodalis.Core.Importing;

/// <summary>
/// Identifies the kind of file discovered during a bulk Markdown import.
/// </summary>
public enum MarkdownBulkImportItemKind
{
    Markdown,
    Attachment
}

/// <summary>
/// Describes one application or project that can receive a bulk Markdown import.
/// </summary>
public sealed record MarkdownBulkImportTargetOption
{
    public required Guid Id { get; init; }

    public required string DisplayName { get; init; }

    public required string QualifiedName { get; init; }

    public required string RelativePath { get; init; }

    public required Guid ApplicationId { get; init; }
}

/// <summary>
/// Describes one source file that will be copied by a bulk Markdown import.
/// </summary>
public sealed record MarkdownBulkImportSourceItem
{
    public required string SourcePath { get; init; }

    public required string RelativePath { get; init; }

    public required MarkdownBulkImportItemKind Kind { get; init; }

    public required long Length { get; init; }

    public required string Sha256 { get; init; }

    public string KindLabel =>
        Kind == MarkdownBulkImportItemKind.Markdown
            ? "Markdown"
            : "Pièce jointe";

    public string SizeLabel =>
        Length < 1024
            ? Length + " o"
            : Length < 1024 * 1024
                ? Math.Round(
                        Length / 1024d,
                        1)
                    .ToString(
                        System.Globalization.CultureInfo.CurrentCulture) +
                  " Ko"
                : Math.Round(
                        Length / 1024d / 1024d,
                        1)
                    .ToString(
                        System.Globalization.CultureInfo.CurrentCulture) +
                  " Mo";
}

/// <summary>
/// Contains the read-only analysis shown before a bulk Markdown import.
/// </summary>
public sealed record MarkdownBulkImportPreview
{
    public required string SourceRoot { get; init; }

    public required string SourceDisplayName { get; init; }

    public List<MarkdownBulkImportSourceItem> Items { get; init; } = [];

    public List<string> Warnings { get; init; } = [];

    public List<MarkdownBulkImportTargetOption> Applications { get; init; } = [];

    public List<MarkdownBulkImportTargetOption> Projects { get; init; } = [];

    public Guid? SuggestedApplicationId { get; init; }

    public Guid? SuggestedProjectId { get; init; }

    public string SuggestedSection { get; init; } = "Technique";

    public int RelativeLinkCount { get; init; }

    public int MarkdownCount =>
        Items.Count(item =>
            item.Kind == MarkdownBulkImportItemKind.Markdown);

    public int AttachmentCount =>
        Items.Count(item =>
            item.Kind == MarkdownBulkImportItemKind.Attachment);
}

/// <summary>
/// Describes the target selected by the user for a bulk Markdown import.
/// </summary>
public sealed record MarkdownBulkImportRequest
{
    public required Guid ApplicationId { get; init; }

    public required Guid ProjectId { get; init; }

    public required string TargetSection { get; init; }
}

/// <summary>
/// Describes one filesystem operation that will occur if a bulk Markdown import is validated.
/// </summary>
public sealed record MarkdownBulkImportPlannedChange
{
    public required string Operation { get; init; }

    public required string SourceRelativePath { get; init; }

    public required string DestinationRelativePath { get; init; }

    public required string Detail { get; init; }
}

/// <summary>
/// Contains the complete write plan shown before a bulk Markdown import is committed.
/// </summary>
public sealed record MarkdownBulkImportPlan
{
    public required string TargetProjectDisplayName { get; init; }

    public required string TargetProjectRelativePath { get; init; }

    public required string ImportDirectoryRelativePath { get; init; }

    public required bool CreatesSection { get; init; }

    public List<MarkdownBulkImportPlannedChange> Changes { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Contains the filesystem result of a completed bulk Markdown import.
/// </summary>
public sealed record MarkdownBulkImportCommitResult
{
    public required string ProjectDirectory { get; init; }

    public required string ImportDirectory { get; init; }

    public List<string> ImportedFiles { get; init; } = [];
}
