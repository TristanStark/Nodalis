using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Tests;

/// <summary>Human campaign lifecycle; no status follows automatically from a test.</summary>
public enum ProjectTestCampaignState
{
    Draft,
    InProgress,
    InQualification,
    Closed,
    Canceled
}

/// <summary>A test outcome is distinct from coverage, validation and execution.</summary>
public enum ProjectTestOutcome
{
    Passed,
    Failed,
    Blocked,
    NotApplicable
}

/// <summary>
/// One immutable exact test definition at passage start. Markdown is included
/// so an old passage is inspectable even after the original file changes.
/// </summary>
public sealed record ProjectTestFrozenDefinition
{
    /// <summary>Stable source block ID.</summary>
    public required Guid TestId { get; init; }

    /// <summary>Human-readable test reference at capture.</summary>
    public required string Reference { get; init; }

    /// <summary>Workspace-relative source location at capture.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Exact captured Markdown test section.</summary>
    public required string Markdown { get; init; }

    /// <summary>SHA-256 of the exact section, not the whole source cahier.</summary>
    public required string Sha256 { get; init; }

    /// <summary>One-based heading line at capture, only a navigation hint.</summary>
    public required int HeadingLine { get; init; }
}

/// <summary>
/// Human-recorded result. A passage has at most one result per captured test;
/// a re-execution creates another frozen passage instead of rewriting history.
/// </summary>
public sealed record ProjectTestResultEvent
{
    /// <summary>Unique result event identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Exact captured test GUID.</summary>
    public required Guid TestId { get; init; }

    /// <summary>Human-declared outcome; NotRun is represented by no event.</summary>
    public required ProjectTestOutcome Outcome { get; init; }

    /// <summary>Human executor.</summary>
    public required string Actor { get; init; }

    /// <summary>Human justification.</summary>
    public required string Rationale { get; init; }

    /// <summary>Optional evidence note; no proof validation is inferred.</summary>
    public string? EvidenceNote { get; init; }

    /// <summary>Time of the explicit human declaration.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
}

/// <summary>One immutable selection/context and append-only results.</summary>
public sealed record ProjectTestPassage
{
    /// <summary>Permanent passage GUID.</summary>
    public required Guid Id { get; init; }

    /// <summary>Monotonic passage number inside its campaign.</summary>
    public required int Number { get; init; }

    /// <summary>Frozen execution environment.</summary>
    public required string Environment { get; init; }

    /// <summary>Frozen application or deployment version.</summary>
    public required string ApplicationVersion { get; init; }

    /// <summary>Human who authorized this new passage.</summary>
    public required string StartedBy { get; init; }

    /// <summary>Start of this passage; earlier passages cannot be rewritten.</summary>
    public required DateTimeOffset StartedUtc { get; init; }

    /// <summary>Exact test blocks frozen when passage began.</summary>
    public IReadOnlyList<ProjectTestFrozenDefinition> Tests { get; init; } = [];

    /// <summary>Explicit outcomes, append-only and one per test.</summary>
    public IReadOnlyList<ProjectTestResultEvent> Results { get; init; } = [];
}

/// <summary>
/// Portable Markdown-based campaign; embedded JSON is authoritative while the
/// Markdown heading remains a readable entry point in the worktree.
/// </summary>
public sealed record ProjectTestCampaign
{
    /// <summary>Schema version.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Stable campaign GUID.</summary>
    public required Guid Id { get; init; }

    /// <summary>Owning project GUID.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Readable REC-xxxx reference.</summary>
    public required string Reference { get; init; }

    /// <summary>Human campaign title.</summary>
    public required string Title { get; init; }

    /// <summary>Creation time.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>Human-owned campaign lifecycle.</summary>
    public ProjectTestCampaignState State { get; init; } =
        ProjectTestCampaignState.Draft;

    /// <summary>Passages ordered by number; each freezes selection and context.</summary>
    public IReadOnlyList<ProjectTestPassage> Passages { get; init; } = [];
}

