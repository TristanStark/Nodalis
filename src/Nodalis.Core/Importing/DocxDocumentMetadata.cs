namespace Nodalis.Core.Importing;

public sealed record DocxDocumentMetadata
{
    public string? Title { get; init; }

    public string? Subject { get; init; }

    public string? Creator { get; init; }

    public string? Description { get; init; }

    public string? Keywords { get; init; }

    public string? Category { get; init; }

    public DateTimeOffset? CreatedUtc { get; init; }

    public DateTimeOffset? ModifiedUtc { get; init; }
}
