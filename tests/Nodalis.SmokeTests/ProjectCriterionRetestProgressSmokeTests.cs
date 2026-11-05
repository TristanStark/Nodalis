using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Markdown;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic, read-only follow-up of CA-related selections/results in REC.
/// No persistence, user project names or inferred human verification.
/// </summary>
internal static class ProjectCriterionRetestProgressSmokeTests
{
    /// <summary>
    /// Checks no passage, latest nonexecuted vs old Passed, source mismatch,
    /// project isolation, human declarations and ambiguous suggestions.
    /// </summary>
    public static void Run()
    {
        Guid projectId = Guid.NewGuid();
        Guid[] tests = Enumerable.Range(0, 4)
            .Select(_ => Guid.NewGuid()).ToArray();
        Guid[] criteria = Enumerable.Range(0, 3)
            .Select(_ => Guid.NewGuid()).ToArray();
        DateTimeOffset initial = DateTimeOffset.UtcNow.AddHours(-4);
        ProjectCriterionBaselineSnapshot reference = new()
        {
            Schema = "nodalis.ca-baseline.v1",
            ProjectId = projectId,
            Sequence = 3,
            RecordedUtc = initial,
            Reason = "Point de contrôle fictif.",
            Entries = []
        };
        ProjectCriterionBaselineOverview baseline = new(
            reference, [], []);
        const string source = "Applications/Fictive/Projets/Source/Tests/Plan.md";
        ProjectCriterionRetestProposal proposal = new(
            reference.Sequence, 3,
        [
            new ProjectCriterionRetestCandidate(tests[0],
                "TEST-0001 — Déjà déclaré", source,
                [criteria[0], criteria[1]], ["CA-01 changé", "CA-02 changé"]),
            new ProjectCriterionRetestCandidate(tests[1],
                "TEST-0002 — Non exécuté", source,
                [criteria[1]], ["CA-02 changé"]),
            new ProjectCriterionRetestCandidate(tests[2],
                "TEST-0003 — Non sélectionné", source,
                [criteria[2]], ["CA-03 ajouté"]),
            new ProjectCriterionRetestCandidate(tests[3],
                "TEST-0004 — Déplacé", source,
                [criteria[0]], ["CA-01 changé"])
        ], ["CA supprimé à vérifier manuellement."]);

        ProjectTestCampaign campaign = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Reference = "REC-0001",
            Title = "Campagne fictive",
            CreatedUtc = initial.AddMinutes(-20),
            State = ProjectTestCampaignState.InProgress
        };

        ProjectCriterionRetestProgressOverview empty =
            ProjectCriterionRetestProgress.Build(baseline, proposal, campaign);
        Assert(empty.Rows.Count == 4 &&
            empty.NotSelectedCount == 4 &&
            empty.WithoutResultCount == 0 &&
            empty.HumanResultCount == 0 &&
            empty.Rows.All(row => row.State ==
                ProjectCriterionRetestProgressState.NoPassage),
            "No passage is not a test execution or historic passing outcome.");

