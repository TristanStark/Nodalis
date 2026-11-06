namespace Nodalis.Core.Projects;

/// <summary>Mandatory human controls for acceptance by the operations team.</summary>
public enum ProjectDeploymentHandoverArea
{
    Documentation,
    Monitoring,
    BackupsAndRecovery,
    Security,
    SupportAndOwnership,
    RisksAndReservations,
    PostDeploymentTests
}

/// <summary>Human-observed control state; unknown is never inferred as passed.</summary>
public enum ProjectDeploymentHandoverStatus
{
    NotVerified,
    Confirmed,
    Reservation,
    NotApplicable
}

/// <summary>Explicit handover decision, separate from a deployment GO.</summary>
public enum ProjectDeploymentHandoverOutcome
{
    Accepted,
    AcceptedWithReservations,
    Refused,
    Deferred
}

/// <summary>
/// One user-declared operational check; optional source fingerprint is
/// evidence, not the result of a background system health check.
/// </summary>
public sealed record ProjectDeploymentHandoverCheck
{
    public required ProjectDeploymentHandoverArea Area { get; init; }
    public required ProjectDeploymentHandoverStatus Status { get; init; }
    public required string Note { get; init; }
    public ProjectGateEvidenceSnapshot? Evidence { get; init; }
}

/// <summary>
/// Append-only human transfer review, independently attributable to an
/// explicitly finished attempt. A later review never replaces a prior one.
/// </summary>
public sealed record ProjectDeploymentHandoverReview
{
    public required Guid Id { get; init; }
    public required Guid AttemptId { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public required string Actor { get; init; }
    public required string Recipient { get; init; }
    public required ProjectDeploymentHandoverOutcome Outcome { get; init; }
    public required string Rationale { get; init; }
    public string? Reservations { get; init; }
    public List<ProjectDeploymentHandoverCheck> Checks { get; init; } = [];
}

/// <summary>
/// Validates explicit project-local handovers and produces non-blocking
/// warnings. No review here authorizes deployment or closes another object.
/// </summary>
public static class ProjectDeploymentHandoverValidation
{
    /// <summary>Seven mandatory human control areas in a stable order.</summary>
    public static IReadOnlyList<ProjectDeploymentHandoverArea> RequiredAreas
    { get; } = Enum.GetValues<ProjectDeploymentHandoverArea>();

