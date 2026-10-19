using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Associates previously imported DOCX fragments with sections in a newer
/// Word preview. Strong signals are considered before heading or position.
/// Each automatic association must be one-to-one in the complete inputs:
/// duplicate headings or duplicated text never win by list order.
/// No workspace data is read or written by this matcher.
/// </summary>
public static class DocxReimportSectionMatcher
{
    /// <summary>
    /// Matches every old fragment and identifies new, unresolved sections.
    /// This report is only a proposal; an import writer must explicitly
    /// verify the user's decisions and never use positions as sole proof.
    /// </summary>
    public static DocxReimportMatchReport Match(
        DocxImportManifest previous,
        DocxImportPreview latest)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(latest);

        List<NewSection> current = latest.Sections
            .Select(section =>
            {
                DocxMappingSourceSection source = DocxMappingSourceBuilder.Build(
                    section, latest.StagedImport.Document);
                string markdown = DocxImportSelectionRenderer.RenderAll(section).TrimEnd();
                return new NewSection(section.Index, section.SourceHeading,
                    section.HeadingBlockIndex, source.ParentPath,
                    DocxMappingProfileEngine.Normalize(section.SourceHeading),
                    DocxMappingProfileEngine.Normalize(source.ParentPath),
                    NormalizeBody(markdown), markdown);
            })
            .ToList();

        List<OldSection> original = previous.Fragments
            .Select(fragment => new OldSection(fragment,
                DocxMappingProfileEngine.Normalize(fragment.SourceHeading),
                fragment.SourceParentPath is null ? null :
                    DocxMappingProfileEngine.Normalize(fragment.SourceParentPath),
                NormalizeBody(fragment.BaseMarkdown)))
            .ToList();

        Dictionary<Guid, (int Index, DocxReimportAssociationKind Kind)> confirmed = [];
        HashSet<int> allocated = [];

        // Strict one-to-one body signatures across ALL sections, not merely
        // among those still unallocated, avoid opportunistic collision matching.
        foreach (OldSection old in original)
        {
            if (old.Body.Length == 0)
            {
                continue;
            }

            NewSection[] matches = current.Where(section =>
                string.Equals(section.Body, old.Body, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1 &&
                original.Count(section => section.Body.Length > 0 &&
                    string.Equals(section.Body, old.Body, StringComparison.Ordinal)) == 1 &&
                allocated.Add(matches[0].Index))
            {
                confirmed.Add(old.Fragment.FragmentId,
                    (matches[0].Index, DocxReimportAssociationKind.ExactBody));
            }
        }

        // A heading with known ancestry can survive body changes. Ancestry
        // must be unique globally; old manifests without ancestry skip this.
        foreach (OldSection old in original.Where(section =>
                     !confirmed.ContainsKey(section.Fragment.FragmentId) &&
                     section.Parent is not null))
        {
            NewSection[] matches = current.Where(section =>
                section.Heading == old.Heading && section.Parent == old.Parent).ToArray();
            if (matches.Length == 1 &&
                original.Count(section => section.Parent is not null &&
                    section.Heading == old.Heading && section.Parent == old.Parent) == 1 &&
                allocated.Add(matches[0].Index))
            {
                confirmed.Add(old.Fragment.FragmentId,
                    (matches[0].Index, DocxReimportAssociationKind.HeadingAndAncestry));
            }
        }

        // Fallback for old records without a parent path and unique headings
        // (the pre-v1.9 context-less manifest remains readable).
        foreach (OldSection old in original.Where(section =>
                     !confirmed.ContainsKey(section.Fragment.FragmentId) &&
                     section.Heading.Length > 0))
        {
            NewSection[] matches = current.Where(section =>
                section.Heading == old.Heading).ToArray();
            if (matches.Length == 1 &&
                original.Count(section => section.Heading == old.Heading) == 1 &&
                allocated.Add(matches[0].Index))
            {
                confirmed.Add(old.Fragment.FragmentId,
                    (matches[0].Index, DocxReimportAssociationKind.UniqueHeading));
            }
        }

        List<DocxReimportSectionAssociation> results = [];
        HashSet<int> unresolved = [];
        foreach (OldSection old in original)
        {
            if (confirmed.TryGetValue(old.Fragment.FragmentId, out
                    (int Index, DocxReimportAssociationKind Kind) match))
            {
                NewSection fresh = current.Single(section => section.Index == match.Index);
                bool structureChanged = fresh.HeadingBlockIndex != old.Fragment.HeadingBlockIndex ||
                    fresh.Heading != old.Heading ||
                    (old.Parent is not null && fresh.Parent != old.Parent);
                results.Add(new DocxReimportSectionAssociation
                {
                    FragmentId = old.Fragment.FragmentId,
                    PreviousHeading = old.Fragment.SourceHeading,
                    PreviousSectionIndex = old.Fragment.SourceSectionIndex,
                    CurrentSectionIndex = match.Index,
                    Kind = match.Kind,
                    RequiresConfirmation = structureChanged ||
                        match.Kind == DocxReimportAssociationKind.UniqueHeading,
                    CandidateSectionIndexes = [match.Index]
                });
                continue;
            }

            // Positions provide a suggested candidate for manual inspection
            // only. They must never produce an automatic association.
            int[] candidates = current
                .Where(section => !allocated.Contains(section.Index) &&
                    (section.Heading == old.Heading && old.Heading.Length > 0 ||
                     section.Body == old.Body && old.Body.Length > 0 ||
                     (old.Fragment.HeadingBlockIndex is not null &&
                      section.HeadingBlockIndex == old.Fragment.HeadingBlockIndex)))
                .Select(section => section.Index)
                .OrderBy(index => index)
                .ToArray();

            foreach (int candidate in candidates)
            {
                unresolved.Add(candidate);
            }

            results.Add(new DocxReimportSectionAssociation
            {
                FragmentId = old.Fragment.FragmentId,
                PreviousHeading = old.Fragment.SourceHeading,
                PreviousSectionIndex = old.Fragment.SourceSectionIndex,
                Kind = candidates.Length > 0
                    ? DocxReimportAssociationKind.Ambiguous
                    : DocxReimportAssociationKind.MissingFromWord,
                RequiresConfirmation = true,
                CandidateSectionIndexes = candidates.ToList()
            });
        }

        return new DocxReimportMatchReport
        {
            Associations = results,
            AddedSectionIndexes = current
                .Where(section => !allocated.Contains(section.Index) &&
                    !unresolved.Contains(section.Index))
                .Select(section => section.Index)
                .ToList(),
            UnresolvedNewSectionIndexes = unresolved.OrderBy(index => index).ToList()
        };
    }

