namespace Nodalis.Core.Updates;

public sealed record ReleaseFileEntry
{
    public required string Path { get; init; }

    public required string Sha256 { get; init; }

    public long Length { get; init; }
}
