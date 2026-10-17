namespace Nodalis.Core.Inbox;

/// <summary>Existing Nodalis formats available to the Inbox conversion workflow.</summary>
public enum InboxConversionMode
{
    QuickNote,
    NewDocument,
    AppendDocument,
    MarkdownTask
}

/// <summary>One explicit conversion, scoped to a workspace-owned directory.</summary>
public sealed record InboxConversionRequest
{
    public required Guid EntryId { get; init; }

    public required InboxConversionMode Mode { get; init; }

    /// <summary>Workspace-relative Global, Application or Project directory; empty = Global.</summary>
    public required string DestinationRelativeDirectory { get; init; }

    /// <summary>Filename without directories, for a new Markdown document.</summary>
    public string? NewDocumentName { get; init; }

    /// <summary>Workspace-relative existing Markdown file selected by the user.</summary>
    public string? ExistingDocumentRelativePath { get; init; }
}

/// <summary>Conversion result, including safe replay information.</summary>
public sealed record InboxConversionResult
{
    public required string TargetRelativePath { get; init; }

    public required bool AlreadyConverted { get; init; }
}
