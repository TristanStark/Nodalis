using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Markdown;
using Nodalis.Core.Requirements;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic EXI/CA/test/REC joins: preserved context, cross-project
/// exclusion, immutable human verdicts and no inferred verification.
/// </summary>
internal static class ProjectRequirementCampaignTraceabilitySmokeTests
{
    /// <summary>Exercises exact historical joins without filesystem changes.</summary>
    public static void Run()
    {
        const string project = "Applications/Exemple/Projets/Projet-A";
        const string projectSibling = "Applications/Exemple/Projets/Projet-A2";
        const string requirementPath =
            project + "/Documentation/Exigences/EXI-0001.md";
        const string testPath = project + "/Tests/Cahier.md";
        const string campaignPath =
            project + "/Tests/Campagnes/REC-0001.md";
        Guid projectId = Guid.NewGuid();
        Guid testId = Guid.NewGuid();
        Guid criterionId = Guid.NewGuid();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(-5);

        ProjectRequirementCard card = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Reference = "EXI-0001",
            Title = "Exigence fictive",
            CreatedUtc = start.AddDays(-1)
        };
        ProjectRequirementEntry requirement = new(
            card, requirementPath, new string('A', 64));
        ProjectRequirementTestSource source = new(
            testId, testPath, 7, "TEST-0001 — Scénario fictif", 14);
        ProjectRequirementTraceabilityOverview links = new(
        [
            new ProjectRequirementTraceabilityRow(
                requirement, new ProjectRequirementCriterion(
                    "CA-01", "Réponse attendue", criterionId, 3, true),
                ProjectRequirementCoverage.LinkedTest, [source], null),
            new ProjectRequirementTraceabilityRow(
                requirement, new ProjectRequirementCriterion(
                    "CA-02", "Aucun test", Guid.NewGuid(), 4, true),
                ProjectRequirementCoverage.Uncovered, [], null),
            new ProjectRequirementTraceabilityRow(
                requirement, new ProjectRequirementCriterion(
                    "CA-03", "Source obsolète", Guid.NewGuid(), 5, true),
                ProjectRequirementCoverage.StaleSource, [], "Index obsolète")
        ], []);

