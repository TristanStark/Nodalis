using Nodalis.Core.Projects;

/// <summary>Regression of append-only MEP attempts and observed rollback.</summary>
internal static class ProjectDeploymentSmokeTests
{
    /// <summary>Checks canonical sources, snapshots and failed retry behavior.</summary>
    public static void Run()
    {
        ProjectDeploymentOperation initial =
            ProjectDeploymentEditor.Create(Guid.NewGuid(),
                "MEP-0001", "Livraison de validation");
        ProjectGateEvidenceSnapshot procedure = new()
        {
            RelativePath = "Exploitation/Procedure.md",
            Sha256 = new string('A', 64)
        };
        ProjectGateEvidenceSnapshot rollback = new()
        {
            RelativePath = "Exploitation/Retour.md",
            Sha256 = new string('B', 64)
        };
        DateTimeOffset started = initial.CreatedUtc.AddSeconds(1);
        ProjectDeploymentAttempt first = new()
        {
            Id = Guid.NewGuid(),
            Number = 1,
            StartedUtc = started,
            StartedBy = "Responsable",
            Rationale = "Première tentative validée manuellement.",
            ObservedVersionBefore = "1.9.0",
            TargetVersion = "2.0.0",
            Environment = "PROD",
            Scope = "Application pilote",
            Procedure = procedure,
            RollbackProcedure = rollback,
            GateDecisionId = Guid.NewGuid(),
            AuthorizationReviewedBy = "Responsable",
            AuthorizationRationale = "Gate et périmètre revus pour la tentative."
        };
        ProjectDeploymentOperation one =
            ProjectDeploymentEditor.StartAttempt(initial, first);
        MustReject(() => ProjectDeploymentEditor.StartAttempt(one,
            first with { Id = Guid.NewGuid(), Number = 2,
                StartedUtc = started.AddSeconds(5) }),
            "Cannot open attempt two while first has no final human event.");

        ProjectDeploymentOperation requested = Add(one, first.Id,
            ProjectDeploymentEventKind.RollbackRequested, 1);
        MustReject(() => Add(requested, first.Id,
            ProjectDeploymentEventKind.FinishedSucceeded, 2,
            observed: "2.0.0"),
            "Rollback requested must never be treated as a successful MEP.");
        MustReject(() => Add(requested, first.Id,
            ProjectDeploymentEventKind.RollbackObserved, 2,
            observed: "1.9.0",
            rollback: ProjectDeploymentRollbackOutcome.Succeeded),
            "Rollback cannot be confirmed without a start.");
        ProjectDeploymentOperation executing = Add(requested, first.Id,
            ProjectDeploymentEventKind.RollbackStarted, 2);
        ProjectDeploymentOperation observedPartial = Add(executing, first.Id,
            ProjectDeploymentEventKind.RollbackObserved, 3,
            observed: "1.9.5",
            rollback: ProjectDeploymentRollbackOutcome.Partial);
        ProjectDeploymentOperation failed = Add(observedPartial, first.Id,
            ProjectDeploymentEventKind.FinishedFailed, 4,
            observed: "1.9.5");
        ProjectDeploymentAttemptState state =
            ProjectDeploymentMarkdownCodec.GetAttemptState(failed, first.Id);
        Check(state.IsFinished && state.RollbackRequested &&
            state.RollbackStarted &&
            state.RollbackObserved == ProjectDeploymentRollbackOutcome.Partial &&
            state.LastEventKind == ProjectDeploymentEventKind.FinishedFailed,
            "Partial rollback stays distinct from MEP success.");

        ProjectDeploymentAttempt second = first with
        {
            Id = Guid.NewGuid(),
            Number = 2,
            StartedUtc = started.AddSeconds(6),
            ObservedVersionBefore = "1.9.5",
            GateDecisionId = Guid.NewGuid(),
            AuthorizationRationale = "Nouvelle revue expresse des conditions."
        };
        ProjectDeploymentOperation retry =
            ProjectDeploymentEditor.StartAttempt(failed, second);
        Check(retry.Attempts.Count == 2 &&
            retry.Attempts[0].ObservedVersionBefore == "1.9.0" &&
            retry.Attempts[1].ObservedVersionBefore == "1.9.5" &&
            retry.Attempts[0].GateDecisionId != retry.Attempts[1].GateDecisionId,
            "Retry must preserve first snapshot and declare real observed state.");

        MustReject(() => Add(retry, first.Id,
            ProjectDeploymentEventKind.ActionLogged, 7),
            "Closed attempt is immutable.");
        MustReject(() => ProjectDeploymentEditor.StartAttempt(failed,
            second with { ObservedVersionBefore = string.Empty }),
            "Blank observed version is never guessed after rollback.");
        MustReject(() => ProjectDeploymentEditor.StartAttempt(failed,
            second with { RollbackProcedure = rollback with
                { RelativePath = "../outside.md" } }),
            "Source references cannot escape the workspace.");
        MustReject(() => ProjectDeploymentEditor.StartAttempt(failed,
            second with { GateDecisionId = Guid.Empty }),
            "A reference to the human gate decision is mandatory.");
        MustReject(() => ProjectDeploymentEditor.StartAttempt(failed,
            second with { Number = 1 }),
            "Attempt numbers are not reusable.");

        ProjectDeploymentOperation success = Add(retry, second.Id,
            ProjectDeploymentEventKind.FinishedSucceeded, 7,
            observed: "2.0.0");
        string markdown = ProjectDeploymentMarkdownCodec.Format(success);
        ProjectDeploymentOperation reread =
            ProjectDeploymentMarkdownCodec.Parse(markdown);
        Check(reread.Attempts.Count == 2 && reread.Events.Count == 5 &&
            reread.Attempts[0].TargetVersion == "2.0.0" &&
            reread.Events[3].ObservedVersionAfter == "1.9.5" &&
            reread.Events[^1].Kind ==
                ProjectDeploymentEventKind.FinishedSucceeded,
            "Canonical MEP source must retain both complete attempts.");
        MustReject(() => ProjectDeploymentMarkdownCodec.Parse(
            markdown.Replace("~~~json", "~~~json\n  ",
                StringComparison.Ordinal)),
            "Noncanonical payload changes must be refused.");
        MustReject(() => ProjectDeploymentMarkdownCodec.Parse(
            markdown.Replace("<!-- nodalis:deployment-operation:v1 -->",
                "<!-- nodalis:deployment-operation:v1 -->\n\n" +
                "<!-- nodalis:deployment-operation:v1 -->",
                StringComparison.Ordinal)),
            "Duplicate markers are invalid.");
        MustReject(() => ProjectDeploymentMarkdownCodec.Validate(
            success with { Events = success.Events
                .Skip(1).ToList() }),
            "Historical rollback cannot be silently deleted.");
        MustReject(() => ProjectDeploymentMarkdownCodec.Validate(
            success with { ProjectId = Guid.Empty }),
            "Unowned MEPs are invalid.");
    }

