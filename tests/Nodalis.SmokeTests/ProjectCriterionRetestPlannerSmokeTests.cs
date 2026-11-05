using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Pure synthetic CA-drift-to-REC recommendations: stable identities,
/// cross-project isolation and historical verdict immutability.
/// </summary>
internal static class ProjectCriterionRetestPlannerSmokeTests
{
    /// <summary>Runs standalone no-I/O proposal safety checks.</summary>
    public static void Run()
    {
        const string root = "Applications/Fictive/Projets/Projet-fictif";
        const string requirementSource =
            root + "/Documentation/Exigences/EXI-0001.md";
        const string testSource = root + "/Tests/Cahier.md";
        Guid projectId = Guid.NewGuid();
        Guid requirementId = Guid.NewGuid();
        Guid changedId = Guid.NewGuid();
        Guid addedId = Guid.NewGuid();
        Guid missingId = Guid.NewGuid();
        Guid ambiguousId = Guid.NewGuid();
        Guid sharedTestId = Guid.NewGuid();
        Guid uniqueTestId = Guid.NewGuid();
        Guid unsafeTestId = Guid.NewGuid();

        ProjectRequirementCard card = new()
        {
            Id = requirementId,
            ProjectId = projectId,
            Reference = "EXI-0001",
            Title = "Exigence synthétique",
            CreatedUtc = DateTimeOffset.UtcNow
        };
        ProjectRequirementEntry entry = new(
            card, requirementSource, new string('A', 64));
        ProjectRequirementTraceabilityRow changed = new(
            entry, Criterion(changedId, "CA-01"),
            ProjectRequirementCoverage.LinkedTest,
            [
                Test(sharedTestId, testSource, "TEST-0001 — Partagé"),
                Test(uniqueTestId, testSource, "TEST-0002 — Exclusif")
            ], null);
        ProjectRequirementTraceabilityRow added = new(
            entry, Criterion(addedId, "CA-02"),
            ProjectRequirementCoverage.LinkedTest,
            [
                Test(sharedTestId, testSource, "TEST-0001 — Partagé"),
                Test(unsafeTestId, root + "/Tests/Ancien.md",
                    "TEST-0003 — Déplacé")
            ], null);
        ProjectRequirementTraceabilityRow irrelevant = new(
            entry, Criterion(Guid.NewGuid(), "CA-03"),
            ProjectRequirementCoverage.LinkedTest,
            [Test(Guid.NewGuid(), testSource,
                "TEST-0004 — Critère inchangé")], null);
        ProjectRequirementTraceabilityOverview links = new(
            [changed, added, irrelevant], []);
        ProjectCriterionBaselineSnapshot snapshot = new()
        {
            Schema = "nodalis.ca-baseline.v1",
            ProjectId = projectId,
            Sequence = 4,
            RecordedUtc = DateTimeOffset.UtcNow,
            Reason = "Observation humaine préalable",
            Entries = []
        };
        ProjectCriterionBaselineRow[] changes =
        [
            Row(changedId, requirementId, "CA-01",
                ProjectCriterionBaselineState.Changed),
            Row(addedId, requirementId, "CA-02",
                ProjectCriterionBaselineState.Added),
            Row(missingId, requirementId, "CA-04",
                ProjectCriterionBaselineState.Missing),
            Row(ambiguousId, requirementId, "CA-05",
                ProjectCriterionBaselineState.Unresolved),
            Row(irrelevant.Criterion.Id!.Value, requirementId, "CA-03",
                ProjectCriterionBaselineState.Unchanged)
        ];
        ProjectCriterionBaselineOverview baseline =
            new(snapshot, changes, []);
        ProjectCriterionSelectableTest[] available =
        [
            new(sharedTestId, "TEST-0001 — Partagé", testSource),
            new(uniqueTestId, "TEST-0002 — Exclusif", testSource),
            new(unsafeTestId, "TEST-0003 — Déplacé",
                root + "/Tests/Nouveau.md")
        ];

        ProjectCriterionRetestProposal proposed =
            ProjectCriterionRetestPlanner.Propose(baseline, links, available);
        Assert(proposed.BaselineSequence == 4 &&
            proposed.ChangedCriterionCount == 4 &&
            proposed.Candidates.Count == 2 &&
            proposed.Candidates.Select(item => item.TestId).Distinct()
                .Count() == 2,
            "Fixture uses a real human baseline and distinct suggested test IDs.");
        ProjectCriterionRetestCandidate shared = proposed.Candidates.Single(
            item => item.TestId == sharedTestId);
        Assert(shared.CriterionIds.Count == 2 &&
            shared.CriterionIds.Contains(changedId) &&
            shared.CriterionIds.Contains(addedId) &&
            shared.Reasons.Count == 2 &&
            proposed.Candidates.Single(item =>
                item.TestId == uniqueTestId).CriterionIds.SequenceEqual(
                    [changedId]) &&
            proposed.Candidates.All(item =>
                item.TestId != unsafeTestId) &&
            proposed.Warnings.Any(item => item.Contains("déplacé",
                StringComparison.OrdinalIgnoreCase)) &&
            proposed.Warnings.Any(item => item.Contains("Missing",
                StringComparison.Ordinal)) &&
            proposed.Warnings.Any(item => item.Contains("Unresolved",
                StringComparison.Ordinal)),
            "One shared test is deduplicated across CA changes; moved and " +
            "missing sources remain warnings, not fabricated selections.");

        ProjectCriterionRetestProposal none =
            ProjectCriterionRetestPlanner.Propose(
                new ProjectCriterionBaselineOverview(null, changes, []),
                links, available);
        Assert(none.BaselineSequence is null &&
            none.Candidates.Count == 0 &&
            none.Warnings.Any(item => item.Contains("Aucune référence CA",
                StringComparison.Ordinal)),
            "No human baseline means no inferred historical change.");
        ProjectCriterionRetestProposal stable =
            ProjectCriterionRetestPlanner.Propose(
                baseline with
                {
                    Rows =
                    [
                        Row(changedId, requirementId, "CA-01",
                            ProjectCriterionBaselineState.Unchanged)
                    ]
                }, links, available);
        Assert(stable.Candidates.Count == 0 &&
            stable.ChangedCriterionCount == 0,
            "An unchanged criterion is not re-executed on conjecture.");

        ProjectRequirementTraceabilityOverview duplicates = links with
        {
            Rows = [.. links.Rows, changed]
        };
        ProjectCriterionRetestProposal duplicated =
            ProjectCriterionRetestPlanner.Propose(baseline, duplicates,
                available);
        Assert(duplicated.Candidates.Count == 1 &&
            duplicated.Candidates.Single().TestId == sharedTestId &&
            duplicated.Warnings.Any(item => item.Contains("dupliquée",
                StringComparison.Ordinal)),
            "A duplicate CA GUID is excluded without hiding independent " +
            "safe suggestions from other changed criteria.");

        ProjectRequirementTraceabilityOverview foreign = links with
        {
            Rows =
            [
                changed with
                {
                    Requirement = entry with
                    {
                        Card = card with { ProjectId = Guid.NewGuid() }
                    }
                },
                added with
                {
                    Requirement = entry with
                    {
                        Card = card with { Id = Guid.NewGuid() }
                    }
                }
            ]
        };
        ProjectCriterionRetestProposal rejectedForeign =
            ProjectCriterionRetestPlanner.Propose(baseline, foreign, available);
        Assert(rejectedForeign.Candidates.Count == 0 &&
            rejectedForeign.Warnings.Count > 0,
            "Neither foreign project nor foreign EXI may yield a test proposal.");

        ProjectRequirementTraceabilityOverview stale = links with
        {
            Rows = [changed with
            {
                Coverage = ProjectRequirementCoverage.StaleSource
            }]
        };
        ProjectCriterionRetestProposal outdated =
            ProjectCriterionRetestPlanner.Propose(baseline, stale, available);
        Assert(outdated.Candidates.Count == 0 &&
            outdated.Warnings.Any(item => item.Contains("obsolète",
                StringComparison.Ordinal)),
            "Stale linked source is never inferred to be current.");
        ProjectCriterionRetestProposal repeatedSelectable =
            ProjectCriterionRetestPlanner.Propose(
                baseline, links, [.. available, available[0]]);
        Assert(repeatedSelectable.Candidates.Count == 1 &&
            repeatedSelectable.Candidates.Single().TestId == uniqueTestId,
            "Duplicate current test GUID blocks just that test, " +
            "without blocking an unrelated unique test.");

        ProjectCriterionRetestProposal incomplete =
            ProjectCriterionRetestPlanner.Propose(
                baseline with
                {
                    Diagnostics = ["EXI partiellement illisible."]
                },
                links with
                {
                    Diagnostics = ["Relation typée non interprétable."]
                },
                available);
        Assert(incomplete.Candidates.Count == 2 &&
            incomplete.Warnings.Any(item => item.Contains(
                "pas une liste exhaustive", StringComparison.Ordinal)) &&
            incomplete.Warnings.Any(item => item.Contains(
                "Index de relations incomplet", StringComparison.Ordinal)),
            "Partial catalogs remain explicitly uncertain even when some " +
            "current tests have verified identities.");

        // No campaign object or REC result is accepted by the planner:
        // every candidate is inert until a human explicitly starts a new
        // frozen passage in the existing REC transaction service.
    }

