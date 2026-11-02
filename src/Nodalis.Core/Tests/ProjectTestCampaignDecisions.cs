namespace Nodalis.Core.Tests;

/// <summary>
/// Decisions are explicit human actions, independent of test executions.
/// </summary>
public enum ProjectTestCampaignDecisionKind
{
    EnterQualification,
    Close,
    Reopen,
    Cancel
}

/// <summary>Human campaign verdict, not an inferred test conformity status.</summary>
public enum ProjectTestCampaignVerdict
{
    Validated,
    ValidatedWithReservations,
    NotValidated
}

/// <summary>
/// Append-only human lifecycle event. Verdict and reservations only apply to
/// closure, never to an individual test or an EXI acceptance criterion.
/// </summary>
public sealed record ProjectTestCampaignDecisionEvent
{
    /// <summary>Unique event identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Explicit lifecycle action.</summary>
    public required ProjectTestCampaignDecisionKind Kind { get; init; }

    /// <summary>Reviewed state prior to the human action.</summary>
    public required ProjectTestCampaignState FromState { get; init; }

    /// <summary>State explicitly approved by the human.</summary>
    public required ProjectTestCampaignState ToState { get; init; }

    /// <summary>Human author or reviewer.</summary>
    public required string Actor { get; init; }

    /// <summary>Required explicit justification, including for reopening.</summary>
    public required string Rationale { get; init; }

    /// <summary>Optional closure verdict. Null for other actions.</summary>
    public ProjectTestCampaignVerdict? Verdict { get; init; }

    /// <summary>Human-written reservations. No linked incident is inferred.</summary>
    public IReadOnlyList<string>? Reservations { get; init; }

    /// <summary>Human action timestamp.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Validation of event ordering, state transitions and human declaration.
/// No result or source SHA can automatically close a campaign.
/// </summary>
public static class ProjectTestCampaignDecisionPolicy
{
    /// <summary>Checks a strictly append-only sequence and current state.</summary>
    /// <param name="events">Optional history, null for legacy v1 REC.</param>
    /// <param name="state">Persisted campaign lifecycle state.</param>
    /// <param name="createdUtc">Campaign creation time.</param>
    public static void Validate(
        IReadOnlyList<ProjectTestCampaignDecisionEvent>? events,
        ProjectTestCampaignState state, DateTimeOffset createdUtc)
    {
        if (events is null || events.Count == 0)
        {
            if (state is ProjectTestCampaignState.InQualification or
                ProjectTestCampaignState.Closed or
                ProjectTestCampaignState.Canceled)
            {
                throw new InvalidDataException(
                    "État REC final sans décision humaine historisée.");
            }

            return;
        }

        if (events.Count > 1000)
        {
            throw new InvalidDataException(
                "Historique REC trop volumineux.");
        }

        HashSet<Guid> identifiers = [];
        ProjectTestCampaignState? previous = null;
        DateTimeOffset last = createdUtc;
        foreach (ProjectTestCampaignDecisionEvent decision in events)
        {
            if (decision is null ||
                decision.Id == Guid.Empty ||
                !identifiers.Add(decision.Id) ||
                decision.RecordedUtc < last ||
                decision.Actor is not { Length: > 0 and <= 160 } ||
                string.IsNullOrWhiteSpace(decision.Actor) ||
                decision.Actor.Any(char.IsControl) ||
                decision.Rationale is not { Length: > 0 and <= 2000 } ||
                string.IsNullOrWhiteSpace(decision.Rationale) ||
                decision.Rationale.Any(char.IsControl) ||
                !Enum.IsDefined(decision.Kind) ||
                !Enum.IsDefined(decision.FromState) ||
                !Enum.IsDefined(decision.ToState))
            {
                throw new InvalidDataException(
                    "Décision de campagne invalide, doublonnée ou antidatée.");
            }

            if (previous.HasValue && decision.FromState != previous.Value)
            {
                throw new InvalidDataException(
                    "Chaîne de décisions REC discontinue.");
            }

            if (previous is null && decision.FromState is not
                (ProjectTestCampaignState.Draft or
                 ProjectTestCampaignState.InProgress))
            {
                throw new InvalidDataException(
                    "Première décision REC sans état initial admissible.");
            }

            ProjectTestCampaignState expectedTarget;
            try
            {
                expectedTarget = Transition(decision.Kind, decision.FromState);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(
                    "Transition REC non autorisée.", exception);
            }

            if (expectedTarget != decision.ToState)
            {
                throw new InvalidDataException(
                    "État final de décision REC incohérent.");
            }

            bool closing = decision.Kind ==
                ProjectTestCampaignDecisionKind.Close;
            if (closing != decision.Verdict.HasValue ||
                decision.Verdict.HasValue &&
                    !Enum.IsDefined(decision.Verdict.Value) ||
                decision.Reservations is { Count: > 50 } ||
                decision.Reservations?.Any(reservation =>
                    string.IsNullOrWhiteSpace(reservation) ||
                    reservation.Length > 500 ||
                    reservation.Any(char.IsControl)) == true ||
                !closing && decision.Reservations is { Count: > 0 } ||
                closing && decision.Verdict ==
                    ProjectTestCampaignVerdict.ValidatedWithReservations &&
                    decision.Reservations is not { Count: > 0 } ||
                closing && decision.Verdict ==
                    ProjectTestCampaignVerdict.Validated &&
                    decision.Reservations is { Count: > 0 })
            {
                throw new InvalidDataException(
                    "Verdict ou réserves de campagne non conformes.");
            }

            last = decision.RecordedUtc;
            previous = decision.ToState;
        }

        if (previous != state)
        {
            throw new InvalidDataException(
                "État de campagne différent de sa dernière décision.");
        }
    }