/// <summary>
/// Strict, bounded Markdown plus JSON campaign contract; historical snapshots
/// and results can only be extended by the service, never silently inferred.
/// </summary>
public static class ProjectTestCampaignMarkdownCodec
{
    private const string Marker = "<!-- nodalis:test-campaign:v1 -->";
    private const string Opening = "\n\n" + Marker + "\n\n~~~json\n";
    private const string Closing = "\n~~~\n";
    private const int MaxMarkdownCharacters = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    /// <summary>Recognizes positive REC-0001..REC-9999 references.</summary>
    /// <param name="reference">Readable code.</param>
    /// <returns>Whether it is canonical.</returns>
    public static bool IsReference(string? reference) =>
        reference is { Length: 8 } &&
        reference.StartsWith("REC-", StringComparison.Ordinal) &&
        int.TryParse(reference[4..], out int number) &&
        number is >= 1 and <= 9999 &&
        string.Equals(reference,
            "REC-" + number.ToString("D4",
                System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>Validates immutable identities, snapshots and result linkage.</summary>
    /// <param name="campaign">Potential campaign.</param>
    public static void Validate(ProjectTestCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        if (campaign.SchemaVersion != 1 ||
            campaign.Id == Guid.Empty ||
            campaign.ProjectId == Guid.Empty ||
            !IsReference(campaign.Reference) ||
            !SingleLine(campaign.Title, 240) ||
            campaign.CreatedUtc == default ||
            !Enum.IsDefined(campaign.State) ||
            campaign.Passages is null ||
            campaign.Passages.Count > 100)
        {
            throw new InvalidDataException("Campagne REC invalide.");
        }

        HashSet<Guid> passageIds = [];
        int number = 0;
        foreach (ProjectTestPassage passage in campaign.Passages)
        {
            number++;
            if (passage.Id == Guid.Empty ||
                !passageIds.Add(passage.Id) ||
                passage.Number != number ||
                !SingleLine(passage.Environment, 160) ||
                !SingleLine(passage.ApplicationVersion, 160) ||
                !SingleLine(passage.StartedBy, 160) ||
                passage.StartedUtc == default ||
                passage.Tests is null ||
                passage.Tests.Count is < 1 or > 500 ||
                passage.Results is null)
            {
                throw new InvalidDataException("Passage figé invalide.");
            }

            HashSet<Guid> frozenIds = [];
            foreach (ProjectTestFrozenDefinition test in passage.Tests)
            {
                if (test.TestId == Guid.Empty ||
                    !frozenIds.Add(test.TestId) ||
                    !ProjectTestDefinitionCodec.IsReference(test.Reference) ||
                    string.IsNullOrWhiteSpace(test.SourceRelativePath) ||
                    test.SourceRelativePath.Length > 2048 ||
                    test.SourceRelativePath.Any(char.IsControl) ||
                    test.SourceRelativePath.Contains('\\') ||
                    test.SourceRelativePath.StartsWith('/') ||
                    test.SourceRelativePath.Split('/').Any(segment =>
                        segment is "." or ".." or "") ||
                    test.HeadingLine < 1 ||
                    string.IsNullOrWhiteSpace(test.Markdown) ||
                    test.Markdown.Length > 512 * 1024 ||
                    !ValidSha(test.Sha256) ||
                    !string.Equals(test.Sha256,
                        Convert.ToHexString(SHA256.HashData(
                            Encoding.UTF8.GetBytes(test.Markdown))),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Instantané de test invalide ou altéré.");
                }

                ProjectTestDefinitionScan extracted =
                    ProjectTestDefinitionCodec.Inspect(test.Markdown);
                if (extracted.Tests.Count != 1 ||
                    extracted.Tests[0].Id != test.TestId ||
                    extracted.Tests[0].Reference != test.Reference ||
                    extracted.Tests[0].HeadingLine != 1 ||
                    !extracted.Tests[0].CanFreeze ||
                    !string.Equals(extracted.Tests[0].Markdown,
                        test.Markdown, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Instantané sans bloc de test canonique unique.");
                }
            }

            HashSet<Guid> resultIds = [];
            HashSet<Guid> answered = [];
            foreach (ProjectTestResultEvent result in passage.Results)
            {
                if (result.Id == Guid.Empty ||
                    !resultIds.Add(result.Id) ||
                    !frozenIds.Contains(result.TestId) ||
                    !answered.Add(result.TestId) ||
                    !Enum.IsDefined(result.Outcome) ||
                    !SingleLine(result.Actor, 160) ||
                    !SingleLine(result.Rationale, 2000) ||
                    result.EvidenceNote is { Length: > 4000 } ||
                    result.EvidenceNote?.Any(char.IsControl) == true ||
                    result.RecordedUtc == default ||
                    result.RecordedUtc < passage.StartedUtc)
                {
                    throw new InvalidDataException(
                        "Résultat absent, doublonné ou non historisable.");
                }
            }
        }
    }

    /// <summary>Serializes canonical readable Markdown with embedded JSON.</summary>
    /// <param name="campaign">Fully validated campaign.</param>
    /// <returns>One portable Markdown source.</returns>
    public static string Format(ProjectTestCampaign campaign)
    {
        Validate(campaign);
        string header = "# " + campaign.Reference + " — " +
            campaign.Title;
        string body = JsonSerializer.Serialize(campaign, Options);
        string markdown = header + Opening + body + Closing;
        if (markdown.Length > MaxMarkdownCharacters)
        {
            throw new InvalidDataException(
                "Campagne trop volumineuse (>16 Mio).");
        }

        return markdown;
    }

    /// <summary>Rejects any missing, duplicated or malformed payload marker.</summary>
    /// <param name="markdown">Exact UTF-8 Markdown source.</param>
    /// <returns>Validated campaign, never a legacy auto-migration.</returns>
    public static ProjectTestCampaign Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxMarkdownCharacters ||
            !markdown.StartsWith("# REC-", StringComparison.Ordinal) ||
            !markdown.EndsWith(Closing, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Fiche de campagne absente, incompatible ou trop volumineuse.");
        }

        int marker = markdown.IndexOf(Opening, StringComparison.Ordinal);
        if (marker <= 0 || markdown.IndexOf(
                Opening, marker + Opening.Length,
                StringComparison.Ordinal) >= 0)
        {
            throw new InvalidDataException(
                "Un seul bloc de données de campagne est attendu.");
        }

        string body = markdown[
            (marker + Opening.Length)..^Closing.Length];
        try
        {
            ProjectTestCampaign campaign = JsonSerializer.Deserialize<
                ProjectTestCampaign>(body, Options) ??
                throw new InvalidDataException(
                    "Données de campagne manquantes.");
            Validate(campaign);
            if (!string.Equals(markdown, Format(campaign),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Campagne non canonique ou source modifiée hors journal.");
            }

            return campaign;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Données de campagne JSON malformées.", exception);
        }
    }

    /// <summary>Checks a nonempty single-line human declaration.</summary>
    /// <param name="value">Title, actor or environment.</param>
    /// <param name="maximum">Maximum characters.</param>
    /// <returns>Whether it is admissible.</returns>
    private static bool SingleLine(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximum && !value.Any(char.IsControl);

    /// <summary>Checks a real SHA-256 hex string.</summary>
    /// <param name="sha">Saved source fingerprint.</param>
    /// <returns>Whether SHA is syntactically valid.</returns>
    private static bool ValidSha(string sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
}
