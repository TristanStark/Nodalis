using Nodalis.Core.Tests;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only advisory for a human-approved change to one existing CA.
/// No REC result, requirement validation state or link is ever modified.
/// </summary>
public enum ProjectCriterionChangeImpactKind
{
    Unresolved,
    NoLinkedTest,
    LinkedWithoutResult,
    HistoricalResults
}

/// <summary>
/// One uniquely linked test potentially affected by a changed CA.
/// Historical results describe only what a person declared at the time.
/// </summary>
/// <param name="TestId">Current stable test block GUID.</param>
/// <param name="Title">Current test title, never its identity.</param>
/// <param name="RelativePath">Canonical test source.</param>
/// <param name="Passages">Frozen historic test selections.</param>
/// <param name="DeclaredResults">Historical human outcomes, not proof.</param>
/// <param name="PreviouslyPassed">Historical declared passes, not current conformity.</param>
/// <param name="MovedSources">Historic passages referring to a different source.</param>
public sealed record ProjectCriterionAffectedTest(
    Guid TestId, string Title, string RelativePath, int Passages,
    int DeclaredResults, int PreviouslyPassed, int MovedSources);

/// <summary>
/// Conservative snapshot of downstream test impact before changing a CA.
/// The source SHA-256 is a precondition for the separate guarded transaction.
/// </summary>
/// <param name="RequirementId">Exact existing EXI identity.</param>
/// <param name="CriterionId">Exact existing CA stable block identity.</param>
/// <param name="ExpectedSourceSha256">Reviewed EXI source snapshot.</param>
/// <param name="Kind">Impact classification; never a verdict.</param>
/// <param name="Tests">Distinct linked tests with historical observations.</param>
/// <param name="Diagnostics">Limitations that must be visible to the user.</param>
public sealed record ProjectCriterionChangeImpactOverview(
    Guid RequirementId, Guid CriterionId, string ExpectedSourceSha256,
    ProjectCriterionChangeImpactKind Kind,
    IReadOnlyList<ProjectCriterionAffectedTest> Tests,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>Human results found in immutable REC passages.</summary>
    public int HistoricalResultCount => Tests.Sum(item => item.DeclaredResults);

    /// <summary>
    /// Bounded confirmation text. Never states that a CA has become verified
    /// or that any historical result has been invalidated in storage.
    /// </summary>
    /// <returns>Human-readable, read-only requalification warning.</returns>
    public string Describe()
    {
        List<string> lines =
        [
            "IMPACT SUR LES TESTS — LECTURE SEULE",
            "Une modification du critère peut nécessiter une " +
                "requalification humaine des tests associés."
        ];
        if (Kind == ProjectCriterionChangeImpactKind.Unresolved)
        {
            lines.Add("COUVERTURE INDÉTERMINÉE : source, index ou GUID " +
                "incohérent. Ne présumez ni absence de tests ni conformité.");
        }
        else if (Kind == ProjectCriterionChangeImpactKind.NoLinkedTest)
        {
            lines.Add("Aucun lien de test identifié dans l'index " +
                "actuellement vérifié. Ce n'est pas une preuve de couverture.");
        }
        else
        {
            lines.Add(Tests.Count + " test(s) lié(s) à requalifier " +
                "manuellement après modification du CA.");
            lines.Add(HistoricalResultCount +
                " résultat(s) REC humain(s) observé(s) dans les sources " +
                "disponibles, conservés sans modification." +
                (Diagnostics.Count == 0 ? string.Empty :
                    " Inventaire REC potentiellement incomplet."));
            foreach (ProjectCriterionAffectedTest test in Tests.Take(8))
            {
                lines.Add("• " + test.Title + " (" + test.TestId.ToString("D") +
                    ") : " + test.DeclaredResults +
                    " verdict(s) historique(s), " +
                    test.PreviouslyPassed + " conforme(s) déclaré(s).");
            }

            if (Tests.Count > 8)
            {
                lines.Add("… et " + (Tests.Count - 8) +
                    " autre(s) test(s) lié(s).");
            }
        }

        if (Diagnostics.Count != 0)
        {
            lines.Add("LIMITES DE LA PROJECTION :");
            lines.AddRange(Diagnostics.Take(3));
            if (Diagnostics.Count > 3)
            {
                lines.Add("… " + (Diagnostics.Count - 3) +
                    " autre(s) diagnostic(s).");
            }
        }

        lines.Add("Les anciens passages REC, verdicts, preuves et états " +
            "EXI ne sont jamais modifiés ni automatiquement requalifiés.");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Pure cross-check of one exact CA and its already verified project-local
/// coverage/REC projection. Requires no file I/O and changes no business data.
/// </summary>
public static class ProjectCriterionChangeImpact
{
    /// <summary>
    /// Produces a bounded list of affected tests with their frozen historical
    /// outcomes. Refuses to associate a stale, ambiguous or foreign CA.
    /// </summary>
    /// <param name="projectId">Manifest ID of the selected project.</param>
    /// <param name="requirementId">Existing EXI stable GUID.</param>
    /// <param name="criterionId">Existing CA block stable GUID.</param>
    /// <param name="expectedSourceSha256">EXI SHA used during human review.</param>
    /// <param name="links">Already refreshed, project-scoped link matrix.</param>
    /// <param name="history">REC projection built from the SAME link rows.</param>
    /// <returns>Advisory; uncertainty is never treated as zero impact.</returns>
    public static ProjectCriterionChangeImpactOverview Build(
        Guid projectId, Guid requirementId, Guid criterionId,
        string expectedSourceSha256,
        ProjectRequirementTraceabilityOverview links,
        ProjectRequirementCampaignOverview history)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSourceSha256);
        if (projectId == Guid.Empty || requirementId == Guid.Empty ||
            criterionId == Guid.Empty ||
            expectedSourceSha256.Length != 64 ||
            !expectedSourceSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Identité CA ou SHA d'analyse d'impact invalide.");
        }

        List<string> diagnostics = [];
        ProjectRequirementTraceabilityRow[] matches = links.Rows
            .Where(item => item.Requirement.Card.ProjectId == projectId &&
                item.Requirement.Card.Id == requirementId &&
                item.Criterion.Id == criterionId).ToArray();
        // Explicitly reject a duplicated CA identity anywhere in this
        // matrix, including a different parent requirement.
        bool duplicate = links.Rows.Count(item =>
            item.Criterion.Id == criterionId) != 1;
        if (matches.Length != 1 || duplicate ||
            !matches[0].Criterion.LocallyIdentified ||
            !string.Equals(matches[0].Requirement.SourceSha256,
                expectedSourceSha256, StringComparison.OrdinalIgnoreCase) ||
            matches[0].Coverage is ProjectRequirementCoverage.Unidentified or
                ProjectRequirementCoverage.StaleSource)
        {
            diagnostics.Add("EXI/CA obsolète, ambiguë ou sans " +
                "identité de bloc stable ; actualiser la matrice.");
            return new ProjectCriterionChangeImpactOverview(
                requirementId, criterionId, expectedSourceSha256,
                ProjectCriterionChangeImpactKind.Unresolved, [], diagnostics);
        }

        ProjectRequirementTraceabilityRow row = matches[0];
        if (row.Coverage == ProjectRequirementCoverage.Uncovered)
        {
            return new ProjectCriterionChangeImpactOverview(
                requirementId, criterionId, expectedSourceSha256,
                ProjectCriterionChangeImpactKind.NoLinkedTest, [],
                history.Diagnostics.ToArray());
        }

        ProjectRequirementCampaignCriterionHistory[] associations =
            history.Rows.Where(item =>
                ReferenceEquals(item.Criterion, row)).ToArray();
        if (associations.Length != 1 ||
            associations[0].Tests.Select(item => item.Test.BlockId).Distinct()
                .Count() != row.Tests.Count ||
            row.Tests.Select(item => item.BlockId).Distinct().Count() !=
                row.Tests.Count)
        {
            diagnostics.Add("Projection REC non concordante avec le " +
                "snapshot de liens : recharger la matrice.");
            return new ProjectCriterionChangeImpactOverview(
                requirementId, criterionId, expectedSourceSha256,
                ProjectCriterionChangeImpactKind.Unresolved, [], diagnostics);
        }

        List<ProjectCriterionAffectedTest> affected = [];
        foreach (ProjectRequirementTestSource test in row.Tests)
        {
            ProjectRequirementCampaignTestHistory[] testRows =
                associations[0].Tests.Where(item =>
                    item.Test.BlockId == test.BlockId &&
                    item.Test.RelativePath == test.RelativePath).ToArray();
            if (testRows.Length != 1)
            {
                diagnostics.Add("Historique test absent ou ambigu pour " +
                    test.BlockId.ToString("D") + ".");
                return new ProjectCriterionChangeImpactOverview(
                    requirementId, criterionId, expectedSourceSha256,
                    ProjectCriterionChangeImpactKind.Unresolved, [], diagnostics);
            }

            IReadOnlyList<ProjectRequirementCampaignOccurrence> occurrences =
                testRows[0].Occurrences;
            affected.Add(new ProjectCriterionAffectedTest(test.BlockId,
                test.HeadingTitle, test.RelativePath,
                occurrences.Count,
                occurrences.Count(item => item.Outcome.HasValue),
                occurrences.Count(item =>
                    item.Outcome == ProjectTestOutcome.Passed),
                occurrences.Count(item => !item.MatchesLinkedSource)));
        }

        diagnostics.AddRange(history.Diagnostics);
        if (affected.Any(item => item.MovedSources > 0))
        {
            diagnostics.Add("Certains passages REC pointent vers une " +
                "ancienne source de test : requalification nécessaire.");
        }

        return new ProjectCriterionChangeImpactOverview(
            requirementId, criterionId, expectedSourceSha256,
            affected.Any(item => item.DeclaredResults > 0)
                ? ProjectCriterionChangeImpactKind.HistoricalResults
                : ProjectCriterionChangeImpactKind.LinkedWithoutResult,
            affected, diagnostics);
    }
}
