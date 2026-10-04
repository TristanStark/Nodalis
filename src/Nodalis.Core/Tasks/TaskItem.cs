namespace Nodalis.Core.Tasks;

public sealed record TaskItem
{
    public required Guid Id { get; init; }

    public required Guid SourceDocumentId { get; init; }

    public required string SourceRelativePath { get; init; }

    public required int LineNumber { get; init; }

    public required string RawLine { get; init; }

    public required string Text { get; init; }

    public bool IsCompleted { get; init; }

    public string? Owner { get; init; }

    public DateOnly? DueDate { get; init; }

    public Guid? ApplicationId { get; init; }

    public string? ApplicationName { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }
}
