using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Explicit user adjudication of section identities in a DOCX reimport.
/// Overrides are session-local and never mutate the historical manifest.
/// </summary>
public static class DocxReimportMatchResolver
{
    /// <summary>
    /// Applies explicit associations to a read-only matching report, refusing
    /// duplicate ownership of a Word section or unknown fragment identifiers.
    /// A null current index explicitly confirms the section is absent; it
    /// does not request deletion of any Nodalis Markdown.
    /// </summary>
    public static DocxReimportMatchReport Resolve(
        DocxReimportMatchReport proposal,
        DocxImportPreview latest,
        IReadOnlyCollection<DocxReimportSectionMatchOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(overrides);

        HashSet<int> sourceIndexes = latest.Sections.Select(section => section.Index).ToHashSet();
        if (sourceIndexes.Count != latest.Sections.Count)
        {
            throw new InvalidDataException("Sections DOCX de la prévisualisation en doublon.");
        }

        Dictionary<Guid, DocxReimportSectionAssociation> originals = [];
        foreach (DocxReimportSectionAssociation association in proposal.Associations)
        {
            if (!originals.TryAdd(association.FragmentId, association))
            {
                throw new InvalidDataException("Identifiants de fragments DOCX en doublon.");
            }
        }

        Dictionary<Guid, DocxReimportSectionMatchOverride> explicitChoices = [];
        foreach (DocxReimportSectionMatchOverride decision in overrides)
        {
            if (!originals.ContainsKey(decision.FragmentId) ||
                !explicitChoices.TryAdd(decision.FragmentId, decision))
            {
                throw new InvalidDataException("Arbitrage DOCX inconnu ou répété.");
            }

            if (decision.CurrentSectionIndex is int index && !sourceIndexes.Contains(index))
            {
                throw new InvalidDataException(
                    "La section Word choisie n'existe plus dans l'aperçu.");
            }
        }

        List<DocxReimportSectionAssociation> resolved = [];
        HashSet<int> allocated = [];
        foreach (DocxReimportSectionAssociation original in proposal.Associations)
        {
            DocxReimportSectionAssociation row = original;
            if (explicitChoices.TryGetValue(original.FragmentId,
                    out DocxReimportSectionMatchOverride? chosen))
            {
                row = original with
                {
                    CurrentSectionIndex = chosen.CurrentSectionIndex,
                    Kind = chosen.CurrentSectionIndex is null
                        ? DocxReimportAssociationKind.ConfirmedMissingFromWord
                        : DocxReimportAssociationKind.Manual,
                    RequiresConfirmation = false,
                    CandidateSectionIndexes = chosen.CurrentSectionIndex is int target
                        ? [target] : []
                };
            }

            if (row.CurrentSectionIndex is int current && !allocated.Add(current))
            {
                throw new InvalidDataException(
                    "Plusieurs anciennes sections pointent vers la même section Word. " +
                    "Arbitrez toutes les associations concernées avant de poursuivre.");
            }

            resolved.Add(row);
        }

        HashSet<int> unresolvedCandidates = resolved
            .Where(row => row.CurrentSectionIndex is null &&
                row.Kind == DocxReimportAssociationKind.Ambiguous)
            .SelectMany(row => row.CandidateSectionIndexes)
            .Where(index => sourceIndexes.Contains(index) && !allocated.Contains(index))
            .ToHashSet();

        return new DocxReimportMatchReport
        {
            Associations = resolved,
            UnresolvedNewSectionIndexes = unresolvedCandidates.OrderBy(index => index).ToList(),
            AddedSectionIndexes = sourceIndexes
                .Where(index => !allocated.Contains(index) && !unresolvedCandidates.Contains(index))
                .OrderBy(index => index)
                .ToList()
        };
    }
}

/// <summary>
/// One deliberate section identity choice for a single review session.
/// A null index means confirmed absence in the latest Word, not deletion.
/// </summary>
public sealed record DocxReimportSectionMatchOverride
{
    public required Guid FragmentId { get; init; }

    public int? CurrentSectionIndex { get; init; }
}
