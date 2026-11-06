using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Projects;

/// <summary>Immutable source of a follow-up obligation from an existing MEP.</summary>
public enum ProjectDeploymentReservationOrigin
{
    Handover,
    PostCheck,
    Closure
}

/// <summary>Explicit human follow-up actions; no MEP decision changes.</summary>
public enum ProjectDeploymentReservationAction
{
    TakeInCharge,
    ProgressNote,
    Reassign,
    Resolve,
    Reopen
}

/// <summary>Derived only from the initial obligation and human action events.</summary>
public enum ProjectDeploymentReservationStatus
{
    Open,
    InProgress,
    Resolved
}

/// <summary>
/// One individually tracked reservation tied to a stable, historically
/// recorded MEP decision. The MEP remains untouched after its own closure.
/// </summary>
public sealed record ProjectDeploymentReservation
{
    public required Guid Id { get; init; }
    public required string Reference { get; init; }
    public required Guid OperationId { get; init; }
    public required ProjectDeploymentReservationOrigin OriginKind { get; init; }
    public required Guid OriginId { get; init; }
    public required string OriginSha256 { get; init; }
    public required string OriginSummary { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string CreatedBy { get; init; }
    public required string InitialOwner { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public DateOnly? DueDate { get; init; }
}

/// <summary>Append-only, human-signed operational reservation follow-up.</summary>
public sealed record ProjectDeploymentReservationEvent
{
    public required Guid Id { get; init; }
    public required Guid ReservationId { get; init; }
    public required ProjectDeploymentReservationAction Action { get; init; }
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public string? NewOwner { get; init; }
    public ProjectGateEvidenceSnapshot? Evidence { get; init; }
}

/// <summary>Separate project-local Markdown ledger, not the closed MEP file.</summary>
public sealed record ProjectDeploymentReservationLedger
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid ProjectId { get; init; }
    public IReadOnlyList<ProjectDeploymentReservation> Items { get; init; } = [];
    public IReadOnlyList<ProjectDeploymentReservationEvent> Events { get; init; } = [];
}

/// <summary>Status and accountable owner derived from a validated audit history.</summary>
/// <param name="Status">Current human-declared resolution status.</param>
/// <param name="Owner">Accountable operations person or team.</param>
/// <param name="LastUpdatedUtc">Time of the last human action.</param>
public sealed record ProjectDeploymentReservationState(
    ProjectDeploymentReservationStatus Status, string Owner,
    DateTimeOffset LastUpdatedUtc);

/// <summary>One verified historical origin available for explicit tracking.</summary>
/// <param name="Kind">Which MEP business record owns the reserve.</param>
/// <param name="Id">Stable immutable origin GUID.</param>
/// <param name="RecordedUtc">Origin's human decision timestamp.</param>
/// <param name="Description">Original reservation note, never inferred.</param>
public sealed record ProjectDeploymentReservationSource(
    ProjectDeploymentReservationOrigin Kind, Guid Id,
    DateTimeOffset RecordedUtc, string Description);

/// <summary>
/// Offers only actual historical reservations from existing MEP records.
/// One source may produce several separately approved follow-up obligations.
/// </summary>
public static class ProjectDeploymentReservationOrigins
{
    /// <summary>Lists candidate historical source records in the selected MEP.</summary>
    /// <param name="operation">Already validated canonical operation.</param>
    /// <returns>Human-readable immutable sources with stable IDs.</returns>
    public static IReadOnlyList<ProjectDeploymentReservationSource> List(
        ProjectDeploymentOperation operation)
    {
        ProjectDeploymentMarkdownCodec.Validate(operation);
        List<ProjectDeploymentReservationSource> result = [];
        foreach (ProjectDeploymentHandoverReview review in
            operation.HandoverReviews ?? [])
        {
            string[] reserveNotes = review.Checks
                .Where(item => item.Status ==
                    ProjectDeploymentHandoverStatus.Reservation)
                .Select(item => item.Area + " : " + item.Note).ToArray();
            string summary = string.Join("\n",
                (string.IsNullOrWhiteSpace(review.Reservations)
                    ? Array.Empty<string>() : new[] { review.Reservations! })
                .Concat(reserveNotes));
            if (!string.IsNullOrWhiteSpace(summary))
            {
                result.Add(new ProjectDeploymentReservationSource(
                    ProjectDeploymentReservationOrigin.Handover,
                    review.Id, review.RecordedUtc, summary));
            }
        }

        foreach (ProjectDeploymentPostCheck post in operation.PostChecks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(post.Reservations) ||
                post.Failed > 0 || post.Blocked > 0 ||
                post.Verdict == ProjectDeploymentPostCheckVerdict.NonConform)
            {
                string summary = string.IsNullOrWhiteSpace(post.Reservations)
                    ? post.Failed + " test(s) non conforme(s), " +
                      post.Blocked + " bloqué(s) · " + post.Rationale
                    : post.Reservations!;
                result.Add(new ProjectDeploymentReservationSource(
                    ProjectDeploymentReservationOrigin.PostCheck,
                    post.Id, post.RecordedUtc, summary));
            }
        }