        ProjectTestPassage first = new()
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartedUtc = initial.AddMinutes(10),
            StartedBy = "Exécutant fictif",
            Environment = "Recette synthétique",
            ApplicationVersion = "0.1",
            Tests =
            [
                Frozen(tests[0], 1, source),
                Frozen(tests[1], 2, source),
                Frozen(tests[3], 4,
                    "Applications/Fictive/Projets/Source/Tests/Ancien.md")
            ],
            Results =
            [
                Result(tests[0], ProjectTestOutcome.Passed,
                    initial.AddMinutes(20)),
                Result(tests[3], ProjectTestOutcome.Blocked,
                    initial.AddMinutes(22))
            ]
        };
        ProjectTestCampaign withFirst = campaign with
        {
            Passages = [first]
        };
        ProjectCriterionRetestProgressOverview observed =
            ProjectCriterionRetestProgress.Build(
                baseline, proposal, withFirst);
        Assert(observed.Rows.Count == 4 &&
            observed.NotSelectedCount == 1 &&
            observed.WithoutResultCount == 1 &&
            observed.HumanResultCount == 2 &&
            observed.Rows[0].State ==
                ProjectCriterionRetestProgressState.HumanResultDeclared &&
            observed.Rows[0].Outcome == ProjectTestOutcome.Passed &&
            observed.Rows[0].RelatedCriterionIds.Count == 2 &&
            observed.Rows[1].State ==
                ProjectCriterionRetestProgressState.SelectedWithoutResult &&
            observed.Rows[2].State ==
                ProjectCriterionRetestProgressState.NotSelected &&
            observed.Rows[3].State ==
                ProjectCriterionRetestProgressState.SourceMismatch &&
            observed.Rows[3].Outcome == ProjectTestOutcome.Blocked &&
            observed.Warnings.Any(warning => warning.Contains(
                "changé de chemin", StringComparison.Ordinal)) &&
            observed.Describe().Contains(
                "ne prouve PAS une requalification",
                StringComparison.Ordinal),
            "Follow-up counts only latest frozen selection/declared human " +
            "results and visibly flags a moved test source.");
        Assert(withFirst.Passages.Single().Results.Count == 2 &&
            withFirst.Passages.Single().Results.Single(item =>
                item.TestId == tests[0]).Outcome == ProjectTestOutcome.Passed,
            "The read-only projection cannot modify prior human result events.");

        // Latest passage was started after first, in a different version:
        // its unexecuted test never inherits the older human Passed.
        ProjectTestPassage second = new()
        {
            Id = Guid.NewGuid(),
            Number = 2,
            StartedUtc = initial.AddHours(1),
            StartedBy = "Exécutant fictif",
            Environment = "Recette synthétique",
            ApplicationVersion = "0.2",
            Tests =
            [
                Frozen(tests[0], 1, source),
                Frozen(tests[2], 3, source)
            ],
            Results = []
        };
        ProjectTestCampaign withSecond = campaign with
        {
            Passages = [first, second]
        };
        ProjectCriterionRetestProgressOverview next =
            ProjectCriterionRetestProgress.Build(
                baseline, proposal, withSecond);
        Assert(next.WithoutResultCount == 2 &&
            next.NotSelectedCount == 2 &&
            next.HumanResultCount == 0 &&
            next.Rows[0].Outcome is null &&
            next.Rows[0].PassageId == second.Id &&
            withSecond.Passages[0].Results[0].Outcome ==
                ProjectTestOutcome.Passed,
            "Latest unexecuted passage never inherits older Passed results, " +
            "even with the same test GUID.");

        ProjectCriterionBaselineOverview laterBaseline = baseline with
        {
            Snapshot = reference with
            {
                RecordedUtc = initial.AddHours(2)
            }
        };
        ProjectCriterionRetestProgressOverview outdated =
            ProjectCriterionRetestProgress.Build(
                laterBaseline, proposal, withSecond);
        Assert(outdated.Rows.All(row => row.State ==
                ProjectCriterionRetestProgressState.EarlierThanBaseline) &&
            outdated.NotSelectedCount == 4 &&
            outdated.HumanResultCount == 0 &&
            outdated.Warnings.Any(item => item.Contains(
                "antérieur à la référence CA", StringComparison.Ordinal)),
            "Passages earlier than the human baseline are not evidence " +
            "of replaying later CA modifications.");

        Reject(() => ProjectCriterionRetestProgress.Build(
            baseline, proposal, campaign with
            {
                ProjectId = Guid.NewGuid()
            }), "A foreign campaign may not reuse this project's CA baseline.");
        Reject(() => ProjectCriterionRetestProgress.Build(
            baseline, proposal with
            {
                BaselineSequence = 2
            }, withFirst), "Proposal from a different CA reference is stale.");
        Reject(() => ProjectCriterionRetestProgress.Build(
            new ProjectCriterionBaselineOverview(null, [], []),
            proposal, campaign), "A missing human baseline is never assumed.");
        Reject(() => ProjectCriterionRetestProgress.Build(
            baseline, proposal with
            {
                Candidates = [proposal.Candidates[0],
                    proposal.Candidates[0]]
            }, withFirst), "Duplicated test GUIDs must not inflate progress.");
        Reject(() => ProjectCriterionRetestProgress.Build(
            baseline, proposal with
            {
                Candidates =
                [
                    proposal.Candidates[0] with
                    {
                        CriterionIds = [criteria[0], criteria[0]]
                    }
                ]
            }, withFirst), "Duplicated related CA IDs must not inflate counts.");
        Reject(() => ProjectCriterionRetestProgress.Build(
            baseline, proposal, campaign with
            {
                Passages =
                [
                    first with
                    {
                        Results =
                        [
                            first.Results[0],
                            first.Results[0] with { Id = Guid.NewGuid() }
                        ]
                    }
                ]
            }), "Tampered REC outcomes are rejected before follow-up.");

        // Count an explicitly human-declared Failed as historic, never as a
        // completed CA qualification (the exact change time is unknown).
        ProjectTestPassage failed = second with
        {
            Results = [Result(tests[0], ProjectTestOutcome.Failed,
                initial.AddHours(1).AddMinutes(6))]
        };
        ProjectCriterionRetestProgressOverview failure =
            ProjectCriterionRetestProgress.Build(
                baseline, proposal, withSecond with
                {
                    Passages = [first, failed]
                });
        Assert(failure.HumanResultCount == 1 &&
            failure.Rows[0].Outcome == ProjectTestOutcome.Failed &&
            failure.Rows[0].State ==
                ProjectCriterionRetestProgressState.HumanResultDeclared &&
            failure.Describe().Contains(
                "n'est pas conservée", StringComparison.Ordinal) == false,
            "A failed human result is visible but does not complete CA review.");
    }

    /// <summary>Canonical frozen fictional Markdown definition.</summary>
    /// <param name="id">Stable test block ID.</param>
    /// <param name="number">Fake reference number.</param>
    /// <param name="path">Source at passage creation.</param>
    /// <returns>Valid standalone test section.</returns>
    private static ProjectTestFrozenDefinition Frozen(
        Guid id, int number, string path)
    {
        string code = "TEST-" + number.ToString("D4");
        string markdown = "## " + code + " — Fictif\n" +
            MarkdownBlockIdentityCodec.FormatMarker(id) +
            "\n### Attendu\n- Retour observable\n";
        return new ProjectTestFrozenDefinition
        {
            TestId = id,
            Reference = code,
            SourceRelativePath = path,
            HeadingLine = 5,
            Markdown = markdown,
            Sha256 = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(markdown)))
        };
    }

    /// <summary>Human-declared synthetic outcome.</summary>
    /// <param name="id">Frozen test GUID.</param>
    /// <param name="outcome">One declared verdict.</param>
    /// <param name="recorded">Declaration instant after passage start.</param>
    /// <returns>Historical synthetic result event.</returns>
    private static ProjectTestResultEvent Result(
        Guid id, ProjectTestOutcome outcome, DateTimeOffset recorded) => new()
    {
        Id = Guid.NewGuid(),
        TestId = id,
        Outcome = outcome,
        Actor = "Responsable fictif",
        Rationale = "Décision humaine de test fictif.",
        RecordedUtc = recorded
    };

    /// <summary>Requires one guarded action to throw.</summary>
    /// <param name="action">Unsafe action.</param>
    /// <param name="message">Failure message.</param>
    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Throws on one failed smoke invariant.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Reason.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
