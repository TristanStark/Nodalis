using System.Text.Json.Serialization;

namespace Nodalis.Core.Inbox;

/// <summary>Types of content intentionally captured by the user.</summary>
public enum InboxCaptureKind
{
    Text,
    Link,
    Image,
    File
}

/// <summary>Lifecycle of a captured item, independent from quick notes.</summary>
public enum InboxItemState
{
    Pending,
    Processed,
    Archived
}

/// <summary>One file explicitly copied into a captured item.</summary>
public sealed record InboxAttachment
{
    public required string OriginalFileName { get; init; }

    public required string RelativePath { get; init; }

    public required long SizeBytes { get; init; }
}

/// <summary>Persistent, versioned metadata for a Markdown Inbox capture.</summary>
public sealed record InboxEntry
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required InboxCaptureKind Kind { get; init; }

    public InboxItemState State { get; init; } = InboxItemState.Pending;

    public string? Origin { get; init; }

    /// <summary>Optional SHA-256 kind/text/attachment digest, absent in legacy captures.</summary>
    public string? FingerprintSha256 { get; init; }

    public string? ConvertedTargetRelativePath { get; init; }

    public IReadOnlyList<InboxAttachment> Attachments { get; init; } = [];

    [JsonIgnore]
    public string ContentMarkdown { get; init; } = string.Empty;
}

/// <summary>Information supplied during an explicit capture operation.</summary>
public sealed record InboxCaptureRequest
{
    public required InboxCaptureKind Kind { get; init; }

    public required string ContentMarkdown { get; init; }

    public string? Origin { get; init; }

    public IReadOnlyList<string> AttachmentSourcePaths { get; init; } = [];
}

/// <summary>A recoverable storage warning suitable for the UI and diagnostics.</summary>
public sealed record InboxWarning
{
    public required string Item { get; init; }

    public required string Message { get; init; }
}

/// <summary>Inbox contents and non-silent recovery diagnostics.</summary>
public sealed record InboxReadResult
{
    public required IReadOnlyList<InboxEntry> Entries { get; init; }

    public required IReadOnlyList<InboxWarning> Warnings { get; init; }
}


/// <summary>Probable prior capture for voluntary duplicate confirmation.</summary>
public sealed record InboxDuplicateCandidate
{
    public required Guid Id { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public required InboxItemState State { get; init; }

    public required bool InTrash { get; init; }
}