        if (operation.Closure is ProjectDeploymentClosure closure &&
            !string.IsNullOrWhiteSpace(closure.Reservations))
        {
            result.Add(new ProjectDeploymentReservationSource(
                ProjectDeploymentReservationOrigin.Closure,
                closure.Id, closure.RecordedUtc, closure.Reservations));
        }

        return result;
    }
}

/// <summary>
/// Strict, bounded event-sourced operational follow-up. Never guesses that
/// an incident, test or MEP has been closed from a reservation action.
/// </summary>
public static class ProjectDeploymentReservationCodec
{
    private const string Opening =
        "\n\n<!-- nodalis:mep-reservations:v1 -->\n\n~~~json\n";
    private const string Closing = "\n~~~\n";
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    /// <summary>Checks RES-0001 through RES-9999 canonical references.</summary>
    /// <param name="reference">Human-readable reservation number.</param>
    /// <returns>Whether canonical.</returns>
    public static bool IsReference(string? reference) =>
        reference is { Length: 8 } &&
        reference.StartsWith("RES-", StringComparison.Ordinal) &&
        int.TryParse(reference[4..], out int value) &&
        value is >= 1 and <= 9999 &&
        reference == "RES-" + value.ToString("D4",
            System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Validates original items and append-only event state machine.</summary>
    /// <param name="ledger">Untrusted persisted source.</param>
    public static void Validate(ProjectDeploymentReservationLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (ledger.SchemaVersion != 1 || ledger.ProjectId == Guid.Empty ||
            ledger.Items is null || ledger.Events is null ||
            ledger.Items.Count > 500 || ledger.Events.Count > 5000)
        {
            throw new InvalidDataException("Registre de réserves MEP invalide.");
        }

        Dictionary<Guid, ProjectDeploymentReservation> items = [];
        HashSet<string> refs = new(StringComparer.OrdinalIgnoreCase);
        HashSet<Guid> allIds = [];
        foreach (ProjectDeploymentReservation item in ledger.Items)
        {
            if (item is null || item.Id == Guid.Empty ||
                !allIds.Add(item.Id) || !items.TryAdd(item.Id, item) ||
                !IsReference(item.Reference) || !refs.Add(item.Reference) ||
                item.OperationId == Guid.Empty || item.OriginId == Guid.Empty ||
                !Enum.IsDefined(item.OriginKind) ||
                !ValidSha(item.OriginSha256) ||
                !Note(item.OriginSummary, 4000) ||
                !Label(item.Title, 240) ||
                !Note(item.Description, 4000) ||
                !Label(item.CreatedBy, 160) ||
                !Label(item.InitialOwner, 160) ||
                item.CreatedUtc == default)
            {
                throw new InvalidDataException(
                    "Réserve sans origine, identité ou responsable valide.");
            }
        }

        Dictionary<Guid, ProjectDeploymentReservationState> states = items.Values
            .ToDictionary(item => item.Id, item =>
                new ProjectDeploymentReservationState(
                    ProjectDeploymentReservationStatus.Open,
                    item.InitialOwner, item.CreatedUtc));
        DateTimeOffset last = DateTimeOffset.MinValue;
        foreach (ProjectDeploymentReservationEvent action in ledger.Events)
        {
            if (action is null || action.Id == Guid.Empty ||
                !allIds.Add(action.Id) ||
                !Enum.IsDefined(action.Action) ||
                !states.TryGetValue(action.ReservationId,
                    out ProjectDeploymentReservationState? previous) ||
                !Label(action.Actor, 160) ||
                !Note(action.Rationale, 4000) ||
                action.RecordedUtc < previous.LastUpdatedUtc ||
                action.RecordedUtc < last ||
                action.Evidence is not null &&
                    (!ProjectJourneySourcePath.IsSafe(
                        action.Evidence.RelativePath) ||
                     !ValidSha(action.Evidence.Sha256)) ||
                action.Action != ProjectDeploymentReservationAction.Reassign &&
                    action.NewOwner is not null ||
                action.Action == ProjectDeploymentReservationAction.Reassign &&
                    (!Label(action.NewOwner, 160) ||
                     action.NewOwner == previous.Owner))
            {
                throw new InvalidDataException(
                    "Action de suivi de réserve incohérente ou antidatée.");
            }

            ProjectDeploymentReservationStatus status = previous.Status;
            switch (action.Action)
            {
                case ProjectDeploymentReservationAction.TakeInCharge
                    when status == ProjectDeploymentReservationStatus.Open:
                    status = ProjectDeploymentReservationStatus.InProgress;
                    break;
                case ProjectDeploymentReservationAction.ProgressNote
                    when status is ProjectDeploymentReservationStatus.Open or
                        ProjectDeploymentReservationStatus.InProgress:
                case ProjectDeploymentReservationAction.Reassign
                    when status is ProjectDeploymentReservationStatus.Open or
                        ProjectDeploymentReservationStatus.InProgress:
                    break;
                case ProjectDeploymentReservationAction.Resolve
                    when status is ProjectDeploymentReservationStatus.Open or
                        ProjectDeploymentReservationStatus.InProgress:
                    status = ProjectDeploymentReservationStatus.Resolved;
                    break;
                case ProjectDeploymentReservationAction.Reopen
                    when status == ProjectDeploymentReservationStatus.Resolved:
                    status = ProjectDeploymentReservationStatus.Open;
                    break;
                default:
                    throw new InvalidDataException(
                        "Transition de réserve non autorisée.");
            }

            states[action.ReservationId] =
                new ProjectDeploymentReservationState(status,
                    action.NewOwner ?? previous.Owner, action.RecordedUtc);
            last = action.RecordedUtc;
        }
    }

    /// <summary>Returns the exact state derived from already validated history.</summary>
    /// <param name="ledger">Complete event log.</param>
    /// <param name="reservationId">Original stable reservation identity.</param>
    /// <returns>Current state and owner, without writing anything.</returns>
    public static ProjectDeploymentReservationState GetState(
        ProjectDeploymentReservationLedger ledger, Guid reservationId)
    {
        Validate(ledger);
        ProjectDeploymentReservation item = ledger.Items.Single(
            entry => entry.Id == reservationId);
        ProjectDeploymentReservationStatus status =
            ProjectDeploymentReservationStatus.Open;
        string owner = item.InitialOwner;
        DateTimeOffset latest = item.CreatedUtc;
        foreach (ProjectDeploymentReservationEvent action in ledger.Events.Where(
            entry => entry.ReservationId == reservationId))
        {
            status = action.Action switch
            {
                ProjectDeploymentReservationAction.TakeInCharge =>
                    ProjectDeploymentReservationStatus.InProgress,
                ProjectDeploymentReservationAction.Resolve =>
                    ProjectDeploymentReservationStatus.Resolved,
                ProjectDeploymentReservationAction.Reopen =>
                    ProjectDeploymentReservationStatus.Open,
                _ => status
            };
            owner = action.NewOwner ?? owner;
            latest = action.RecordedUtc;
        }

        return new ProjectDeploymentReservationState(status, owner, latest);
    }

    /// <summary>Appends an explicitly created, assigned obligation.</summary>
    /// <param name="ledger">Valid source snapshot.</param>
    /// <param name="item">Human-approved new reservation.</param>
    /// <returns>New in-memory ledger proposal.</returns>
    public static ProjectDeploymentReservationLedger Add(
        ProjectDeploymentReservationLedger ledger, ProjectDeploymentReservation item)
    {
        Validate(ledger);
        ArgumentNullException.ThrowIfNull(item);
        ProjectDeploymentReservationLedger result = ledger with
        {
            Items = ledger.Items.Append(item).ToArray()
        };
        Validate(result);
        return result;
    }

    /// <summary>Appends a human event; no implicit resolution is possible.</summary>
    /// <param name="ledger">Valid snapshot.</param>
    /// <param name="action">Explicit human audit event.</param>
    /// <returns>New append-only proposal.</returns>
    public static ProjectDeploymentReservationLedger Append(
        ProjectDeploymentReservationLedger ledger,
        ProjectDeploymentReservationEvent action)
    {
        Validate(ledger);
        ArgumentNullException.ThrowIfNull(action);
        ProjectDeploymentReservationLedger result = ledger with
        {
            Events = ledger.Events.Append(action).ToArray()
        };
        Validate(result);
        return result;
    }

    /// <summary>Formats exact portable Markdown; no mutation or inference.</summary>
    /// <param name="ledger">Fully validated source.</param>
    /// <returns>Canonical text.</returns>
    public static string Format(ProjectDeploymentReservationLedger ledger)
    {
        Validate(ledger);
        string text = "# Suivi des réserves MEP" + Opening +
            JsonSerializer.Serialize(ledger, Options) + Closing;
        if (text.Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException("Historique des réserves trop long.");
        }

        return text;
    }

    /// <summary>Parses only the one exact v1 Markdown/JSON source.</summary>
    /// <param name="markdown">Untrusted file text.</param>
    /// <returns>Validated immutable historical ledger.</returns>
    public static ProjectDeploymentReservationLedger Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > 2 * 1024 * 1024 ||
            !markdown.StartsWith("# Suivi des réserves MEP" + Opening,
                StringComparison.Ordinal) ||
            !markdown.EndsWith(Closing, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Fichier de réserves MEP non canonique.");
        }

        string payload = markdown[
            ("# Suivi des réserves MEP" + Opening).Length..^Closing.Length];
        ProjectDeploymentReservationLedger ledger;
        try
        {
            ledger = JsonSerializer.Deserialize<ProjectDeploymentReservationLedger>(
                payload, Options) ??
                throw new InvalidDataException("Registre MEP vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Registre MEP JSON invalide.", exception);
        }

        Validate(ledger);
        if (!string.Equals(Format(ledger), markdown, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Registre MEP non canonique ou modifié manuellement.");
        }

        return ledger;
    }

    /// <summary>Checks a safe SHA-256 fingerprint.</summary>
    /// <param name="sha">Untrusted digest.</param>
    /// <returns>Whether a hex SHA-256.</returns>
    private static bool ValidSha(string? sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);

    /// <summary>Checks a single line of human text.</summary>
    /// <param name="value">Untrusted label.</param>
    /// <param name="max">Maximum characters.</param>
    /// <returns>True for a meaningful, bounded single line.</returns>
    private static bool Label(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Any(char.IsControl);

    /// <summary>Checks a bounded multiline human note.</summary>
    /// <param name="value">Untrusted note.</param>
    /// <param name="max">Maximum length.</param>
    /// <returns>True for a meaningful note.</returns>
    private static bool Note(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
        !value.Contains('\0');
}
