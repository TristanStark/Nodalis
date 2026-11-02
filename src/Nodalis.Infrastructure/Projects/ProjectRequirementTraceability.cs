using Nodalis.Core.Links;
using Nodalis.Core.Relations;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// The coverage indicator only asserts a uniquely linked test source.
/// It never asserts execution, conformity or human validation.
/// </summary>
public enum ProjectRequirementCoverage
{
    Unidentified,
    StaleSource,
    Uncovered,
    LinkedTest
}

/// <summary>One resolved, uniquely indexed test source referring to a criterion.</summary>
/// <param name="BlockId">Stable source block ID.</param>
/// <param name="RelativePath">Current source document path.</param>
/// <param name="HeadingLine">Current indexed heading line.</param>
/// <param name="HeadingTitle">Current label, never used for identity.</param>
/// <param name="RelationLine">One-based relation line.</param>
public sealed record ProjectRequirementTestSource(
    Guid BlockId, string RelativePath, int HeadingLine,
    string HeadingTitle, int RelationLine);

/// <summary>Read-only coverage of one canonical EXI acceptance criterion.</summary>
/// <param name="Requirement">Verified owning requirement.</param>
/// <param name="Criterion">One local acceptance criterion.</param>
/// <param name="Coverage">Link-only state, never execution status.</param>
/// <param name="Tests">Unique and resolved test sources, if any.</param>
/// <param name="Diagnostic">Unresolved source/identity reason, if any.</param>
public sealed record ProjectRequirementTraceabilityRow(
    ProjectRequirementEntry Requirement,
    Nodalis.Core.Requirements.ProjectRequirementCriterion Criterion,
    ProjectRequirementCoverage Coverage,
    IReadOnlyList<ProjectRequirementTestSource> Tests,
    string? Diagnostic);

/// <summary>Computed snapshot from one requirement catalog and shared link index.</summary>
/// <param name="Rows">Criteria in project and source order.</param>
/// <param name="Diagnostics">Problems requiring an explicit refresh or review.</param>
public sealed record ProjectRequirementTraceabilityOverview(
    IReadOnlyList<ProjectRequirementTraceabilityRow> Rows,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Count of criteria with at least one verified test source link.</summary>
    public int LinkedCount => Rows.Count(row =>
        row.Coverage == ProjectRequirementCoverage.LinkedTest);

    /// <summary>Count of criteria that cannot currently be trusted.</summary>
    public int UnresolvedCount => Rows.Count(row =>
        row.Coverage is ProjectRequirementCoverage.Unidentified or
            ProjectRequirementCoverage.StaleSource);
}

/// <summary>
/// Joins requirement criteria with typed test-to-criterion relations from one
/// already refreshed workspace index. No I/O, side effects or implicit statuses.
/// </summary>
public static class ProjectRequirementTraceability
{
    /// <summary>
    /// Builds an explicitly conservative coverage matrix. An indexed relation
    /// is counted only when both block IDs and their source documents are
    /// unique, the requirement's SHA matches the link snapshot, and the test
    /// source is in the same project with a recognizable test context.
    /// </summary>
    /// <param name="catalog">Project-owned canonical EXI snapshot.</param>
    /// <param name="index">Already refreshed workspace link index snapshot.</param>
    /// <returns>Pure coverage and diagnostic projection.</returns>
    public static ProjectRequirementTraceabilityOverview Build(
        ProjectRequirementCatalog catalog, LinkIndexCatalog index)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(index);

        Dictionary<string, LinkTargetEntry[]> documentsByPath = index.Targets
            .Where(target => target.Kind == LinkTargetKind.Document)
            .GroupBy(target => target.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<Guid, LinkTargetEntry[]> targetsById = index.Targets
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());

        Dictionary<Guid, IndexedMarkdownBlock[]> blocksById = index.Blocks
            .GroupBy(block => block.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());

