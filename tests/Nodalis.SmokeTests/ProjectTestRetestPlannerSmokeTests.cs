using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Markdown;
using Nodalis.Core.Tests;

/// <summary>
/// Synthetic tests for explicitly reviewed, latest-passage-only REC replays.
/// </summary>
internal static class ProjectTestRetestPlannerSmokeTests
{
    /// <summary>Tests failure, block, NotRun, changed source and context boundaries.</summary>
    public static void Run()
    {
        Guid[] ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid())
            .ToArray();
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(-3);
        ProjectTestFrozenDefinition[] definitions = ids.Select((id, i) =>
            Frozen(id, i + 1)).ToArray();
        ProjectTestResultEvent[] results =
        [
            Result(ids[0], ProjectTestOutcome.Passed, start),
            Result(ids[1], ProjectTestOutcome.Failed, start),
            Result(ids[2], ProjectTestOutcome.Blocked, start),
            Result(ids[4], ProjectTestOutcome.NotApplicable, start)
        ];
        ProjectTestPassage first = new()
        {
            Id = Guid.NewGuid(),
            Number = 1,
            Environment = "REC-longue",
            ApplicationVersion = "1.0",
            StartedBy = "Responsable fictif",
            StartedUtc = start,
            Tests = definitions,
            Results = results
        };
        ProjectTestCampaign campaign = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Reference = "REC-0001",
            Title = "Reprises fictives",
            CreatedUtc = start.AddMinutes(-5),
            State = ProjectTestCampaignState.InProgress,
            Passages = [first]
        };

        ProjectTestRetestProposal notChecked =
            ProjectTestRetestPlanner.Propose(campaign, null);
        Assert(notChecked.PassageId == first.Id &&
               notChecked.Candidates.Select(item => item.TestId)
                   .SequenceEqual(new[] { ids[1], ids[2], ids[3] }) &&
               notChecked.Candidates[0].Reasons.Single() ==
                   ProjectTestRetestReason.Failed &&
               notChecked.Candidates[1].Reasons.Single() ==
                   ProjectTestRetestReason.Blocked &&
               notChecked.Candidates[2].Reasons.Single() ==
                   ProjectTestRetestReason.NotRun &&
               notChecked.Warnings.Any(item =>
                   item.Contains("non", StringComparison.OrdinalIgnoreCase)),
            "Unknown source status cannot imply earlier Passed tests need " +
            "or do not need current verification.");

        ProjectTestRetestProposal checkedSources =
            ProjectTestRetestPlanner.Propose(campaign,
                new HashSet<Guid> { ids[0], ids[4] });
        Assert(checkedSources.Candidates.Count == 5 &&
               checkedSources.Candidates[0].Reasons.Single() ==
                   ProjectTestRetestReason.SourceNeedsReview &&
               checkedSources.Candidates[4].Reasons.Single() ==
                   ProjectTestRetestReason.SourceNeedsReview &&
               !checkedSources.Warnings.Any(item =>
                   item.Contains("non contrôlées", StringComparison.Ordinal)),
            "Explicitly changed or uncertain sources need review even " +
            "when a historic verdict was Passed or NotApplicable.");

        ProjectTestRetestProposal allCurrent =
            ProjectTestRetestPlanner.Propose(campaign, new HashSet<Guid>());
        Assert(allCurrent.Candidates.Count == 3,
            "A verified-current passing test must not be preselected.");

        ProjectTestPassage second = first with
        {
            Id = Guid.NewGuid(),
            Number = 2,
            StartedUtc = start.AddHours(2),
            ApplicationVersion = "1.1",
            Results = []
        };
        ProjectTestCampaign ongoing = campaign with
        {
            Passages = [first, second]
        };
        ProjectTestRetestProposal latest =
            ProjectTestRetestPlanner.Propose(ongoing,
                new HashSet<Guid>());
        Assert(latest.PassageId == second.Id &&
               latest.Candidates.Count == 5 &&
               latest.Candidates.All(item =>
                   item.Reasons.SequenceEqual(
                       new[] { ProjectTestRetestReason.NotRun })) &&
               campaign.Passages[0].Results[0].Outcome ==
                   ProjectTestOutcome.Passed,
            "An unexecuted new passage must not inherit an older success.");

        ProjectTestRetestProposal empty =
            ProjectTestRetestPlanner.Propose(campaign with
            {
                Passages = []
            }, null);
        Assert(empty.PassageId is null &&
               empty.Candidates.Count == 0 &&
               empty.Warnings.Count > 0,
            "Without a passage no automatic replay set can be inferred.");
    }

    /// <summary>Creates a canonical fictional test section snapshot.</summary>
    /// <param name="id">Stable test GUID.</param>
    /// <param name="number">Human readable fake test reference.</param>
    /// <returns>Valid frozen definition.</returns>
    private static ProjectTestFrozenDefinition Frozen(Guid id, int number)
    {
        string reference = "TEST-" + number.ToString("D4");
        string markdown = "## " + reference + " — Test fictif\n" +
            MarkdownBlockIdentityCodec.FormatMarker(id) +
            "\n### Attendu\n- Réponse " + number + "\n";
        return new ProjectTestFrozenDefinition
        {
            TestId = id,
            Reference = reference,
            SourceRelativePath =
                "Applications/Exemple/Projets/Projet-A/Tests/Cahier.md",
            HeadingLine = number * 8,
            Markdown = markdown,
            Sha256 = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(markdown)))
        };
    }

    /// <summary>Creates an explicit synthetic human result.</summary>
    /// <param name="id">Frozen test ID.</param>
    /// <param name="outcome">Declared verdict.</param>
    /// <param name="started">Passage start time.</param>
    /// <returns>One valid human outcome event.</returns>
    private static ProjectTestResultEvent Result(
        Guid id, ProjectTestOutcome outcome, DateTimeOffset started) => new()
    {
        Id = Guid.NewGuid(),
        TestId = id,
        Outcome = outcome,
        Actor = "Exécutant fictif",
        Rationale = "Décision manuelle fictive.",
        RecordedUtc = started.AddMinutes(5)
    };

    /// <summary>Fails only on a synthetic invariant.</summary>
    /// <param name="condition">Expected behavior.</param>
    /// <param name="message">Safe failure description.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
