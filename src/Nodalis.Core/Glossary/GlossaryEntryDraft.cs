namespace Nodalis.Core.Glossary;

public sealed record GlossaryEntryDraft
{
    public required string Term { get; init; }

    public required string Definition { get; init; }

    public List<string> Synonyms { get; init; } = [];

    public List<string> Acronyms { get; init; } = [];

    public List<string> Links { get; init; } = [];
}