    /// <summary>Produces a human event one or more seconds into attempt one.</summary>
    /// <param name="operation">Existing ledger.</param>
    /// <param name="attemptId">Target attempt.</param>
    /// <param name="second">Offset from first attempt start.</param>
    /// <param name="kind">Explicit action.</param>
    /// <param name="observed">Real version, when recorded.</param>
    /// <param name="rollback">Observed rollback result, if any.</param>
    /// <returns>Updated immutable ledger.</returns>
    private static ProjectDeploymentOperation Add(
        ProjectDeploymentOperation operation, Guid attemptId,
        ProjectDeploymentEventKind kind, int second,
        string? observed = null,
        ProjectDeploymentRollbackOutcome? rollback = null)
    {
        DateTimeOffset origin = operation.Attempts[0].StartedUtc;
        return ProjectDeploymentEditor.AppendEvent(operation,
            new ProjectDeploymentAttemptEvent
            {
                Id = Guid.NewGuid(),
                AttemptId = attemptId,
                Kind = kind,
                Actor = "Opérateur",
                Rationale = "Action confirmée par la personne responsable.",
                RecordedUtc = origin.AddSeconds(second),
                ObservedVersionAfter = observed,
                RollbackOutcome = rollback
            });
    }

    /// <summary>Checks one expected invalid operation or source.</summary>
    /// <param name="action">Operation that must fail safely.</param>
    /// <param name="context">Diagnostic.</param>
    private static void MustReject(Action action, string context)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(context);
    }

    /// <summary>Asserts a deterministic business invariant.</summary>
    /// <param name="condition">Test predicate.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
