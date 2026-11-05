namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One current, unique test block offered by the existing REC selector.
/// This detached projection prevents domain code depending on WPF models.
/// </summary>
/// <param name="Id">Stable indexed Markdown test GUID.</param>
/// <param name="Label">Current test heading, not identity.</param>
/// <param name="RelativePath">Current workspace-relative test source.</param>
public sealed record ProjectCriterionSelectableTest(
    Guid Id, string Label, string RelativePath);

/// <summary>
/// One current, uniquely identifiable test suggested for human selection
/// because one or more linked CA changed since an explicit project baseline.
/// </summary>
/// <param name="TestId">Stable current test-block GUID, never test title.</param>
/// <param name="Label">Current display title, not an identity.</param>
/// <param name="RelativePath">Unique current source path.</param>
/// <param name="CriterionIds">Affected stable CA GUIDs, without duplicates.</param>
/// <param name="Reasons">CA references and change categories for user review.</param>
public sealed record ProjectCriterionRetestCandidate(
    Guid TestId, string Label, string RelativePath,
    IReadOnlyList<Guid> CriterionIds, IReadOnlyList<string> Reasons);

/// <summary>
/// Read-only proposal, not a REC passage, proof, result or approval.
/// </summary>
/// <param name="BaselineSequence">Exact explicitly recorded CA baseline number.</param>
/// <param name="ChangedCriterionCount">Changed/added/missing CA in comparison.</param>
/// <param name="Candidates">Unique available test block GUIDs.</param>
/// <param name="Warnings">Incomplete/ambiguous coverage and unsafe sources.</param>
public sealed record ProjectCriterionRetestProposal(
    int? BaselineSequence, int ChangedCriterionCount,
    IReadOnlyList<ProjectCriterionRetestCandidate> Candidates,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Pure, project-bound recommendation based on the latest human CA baseline,
/// current EXI→CA→test relations and currently selectable tests. Missing CA
/// and unsafe links are WARNINGS, never invented test identities.
/// </summary>
public static class ProjectCriterionRetestPlanner
{
    /// <summary>
    /// Creates a bounded, deterministic, deduplicated test selection.
    /// Every proposed test must have one unambiguous current source in the
    /// same project, and an EXI-owned CA identified in the drift projection.
    /// </summary>
    /// <param name="baseline">Read-only comparison with recorded reference.</param>
    /// <param name="links">Current project-local link and criterion matrix.</param>
    /// <param name="availableTests">Uniquely indexed, selectable test blocks.</param>
    /// <returns>Proposal and limitations. Never writes to the workspace.</returns>
    public static ProjectCriterionRetestProposal Propose(
        ProjectCriterionBaselineOverview baseline,
        ProjectRequirementTraceabilityOverview links,
        IReadOnlyCollection<ProjectCriterionSelectableTest> availableTests)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(availableTests);
        List<string> warnings = [];

        if (baseline.Snapshot is null)
        {
            warnings.Add("Aucune référence CA humaine enregistrée : " +
                "activer le suivi dans la matrice de traçabilité avant " +
                "de proposer une reprise fondée sur des modifications.");
            return new ProjectCriterionRetestProposal(
                null, 0, [], warnings);
        }

        Guid projectId = baseline.Snapshot.ProjectId;
        if (projectId == Guid.Empty)
        {
            throw new InvalidDataException("Identité du projet CA absente.");
        }

        if (baseline.Diagnostics.Count != 0)
        {
            warnings.Add("Sources CA partiellement illisibles ou ambiguës : " +
                "les tests proposés ne constituent pas une liste exhaustive.");
            warnings.AddRange(baseline.Diagnostics.Take(5));
        }

        if (links.Diagnostics.Count != 0)
        {
            warnings.Add("Index de relations incomplet : vérifier " +
                "manuellement les liens absents ou ambigus.");
            warnings.AddRange(links.Diagnostics.Take(5));
        }

        ProjectCriterionBaselineRow[] dirty = baseline.Rows
            .Where(item => item.State is ProjectCriterionBaselineState.Changed
                or ProjectCriterionBaselineState.Added
                or ProjectCriterionBaselineState.Missing
                or ProjectCriterionBaselineState.Unresolved)
            .ToArray();
        ProjectCriterionBaselineRow[] active = dirty.Where(item =>
            item.State is ProjectCriterionBaselineState.Changed or
                ProjectCriterionBaselineState.Added).ToArray();

        ProjectCriterionSelectableTest[] sources = availableTests.ToArray();
        Dictionary<Guid, ProjectCriterionSelectableTest[]> sourceById = sources
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, ProjectRequirementTraceabilityRow[]> linksByCa =
            links.Rows.Where(item => item.Criterion.Id.HasValue)
                .GroupBy(item => item.Criterion.Id!.Value)
                .ToDictionary(group => group.Key, group => group.ToArray());

        Dictionary<Guid, (ProjectCriterionSelectableTest Source,
            HashSet<Guid> Criteria, List<string> Reasons)> selected = [];

        foreach (ProjectCriterionBaselineRow change in dirty)
        {
            if (change.State is ProjectCriterionBaselineState.Missing or
                ProjectCriterionBaselineState.Unresolved)
            {
                warnings.Add(change.Label + " : " + change.State +
                    " — identité ou relations impossibles à reconstruire " +
                    "automatiquement ; inspecter manuellement.");
                continue;
            }

            if (!linksByCa.TryGetValue(change.CriterionId,
                    out ProjectRequirementTraceabilityRow[]? rows) ||
                rows.Length != 1 ||
                rows[0].Requirement.Card.ProjectId != projectId ||
                rows[0].Requirement.Card.Id != change.RequirementId ||
                !rows[0].Criterion.LocallyIdentified)
            {
                warnings.Add(change.Label +
                    " : relation CA introuvable, dupliquée ou hors projet.");
                continue;
            }

            ProjectRequirementTraceabilityRow row = rows[0];
            if (row.Coverage != ProjectRequirementCoverage.LinkedTest)
            {
                warnings.Add(change.Label + " : " + (row.Coverage ==
                    ProjectRequirementCoverage.Uncovered
                    ? "aucun test lié avec identité vérifiée."
                    : "source de lien obsolète ou ambiguë."));
                continue;
            }

            foreach (ProjectRequirementTestSource test in row.Tests)
            {
                if (!sourceById.TryGetValue(test.BlockId,
                        out ProjectCriterionSelectableTest[]? matches) ||
                    matches.Length != 1 ||
                    !string.Equals(matches[0].RelativePath,
                        test.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(change.Label + " : test " +
                        test.BlockId.ToString("D") +
                        " indisponible, déplacé ou ambigu ; pas de " +
                        "présélection automatique.");
                    continue;
                }

                if (!selected.TryGetValue(test.BlockId,
                        out (ProjectCriterionSelectableTest Source,
                            HashSet<Guid> Criteria,
                            List<string> Reasons) existing))
                {
                    existing = (matches[0], [], []);
                    selected.Add(test.BlockId, existing);
                }

                if (existing.Criteria.Add(change.CriterionId))
                {
                    existing.Reasons.Add(change.Label + " : " +
                        change.State);
                }
            }
        }

        ProjectCriterionRetestCandidate[] candidates = selected.Values
            .OrderBy(item => item.Source.Label,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Source.Id)
            .Select(item => new ProjectCriterionRetestCandidate(
                item.Source.Id, item.Source.Label,
                item.Source.RelativePath,
                item.Criteria.OrderBy(id => id).ToArray(),
                item.Reasons.ToArray()))
            .ToArray();

        if (active.Length == 0)
        {
            warnings.Add("Aucun CA présent avec un changement de " +
                "titre/contenu depuis la référence. Un CA disparu " +
                "nécessite une inspection manuelle des anciens liens.");
        }
        else if (candidates.Length == 0)
        {
            warnings.Add("Aucun test actuel suffisamment identifié pour " +
                "une présélection. Vérifier les liens EXI→CA→TEST.");
        }

        warnings.Add("Cette proposition ne crée aucun passage REC, " +
            "n'exécute aucun test et ne modifie aucun verdict antérieur.");
        return new ProjectCriterionRetestProposal(
            baseline.Snapshot.Sequence, dirty.Length, candidates, warnings);
    }
}
