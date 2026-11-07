using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Projects;

/// <summary>Requested ownership strategy; neither value executes a transfer.</summary>
public enum ProjectHandoverMethod
{
    MoveOriginal,
    LinkedSuccessor
}

/// <summary>
/// One human-approved, non-executed transfer intention. The source Markdown
/// remains authoritative until an independent transactional operation exists.
/// </summary>
public sealed record ProjectHandoverIntent
{
    /// <summary>Stable unique intention identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Project still owning the original item.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Intended next owning project.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Stable source item identity.</summary>
    public required Guid SourceObjectId { get; init; }

    /// <summary>Original register type.</summary>
    public required string SourceObjectKind { get; init; }

    /// <summary>Source reference, not necessarily unique across projects.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Original validated source path for subsequent review.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Destination project path relative to this workspace.</summary>
    public required string TargetProjectRelativePath { get; init; }

    /// <summary>Proposed strategy, never an executed action.</summary>
    public required ProjectHandoverMethod Method { get; init; }

    /// <summary>Existing GUID for A, proposed NEW GUID for B.</summary>
    public required Guid ProposedTargetObjectId { get; init; }

    /// <summary>Source inventory revision captured during explicit review.</summary>
    public required string SourceFingerprint { get; init; }

    /// <summary>Destination inventory revision captured during explicit review.</summary>
    public required string TargetFingerprint { get; init; }

    /// <summary>Human who requested future passation.</summary>
    public required string Actor { get; init; }

    /// <summary>Required justification and follow-up responsibility.</summary>
    public required string Rationale { get; init; }

    /// <summary>Time of intention, not transfer completion time.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>Append-only handover intention history owned by the source project.</summary>
public sealed record ProjectHandoverIntentLedger
{
    /// <summary>Persisted storage contract version.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Only project that may append to this ledger.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Chronological, immutable human intentions; no execution flags.</summary>
    public IReadOnlyList<ProjectHandoverIntent> Intents { get; init; } = [];
}

/// <summary>
/// Strict portable history format; no compatibility with arbitrary human
/// Markdown is inferred and no unknown content is silently overwritten.
/// </summary>
public static class ProjectHandoverIntentCodec
{
    private const int MaximumCharacters = 4 * 1024 * 1024;
    private const string Prefix =
        "# Passations préparées\n\n<!-- nodalis:handover-intents:v1 -->\n\n~~~json\n";
    private const string Suffix = "\n~~~\n";
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    /// <summary>Validates source ownership, human intent and append-only limits.</summary>
    /// <param name="ledger">Proposed complete immutable history.</param>
    public static void Validate(ProjectHandoverIntentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (ledger.ProjectId == Guid.Empty || ledger.SchemaVersion != 1 ||
            ledger.Intents is null || ledger.Intents.Count > 500)
        {
            throw new InvalidDataException("Journal de passation non canonique.");
        }

        HashSet<Guid> eventIds = [];
        DateTimeOffset previous = DateTimeOffset.MinValue;
        foreach (ProjectHandoverIntent intent in ledger.Intents)
        {
            if (intent is null || intent.Id == Guid.Empty ||
                !eventIds.Add(intent.Id) ||
                intent.SourceProjectId != ledger.ProjectId ||
                intent.TargetProjectId == Guid.Empty ||
                intent.TargetProjectId == ledger.ProjectId ||
                intent.SourceObjectId == Guid.Empty ||
                !Enum.IsDefined(intent.Method) ||
                intent.ProposedTargetObjectId == Guid.Empty ||
                (intent.Method == ProjectHandoverMethod.MoveOriginal &&
                 intent.ProposedTargetObjectId != intent.SourceObjectId) ||
                (intent.Method == ProjectHandoverMethod.LinkedSuccessor &&
                 intent.ProposedTargetObjectId == intent.SourceObjectId) ||
                !ValidShort(intent.SourceObjectKind, 80) ||
                !ValidShort(intent.SourceReference, 100) ||
                !ProjectJourneySourcePath.IsSafe(intent.SourceRelativePath) ||
                !ProjectJourneySourcePath.IsSafe(intent.TargetProjectRelativePath) ||
                !ValidHash(intent.SourceFingerprint) ||
                !ValidHash(intent.TargetFingerprint) ||
                !ValidShort(intent.Actor, 160) ||
                string.IsNullOrWhiteSpace(intent.Rationale) ||
                intent.Rationale.Length > 4000 ||
                intent.Rationale.Contains('\0') ||
                intent.RecordedUtc == default ||
                intent.RecordedUtc < previous)
            {
                throw new InvalidDataException(
                    "Proposition de passation incohérente ou incomplète.");
            }

            previous = intent.RecordedUtc;
        }
    }

    /// <summary>Serializes the canonical single-markdown ledger exactly.</summary>
    /// <param name="ledger">Validated source ledger.</param>
    /// <returns>Portable Markdown document.</returns>
    public static string Format(ProjectHandoverIntentLedger ledger)
    {
        Validate(ledger);
        string markdown = Prefix + JsonSerializer.Serialize(ledger, Options) +
            Suffix;
        if (markdown.Length > MaximumCharacters)
        {
            throw new InvalidDataException(
                "Historique de passation trop volumineux.");
        }

        return markdown;
    }

    /// <summary>Requires an exact canonical document; refuses legacy overwrites.</summary>
    /// <param name="markdown">Untrusted Markdown.</param>
    /// <returns>Validated, immutable history model.</returns>
    public static ProjectHandoverIntentLedger Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaximumCharacters ||
            !markdown.StartsWith(Prefix, StringComparison.Ordinal) ||
            !markdown.EndsWith(Suffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Fichier Passations non canonique : aucun écrasement autorisé.");
        }

        ProjectHandoverIntentLedger ledger;
        try
        {
            ledger = JsonSerializer.Deserialize<ProjectHandoverIntentLedger>(
                markdown[Prefix.Length..^Suffix.Length], Options) ??
                throw new InvalidDataException("Journal de passation vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Données de passation illisibles.", exception);
        }

        Validate(ledger);
        if (!string.Equals(Format(ledger), markdown, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Journal de passation altéré ou non canonique.");
        }

        return ledger;
    }

    /// <summary>Validates a 64-character source SHA-256.</summary>
    /// <param name="fingerprint">Expected hexadecimal fingerprint.</param>
    /// <returns>Whether the hash is canonical.</returns>
    private static bool ValidHash(string? fingerprint) =>
        fingerprint is { Length: 64 } && fingerprint.All(Uri.IsHexDigit);

    /// <summary>Validates a human-readable single-line field.</summary>
    /// <param name="value">Untrusted string.</param>
    /// <param name="limit">Maximum permitted length.</param>
    /// <returns>Whether the value is nonblank and portable.</returns>
    private static bool ValidShort(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit &&
        !value.Any(char.IsControl);
}
