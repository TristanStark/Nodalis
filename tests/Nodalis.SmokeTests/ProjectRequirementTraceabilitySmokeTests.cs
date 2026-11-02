using Nodalis.Core.Links;
using Nodalis.Core.Relations;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Pure synthetic tests: block collisions, project confinement and link-only
/// coverage without inventing execution, conformity or human approval.
/// </summary>
internal static class ProjectRequirementTraceabilitySmokeTests
{
    /// <summary>Runs deterministic matrix and cross-project regressions.</summary>
    public static void Run()
    {
        const string project = "Applications/Exemple/Projets/Projet-A";
        const string requirementPath =
            project + "/Documentation/Exigences/EXI-0001.md";
        const string testPath = project + "/Tests/Cahier.md";
        const string otherPath =
            "Applications/Exemple/Projets/Projet-A2/Tests/Autre.md";
        const string notePath = project + "/Documentation/Note.md";
        string hash = new('A', 64);
        Guid requirementId = Guid.NewGuid();
        Guid requirementDocumentId = Guid.NewGuid();
        Guid testDocumentId = Guid.NewGuid();
        Guid otherDocumentId = Guid.NewGuid();
        Guid noteDocumentId = Guid.NewGuid();
        Guid firstCriterionId = Guid.NewGuid();
        Guid secondCriterionId = Guid.NewGuid();
        Guid testBlockId = Guid.NewGuid();
        Guid foreignTestId = Guid.NewGuid();
        Guid noteBlockId = Guid.NewGuid();

        ProjectRequirementCard card = new()
        {
            Id = requirementId,
            ProjectId = Guid.NewGuid(),
            Reference = "EXI-0001",
            Title = "Exigence fictive",
            CreatedUtc = DateTimeOffset.UtcNow
        };
        ProjectRequirementEntry requirement = new(
            card, requirementPath, hash)
        {
            Criteria =
            [
                new("CA-01", "Réponse attendue", firstCriterionId, 10, true),
                new("CA-02", "Deuxième réponse", secondCriterionId, 20, true),
                new("CA-03", "Critère sans ID", null, 30, false)
            ]
        };
        ProjectRequirementCatalog requirements = new(
            [requirement], [], "EXI-0002");

        LinkIndexCatalog index = new()
        {
            Targets =
            [
                Document(requirementDocumentId, requirementPath, hash),
                Document(testDocumentId, testPath, new string('B', 64)),
                Document(otherDocumentId, otherPath, new string('C', 64)),
                Document(noteDocumentId, notePath, new string('D', 64))
            ],
            Blocks =
            [
                Block(firstCriterionId, requirementDocumentId,
                    "CA-01 — Réponse attendue", 10),
                Block(secondCriterionId, requirementDocumentId,
                    "CA-02 — Deuxième réponse", 20),
                Block(testBlockId, testDocumentId, "Cas de test fictif", 4),
                Block(foreignTestId, otherDocumentId,
                    "TEST-001 — Autre projet", 3),
                Block(noteBlockId, noteDocumentId,
                    "Note libre non qualifiée", 3)
            ],
            Relations =
            [
                Relation(testDocumentId, testBlockId, firstCriterionId, 7),
                // Same test, repeated occurrence: only one source link.
                Relation(testDocumentId, testBlockId, firstCriterionId, 8),
                Relation(otherDocumentId, foreignTestId, firstCriterionId, 6),
                Relation(noteDocumentId, noteBlockId, firstCriterionId, 6),
                Relation(testDocumentId, null, secondCriterionId, 12),
                Relation(testDocumentId, testBlockId, secondCriterionId, 13,
                    "concerne")
            ]
        };
        ProjectRequirementTraceabilityOverview baseline =
            ProjectRequirementTraceability.Build(requirements, index);
        Assert(baseline.Rows.Count == 3 &&
               baseline.LinkedCount == 1 &&
               baseline.UnresolvedCount == 1 &&
               baseline.Rows[0].Coverage ==
                   ProjectRequirementCoverage.LinkedTest &&
               baseline.Rows[0].Tests.Count == 1 &&
               baseline.Rows[0].Tests[0].BlockId == testBlockId &&
               baseline.Rows[0].Tests[0].RelativePath == testPath &&
               baseline.Rows[1].Coverage ==
                   ProjectRequirementCoverage.Uncovered &&
               baseline.Rows[2].Coverage ==
                   ProjectRequirementCoverage.Unidentified &&
               baseline.Diagnostics.Any(item => item.Contains(
                   "hors du référentiel", StringComparison.Ordinal)) &&
               card.Verification == ProjectRequirementVerification.NotVerified &&
               card.Validation == ProjectRequirementValidation.Draft,
            "Only a unique, project-local test block proves a LINK, never execution.");

        LinkIndexCatalog changedSource = index with
        {
            Targets = index.Targets.Select(target =>
                target.Id == requirementDocumentId
                    ? target with { ContentHash = new string('E', 64) }
                    : target).ToList()
        };
        ProjectRequirementTraceabilityOverview stale =
            ProjectRequirementTraceability.Build(requirements, changedSource);
        Assert(stale.LinkedCount == 0 &&
               stale.UnresolvedCount == 3 &&
               stale.Rows[0].Coverage ==
                   ProjectRequirementCoverage.StaleSource,
            "A stale requirement SHA must invalidate every coverage claim.");

        LinkIndexCatalog duplicateCriterion = index with
        {
            Blocks = index.Blocks.Append(Block(
                firstCriterionId, otherDocumentId,
                "CA-01 — Copie", 9)).ToList()
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, duplicateCriterion).Rows[0].Coverage ==
                   ProjectRequirementCoverage.StaleSource,
            "A duplicated block ID in another project must never resolve.");