    /// <summary>Creates a synthetic locally identified CA.</summary>
    /// <param name="id">Stable CA GUID.</param>
    /// <param name="reference">Local reference.</param>
    /// <returns>CA with no inferred outcome.</returns>
    private static ProjectRequirementCriterion Criterion(Guid id,
        string reference) => new(reference, "Résultat synthétique", id, 8, true);

    /// <summary>Creates a synthetic test link.</summary>
    /// <param name="id">Stable test GUID.</param>
    /// <param name="path">Current workspace-relative path.</param>
    /// <param name="title">Display title.</param>
    /// <returns>Verified-link fixture.</returns>
    private static ProjectRequirementTestSource Test(Guid id, string path,
        string title) => new(id, path, 7, title, 13);

    /// <summary>Creates one human-baseline diff row.</summary>
    /// <param name="id">Stable CA GUID.</param>
    /// <param name="parent">Parent EXI GUID.</param>
    /// <param name="reference">CA reference for display only.</param>
    /// <param name="state">Synthetic drift status.</param>
    /// <returns>One exact CA change.</returns>
    private static ProjectCriterionBaselineRow Row(Guid id, Guid parent,
        string reference, ProjectCriterionBaselineState state) =>
        new(id, parent, "EXI-0001/" + reference, state, "Écart synthétique.");

    /// <summary>Fails on an unsafe recommendation.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Synthetic failure diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
