namespace Nodalis.Core.Updates;

public sealed record StagedReleasePackage
{
    public required string ArchivePath { get; init; }

    public required string StagedApplicationDirectory { get; init; }

    public required ReleaseManifest Manifest { get; init; }
}