        ProjectTestFrozenDefinition frozenOriginal = Freeze(
            testId, testPath, "Résultat A");
        ProjectTestFrozenDefinition frozenRevised = Freeze(
            testId, testPath, "Résultat B");
        ProjectTestResultEvent declared = new()
        {
            Id = Guid.NewGuid(),
            TestId = testId,
            Outcome = ProjectTestOutcome.Passed,
            Actor = "Exécutant fictif",
            Rationale = "Succès déclaré dans l'ancien environnement.",
            EvidenceNote = "Référence de preuve non contrôlée",
            RecordedUtc = start.AddMinutes(10)
        };
        ProjectTestPassage first = new()
        {
            Id = Guid.NewGuid(),
            Number = 1,
            Environment = "Recette longue",
            ApplicationVersion = "1.0",
            StartedBy = "Responsable fictif",
            StartedUtc = start,
            Tests = [frozenOriginal],
            Results = [declared]
        };
        ProjectTestPassage second = first with
        {
            Id = Guid.NewGuid(),
            Number = 2,
            Environment = "Recette courte",
            ApplicationVersion = "1.1",
            StartedUtc = start.AddHours(2),
            Tests = [frozenRevised],
            Results = []
        };
        ProjectTestCampaign campaign = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Reference = "REC-0001",
            Title = "Campagne de démonstration",
            CreatedUtc = start.AddMinutes(-5),
            State = ProjectTestCampaignState.InProgress,
            Passages = [first, second]
        };
        ProjectTestCampaignMarkdownCodec.Validate(campaign);
        ProjectTestCampaignCatalog catalog = new(
        [
            new ProjectTestCampaignEntry(
                campaign, campaignPath, new string('B', 64))
        ], [], "REC-0002");

        ProjectRequirementCampaignOverview overview =
            ProjectRequirementCampaignTraceability.Build(links, catalog);
        ProjectRequirementCampaignCriterionHistory firstRow =
            overview.Rows[0];
        IReadOnlyList<ProjectRequirementCampaignOccurrence> history =
            firstRow.Tests.Single().Occurrences;

        Assert(overview.Rows.Count == 3 &&
               firstRow.Tests.Count == 1 &&
               history.Count == 2 &&
               history[0].CampaignId == campaign.Id &&
               history[0].PassageId == first.Id &&
               history[0].Outcome == ProjectTestOutcome.Passed &&
               history[0].Actor == declared.Actor &&
               history[0].EvidenceNote == declared.EvidenceNote &&
               history[0].Environment == "Recette longue" &&
               history[0].ApplicationVersion == "1.0" &&
               history[1].PassageId == second.Id &&
               history[1].Outcome is null &&
               history[1].Actor is null &&
               history[1].Environment == "Recette courte" &&
               history[1].ApplicationVersion == "1.1" &&
               history[1].FrozenSha256 != history[0].FrozenSha256 &&
               history.All(item => item.MatchesLinkedSource) &&
               overview.PassageTestCount == 2 &&
               overview.DeclaredResultCount == 1 &&
               overview.Rows[1].Tests.Count == 0 &&
               overview.Rows[2].Tests.Count == 0 &&
               card.Verification == ProjectRequirementVerification.NotVerified &&
               card.Validation == ProjectRequirementValidation.Draft,
            "Historical Passed cannot be promoted to the unexecuted new " +
            "passage or an automatically Verified EXI criterion.");

        ProjectCriterionChangeImpactOverview impact =
            ProjectCriterionChangeImpact.Build(projectId, card.Id, criterionId,
                requirement.SourceSha256, links, overview);
        Assert(impact.Kind ==
                ProjectCriterionChangeImpactKind.HistoricalResults &&
            impact.Tests.Count == 1 &&
            impact.Tests[0].TestId == testId &&
            impact.Tests[0].Passages == 2 &&
            impact.Tests[0].DeclaredResults == 1 &&
            impact.Tests[0].PreviouslyPassed == 1 &&
            impact.HistoricalResultCount == 1 &&
            impact.Describe().Contains("requalifier", StringComparison.Ordinal) &&
            card.Verification == ProjectRequirementVerification.NotVerified,
            "Changed CA must advise requalification of linked tests while " +
            "preserving all historic REC verdicts and EXI statuses.");
        ProjectCriterionChangeImpactOverview uncoveredImpact =
            ProjectCriterionChangeImpact.Build(projectId, card.Id,
                links.Rows[1].Criterion.Id!.Value, requirement.SourceSha256,
                links, overview);
        Assert(uncoveredImpact.Kind ==
                ProjectCriterionChangeImpactKind.NoLinkedTest &&
            uncoveredImpact.Tests.Count == 0 &&
            uncoveredImpact.HistoricalResultCount == 0,
            "An unlinked CA is not described as executed or verified.");
        ProjectCriterionChangeImpactOverview staleImpact =
            ProjectCriterionChangeImpact.Build(projectId, card.Id,
                links.Rows[2].Criterion.Id!.Value, requirement.SourceSha256,
                links, overview);
        Assert(staleImpact.Kind ==
                ProjectCriterionChangeImpactKind.Unresolved,
            "A stale CA index must never be interpreted as no test impact.");
        Assert(ProjectCriterionChangeImpact.Build(projectId, card.Id,
                criterionId, new string('B', 64), links, overview).Kind ==
            ProjectCriterionChangeImpactKind.Unresolved,
            "An EXI edited after review forces a new impact snapshot.");
        Assert(ProjectCriterionChangeImpact.Build(Guid.NewGuid(), card.Id,
                criterionId, requirement.SourceSha256, links, overview).Kind ==
            ProjectCriterionChangeImpactKind.Unresolved,
            "An outside-project identifier cannot reuse linked REC results.");
        ProjectRequirementTraceabilityOverview duplicateCriteria = links with
        {
            Rows = [.. links.Rows, links.Rows[0]]
        };
        Assert(ProjectCriterionChangeImpact.Build(projectId, card.Id,
                criterionId, requirement.SourceSha256,
                duplicateCriteria, overview).Kind ==
            ProjectCriterionChangeImpactKind.Unresolved,
            "Duplicated CA GUIDs across rows block impact resolution.");
        ProjectRequirementCampaignOverview wrongHistory = overview with
        {
            Rows = [overview.Rows[1]]
        };
        Assert(ProjectCriterionChangeImpact.Build(projectId, card.Id,
                criterionId, requirement.SourceSha256, links,
                wrongHistory).Kind ==
            ProjectCriterionChangeImpactKind.Unresolved,
            "Historic REC associations must be built from the same link snapshot.");
        ProjectTestCampaignCatalog emptyCatalog = catalog with
        {
            Campaigns = []
        };
        ProjectRequirementCampaignOverview noResults =
            ProjectRequirementCampaignTraceability.Build(links, emptyCatalog);
        ProjectCriterionChangeImpactOverview neverExecuted =
            ProjectCriterionChangeImpact.Build(projectId, card.Id, criterionId,
                requirement.SourceSha256, links, noResults);
        Assert(neverExecuted.Kind ==
                ProjectCriterionChangeImpactKind.LinkedWithoutResult &&
            neverExecuted.Tests.Count == 1 &&
            neverExecuted.Tests[0].Passages == 0,
            "A linked but never executed test must still be recommended for " +
            "human review, without fabricating an outcome.");

        ProjectRequirementTraceabilityOverview sharedTest = links with
        {
            Rows =
            [
                links.Rows[0],
                links.Rows[0] with
                {
                    Criterion = new ProjectRequirementCriterion(
                        "CA-04", "Même test, autre critère",
                        Guid.NewGuid(), 8, true)
                },
                links.Rows[1]
            ]
        };
        ProjectRequirementCampaignOverview sharedHistory =
            ProjectRequirementCampaignTraceability.Build(
                sharedTest, catalog);
        Assert(sharedHistory.Rows[0].Tests[0].Occurrences.Count == 2 &&
               sharedHistory.Rows[1].Tests[0].Occurrences.Count == 2 &&
               sharedHistory.PassageTestCount == 2 &&
               sharedHistory.DeclaredResultCount == 1,
            "One actual human passage result linked to two CA criteria " +
            "must not double-count project-level execution totals.");

        ProjectTestCampaign foreign = campaign with
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Reference = "REC-0002"
        };
        ProjectTestCampaign sibling = campaign with
        {
            Id = Guid.NewGuid(),
            Reference = "REC-0003"
        };
        ProjectTestCampaignCatalog mixed = catalog with
        {
            Campaigns =
            [
                .. catalog.Campaigns,
                new ProjectTestCampaignEntry(
                    foreign, project + "/Tests/Campagnes/REC-0002.md",
                    new string('C', 64)),
                new ProjectTestCampaignEntry(
                    sibling,
                    projectSibling + "/Tests/Campagnes/REC-0003.md",
                    new string('D', 64))
            ]
        };
        ProjectRequirementCampaignOverview confined =
            ProjectRequirementCampaignTraceability.Build(links, mixed);
        Assert(confined.PassageTestCount == 2 &&
               confined.DeclaredResultCount == 1,
            "Same GUID and similar project prefix cannot leak foreign or " +
            "sibling campaigns into the matrix.");

        ProjectTestCampaign alienDefinition = campaign with
        {
            Id = Guid.NewGuid(),
            Reference = "REC-0004",
            Passages =
            [
                first with
                {
                    Tests =
                    [
                        frozenOriginal with
                        {
                            SourceRelativePath =
                                projectSibling + "/Tests/Cahier.md"
                        }
                    ]
                }
            ]
        };
        ProjectTestCampaignCatalog alienCatalog = catalog with
        {
            Campaigns =
            [
                new ProjectTestCampaignEntry(
                    alienDefinition, project + "/Tests/Campagnes/REC-0004.md",
                    new string('E', 64))
            ]
        };
        Assert(ProjectRequirementCampaignTraceability.Build(
                   links, alienCatalog).PassageTestCount == 0,
            "A forged frozen test path outside the project is never linked.");

        ProjectTestCampaign movedDefinition = campaign with
        {
            Id = Guid.NewGuid(),
            Reference = "REC-0005",
            Passages =
            [
                first with
                {
                    Tests =
                    [
                        frozenOriginal with
                        {
                            SourceRelativePath =
                                project + "/Recette/AncienCahier.md"
                        }
                    ]
                }
            ]
        };
        ProjectTestCampaignCatalog movedCatalog = catalog with
        {
            Campaigns =
            [
                new ProjectTestCampaignEntry(
                    movedDefinition, project + "/Tests/Campagnes/REC-0005.md",
                    new string('F', 64))
            ],
            Diagnostics = ["Document REC non reconnu à examiner."]
        };
        ProjectRequirementCampaignOverview relocated =
            ProjectRequirementCampaignTraceability.Build(links, movedCatalog);
        Assert(relocated.PassageTestCount == 1 &&
               !relocated.Rows[0].Tests[0].Occurrences[0].MatchesLinkedSource &&
               relocated.Diagnostics.Any(item =>
                   item.Contains("emplacement", StringComparison.Ordinal)) &&
               relocated.Diagnostics.Any(item =>
                   item.Contains("non reconnu", StringComparison.Ordinal)),
            "Historic moves remain visible but require explicit requalification.");

        Assert(campaign.Passages[0].Results.Single().Outcome ==
                   ProjectTestOutcome.Passed &&
               campaign.Passages[1].Results.Count == 0 &&
               links.Rows[0].Coverage == ProjectRequirementCoverage.LinkedTest,
            "Pure projection must not alter REC or EXI canonical sources.");
    }

    /// <summary>Creates an independently fingerprinted synthetic test block.</summary>
    /// <param name="id">Stable test identity.</param>
    /// <param name="path">Source belonging to a fictional project.</param>
    /// <param name="result">Revision marker, not an actual test result.</param>
    /// <returns>One intact section snapshot.</returns>
    private static ProjectTestFrozenDefinition Freeze(
        Guid id, string path, string result)
    {
        string markdown = "## TEST-0001 — Scénario fictif\n" +
            MarkdownBlockIdentityCodec.FormatMarker(id) +
            "\n### Étapes\n- Exécuter un scénario fictif\n" +
            "### Attendu\n- " + result + "\n";
        return new ProjectTestFrozenDefinition
        {
            TestId = id,
            Reference = "TEST-0001",
            SourceRelativePath = path,
            Markdown = markdown,
            Sha256 = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(markdown))),
            HeadingLine = 7
        };
    }

    /// <summary>Throws a failure without personal workspace data.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Safe synthetic diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