    /// <summary>Returns the only legal destination for a human action.</summary>
    /// <param name="kind">Requested explicit action.</param>
    /// <param name="current">Current persisted state.</param>
    /// <returns>Permitted destination.</returns>
    public static ProjectTestCampaignState Transition(
        ProjectTestCampaignDecisionKind kind,
        ProjectTestCampaignState current)
    {
        return (kind, current) switch
        {
            (ProjectTestCampaignDecisionKind.EnterQualification,
                ProjectTestCampaignState.Draft or
                ProjectTestCampaignState.InProgress) =>
                ProjectTestCampaignState.InQualification,
            (ProjectTestCampaignDecisionKind.Close,
                ProjectTestCampaignState.InQualification) =>
                ProjectTestCampaignState.Closed,
            (ProjectTestCampaignDecisionKind.Reopen,
                ProjectTestCampaignState.Closed or
                ProjectTestCampaignState.Canceled) =>
                ProjectTestCampaignState.InProgress,
            (ProjectTestCampaignDecisionKind.Cancel,
                ProjectTestCampaignState.Draft or
                ProjectTestCampaignState.InProgress or
                ProjectTestCampaignState.InQualification) =>
                ProjectTestCampaignState.Canceled,
            _ => throw new InvalidOperationException(
                "Transition de campagne interdite : action humaine requise.")
        };
    }
}

/// <summary>Warnings for one human-guided qualification, never blockers.</summary>
/// <param name="Warnings">Observed cautions and unknowns, read-only.</param>
/// <param name="NotRun">Unexecuted tests in the latest passage.</param>
/// <param name="Failed">Explicitly failed tests in the latest passage.</param>
/// <param name="Blocked">Explicitly blocked tests in the latest passage.</param>
/// <param name="NotApplicable">Explicitly non-applicable tests.</param>
/// <param name="SourceNeedsReview">Changed, missing or ambiguous latest definitions.</param>
public sealed record ProjectTestQualificationSummary(
    IReadOnlyList<string> Warnings, int NotRun, int Failed, int Blocked,
    int NotApplicable, int SourceNeedsReview);

/// <summary>
/// Produces conservative closure guidance. The user may close with a required
/// rationale despite the warnings; no acceptance criterion status is changed.
/// </summary>
public static class ProjectTestCampaignQualification
{
    /// <summary>Reviews latest passage results without blending test versions.</summary>
    /// <param name="campaign">Already validated REC.</param>
    /// <param name="needsSourceReview">Number of uncertain current sources, or null.</param>
    /// <returns>Human-readable warnings with exact latest-passage counts.</returns>
    public static ProjectTestQualificationSummary Review(
        ProjectTestCampaign campaign, int? needsSourceReview)
    {
        ProjectTestCampaignMarkdownCodec.Validate(campaign);
        List<string> warnings = [];
        int notRun = 0;
        int failed = 0;
        int blocked = 0;
        int notApplicable = 0;
        if (campaign.Passages.Count == 0)
        {
            warnings.Add("Aucun passage de tests enregistré.");
        }
        else
        {
            ProjectTestPassage latest = campaign.Passages[^1];
            notRun = latest.Tests.Count - latest.Results.Count;
            failed = latest.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.Failed);
            blocked = latest.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.Blocked);
            notApplicable = latest.Results.Count(item =>
                item.Outcome == ProjectTestOutcome.NotApplicable);
            if (notRun > 0)
            {
                warnings.Add(notRun + " test(s) non exécuté(s) au dernier passage.");
            }

            if (failed > 0 || blocked > 0)
            {
                warnings.Add(failed + " non conforme(s), " +
                    blocked + " bloqué(s) au dernier passage.");
            }

            if (notApplicable > 0)
            {
                warnings.Add(notApplicable +
                    " test(s) non applicable(s) : justification à vérifier.");
            }
        }

        if (!needsSourceReview.HasValue)
        {
            warnings.Add(
                "La validité des définitions actuelles n'a pas été contrôlée.");
        }
        else if (needsSourceReview.Value > 0)
        {
            warnings.Add(needsSourceReview.Value +
                " définition(s) modifiée(s), absente(s) ou ambiguë(s).");
        }

        warnings.Add(
            "Les preuves et incidents n'ont pas été qualifiés dans ce contrôle.");
        return new ProjectTestQualificationSummary(
            warnings, notRun, failed, blocked, notApplicable,
            needsSourceReview ?? -1);
    }
}
