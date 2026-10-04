namespace Nodalis.Core.Glossary;

public sealed record GlossaryEntry
{
    public required string Term { get; init; }

    public required string Definition { get; init; }

    public List<string> Synonyms { get; init; } = [];

    public List<string> Acronyms { get; init; } = [];

    public List<string> Links { get; init; } = [];

    public required GlossaryScope Scope { get; init; }

    public int LineNumber { get; init; }
}
