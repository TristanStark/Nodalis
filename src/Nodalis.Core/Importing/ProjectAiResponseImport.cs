using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nodalis.Core.Projects;

namespace Nodalis.Core.Importing;

/// <summary>
/// A response supplied by an EXTERNAL model, never trusted as a command.
/// Each candidate still requires a human decision and project-local matching.
/// </summary>
public sealed record ProjectAiResponseEnvelope
{
    /// <summary>Required portable discriminator, not an auto-upgrade signal.</summary>
    public required string Schema { get; init; }

    /// <summary>Stable external response identity for future deduplication.</summary>
    public required Guid ResponseId { get; init; }

    /// <summary>Optional informative key; does not authorize any prompt.</summary>
    public string? PromptKey { get; init; }

    /// <summary>Up to 100 independent proposals, no imported action is run.</summary>
    public required List<ProjectAiImportCandidate> Proposals { get; init; }
}

/// <summary>
/// Descriptive candidate, not a persisted domain object or a decision.
/// An AI cannot assign any official Nodalis identity or approval here.
/// </summary>
public sealed record ProjectAiImportCandidate
{
    /// <summary>Idempotency candidate key, not a domain object GUID.</summary>
    public required Guid ProposalId { get; init; }

    /// <summary>requirement/criterion/risk/test/task/decision.</summary>
    public required string Kind { get; init; }

    /// <summary>Only create/update/link suggestions, never delete or approve.</summary>
    public required string Operation { get; init; }

    /// <summary>Optional existing object hint; not trusted without resolution.</summary>
    public Guid? TargetId { get; init; }

    /// <summary>Optional human reference hint, never an official new reference.</summary>
    public string? CandidateReference { get; init; }

    /// <summary>Optional untrusted workspace-relative provenance hint.</summary>
    public string? SourcePath { get; init; }

    /// <summary>explicit/derived/hypothesis; no automatic acceptance.</summary>
    public required string Certainty { get; init; }

    /// <summary>Stated base values for later three-way comparison, not authority.</summary>
    public Dictionary<string, string> BaseFields { get; init; } = [];

    /// <summary>Untrusted candidate values; no direct write permitted.</summary>
    public required Dictionary<string, string> ProposedFields { get; init; }
}

/// <summary>
/// Bounded read-only result of parsing untrusted external JSON. The raw
/// response is owned by the caller and is not stored or forwarded by Nodalis.
/// </summary>
/// <param name="Document">Deserialized, strictly validated proposals.</param>
/// <param name="RawSha256">Hash of the exact user-supplied raw response.</param>
public sealed record ProjectAiResponsePreview(
    ProjectAiResponseEnvelope Document, string RawSha256);

/// <summary>
/// Strict nodalis.import.v1 parser. Deliberately has no workspace path,
/// serializer-to-domain adapter, commit or network methods.
/// </summary>
public static class ProjectAiResponseImport
{
    private const int MaxRawCharacters = 1024 * 1024;
    private static readonly HashSet<string> Kinds = new(
        ["requirement", "criterion", "risk", "test", "task", "decision"],
        StringComparer.Ordinal);
    private static readonly HashSet<string> Operations = new(
        ["create", "update", "link"], StringComparer.Ordinal);
    private static readonly HashSet<string> Certainties = new(
        ["explicit", "derived", "hypothesis"], StringComparer.Ordinal);
    private static readonly HashSet<string> ForbiddenFields = new(
        [
            "id", "guid", "projectid", "status", "state", "approval",
            "approved", "verdict", "gonogo", "go", "deleted",
            "isdeleted", "closed", "isclosed", "verified", "validated"
        ], StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    /// <summary>Parses one plain JSON object or one entire json code fence.</summary>
    /// <param name="raw">Pasted external response, never executed.</param>
    /// <returns>Untrusted candidates and exact raw SHA-256.</returns>
    public static ProjectAiResponsePreview Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length is < 2 or > MaxRawCharacters)
        {
            throw new InvalidDataException(
                "Réponse IA absente ou trop volumineuse (>1 Mio).");
        }

