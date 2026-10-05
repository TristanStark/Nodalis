namespace Nodalis.Core.Glossary;

public sealed record GlossaryScope
{
    public required GlossaryScopeKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string DirectoryPath { get; init; }

    public required string FilePath { get; init; }
}
