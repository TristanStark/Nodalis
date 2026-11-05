using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Tests;

/// <summary>
/// Human disposition of an incident after reviewing REC evidence.
/// No value authorizes an automatic change to maintenance incident status.
/// </summary>
public enum ProjectRecRevalidationDisposition
{
    Confirmed,
    Rejected,
    Deferred
}

/// <summary>
/// Immutable exact human-reviewed evidence for one stable affected TEST ID.
/// A nullable retest means the historic failure was reviewed without a pass.
/// </summary>
public sealed record ProjectRecRevalidationTestSnapshot
{
    /// <summary>Globally stable test block identity.</summary>
    public required Guid TestId { get; init; }

    /// <summary>Captured human test reference, not a live alias.</summary>
    public required string Reference { get; init; }

    /// <summary>Original REC result used as incident-linked failure.</summary>
    public required Guid FailureResultId { get; init; }

    /// <summary>Human failed or blocked result declaration time.</summary>
    public required DateTimeOffset FailureUtc { get; init; }

    /// <summary>Latest newly executed outcome, if any.</summary>
    public Guid? RetestResultId { get; init; }

    /// <summary>Human re-execution time, if any.</summary>
    public DateTimeOffset? RetestUtc { get; init; }

    /// <summary>Human re-execution outcome, if any.</summary>
    public ProjectTestOutcome? RetestOutcome { get; init; }

    /// <summary>Frozen retest execution environment, if any.</summary>
    public string? RetestEnvironment { get; init; }

    /// <summary>Frozen retest application version, if any.</summary>
    public string? RetestVersion { get; init; }
}

/// <summary>
/// Append-only human decision stored INSIDE the existing maintenance sheet.
/// The REC registry fingerprint and test snapshots freeze the reviewed scope.
/// </summary>
public sealed record ProjectRecRevalidationEvent
{
    /// <summary>Unique journal entry identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Canonical project-local maintenance incident ID.</summary>
    public required string IncidentId { get; init; }

    /// <summary>Last decision for this incident, or null on its first decision.</summary>
    public Guid? PreviousDecisionId { get; init; }

    /// <summary>Human outcome, unrelated to maintenance status.</summary>
    public required ProjectRecRevalidationDisposition Disposition { get; init; }

    /// <summary>Explicit author of this decision.</summary>
    public required string Actor { get; init; }

    /// <summary>Mandatory human justification.</summary>
    public required string Rationale { get; init; }

    /// <summary>Human-referenced evidence; not automatically verified.</summary>
    public required string EvidenceReference { get; init; }

    /// <summary>Human-approved environment scope.</summary>
    public required string TargetEnvironment { get; init; }

    /// <summary>Human-approved application version scope.</summary>
    public required string TargetVersion { get; init; }

    /// <summary>Human attestation that live TEST definitions were reviewed.</summary>
    public required bool SourceDefinitionsChecked { get; init; }

    /// <summary>Human attestation that evidence references were inspected.</summary>
    public required bool EvidenceChecked { get; init; }

    /// <summary>Human attestation of target scope, version and regressions.</summary>
    public required bool ScopeChecked { get; init; }

    /// <summary>SHA-256 of the reviewed project-local REC registry revisions.</summary>
    public required string RecRegistrySha256 { get; init; }

    /// <summary>Frozen linked test results underlying the human choice.</summary>
    public required IReadOnlyList<ProjectRecRevalidationTestSnapshot> Tests { get; init; }

    /// <summary>UTC decision timestamp, not a test execution time.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>
/// Portable, append-only *logical* journal embedded as JSON in the existing
/// Fiche de Maintenance.md. The ordinary incident/intervention codec ignores
/// this distinct heading, preserving its existing contract and notes.
/// </summary>
public static class ProjectRecRevalidationCodec
{
    private const string Header = "## Décisions de revalidation REC";
    private const string Marker = "<!-- nodalis:rec-revalidation:v1 -->";
    private const int MaxEvents = 2048;
    private sealed record Registry(int SchemaVersion,
        IReadOnlyList<ProjectRecRevalidationEvent> Events);

