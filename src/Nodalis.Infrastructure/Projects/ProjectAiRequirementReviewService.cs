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
    CriterionGuidMatch,
    CriterionCreationDraft,
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
    IReadOnlyList<string> Warnings)
{
    /// <summary>Verified owning project of this catalog snapshot.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>
    /// Exact locally identified CA block, never deduced from a title or a CA
    /// reference alone. Remains read-only until its own publication gate.
    /// </summary>
    public ProjectRequirementCriterion? Criterion { get; init; }
}

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
    /// fields and identity are compared conservatively. Criterion titles
    /// require an unambiguous stable block GUID; their publication is a
    /// separate human-confirmed operation, never a side effect of comparison.
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
        // Build the complete requirement reference and GUID indexes ONCE
        // per comparison, not once per AI candidate (up to 100 proposals).
        // Keep duplicate groups intact: ambiguity must fail closed.
        Dictionary<Guid, ProjectRequirementEntry[]> requirementsById =
            catalog.Requirements.GroupBy(item => item.Card.Id)
                .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<string, ProjectRequirementEntry[]> requirementsByReference =
            catalog.Requirements.GroupBy(item => item.Card.Reference,
                StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(),
                    StringComparer.Ordinal);

        // The selected project ownership invariant is computed once,
        // including for AI responses containing many CA-creation candidates.
        bool catalogOwnedByProject = catalog.Requirements.All(item =>
            item.Card.ProjectId == catalog.ProjectId);

        // A criterion CA-01 is only project-local within its parent EXI.
        // Build one GUID index across the selected project's canonical
        // registry rather than scanning all Markdown for each AI proposal.
        Dictionary<Guid, List<(ProjectRequirementEntry Parent,
            ProjectRequirementCriterion Criterion)>> criterionIndex = [];
        if (preview.Document.Proposals.Any(candidate =>
            candidate.Kind == "criterion"))
        {
            foreach (ProjectRequirementEntry parent in catalog.Requirements)
            {
                foreach (ProjectRequirementCriterion criterion in parent.Criteria)
                {
                    if (criterion.Id is not Guid id)
                    {
                        continue;
                    }

                    if (!criterionIndex.TryGetValue(id, out List<(
                        ProjectRequirementEntry Parent,
                        ProjectRequirementCriterion Criterion)>? targets))
                    {
                        targets = [];
                        criterionIndex.Add(id, targets);
                    }

                    targets.Add((parent, criterion));
                }
            }
        }

        List<ProjectAiRequirementReview> reviews = [];
        foreach (ProjectAiImportCandidate candidate in preview.Document.Proposals)
        {
            ProjectAiRequirementReview comparison = candidate.Kind == "criterion"
                ? ResolveCriterion(candidate, catalog, criterionIndex,
                    requirementsById, catalogOwnedByProject)
                : Resolve(candidate, catalog, requirementsById,
                    requirementsByReference);
            reviews.Add(comparison with
            {
                ProjectId = catalog.ProjectId
            });
        }

        return reviews;
    }

    /// <summary>
    /// Resolves a CA block against the one selected project by stable block
    /// GUID only. An ambiguous, invalid or cross-document collision cannot
    /// become a false positive even when the CA-xx text matches.
    /// </summary>
    /// <param name="candidate">Untrusted external criterion candidate.</param>
    /// <param name="catalog">Canonical project-scoped requirement catalog.</param>
    /// <param name="index">Indexed CA GUIDs including invalid duplicates.</param>
    /// <param name="requirementsById">Duplicate-aware parent EXI GUID index.</param>
    /// <param name="catalogOwnedByProject">One verified owner check per comparison.</param>
    /// <returns>Read-only comparison; no publication capability.</returns>
    private static ProjectAiRequirementReview ResolveCriterion(
        ProjectAiImportCandidate candidate, ProjectRequirementCatalog catalog,
        IReadOnlyDictionary<Guid, List<(ProjectRequirementEntry Parent,
            ProjectRequirementCriterion Criterion)>> index,
        IReadOnlyDictionary<Guid, ProjectRequirementEntry[]> requirementsById,
        bool catalogOwnedByProject)
    {
        List<string> warnings = [];
        if (candidate.Operation == "create")
        {
            // The existing *parent requirement* is the only acceptable target
            // hint when creating a new CA. Neither a new block GUID nor a
            // new CA-xx reference can be supplied by the external model.
            if (candidate.TargetId is not Guid parentId)
            {
                warnings.Add("Création CA : fournir le GUID de l'exigence " +
                    "EXI parente, jamais le GUID du futur critère.");
                return new ProjectAiRequirementReview(candidate,
                    ProjectAiRequirementMatchStatus.NotFound, null, [], warnings);
            }

            ProjectRequirementEntry[] parents =
                requirementsById.TryGetValue(parentId,
                    out ProjectRequirementEntry[]? existingParents)
                    ? existingParents : [];
            if (parents.Length != 1 || index.ContainsKey(parentId) ||
                !catalogOwnedByProject)
            {
                warnings.Add("Exigence parente absente ou ambiguë, ou " +
                    "collision avec un GUID de bloc existant.");
                return new ProjectAiRequirementReview(candidate,
                    parents.Length == 0
                        ? ProjectAiRequirementMatchStatus.NotFound
                        : ProjectAiRequirementMatchStatus.Ambiguous,
                    null, [], warnings);
            }

            if (!string.IsNullOrWhiteSpace(candidate.CandidateReference))
            {
                warnings.Add("Création CA : la référence CA-xx sera attribuée " +
                    "par Nodalis, jamais acceptée depuis la réponse IA.");
                return new ProjectAiRequirementReview(candidate,
                    ProjectAiRequirementMatchStatus.CreateCollision, null, [],
                    warnings);
            }

            ProjectRequirementEntry creationParent = parents[0];
            if (creationParent.Card.ProjectId != catalog.ProjectId ||
                creationParent.CriterionDiagnostics.Any(message =>
                    !message.StartsWith("Critère sans GUID stable",
                        StringComparison.Ordinal)))
            {
                warnings.Add("Exigence parente ou registre CA à régulariser " +
                    "avant de créer un critère.");
                return new ProjectAiRequirementReview(candidate,
                    ProjectAiRequirementMatchStatus.Ambiguous, null, [], warnings);
            }

            warnings.Add("Nouvelle proposition CA sous " +
                creationParent.Card.Reference + " : code CA et GUID attribués " +
                "uniquement par Nodalis après relecture du Markdown local. " +
                "Cette comparaison ne crée et n'approuve rien.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.CriterionCreationDraft,
                creationParent, [], warnings);
        }

        if (candidate.Operation != "update")
        {
            warnings.Add("Liaison de critère IA non prise en charge.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.Unsupported, null, [], warnings);
        }

        if (candidate.TargetId is not Guid target)
        {
            warnings.Add("Un GUID de bloc CA stable est obligatoire : " +
                "la référence CA-01 seule n'identifie jamais un critère.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.NotFound, null, [], warnings);
        }

        if (!index.TryGetValue(target, out List<(
            ProjectRequirementEntry Parent,
            ProjectRequirementCriterion Criterion)>? matches) ||
            matches.Count == 0)
        {
            warnings.Add("GUID de critère absent des exigences du projet " +
                "sélectionné : aucun repli par titre, CA ou chemin externe.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.NotFound, null, [], warnings);
        }

        if (matches.Count != 1 || requirementsById.ContainsKey(target))
        {
            warnings.Add("GUID CA dupliqué entre fiches ou objet EXI : " +
                "correspondance non qualifiable.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.Ambiguous, null, [], warnings);
        }

        (ProjectRequirementEntry parent,
            ProjectRequirementCriterion criterion) = matches[0];
        if (!criterion.LocallyIdentified ||
            parent.CriterionDiagnostics.Any(message => message.StartsWith(
                "Section de critères répétée", StringComparison.Ordinal)))
        {
            warnings.Add("Identité CA locale ou section d'acceptation ambiguë.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.Ambiguous, null, [], warnings);
        }

        if (candidate.CandidateReference is string reference &&
            (!ProjectRequirementCriterionCodec.IsReference(reference) ||
             !string.Equals(reference, criterion.Reference,
                 StringComparison.Ordinal)))
        {
            warnings.Add("La référence CA fournie ne concorde pas avec " +
                "le GUID de bloc identifié.");
            return new ProjectAiRequirementReview(candidate,
                ProjectAiRequirementMatchStatus.ConflictingHints, null,
                [], warnings);
        }

        Dictionary<string, string> current = new(StringComparer.Ordinal)
        {
            ["title"] = criterion.Title
        };
        Dictionary<string, string> proposed = new(StringComparer.Ordinal);
        if (candidate.ProposedFields.TryGetValue("title", out string? title))
        {
            proposed.Add("title", title);
        }

        if (candidate.ProposedFields.TryGetValue("description",
            out string? description))
        {
            if (criterion.Body is string body)
            {
                current.Add("description", body);
                proposed.Add("description", description);
                warnings.Add("Description CA : comparaison à trois voies " +
                    "uniquement, aucune publication automatique.");
            }
            else
            {
                warnings.Add("Description CA impossible à extraire du " +
                    "Markdown local : source absente, ambiguë ou trop longue.");
            }
        }

        foreach (string field in candidate.ProposedFields.Keys.OrderBy(
            item => item, StringComparer.Ordinal))
        {
            if (field is not ("title" or "description"))
            {
                warnings.Add("Champ CA « " + field + " » non résolu : " +
                    "aucun diff fiable ni autorisation de publication.");
            }
        }

        if (catalog.Diagnostics.Count > 0)
        {
            warnings.Add("Le registre présente " + catalog.Diagnostics.Count +
                " diagnostic(s) : vérifier les collisions du workspace.");
        }

        warnings.Add("Critère CA identifié par GUID stable sous " +
            parent.Card.Reference + " ; comparaison non autorisante. " +
            "Le titre ne peut être publié qu'après une décision humaine, " +
            "une confirmation unitaire et relecture SHA. Aucun verdict IA.");
        return new ProjectAiRequirementReview(candidate,
            ProjectAiRequirementMatchStatus.CriterionGuidMatch, parent,
            ProjectAiThreeWayDiff.Compare(candidate.BaseFields, current,
                proposed), warnings)
        {
            Criterion = criterion
        };
    }

    /// <summary>Resolves one candidate without relying on a human-readable title.</summary>
    /// <param name="candidate">Untrusted metadata and text.</param>
    /// <param name="catalog">Only the requested project's requirements.</param>
    /// <returns>Explicit match classification and safe field projection.</returns>
    private static ProjectAiRequirementReview Resolve(
        ProjectAiImportCandidate candidate, ProjectRequirementCatalog catalog,
        IReadOnlyDictionary<Guid, ProjectRequirementEntry[]> requirementsById,
        IReadOnlyDictionary<string, ProjectRequirementEntry[]>
            requirementsByReference)
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
        ProjectRequirementEntry[] ids =
            candidate.TargetId is Guid id &&
            requirementsById.TryGetValue(id,
                out ProjectRequirementEntry[]? identified)
                ? identified : [];
        ProjectRequirementEntry[] refs =
            ProjectRequirementMarkdownCodec.IsReference(candidate.CandidateReference) &&
            requirementsByReference.TryGetValue(candidate.CandidateReference!,
                out ProjectRequirementEntry[]? referenced)
                ? referenced : [];

        if (candidate.Operation == "create")
        {
            if (candidate.TargetId.HasValue ||
                !string.IsNullOrWhiteSpace(candidate.CandidateReference) ||
                ids.Length > 0 || refs.Length > 0)
            {
                warnings.Add("Création non qualifiable : identité proposée ou " +
                    "référence candidate fournie. Les identifiants officiels " +
                    "doivent être attribués par Nodalis.");
                return new ProjectAiRequirementReview(candidate,
                    ProjectAiRequirementMatchStatus.CreateCollision, null, [], warnings);
            }

            warnings.Add("Nouvelle exigence candidate : référence, GUID et " +
                "champs à confirmer par l'utilisateur avant toute création.");
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

        if (candidate.ProposedFields.TryGetValue("description",
                out string? description) &&
            match.Description is string localDescription)
        {
            current["description"] = localDescription;
            proposed["description"] = description;
            warnings.Add("Description vérifiable : comparaison sans écriture. " +
                "Toute publication exige une revue humaine, un plan et une " +
                "confirmation de transaction séparée.");
            if (candidate.BaseFields.TryGetValue("description",
                    out string? declaredDescription) &&
                !string.Equals(declaredDescription, localDescription,
                    StringComparison.Ordinal))
            {
                warnings.Add("La base déclarée de la description diffère " +
                    "du Markdown local : conflit à examiner.");
            }
        }

        ProjectAiFieldDifference[] differences = ProjectAiThreeWayDiff.Compare(
            candidate.BaseFields, current, proposed).ToArray();
        foreach (string field in candidate.ProposedFields.Keys.OrderBy(
            key => key, StringComparer.Ordinal))
        {
            if (field != "title" &&
                !(field == "description" && match.Description is not null))
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
