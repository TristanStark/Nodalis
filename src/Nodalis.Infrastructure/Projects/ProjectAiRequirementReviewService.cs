using Nodalis.Core.Importing;
using Nodalis.Core.Requirements;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Read-only disposition of an untrusted proposed requirement change.</summary>
public enum ProjectAiRequirementMatchStatus
{
    Unsupported,
    CreationDraft,
    CreateCollision,
    GuidMatch,
    ReferenceMatch,
    NotFound,
    ConflictingHints,
    Ambiguous
}

/// <summary>
/// One project-scoped review. Neither a match nor a diff authorizes a write.
/// </summary>
/// <param name="Candidate">The original untrusted proposal.</param>
/// <param name="Status">Resolution result; no implicit human decision.</param>
/// <param name="Requirement">Verified catalog entry, if uniquely matched.</param>
/// <param name="Fields">Comparison of readable canonical fields only.</param>
/// <param name="Warnings">Limitations and diagnostics for human review.</param>
public sealed record ProjectAiRequirementReview(
    ProjectAiImportCandidate Candidate,
    ProjectAiRequirementMatchStatus Status,
    ProjectRequirementEntry? Requirement,
    IReadOnlyList<ProjectAiFieldDifference> Fields,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Resolves external hints against the existing, project-scoped canonical
/// requirement catalog. Never reads candidate sourcePath, matches titles,
/// changes identifiers, or writes any workspace file.
/// </summary>
public static class ProjectAiRequirementReviewService
{
    /// <summary>
    /// Projects at most 100 previously validated candidates against an
    /// authoritative, project-owned catalog. Only the canonical requirement
    /// title is supported for three-way comparison in this increment.
    /// </summary>
    /// <param name="preview">Validated external response preview.</param>
    /// <param name="catalog">Catalog obtained for the selected project alone.</param>
    /// <returns>Read-only review in original proposal order.</returns>
    public static IReadOnlyList<ProjectAiRequirementReview> Compare(
        ProjectAiResponsePreview preview, ProjectRequirementCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(catalog);
        ProjectAiResponseImport.Validate(preview.Document);
        List<ProjectAiRequirementReview> reviews = [];
        foreach (ProjectAiImportCandidate candidate in preview.Document.Proposals)
        {
            reviews.Add(Resolve(candidate, catalog));
        }

        return reviews;
    }

    /// <summary>Resolves one candidate without relying on a human-readable title.</summary>
    /// <param name="candidate">Untrusted metadata and text.</param>
    /// <param name="catalog">Only the requested project's requirements.</param>
    /// <returns>Explicit match classification and safe field projection.</returns>
    private static ProjectAiRequirementReview Resolve(
        ProjectAiImportCandidate candidate, ProjectRequirementCatalog catalog)
    {
        List<string> warnings = [];
        if (catalog.Diagnostics.Count > 0)
        {
            warnings.Add(catalog.Diagnostics.Count +
                " anomalie(s) du registre : toute correspondance doit être requalifiée.");
        }

        if (candidate.Kind != "requirement" || candidate.Operation == "link")
        {
            warnings.Add("Ce type ou cette opération n'a pas encore de résolveur canonique.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.Unsupported, null, [], warnings);
        }

        // SourcePath is merely an untrusted provenance hint. It is never
        // interpreted as a target, and may not be used to read another project.
        ProjectRequirementEntry[] ids = candidate.TargetId is Guid id
            ? catalog.Requirements.Where(item => item.Card.Id == id).ToArray()
            : [];
        ProjectRequirementEntry[] refs =
            ProjectRequirementMarkdownCodec.IsReference(candidate.CandidateReference)
                ? catalog.Requirements.Where(item => string.Equals(
                    item.Card.Reference, candidate.CandidateReference,
                    StringComparison.Ordinal)).ToArray()
                : [];

        if (candidate.Operation == "create")
        {
            if (candidate.TargetId.HasValue || ids.Length > 0 || refs.Length > 0)
            {
                warnings.Add("Création non qualifiable : identité proposée ou " +
                    "référence EXI déjà présente. Aucun nouvel ID ne vient de l'IA.");
                return new ProjectAiRequirementReview(candidate,
                    ProjectAiRequirementMatchStatus.CreateCollision, null, [], warnings);
            }

            warnings.Add("Nouvelle exigence candidate : référence, GUID et " +
                "champs à choisir et confirmer dans une étape ultérieure.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.CreationDraft, null, [], warnings);
        }

        if (ids.Length > 1 || refs.Length > 1)
        {
            warnings.Add("GUID ou référence EXI ambiguë dans le registre.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.Ambiguous, null, [], warnings);
        }

        if (candidate.TargetId.HasValue && ids.Length == 0)
        {
            warnings.Add("GUID cible absent du projet sélectionné : aucun " +
                "repli par titre ou référence n'est autorisé.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.NotFound, null, [], warnings);
        }

        ProjectRequirementEntry? match = candidate.TargetId.HasValue
            ? ids.SingleOrDefault()
            : refs.SingleOrDefault();
        if (match is null)
        {
            warnings.Add("Aucune exigence identifiée : fournir un GUID stable " +
                "ou une référence EXI-xxxx exacte, puis contrôler la cible.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.NotFound, null, [], warnings);
        }

        if (candidate.CandidateReference is string reference &&
            ProjectRequirementMarkdownCodec.IsReference(reference) &&
            !string.Equals(match.Card.Reference, reference,
                StringComparison.Ordinal))
        {
            warnings.Add("Le GUID et la référence EXI ne désignent pas la même cible.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.ConflictingHints, null, [], warnings);
        }

        Dictionary<string, string> current = new(StringComparer.Ordinal)
        {
            ["title"] = match.Card.Title
        };
        Dictionary<string, string> proposed = new(StringComparer.Ordinal);
        if (candidate.ProposedFields.TryGetValue("title", out string? title))
        {
            proposed["title"] = title;
        }

        ProjectAiFieldDifference[] differences = ProjectAiThreeWayDiff.Compare(
            candidate.BaseFields, current, proposed).ToArray();
        foreach (string field in candidate.ProposedFields.Keys.OrderBy(
            key => key, StringComparer.Ordinal))
        {
            if (field != "title")
            {
                warnings.Add("Champ « " + field + " » : état local non résolu, " +
                    "aucun diff fiable ni écriture possible.");
            }
        }

        if (candidate.BaseFields.ContainsKey("title") &&
            !string.Equals(candidate.BaseFields["title"], match.Card.Title,
                StringComparison.Ordinal))
        {
            warnings.Add("La base déclarée diffère du titre local : " +
                "révision concurrente ou base externe non vérifiée.");
        }

        warnings.Add("Instantané SHA-256 : à relire avant toute future " +
            "transaction. Cette résolution ne vaut ni approbation ni import.");
        return new ProjectAiRequirementReview(candidate,
            candidate.TargetId.HasValue
                ? ProjectAiRequirementMatchStatus.GuidMatch
                : ProjectAiRequirementMatchStatus.ReferenceMatch,
            match, differences, warnings);
    }
}