        LinkIndexCatalog crossKindCollision = index with
        {
            Targets = index.Targets.Append(
                Document(firstCriterionId,
                    project + "/AutreDocument.md",
                    new string('F', 64))).ToList()
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, crossKindCollision).LinkedCount == 0,
            "A block/document GUID collision must refuse linking.");

        LinkIndexCatalog wrongHeading = index with
        {
            Blocks = index.Blocks.Select(block =>
                block.Id == firstCriterionId
                    ? block with { HeadingLine = 11 }
                    : block).ToList()
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, wrongHeading).Rows[0].Coverage ==
                   ProjectRequirementCoverage.StaleSource,
            "Source line changes require an index refresh before coverage.");

        LinkIndexCatalog changedTestLocation = index with
        {
            Targets = index.Targets.Select(target =>
                target.Id == testDocumentId
                    ? target with { RelativePath = otherPath }
                    : target).ToList()
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, changedTestLocation).LinkedCount == 0,
            "A relocated test outside the project must not count as covered.");

        ProjectRequirementEntry renamed = requirement with
        {
            Criteria =
            [
                requirement.Criteria[0] with
                {
                    Title = "Libellé renommé"
                },
                requirement.Criteria[1],
                requirement.Criteria[2]
            ]
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements with { Requirements = [renamed] },
                   index).LinkedCount == 1,
            "A renamed title keeps coverage through its stable block GUID.");

        LinkIndexCatalog documentSourceOnly = index with
        {
            Relations =
            [
                Relation(testDocumentId, null, firstCriterionId, 9)
            ]
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, documentSourceOnly).LinkedCount == 0,
            "A document-level relation cannot impersonate an identified test.");

        LinkIndexCatalog noteOnly = index with
        {
            Relations =
            [
                Relation(noteDocumentId, noteBlockId, firstCriterionId, 9)
            ]
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, noteOnly).LinkedCount == 0,
            "Ordinary project notes cannot count as test definitions.");

        LinkIndexCatalog invalidSourceBlock = index with
        {
            Blocks = index.Blocks.Append(Block(
                testBlockId, noteDocumentId, "Copie test", 15)).ToList()
        };
        Assert(ProjectRequirementTraceability.Build(
                   requirements, invalidSourceBlock).LinkedCount == 0,
            "A duplicated test source block cannot contribute to coverage.");

        Assert(baseline.Rows[0].Criterion.Id == firstCriterionId &&
               index.Relations.Count == 6 &&
               requirements.Requirements[0].Criteria.Count == 3,
            "The matrix must not modify the canonical or derived snapshots.");
    }

    /// <summary>Creates a synthetic indexed document with an exact source hash.</summary>
    /// <param name="id">Stable document identity.</param>
    /// <param name="path">Workspace-relative Markdown source.</param>
    /// <param name="hash">Indexed source fingerprint.</param>
    /// <returns>Link target.</returns>
    private static LinkTargetEntry Document(
        Guid id, string path, string hash) => new()
    {
        Id = id,
        Kind = LinkTargetKind.Document,
        RelativePath = path,
        QualifiedName = path,
        DisplayName = "Fichier fictif",
        ContentHash = hash
    };

    /// <summary>Creates a synthetic stable heading.</summary>
    /// <param name="id">Block identity.</param>
    /// <param name="documentId">Owning document identity.</param>
    /// <param name="title">Current human heading.</param>
    /// <param name="line">One-based source position.</param>
    /// <returns>Indexed block.</returns>
    private static IndexedMarkdownBlock Block(
        Guid id, Guid documentId, string title, int line) => new()
    {
        Id = id,
        DocumentId = documentId,
        HeadingTitle = title,
        HeadingLine = line,
        HeadingLevel = 3,
        HeadingOffset = line * 24
    };

    /// <summary>Creates a canonical source-block to criterion typed relation.</summary>
    /// <param name="sourceDocument">Source document identity.</param>
    /// <param name="sourceBlock">Source test identity, if any.</param>
    /// <param name="target">Acceptance criterion identity.</param>
    /// <param name="line">Relation position.</param>
    /// <param name="type">Typed relation verb.</param>
    /// <returns>Indexed relation.</returns>
    private static TypedRelationEntry Relation(
        Guid sourceDocument, Guid? sourceBlock, Guid target,
        int line, string type = "vérifie") => new()
    {
        SourceId = sourceDocument,
        SourceBlockId = sourceBlock,
        TargetId = target,
        RawTargetId = target.ToString("D"),
        RelationType = type,
        TargetLabel = "Critère fictif",
        LineNumber = line,
        Excerpt = "Relation synthétique"
    };

    /// <summary>Throws only synthetic failure text.</summary>
    /// <param name="condition">Expected test invariant.</param>
    /// <param name="message">Failure description.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
