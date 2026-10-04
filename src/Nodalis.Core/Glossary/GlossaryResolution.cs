namespace Nodalis.Core.Glossary;

public sealed record GlossaryResolution
{
    public required string Query { get; init; }

    public GlossaryEntry? Primary { get; init; }

    public List<GlossaryEntry> Alternatives { get; init; } = [];

    public bool Found => Primary is not null;
}
