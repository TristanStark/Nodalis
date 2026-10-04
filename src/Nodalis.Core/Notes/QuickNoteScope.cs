namespace Nodalis.Core.Notes;

public sealed record QuickNoteScope
{
    public required QuickNoteScopeKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public required string DirectoryPath { get; init; }

    public required string FilePath { get; init; }
}
