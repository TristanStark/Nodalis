namespace Nodalis.Core.Glossary;

public sealed record GlossaryTextMatch
{
    public required int Start { get; init; }

    public required int Length { get; init; }

    public required string MatchedText { get; init; }

    public required GlossaryEntry Entry { get; init; }
}
