using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Classifies a potential three-way DOCX reimport without writing any file.
/// An entire destination Markdown hash is compared to the previous import's
/// destination hash: when multiple Word fragments shared one file, a local
/// edit conservatively marks all fragments as modified rather than risking
/// an undetected user change.
/// </summary>
public static class DocxReimportDiffEngine
{
    /// <summary>
    /// Compares the saved base fragment with a newly rendered Word fragment
    /// and the current complete Nodalis Markdown destination.
    /// Null means that the Word section or local destination is missing.
    /// A result is advisory only, never permission to overwrite a file.
    /// </summary>
    public static DocxReimportChange Compare(
        DocxImportManifestFragment baseline,
        string? updatedWordMarkdown,
        string? currentDestinationMarkdown,
        string? updatedSourceHeading = null,
        int? updatedHeadingBlockIndex = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        bool sourceMissing = updatedWordMarkdown is null;
        bool destinationMissing = currentDestinationMarkdown is null;
        // A user may retain Nodalis or manually merge Word changes.
        // In either case the accepted Markdown snapshot is NOT necessarily
        // what Word contained. Compare Word to the separately recorded Word
        // snapshot rather than reporting a fictitious source change forever.
        string expectedWordSha = baseline.LatestWordSnapshotSha256 ??
            baseline.FragmentSha256;
        bool wordChanged = sourceMissing ||
            !string.Equals(
                DocxImportManifestStore.ComputeSha256(updatedWordMarkdown!),
                expectedWordSha,
                StringComparison.OrdinalIgnoreCase);
        bool nodalisChanged = destinationMissing ||
            !string.Equals(
                DocxImportManifestStore.ComputeSha256(currentDestinationMarkdown!),
                baseline.DestinationSha256,
                StringComparison.OrdinalIgnoreCase);

        bool headingRenamed = updatedSourceHeading is not null &&
            !string.Equals(updatedSourceHeading.Trim(), baseline.SourceHeading.Trim(),
                StringComparison.Ordinal);
        bool headingMoved = updatedHeadingBlockIndex.HasValue &&
            updatedHeadingBlockIndex != baseline.HeadingBlockIndex;

        DocxReimportChangeKind kind =
            destinationMissing ? DocxReimportChangeKind.DestinationMissing :
            sourceMissing ? DocxReimportChangeKind.WordDeleted :
            wordChanged && nodalisChanged ? DocxReimportChangeKind.BothChanged :
            wordChanged ? DocxReimportChangeKind.WordChanged :
            nodalisChanged ? DocxReimportChangeKind.NodalisChanged :
            headingRenamed || headingMoved ? DocxReimportChangeKind.SourceMovedOrRenamed :
            DocxReimportChangeKind.Unchanged;

        return new DocxReimportChange
        {
            FragmentId = baseline.FragmentId,
            DestinationRelativePath = baseline.DestinationRelativePath,
            Kind = kind,
            WordChanged = wordChanged,
            NodalisChanged = nodalisChanged,
            SourceStructureChanged = headingRenamed || headingMoved,
            RequiresExplicitResolution =
                kind is DocxReimportChangeKind.BothChanged or
                    DocxReimportChangeKind.WordDeleted or
                    DocxReimportChangeKind.DestinationMissing or
                    DocxReimportChangeKind.SourceMovedOrRenamed ||
                ((headingRenamed || headingMoved) && wordChanged)
        };
    }
}

/// <summary>The conservative classification of one Word-to-Nodalis fragment.</summary>
public enum DocxReimportChangeKind
{
    Unchanged,
    WordChanged,
    NodalisChanged,
    BothChanged,
    WordDeleted,
    DestinationMissing,
    SourceMovedOrRenamed
}

/// <summary>
/// An inspectable difference only: no action is applied without an explicit
/// user-approved reimport plan and resolution of every conflict.
/// </summary>
public sealed record DocxReimportChange
{
    public required Guid FragmentId { get; init; }

    public required string DestinationRelativePath { get; init; }

    public required DocxReimportChangeKind Kind { get; init; }

    public bool WordChanged { get; init; }

    public bool NodalisChanged { get; init; }

    public bool SourceStructureChanged { get; init; }

    public bool RequiresExplicitResolution { get; init; }
}
