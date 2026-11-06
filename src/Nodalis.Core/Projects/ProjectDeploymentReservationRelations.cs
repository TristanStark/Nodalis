namespace Nodalis.Core.Projects;

/// <summary>Stable relation target; task and milestone IDs never mean the same kind.</summary>
public enum ProjectDeploymentReservationRelationKind
{
    Task,
    Milestone
}

/// <summary>Human-made link and unlink decisions; no automatic synchronization.</summary>
public enum ProjectDeploymentReservationRelationAction
{
    Link,
    Unlink
}

/// <summary>
/// One append-only explicit relation to an existing stable project object.
/// Historical source paths are navigation hints, not authoritative identities.
/// </summary>
public sealed record ProjectDeploymentReservationRelationEvent
{
    public required Guid Id { get; init; }
    public required Guid ReservationId { get; init; }
    public required ProjectDeploymentReservationRelationKind Kind { get; init; }
    public required Guid TargetId { get; init; }
    public required string SourceRelativePath { get; init; }
    public required ProjectDeploymentReservationRelationAction Action { get; init; }
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// One currently linked task or milestone with its last human-attested source.
/// </summary>
/// <param name="Kind">Link target category.</param>
/// <param name="TargetId">Persistent ID of task/milestone.</param>
/// <param name="SourceRelativePath">Human-readable historical source hint.</param>
public sealed record ProjectDeploymentReservationActiveLink(
    ProjectDeploymentReservationRelationKind Kind, Guid TargetId,
    string SourceRelativePath);

/// <summary>
/// Validates and projects relations without reading or altering task or
/// milestone files. A missing target may always be explicitly unlinked.
/// </summary>
public static class ProjectDeploymentReservationRelations
{
    /// <summary>Validates the complete optional relation audit history.</summary>
    /// <param name="ledger">Canonical reservation source.</param>
    public static void Validate(ProjectDeploymentReservationLedger ledger)
    {
        IReadOnlyList<ProjectDeploymentReservationRelationEvent>? events =
            ledger.Relations;
        if (events is null)
        {
            return;
        }

        if (events.Count is < 1 or > 5000)
        {
            throw new InvalidDataException(
                "Journal de liens réserve/tâche/jalon trop volumineux.");
        }

        Dictionary<Guid, ProjectDeploymentReservation> items =
            ledger.Items.ToDictionary(item => item.Id);
        HashSet<Guid> ids = ledger.Items.Select(item => item.Id)
            .Concat(ledger.Events.Select(item => item.Id)).ToHashSet();
        HashSet<(Guid ReservationId,
            ProjectDeploymentReservationRelationKind Kind, Guid TargetId)>
            active = [];
        DateTimeOffset last = DateTimeOffset.MinValue;
        foreach (ProjectDeploymentReservationRelationEvent link in events)
        {
            if (link is null || link.Id == Guid.Empty ||
                !ids.Add(link.Id) || link.TargetId == Guid.Empty ||
                !items.TryGetValue(link.ReservationId,
                    out ProjectDeploymentReservation? reservation) ||
                !Enum.IsDefined(link.Kind) || !Enum.IsDefined(link.Action) ||
                !ProjectJourneySourcePath.IsSafe(link.SourceRelativePath) ||
                string.IsNullOrWhiteSpace(link.Actor) ||
                link.Actor.Length > 160 || link.Actor.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(link.Rationale) ||
                link.Rationale.Length > 4000 ||
                link.Rationale.Contains('\0') ||
                link.RecordedUtc < reservation.CreatedUtc ||
                link.RecordedUtc < last)
            {
                throw new InvalidDataException(
                    "Lien de réserve non canonique ou antidaté.");
            }

            (Guid ReservationId, ProjectDeploymentReservationRelationKind Kind,
                Guid TargetId) key =
                (link.ReservationId, link.Kind, link.TargetId);
            if (link.Action == ProjectDeploymentReservationRelationAction.Link)
            {
                if (!active.Add(key))
                {
                    throw new InvalidDataException(
                        "Lien déjà actif pour cette réserve.");
                }
            }
            else if (!active.Remove(key))
            {
                throw new InvalidDataException(
                    "Dissociation d'un lien non actif.");
            }

            last = link.RecordedUtc;
        }
    }

    /// <summary>
    /// Returns currently linked identities from the canonical event history.
    /// History remains available for display, including deleted sources.
    /// </summary>
    /// <param name="ledger">Canonical reservation register.</param>
    /// <param name="reservationId">Stable reservation GUID.</param>
    /// <returns>Currently linked task/milestone targets.</returns>
    public static IReadOnlyList<ProjectDeploymentReservationActiveLink> Active(
        ProjectDeploymentReservationLedger ledger, Guid reservationId)
    {
        ProjectDeploymentReservationCodec.Validate(ledger);
        if (!ledger.Items.Any(item => item.Id == reservationId))
        {
            throw new InvalidDataException("Réserve absente du registre.");
        }

        Dictionary<(ProjectDeploymentReservationRelationKind Kind,
            Guid TargetId), string> links = [];
        foreach (ProjectDeploymentReservationRelationEvent action in
            ledger.Relations ?? [])
        {
            if (action.ReservationId != reservationId)
            {
                continue;
            }

            (ProjectDeploymentReservationRelationKind Kind, Guid TargetId)
                key = (action.Kind, action.TargetId);
            if (action.Action == ProjectDeploymentReservationRelationAction.Link)
            {
                links[key] = action.SourceRelativePath;
            }
            else
            {
                links.Remove(key);
            }
        }

        return links.Select(item =>
            new ProjectDeploymentReservationActiveLink(
                item.Key.Kind, item.Key.TargetId, item.Value))
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.SourceRelativePath,
                StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Appends one validated explicit association/dissociation.</summary>
    /// <param name="ledger">Previously validated reservation source.</param>
    /// <param name="action">Human confirmed relation event.</param>
    /// <returns>In-memory proposal, with all older events untouched.</returns>
    public static ProjectDeploymentReservationLedger Append(
        ProjectDeploymentReservationLedger ledger,
        ProjectDeploymentReservationRelationEvent action)
    {
        ProjectDeploymentReservationCodec.Validate(ledger);
        ArgumentNullException.ThrowIfNull(action);
        ProjectDeploymentReservationLedger next = ledger with
        {
            Relations = [.. (ledger.Relations ?? []), action]
        };
        ProjectDeploymentReservationCodec.Validate(next);
        return next;
    }
}
