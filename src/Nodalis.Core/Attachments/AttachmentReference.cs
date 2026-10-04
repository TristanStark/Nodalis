namespace Nodalis.Core.Attachments;

public sealed record AttachmentReference
{
    public required AttachmentStorageMode StorageMode { get; init; }

    public required string DisplayName { get; init; }

    public required string FullPath { get; init; }

    public required string MarkdownTarget { get; init; }

    public bool Exists { get; init; }
}