    /// <summary>
    /// Returns content without the first generated Markdown heading, retaining
    /// line endings and other Markdown bytes (not semantic-only similarity).
    /// Empty headings and empty bodies cannot be used as text identity.
    /// </summary>
    private static string NormalizeBody(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        string normalized = markdown.Replace("\r\n", "\n");
        int headingEnd = normalized.IndexOf('\n');
        return headingEnd < 0 ? string.Empty : normalized[(headingEnd + 1)..].Trim();
    }

    /// <summary>A cached previous section signature.</summary>
    private sealed record OldSection(DocxImportManifestFragment Fragment,
        string Heading, string? Parent, string Body);

    /// <summary>A cached current section signature.</summary>
    private sealed record NewSection(int Index, string HeadingText, int? HeadingBlockIndex,
        string ParentText, string Heading, string Parent, string Body, string Markdown);
}

/// <summary>Evidence supporting an association; ambiguity is never auto-resolved.</summary>
public enum DocxReimportAssociationKind
{
    ExactBody,
    HeadingAndAncestry,
    UniqueHeading,
    Ambiguous,
    MissingFromWord
}

/// <summary>One baseline fragment and its proposed current section association.</summary>
public sealed record DocxReimportSectionAssociation
{
    public required Guid FragmentId { get; init; }

    public required string PreviousHeading { get; init; }

    public required int PreviousSectionIndex { get; init; }

    public int? CurrentSectionIndex { get; init; }

    public required DocxReimportAssociationKind Kind { get; init; }

    public bool RequiresConfirmation { get; init; }

    public List<int> CandidateSectionIndexes { get; init; } = [];
}

/// <summary>
/// The complete read-only matching report. Ambiguous new sections are not
/// incorrectly counted as additional new sections.
/// </summary>
public sealed record DocxReimportMatchReport
{
    public List<DocxReimportSectionAssociation> Associations { get; init; } = [];

    public List<int> AddedSectionIndexes { get; init; } = [];

    public List<int> UnresolvedNewSectionIndexes { get; init; } = [];

    public bool RequiresConfirmation =>
        Associations.Any(item => item.RequiresConfirmation) ||
        AddedSectionIndexes.Count > 0 ||
        UnresolvedNewSectionIndexes.Count > 0;
}
