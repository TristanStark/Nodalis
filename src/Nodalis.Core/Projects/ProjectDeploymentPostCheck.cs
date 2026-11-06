namespace Nodalis.Core.Projects;

/// <summary>
/// Human assessment of one frozen REC passage after a MEP. The result counts
/// are evidence, never an automatic decision or incident status.
/// </summary>
public enum ProjectDeploymentPostCheckVerdict
{
    ToReview,
    Conform,
    ConformWithReservations,
    NonConform
}

/// <summary>
/// Append-only human link from a finished deployment attempt to a single
/// project-owned REC passage and optionally linked maintenance incidents.
/// </summary>
public sealed record ProjectDeploymentPostCheck
{
    public required Guid Id { get; init; }
    public required Guid AttemptId { get; init; }
    public required Guid CampaignId { get; init; }
    public required Guid PassageId { get; init; }
    public required string CampaignSha256 { get; init; }
    public required int FrozenTests { get; init; }
    public required int Passed { get; init; }
    public required int Failed { get; init; }
    public required int Blocked { get; init; }
    public required int NotApplicable { get; init; }
    public IReadOnlyList<string> IncidentIds { get; init; } = [];
    public required DateTimeOffset RecordedUtc { get; init; }
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
    public required ProjectDeploymentPostCheckVerdict Verdict { get; init; }
    public string? Reservations { get; init; }
}

/// <summary>
/// Validates one optional, historical post-MEP test/incident audit stream.
/// All campaign and incident ownership checks happen again at persistence.
/// </summary>
public static class ProjectDeploymentPostCheckPolicy
{
    /// <summary>Validates append-only test and incident references and order.</summary>
    /// <param name="operation">Untrusted canonical MEP source.</param>
    public static void Validate(ProjectDeploymentOperation operation)
    {
        if (operation.PostChecks is null)
        {
            return;
        }

        if (operation.PostChecks.Count is < 1 or > 150)
        {
            throw new InvalidDataException("Historique post-MEP trop volumineux.");
        }

        HashSet<Guid> identifiers = [];
        HashSet<(Guid AttemptId, Guid PassageId, string Sha256)> snapshots = [];
        DateTimeOffset previous = operation.CreatedUtc;
        foreach (ProjectDeploymentPostCheck check in operation.PostChecks)
        {
            if (check is null || check.Id == Guid.Empty ||
                !identifiers.Add(check.Id) ||
                check.AttemptId == Guid.Empty || check.CampaignId == Guid.Empty ||
                check.PassageId == Guid.Empty ||
                check.CampaignSha256 is not { Length: 64 } ||
                !check.CampaignSha256.All(Uri.IsHexDigit) ||
                !snapshots.Add((check.AttemptId, check.PassageId,
                    check.CampaignSha256.ToUpperInvariant())) ||
                check.FrozenTests is < 1 or > 500 ||
                check.Passed < 0 || check.Failed < 0 || check.Blocked < 0 ||
                check.NotApplicable < 0 ||
                (long)check.Passed + check.Failed + check.Blocked +
                    check.NotApplicable > check.FrozenTests ||
                check.IncidentIds is null || check.IncidentIds.Count > 50 ||
                check.IncidentIds.Any(item => !ProjectGateValidation.IsIncidentId(item)) ||
                check.IncidentIds.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != check.IncidentIds.Count ||
                !Label(check.Actor, 160) || !Note(check.Rationale, 4000) ||
                !Enum.IsDefined(check.Verdict) ||
                check.Reservations is { Length: > 4000 } ||
                check.Reservations?.Contains('\0') == true ||
                check.RecordedUtc < previous)
            {
                throw new InvalidDataException(
                    "Contrôle post-MEP non canonique, doublonné ou non justifié.");
            }

            int attemptIndex = operation.Attempts.FindIndex(item =>
                item.Id == check.AttemptId);
            if (attemptIndex < 0)
            {
                throw new InvalidDataException(
                    "Contrôle post-MEP référencé hors des tentatives connues.");
            }

            ProjectDeploymentAttemptEvent? terminal = operation.Events
                .LastOrDefault(item => item.AttemptId == check.AttemptId);
            if (terminal is null ||
                !ProjectDeploymentMarkdownCodec.IsFinish(terminal.Kind) ||
                check.RecordedUtc < terminal.RecordedUtc ||
                attemptIndex + 1 < operation.Attempts.Count &&
                    check.RecordedUtc > operation.Attempts[attemptIndex + 1]
                        .StartedUtc ||
                operation.Closure is not null &&
                    check.RecordedUtc > operation.Closure.RecordedUtc)
            {
                throw new InvalidDataException(
                    "Contrôle post-MEP hors de la fenêtre historique de la tentative.");
            }

            if (check.Verdict ==
                    ProjectDeploymentPostCheckVerdict.ConformWithReservations &&
                !Note(check.Reservations, 4000))
            {
                throw new InvalidDataException(
                    "Réserves obligatoires pour un verdict avec réserves.");
            }

            previous = check.RecordedUtc;
        }
    }

    /// <summary>
    /// Lists warning signals for a human decision. No automatic revalidation
    /// and no incident closure follow from a conforming verdict.
    /// </summary>
    /// <param name="check">One explicit human-linked passage.</param>
    /// <returns>Non-blocking warnings and unknowns.</returns>
    public static IReadOnlyList<string> Warnings(ProjectDeploymentPostCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        List<string> warnings = [];
        int notRun = check.FrozenTests - check.Passed - check.Failed -
            check.Blocked - check.NotApplicable;
        if (notRun > 0)
        {
            warnings.Add(notRun + " test(s) non exécuté(s) au passage lié.");
        }

        if (check.Failed > 0 || check.Blocked > 0)
        {
            warnings.Add(check.Failed + " non conforme(s), " +
                check.Blocked + " bloqué(s) dans ce passage.");
        }

        if (check.NotApplicable > 0)
        {
            warnings.Add(check.NotApplicable +
                " test(s) déclarés non applicables par un humain.");
        }

        if (check.IncidentIds.Count > 0)
        {
            warnings.Add(check.IncidentIds.Count +
                " incident(s) liés : résolution et pertinence à vérifier " +
                "dans les fiches de maintenance.");
        }

        warnings.Add("Résultats figés à la révision REC référencée ; " +
            "vérifier les corrections et passages plus récents.");
        return warnings;
    }

    /// <summary>Checks one human identity.</summary>
    /// <param name="value">Untrusted label.</param>
    /// <param name="max">Maximum characters.</param>
    /// <returns>Whether valid.</returns>
    private static bool Label(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);

    /// <summary>Checks one non-empty human rationale.</summary>
    /// <param name="value">Untrusted description.</param>
    /// <param name="max">Maximum characters.</param>
    /// <returns>Whether valid.</returns>
    private static bool Note(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Contains('\0');
}