    /// <summary>Validates chronology and frozen references in the operation.</summary>
    /// <param name="operation">Untrusted canonical operation to validate.</param>
    public static void Validate(ProjectDeploymentOperation operation)
    {
        if (operation.HandoverReviews is null)
        {
            return;
        }

        if (operation.HandoverReviews.Count is < 1 or > 30)
        {
            throw new InvalidDataException("Nombre de revues de passation invalide.");
        }

        HashSet<Guid> ids = [];
        DateTimeOffset previous = operation.CreatedUtc;
        foreach (ProjectDeploymentHandoverReview review in operation.HandoverReviews)
        {
            if (review is null || review.Id == Guid.Empty ||
                !ids.Add(review.Id) || !Enum.IsDefined(review.Outcome) ||
                !Label(review.Actor, 160) || !Label(review.Recipient, 160) ||
                !Note(review.Rationale, 4000) ||
                review.Reservations is { Length: > 4000 } ||
                review.Reservations?.Contains('\0') == true ||
                review.RecordedUtc < previous)
            {
                throw new InvalidDataException(
                    "Revue de passation invalide, mal ordonnée ou dupliquée.");
            }

            int index = operation.Attempts.FindIndex(item =>
                item.Id == review.AttemptId);
            if (index < 0)
            {
                throw new InvalidDataException(
                    "Passation sans tentative MEP existante.");
            }

            ProjectDeploymentAttempt attempt = operation.Attempts[index];
            ProjectDeploymentAttemptEvent? last = operation.Events
                .LastOrDefault(item => item.AttemptId == attempt.Id);
            if (last is null ||
                !ProjectDeploymentMarkdownCodec.IsFinish(last.Kind) ||
                review.RecordedUtc < last.RecordedUtc ||
                index + 1 < operation.Attempts.Count &&
                    review.RecordedUtc > operation.Attempts[index + 1].StartedUtc ||
                operation.Closure is not null &&
                    review.RecordedUtc > operation.Closure.RecordedUtc)
            {
                throw new InvalidDataException(
                    "Passation avant fin de tentative ou après bilan.");
            }

            bool accepted = review.Outcome is
                ProjectDeploymentHandoverOutcome.Accepted or
                ProjectDeploymentHandoverOutcome.AcceptedWithReservations;
            if (accepted &&
                last.Kind != ProjectDeploymentEventKind.FinishedSucceeded)
            {
                throw new InvalidDataException(
                    "Tentative échouée : transfert accepté impossible.");
            }

            if (review.Outcome ==
                    ProjectDeploymentHandoverOutcome.AcceptedWithReservations &&
                !Note(review.Reservations, 4000))
            {
                throw new InvalidDataException(
                    "Acceptation avec réserves sans description des réserves.");
            }

            if (review.Checks is null ||
                review.Checks.Count != RequiredAreas.Count)
            {
                throw new InvalidDataException(
                    "Les sept contrôles de passation doivent être présentés.");
            }

            HashSet<ProjectDeploymentHandoverArea> areas = [];
            foreach (ProjectDeploymentHandoverCheck check in review.Checks)
            {
                if (check is null || !Enum.IsDefined(check.Area) ||
                    !Enum.IsDefined(check.Status) || !areas.Add(check.Area) ||
                    !Note(check.Note, 2000) ||
                    check.Evidence is not null &&
                        (!ProjectJourneySourcePath.IsSafe(
                            check.Evidence.RelativePath) ||
                         check.Evidence.Sha256 is not { Length: 64 } ||
                         !check.Evidence.Sha256.All(Uri.IsHexDigit)))
                {
                    throw new InvalidDataException(
                        "Contrôle de passation ambigu, non justifié ou hors workspace.");
                }
            }

            previous = review.RecordedUtc;
        }
    }

    /// <summary>
    /// Exposes all unresolved conditions without changing the human verdict.
    /// A matching result still does not constitute a deployment permission.
    /// </summary>
    /// <param name="review">The human-reviewed handover candidate.</param>
    /// <returns>Advisory warnings to display before confirmation.</returns>
    public static IReadOnlyList<string> Warnings(
        ProjectDeploymentHandoverReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        List<string> warnings = [];
        foreach (ProjectDeploymentHandoverCheck check in review.Checks)
        {
            if (check.Status is ProjectDeploymentHandoverStatus.NotVerified or
                ProjectDeploymentHandoverStatus.Reservation)
            {
                warnings.Add(check.Area + " : " + check.Status + " · " +
                    check.Note);
            }

            if (check.Status == ProjectDeploymentHandoverStatus.Confirmed &&
                check.Evidence is null)
            {
                warnings.Add(check.Area + " : aucune preuve liée.");
            }
        }

        if (review.Outcome == ProjectDeploymentHandoverOutcome.Accepted &&
            review.Checks.Any(item =>
                item.Status is not ProjectDeploymentHandoverStatus.Confirmed))
        {
            warnings.Add("Acceptation sans réserves malgré des contrôles " +
                "non confirmés ou déclarés non applicables.");
        }

        return warnings;
    }

    /// <summary>Checks a bounded, single-line human identity.</summary>
    /// <param name="value">Text to validate.</param>
    /// <param name="max">Maximum length.</param>
    /// <returns>True if valid.</returns>
    private static bool Label(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);

    /// <summary>Checks a bounded human note.</summary>
    /// <param name="value">Text to validate.</param>
    /// <param name="max">Maximum length.</param>
    /// <returns>True if valid.</returns>
    private static bool Note(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Contains('\0');
}