    private readonly record struct Section(int Start, int End);

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    /// <summary>
    /// Parses the optional audit section without altering other maintenance
    /// notes; rejects duplicate or malformed sections rather than hiding loss.
    /// </summary>
    /// <param name="markdown">Unmodified source maintenance document.</param>
    /// <returns>Validated ordered human decisions; empty for legacy files.</returns>
    public static IReadOnlyList<ProjectRecRevalidationEvent> Read(
        string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        Section? section = FindSection(markdown);
        if (section is null)
        {
            return [];
        }

        string block = markdown[section.Value.Start..section.Value.End]
            .Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        string prefix = Header + "\n\n" + Marker + "\n~~~json\n";
        if (!block.StartsWith(prefix, StringComparison.Ordinal) ||
            !block.EndsWith("\n~~~", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Historique de revalidation REC incomplet ou modifié.");
        }

        string json = block[prefix.Length..^4];
        Registry? registry;
        try
        {
            registry = JsonSerializer.Deserialize<Registry>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Décisions de revalidation REC : JSON invalide.", exception);
        }

        if (registry is null || registry.SchemaVersion != 1 ||
            registry.Events is null)
        {
            throw new InvalidDataException(
                "Registre de revalidation REC absent ou inconnu.");
        }

        Validate(registry.Events);
        return registry.Events;
    }

    /// <summary>
    /// Adds one signed decision while keeping all prior events untouched.
    /// Existing free-form text outside the section is byte-preserved.
    /// The caller must persist using the original maintenance SHA-CAS.
    /// </summary>
    /// <param name="markdown">Exact current canonical maintenance text.</param>
    /// <param name="entry">Human-confirmed decision with prior audit pointer.</param>
    /// <returns>Updated maintenance Markdown, without writing a file.</returns>
    public static string Append(string markdown,
        ProjectRecRevalidationEvent entry)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(entry);
        IReadOnlyList<ProjectRecRevalidationEvent> old = Read(markdown);
        if (old.Count == MaxEvents)
        {
            throw new InvalidDataException(
                "Historique des revalidations REC à sa limite.");
        }

        ProjectRecRevalidationEvent[] updated = [.. old, entry];
        Validate(updated);
        string nl = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string json = JsonSerializer.Serialize(new Registry(1, updated),
            Options).Replace("\n", nl, StringComparison.Ordinal);
        string sectionText = Header + nl + nl + Marker + nl +
            "~~~json" + nl + json + nl + "~~~" + nl;
        Section? section = FindSection(markdown);
        if (section is not null)
        {
            return markdown[..section.Value.Start] + sectionText +
                markdown[section.Value.End..];
        }

        string separator = markdown.Length == 0 ? string.Empty :
            markdown.EndsWith(nl + nl, StringComparison.Ordinal)
                ? string.Empty
                : markdown.EndsWith(nl, StringComparison.Ordinal)
                    ? nl : nl + nl;
        return markdown + separator + sectionText;
    }

