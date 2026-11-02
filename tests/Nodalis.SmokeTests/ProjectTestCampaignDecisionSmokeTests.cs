using Nodalis.Core.Tests;

/// <summary>Pure human decision lifecycle and legacy REC compatibility tests.</summary>
internal static class ProjectTestCampaignDecisionSmokeTests
{
    /// <summary>Tests append-only decision chronology and nonblocking guidance.</summary>
    public static void Run()
    {
        DateTimeOffset created = DateTimeOffset.UtcNow.AddMinutes(-15);
        ProjectTestCampaign draft = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Reference = "REC-0001",
            Title = "Qualification fictive",
            CreatedUtc = created
        };
        string oldMarkdown = ProjectTestCampaignMarkdownCodec.Format(draft);
        Assert(!oldMarkdown.Contains("decisionHistory",
                StringComparison.Ordinal) &&
               ProjectTestCampaignMarkdownCodec.Parse(oldMarkdown)
                   .DecisionHistory is null,
            "Legacy v1 REC files must round-trip without implicit migration.");

        ProjectTestQualificationSummary withoutPassage =
            ProjectTestCampaignQualification.Review(draft, null);
        Assert(withoutPassage.Warnings.Any(item =>
                   item.Contains("Aucun passage", StringComparison.Ordinal)) &&
               withoutPassage.Warnings.Any(item =>
                   item.Contains("n'a pas été contrôlée",
                       StringComparison.Ordinal)),
            "No passage and unknown source validity must remain visible.");

        ProjectTestCampaignDecisionEvent review = new()
        {
            Id = Guid.NewGuid(),
            Kind = ProjectTestCampaignDecisionKind.EnterQualification,
            FromState = ProjectTestCampaignState.Draft,
            ToState = ProjectTestCampaignState.InQualification,
            Actor = "Responsable fictif",
            Rationale = "Dossier à examiner malgré les points ouverts.",
            RecordedUtc = created.AddMinutes(2)
        };
        ProjectTestCampaignDecisionEvent close = new()
        {
            Id = Guid.NewGuid(),
            Kind = ProjectTestCampaignDecisionKind.Close,
            FromState = ProjectTestCampaignState.InQualification,
            ToState = ProjectTestCampaignState.Closed,
            Actor = "Responsable fictif",
            Rationale = "Clôture avec réserves, aucune conformité implicite.",
            Verdict = ProjectTestCampaignVerdict.ValidatedWithReservations,
            Reservations = ["Requalifier les scénarios non exécutés."],
            RecordedUtc = created.AddMinutes(3)
        };
        ProjectTestCampaign closed = draft with
        {
            State = ProjectTestCampaignState.Closed,
            DecisionHistory = [review, close]
        };
        ProjectTestCampaign parsed = ProjectTestCampaignMarkdownCodec.Parse(
            ProjectTestCampaignMarkdownCodec.Format(closed));
        Assert(parsed.DecisionHistory is { Count: 2 } &&
               parsed.DecisionHistory[1].Verdict ==
                   ProjectTestCampaignVerdict.ValidatedWithReservations &&
               parsed.DecisionHistory[1].Reservations?.Count == 1 &&
               parsed.Passages.Count == 0,
            "A nonblocking, human-reserved closure must be portable and dated.");

        ProjectTestCampaignDecisionEvent reopen = new()
        {
            Id = Guid.NewGuid(),
            Kind = ProjectTestCampaignDecisionKind.Reopen,
            FromState = ProjectTestCampaignState.Closed,
            ToState = ProjectTestCampaignState.InProgress,
            Actor = "Responsable fictif",
            Rationale = "Nouvelle version à retester.",
            RecordedUtc = created.AddMinutes(4)
        };
        ProjectTestCampaign reopened = closed with
        {
            State = ProjectTestCampaignState.InProgress,
            DecisionHistory = [review, close, reopen]
        };
        Assert(ProjectTestCampaignMarkdownCodec.Parse(
                   ProjectTestCampaignMarkdownCodec.Format(reopened))
                   .DecisionHistory?.Count == 3,
            "Reopening must append rather than rewrite the prior verdict.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            closed with { DecisionHistory = [review] }),
            "A persisted Closed state must match the last decision.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            closed with
            {
                DecisionHistory = [review, close with
                {
                    Id = review.Id
                }]
            }),
            "Decision event GUID collisions must fail closed.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            closed with
            {
                DecisionHistory = [review, close with
                {
                    FromState = ProjectTestCampaignState.Draft
                }]
            }),
            "Noncontiguous transition history must fail closed.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            closed with
            {
                DecisionHistory = [review, close with { Verdict = null }]
            }),
            "A closure without a human verdict must be rejected.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            closed with
            {
                DecisionHistory = [review, close with
                {
                    Reservations = []
                }]
            }),
            "ValidatedWithReservations requires a recorded reservation.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            closed with
            {
                DecisionHistory = [review, close with
                {
                    RecordedUtc = created.AddMinutes(1)
                }]
            }),
            "Historical event time may not move backwards.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Validate(
            draft with { State = ProjectTestCampaignState.Closed }),
            "A campaign may not be marked Closed without an explicit event.");
        Reject(() => ProjectTestCampaignDecisionPolicy.Transition(
            ProjectTestCampaignDecisionKind.Close,
            ProjectTestCampaignState.Draft),
            "A campaign must enter qualification before closure.");
        Assert(ProjectTestCampaignDecisionPolicy.Transition(
                   ProjectTestCampaignDecisionKind.Reopen,
                   ProjectTestCampaignState.Canceled) ==
                   ProjectTestCampaignState.InProgress,
            "Explicitly canceled campaigns may be reopened with a trace.");
    }

    /// <summary>Asserts an invalid action is refused.</summary>
    /// <param name="action">Untrusted model or transition.</param>
    /// <param name="message">Synthetic failure.</param>
    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Throws a synthetic test diagnostic.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