        Dictionary<Guid, TypedRelationEntry[]> relationsByTarget =
            index.Relations.Where(relation =>
                relation.TargetId is not null &&
                (string.Equals(relation.RelationType, "vérifie",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(relation.RelationType, "teste",
                    StringComparison.OrdinalIgnoreCase)))
            .GroupBy(relation => relation.TargetId!.Value)
            .ToDictionary(group => group.Key, group => group.ToArray());

        List<ProjectRequirementTraceabilityRow> rows = [];
        List<string> diagnostics = [.. catalog.Diagnostics];

        foreach (ProjectRequirementEntry requirement in catalog.Requirements)
        {
            string? projectPath = FindProjectPath(requirement.RelativePath);
            documentsByPath.TryGetValue(requirement.RelativePath,
                out LinkTargetEntry[]? requirementDocuments);
            bool validSource = projectPath is not null &&
                requirementDocuments is { Length: 1 } &&
                !string.IsNullOrWhiteSpace(requirementDocuments[0].ContentHash) &&
                string.Equals(requirementDocuments[0].ContentHash,
                    requirement.SourceSha256, StringComparison.OrdinalIgnoreCase) &&
                targetsById.TryGetValue(requirementDocuments[0].Id,
                    out LinkTargetEntry[]? owners) && owners.Length == 1;

            LinkTargetEntry? requirementDocument = validSource
                ? requirementDocuments![0] : null;

            if (!validSource)
            {
                diagnostics.Add(requirement.Card.Reference +
                    " : index documentaire absent, ambigu ou obsolète.");
            }

            diagnostics.AddRange(requirement.CriterionDiagnostics.Select(issue =>
                requirement.Card.Reference + " : " + issue));

            foreach (Nodalis.Core.Requirements.ProjectRequirementCriterion criterion
                in requirement.Criteria)
            {
                string label = requirement.Card.Reference + "/" +
                    criterion.Reference;
                if (!criterion.LocallyIdentified || criterion.Id is not Guid id)
                {
                    rows.Add(new ProjectRequirementTraceabilityRow(
                        requirement, criterion,
                        ProjectRequirementCoverage.Unidentified, [],
                        "Critère non identifié localement sans ambiguïté."));
                    continue;
                }

                if (!validSource || requirementDocument is null ||
                    !blocksById.TryGetValue(id, out IndexedMarkdownBlock[]? blocks) ||
                    blocks.Length != 1 ||
                    targetsById.ContainsKey(id) ||
                    blocks[0].DocumentId != requirementDocument.Id ||
                    blocks[0].HeadingLine != criterion.HeadingLine)
                {
                    string warning = !validSource
                        ? "Document d'exigence non synchronisé avec l'index."
                        : "GUID de critère absent, déplacé ou ambigu dans l'index.";
                    diagnostics.Add(label + " : " + warning);
                    rows.Add(new ProjectRequirementTraceabilityRow(
                        requirement, criterion,
                        ProjectRequirementCoverage.StaleSource, [], warning));
                    continue;
                }

                List<ProjectRequirementTestSource> tests = [];
                HashSet<Guid> seenTests = [];
                if (relationsByTarget.TryGetValue(id,
                        out TypedRelationEntry[]? relations))
                {
                    foreach (TypedRelationEntry relation in relations)
                    {
                        if (relation.SourceBlockId is not Guid sourceBlockId ||
                            !blocksById.TryGetValue(sourceBlockId,
                                out IndexedMarkdownBlock[]? candidates) ||
                            candidates.Length != 1 ||
                            targetsById.ContainsKey(sourceBlockId) ||
                            candidates[0].DocumentId != relation.SourceId ||
                            !targetsById.TryGetValue(relation.SourceId,
                                out LinkTargetEntry[]? sourceDocuments) ||
                            sourceDocuments.Length != 1 ||
                            sourceDocuments[0].Kind != LinkTargetKind.Document ||
                            string.IsNullOrWhiteSpace(sourceDocuments[0].ContentHash))
                        {
                            diagnostics.Add(label +
                                " : relation de test sans source stable unique.");
                            continue;
                        }

                        LinkTargetEntry source = sourceDocuments[0];
                        IndexedMarkdownBlock testBlock = candidates[0];

                        if (!IsInProject(source.RelativePath, projectPath!) ||
                            !IsRecognizableTest(source.RelativePath,
                                projectPath!, testBlock.HeadingTitle))
                        {
                            // A relation from another application, project or
                            // arbitrary note can never certify test coverage.
                            diagnostics.Add(label +
                                " : relation hors du référentiel de tests du projet.");
                            continue;
                        }

                        if (seenTests.Add(sourceBlockId))
                        {
                            tests.Add(new ProjectRequirementTestSource(
                                sourceBlockId, source.RelativePath,
                                testBlock.HeadingLine, testBlock.HeadingTitle,
                                relation.LineNumber));
                        }
                    }
                }

                rows.Add(new ProjectRequirementTraceabilityRow(
                    requirement, criterion,
                    tests.Count == 0
                        ? ProjectRequirementCoverage.Uncovered
                        : ProjectRequirementCoverage.LinkedTest,
                    tests.OrderBy(test => test.RelativePath,
                        StringComparer.OrdinalIgnoreCase)
                        .ThenBy(test => test.HeadingLine).ToArray(),
                    null));
            }
        }

        return new ProjectRequirementTraceabilityOverview(
            rows, diagnostics);
    }

    /// <summary>Locates the project root from a canonical EXI source path.</summary>
    /// <param name="reference">Verified workspace-relative EXI source.</param>
    /// <returns>Project root or null for unexpected layouts.</returns>
    private static string? FindProjectPath(string reference)
    {
        const string segment = "/Documentation/Exigences/";
        string path = reference.Replace('\\', '/');
        int marker = path.LastIndexOf(segment,
            StringComparison.OrdinalIgnoreCase);
        if (marker <= 0 ||
            path[(marker + segment.Length)..].Contains('/'))
        {
            return null;
        }

        return path[..marker];
    }

    /// <summary>Enforces a segment boundary between sibling projects.</summary>
    /// <param name="relativePath">Indexed document source.</param>
    /// <param name="projectRoot">Canonical project prefix.</param>
    /// <returns>True only for a document nested under this project.</returns>
    private static bool IsInProject(
        string relativePath, string projectRoot)
    {
        string path = relativePath.Replace('\\', '/');
        return path.StartsWith(projectRoot + "/",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// This provisional classifier accepts a named Tests/Recette directory or
    /// an explicitly named TEST-/TST- block. It is not a test execution record.
    /// </summary>
    /// <param name="sourcePath">Indexed document location.</param>
    /// <param name="projectRoot">Owning project prefix.</param>
    /// <param name="heading">Indexed test block heading.</param>
    /// <returns>Whether source can be treated as an explicit test reference.</returns>
    private static bool IsRecognizableTest(
        string sourcePath, string projectRoot, string heading)
    {
        string relative = sourcePath.Replace('\\', '/');
        if (!IsInProject(relative, projectRoot))
        {
            return false;
        }

        string local = relative[(projectRoot.Length + 1)..];
        string[] segments = local.Split('/');
        if (segments.SkipLast(1).Any(segment =>
                string.Equals(segment, "Tests",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, "Recette",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return heading.StartsWith("TEST-", StringComparison.OrdinalIgnoreCase) ||
               heading.StartsWith("TST-", StringComparison.OrdinalIgnoreCase);
    }
}