        string json = raw.Trim().Replace("\r\n", "\n",
            StringComparison.Ordinal);
        if (json.StartsWith("```json\n", StringComparison.Ordinal) &&
            json.EndsWith("\n```", StringComparison.Ordinal))
        {
            json = json[8..^4].Trim();
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(json,
                new JsonDocumentOptions
                {
                    MaxDepth = 32,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
            RejectDuplicateProperties(parsed.RootElement);
            ProjectAiResponseEnvelope envelope =
                JsonSerializer.Deserialize<ProjectAiResponseEnvelope>(
                    parsed.RootElement.GetRawText(), Options) ??
                throw new InvalidDataException("Enveloppe d'import IA vide.");
            Validate(envelope);
            return new ProjectAiResponsePreview(envelope,
                Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(raw))));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Réponse IA hors contrat nodalis.import.v1 : JSON invalide " +
                "ou propriétés incompatibles. Consultez le schéma " +
                "intégré au prompt préparé.", exception);
        }
    }

    /// <summary>Rejects duplicate JSON properties even in nested dictionaries.</summary>
    /// <param name="node">Parsed bounded JSON node.</param>
    private static void RejectDuplicateProperties(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in node.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new InvalidDataException(
                        "Clé JSON ambiguë ou dupliquée dans la réponse IA.");
                }

                RejectDuplicateProperties(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in node.EnumerateArray())
            {
                RejectDuplicateProperties(child);
            }
        }
    }

    /// <summary>Checks conservative type, identity, size and permission gates.</summary>
    /// <param name="envelope">Untrusted external data.</param>
    public static void Validate(ProjectAiResponseEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Schema != ProjectAiResponsePromptContract.Schema ||
            envelope.ResponseId == Guid.Empty ||
            envelope.Proposals is null ||
            envelope.Proposals.Count is < 1 or > 100 ||
            envelope.PromptKey is { Length: > 120 } ||
            envelope.PromptKey?.Any(char.IsControl) == true)
        {
            throw new InvalidDataException(
                "Enveloppe d'import IA non prise en charge.");
        }

        HashSet<Guid> proposalIds = [];
        foreach (ProjectAiImportCandidate proposal in envelope.Proposals)
        {
            if (proposal is null || proposal.ProposalId == Guid.Empty ||
                !proposalIds.Add(proposal.ProposalId) ||
                !Kinds.Contains(proposal.Kind) ||
                !Operations.Contains(proposal.Operation) ||
                !Certainties.Contains(proposal.Certainty) ||
                proposal.TargetId == Guid.Empty ||
                proposal.CandidateReference is { Length: > 120 } ||
                proposal.CandidateReference?.Any(char.IsControl) == true ||
                proposal.SourcePath is not null &&
                    !ProjectJourneySourcePath.IsSafe(proposal.SourcePath) ||
                proposal.ProposedFields is null ||
                proposal.ProposedFields.Count is < 1 or > 30 ||
                proposal.BaseFields is null ||
                proposal.BaseFields.Count > 30)
            {
                throw new InvalidDataException(
                    "Proposition IA invalide, dupliquée ou hors contrat.");
            }

            ValidateFields(proposal.BaseFields);
            ValidateFields(proposal.ProposedFields);
        }
    }

    /// <summary>
    /// Only descriptive fields are admitted. Identity, approvals, verdicts,
    /// deletion and status transitions are never proposal fields.
    /// </summary>
    /// <param name="fields">Untrusted user-proposed key/value pairs.</param>
    private static void ValidateFields(IReadOnlyDictionary<string, string> fields)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> field in fields)
        {
            string name = field.Key;
            if (name.Length is < 1 or > 48 ||
                !seen.Add(name) ||
                name[0] is not (>= 'a' and <= 'z') ||
                !name.All(character => character is
                    (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or
                    (>= '0' and <= '9') or '_') ||
                ForbiddenFields.Contains(name) ||
                field.Value is null ||
                field.Value.Length > 8000 ||
                field.Value.Contains('\0'))
            {
                throw new InvalidDataException(
                    "Champ proposé interdit, invalide ou ambigu.");
            }
        }
    }
}

/// <summary>One human-review-only three-way field classification.</summary>
public enum ProjectAiFieldDifferenceKind
{
    CandidateChange,
    AlreadyPresent,
    ConcurrentConflict,
    UnknownBase
}

/// <summary>Result of comparing the AI's base with current and suggested text.</summary>
/// <param name="Field">Safe field key.</param>
/// <param name="Base">Known base or null.</param>
/// <param name="Current">Current local value or null.</param>
/// <param name="Suggested">Untrusted AI proposal.</param>
/// <param name="Kind">Requires human review, never authorizes a write.</param>
public sealed record ProjectAiFieldDifference(
    string Field, string? Base, string? Current, string Suggested,
    ProjectAiFieldDifferenceKind Kind);

/// <summary>
/// Pure, non-mutating three-way comparison; missing base is never treated
/// as approval to overwrite current local data.
/// </summary>
public static class ProjectAiThreeWayDiff
{
    /// <summary>Produces one review row per proposed field.</summary>
    /// <param name="baseFields">Values at prompt preparation, if recorded.</param>
    /// <param name="currentFields">Current local values from an authoritative read.</param>
    /// <param name="proposedFields">Untrusted AI suggestions.</param>
    /// <returns>Review rows with explicit conflicts and unknown baselines.</returns>
    public static IReadOnlyList<ProjectAiFieldDifference> Compare(
        IReadOnlyDictionary<string, string> baseFields,
        IReadOnlyDictionary<string, string> currentFields,
        IReadOnlyDictionary<string, string> proposedFields)
    {
        ArgumentNullException.ThrowIfNull(baseFields);
        ArgumentNullException.ThrowIfNull(currentFields);
        ArgumentNullException.ThrowIfNull(proposedFields);
        List<ProjectAiFieldDifference> rows = [];
        foreach (KeyValuePair<string, string> proposed in proposedFields)
        {
            bool hasBase = baseFields.TryGetValue(proposed.Key,
                out string? baseline);
            currentFields.TryGetValue(proposed.Key, out string? current);
            ProjectAiFieldDifferenceKind kind = !hasBase
                ? ProjectAiFieldDifferenceKind.UnknownBase
                : string.Equals(current, proposed.Value,
                    StringComparison.Ordinal)
                    ? ProjectAiFieldDifferenceKind.AlreadyPresent
                    : string.Equals(current, baseline,
                        StringComparison.Ordinal)
                        ? ProjectAiFieldDifferenceKind.CandidateChange
                        : ProjectAiFieldDifferenceKind.ConcurrentConflict;
            rows.Add(new ProjectAiFieldDifference(
                proposed.Key, baseline, current, proposed.Value, kind));
        }

        return rows;
    }
}
