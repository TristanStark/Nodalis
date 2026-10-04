namespace Nodalis.Core.Importing;

public sealed record DocxHyperlink
{
    public required string Text { get; init; }

    public required string Target { get; init; }
}