    /// <summary>Finds the last human decision for an incident, if any.</summary>
    /// <param name="events">Already validated ordered audit records.</param>
    /// <param name="incidentId">Canonical stable INC identity.</param>
    /// <returns>Latest human disposition, or null.</returns>
    public static ProjectRecRevalidationEvent? Latest(
        IReadOnlyList<ProjectRecRevalidationEvent> events,
        string incidentId)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.LastOrDefault(item =>
            string.Equals(item.IncidentId, incidentId,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures unique IDs, canonical reference fields, immutable test snapshots
    /// and an unbroken incident-local decision chain.
    /// </summary>
    /// <param name="events">Untrusted journal entries.</param>
    public static void Validate(
        IReadOnlyList<ProjectRecRevalidationEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count > MaxEvents)
        {
            throw new InvalidDataException(
                "Trop de décisions REC dans une seule fiche.");
        }

        HashSet<Guid> identities = [];
        Dictionary<string, Guid> previous = new(
            StringComparer.OrdinalIgnoreCase);
        DateTimeOffset lastUtc = DateTimeOffset.MinValue;
        foreach (ProjectRecRevalidationEvent decision in events)
        {
            if (decision is null || decision.Id == Guid.Empty ||
                !identities.Add(decision.Id) ||
                !ValidIncident(decision.IncidentId) ||
                !Enum.IsDefined(decision.Disposition) ||
                !OneLine(decision.Actor, 160) ||
                !OneLine(decision.Rationale, 2000) ||
                decision.Rationale.Trim().Length < 10 ||
                !OneLine(decision.TargetEnvironment, 160) ||
                !OneLine(decision.TargetVersion, 160) ||
                decision.EvidenceReference is null ||
                decision.EvidenceReference.Length > 2000 ||
                decision.EvidenceReference.Any(char.IsControl) ||
                decision.Disposition == ProjectRecRevalidationDisposition.Confirmed &&
                    (decision.EvidenceReference.Trim().Length < 10 ||
                     !decision.SourceDefinitionsChecked ||
                     !decision.EvidenceChecked ||
                     !decision.ScopeChecked) ||
                !ValidSha(decision.RecRegistrySha256) ||
                decision.RecordedUtc == default ||
                decision.RecordedUtc < lastUtc ||
                decision.Tests is null ||
                decision.Tests.Count is < 1 or > 500 ||
                previous.TryGetValue(decision.IncidentId,
                    out Guid prior) != (decision.PreviousDecisionId is not null) ||
                (decision.PreviousDecisionId is Guid expected &&
                    prior != expected))
            {
                throw new InvalidDataException(
                    "Décision humaine REC invalide ou audit chaîné incomplet.");
            }

            HashSet<Guid> testIds = [];
            foreach (ProjectRecRevalidationTestSnapshot test in decision.Tests)
            {
                if (test is null || test.TestId == Guid.Empty ||
                    !testIds.Add(test.TestId) ||
                    test.FailureResultId == Guid.Empty ||
                    test.FailureUtc == default ||
                    !OneLine(test.Reference, 120) ||
                    (test.RetestResultId is null) !=
                        (test.RetestOutcome is null) ||
                    (test.RetestResultId is null) !=
                        (test.RetestUtc is null) ||
                    (test.RetestResultId is null) !=
                        (test.RetestEnvironment is null) ||
                    (test.RetestResultId is null) !=
                        (test.RetestVersion is null) ||
                    test.RetestResultId == Guid.Empty ||
                    test.RetestUtc <= test.FailureUtc ||
                    test.RetestEnvironment is not null &&
                        !OneLine(test.RetestEnvironment, 160) ||
                    test.RetestVersion is not null &&
                        !OneLine(test.RetestVersion, 160))
                {
                    throw new InvalidDataException(
                        "Instantané de test associé à la décision REC invalide.");
                }

                if (decision.Disposition ==
                        ProjectRecRevalidationDisposition.Confirmed &&
                    (test.RetestOutcome != ProjectTestOutcome.Passed ||
                     !string.Equals(test.RetestEnvironment,
                         decision.TargetEnvironment,
                         StringComparison.OrdinalIgnoreCase) ||
                     !string.Equals(test.RetestVersion,
                         decision.TargetVersion,
                         StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        "Validation REC sans résultats conformes dans le " +
                        "périmètre humain explicite.");
                }
            }

            previous[decision.IncidentId] = decision.Id;
            lastUtc = decision.RecordedUtc;
        }
    }

    /// <summary>Recognizes a complete prefixed incident GUID.</summary>
    /// <param name="value">Serialized incident identity.</param>
    /// <returns>True for a canonical INC- plus N-format GUID.</returns>
    private static bool ValidIncident(string? value) =>
        value is { Length: 36 } &&
        value.StartsWith("INC-", StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(value[4..], "N", out Guid parsed) &&
        parsed != Guid.Empty;

    /// <summary>Rejects empty, control and line-breaking human fields.</summary>
    /// <param name="value">Untrusted text.</param>
    /// <param name="maximum">Maximum size.</param>
    /// <returns>Whether a single-line human field is valid.</returns>
    private static bool OneLine(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum &&
        !value.Any(char.IsControl);

    /// <summary>Checks a full review fingerprint.</summary>
    /// <param name="value">SHA-256 hexadecimal string.</param>
    /// <returns>Whether the registry snapshot is exact-length hex.</returns>
    private static bool ValidSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    /// <summary>
    /// Finds an exact heading outside Markdown code fences, rejects duplicates,
    /// and stops before another top-level section to preserve later notes.
    /// </summary>
    /// <param name="markdown">Complete original source.</param>
    /// <returns>Optional exact source interval.</returns>
    private static Section? FindSection(string markdown)
    {
        int start = -1;
        int end = markdown.Length;
        int position = 0;
        char fence = '\0';
        int fenceLength = 0;
        while (position < markdown.Length)
        {
            int newline = markdown.IndexOf('\n', position);
            int next = newline < 0 ? markdown.Length : newline + 1;
            string line = markdown[position..(newline < 0
                ? markdown.Length : newline)].TrimEnd('\r');
            string heading = line.TrimEnd();
            string trimmed = line.TrimStart(' ');
            if (line.Length - trimmed.Length <= 3 &&
                trimmed.Length >= 3 &&
                (trimmed[0] == '~' || trimmed[0] == '`'))
            {
                char token = trimmed[0];
                int run = 0;
                while (run < trimmed.Length && trimmed[run] == token)
                {
                    run++;
                }

                if (run >= 3)
                {
                    if (fence == '\0')
                    {
                        fence = token;
                        fenceLength = run;
                    }
                    else if (fence == token && run >= fenceLength &&
                        string.IsNullOrWhiteSpace(trimmed[run..]))
                    {
                        fence = '\0';
                        fenceLength = 0;
                    }

                    position = next;
                    continue;
                }
            }

            if (fence == '\0' && line == Header)
            {
                if (start >= 0)
                {
                    throw new InvalidDataException(
                        "Sections de revalidation REC en doublon.");
                }

                start = position;
            }
            else if (fence == '\0' && start >= 0 &&
                end == markdown.Length && position > start &&
                heading.StartsWith("## ", StringComparison.Ordinal) &&
                !heading.StartsWith("### ", StringComparison.Ordinal))
            {
                end = position;
            }

            position = next;
        }

        return start < 0 ? null : new Section(start, end);
    }
}
