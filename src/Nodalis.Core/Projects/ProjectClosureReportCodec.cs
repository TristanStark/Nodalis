using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Projects;

/// <summary>Human review outcome, never an automatic archive authorization.</summary>
public enum ProjectClosureReviewOutcome
{
    ReviewRecorded,
    ReadyForArchive,
    Deferred
}

/// <summary>Frozen inventory row from a project-local, read-only audit.</summary>
/// <param name="Kind">Original business register category.</param>
/// <param name="Id">Stable source object GUID.</param>
/// <param name="Reference">Canonical human reference at review time.</param>
/// <param name="Title">Human title at review time.</param>
/// <param name="Detail">Human-declared status at review time.</param>
/// <param name="RelativePath">Original portable source path, if any.</param>
public sealed record ProjectClosureReviewItem(
    string Kind, Guid Id, string Reference, string Title,
    string Detail, string? RelativePath);

/// <summary>
/// One immutable human-signed closure review. It captures only the current
/// audit coverage, without changing any risk, task, incident or milestone.
/// </summary>
public sealed record ProjectClosureReview
{
    public required Guid Id { get; init; }
    public required int Number { get; init; }
    public required DateTimeOffset RecordedUtc { get; init; }
    public required string Actor { get; init; }
    public required string Rationale { get; init; }
    public required ProjectClosureReviewOutcome Outcome { get; init; }
    public required string SourceFingerprint { get; init; }
    public IReadOnlyList<ProjectClosureReviewItem> Items { get; init; } = [];
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}

/// <summary>Canonical single-document, append-only closure review history.</summary>
public sealed record ProjectClosureReport
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid ProjectId { get; init; }
    public IReadOnlyList<ProjectClosureReview> Reviews { get; init; } = [];
}

/// <summary>
/// Strict portable Markdown+JSON report codec. Rejects noncanonical or
/// unreviewed content instead of overwriting preexisting free-form notes.
/// </summary>
public static class ProjectClosureReportCodec
{
    private const int MaxCharacters = 8 * 1024 * 1024;
    private const string Prefix =
        "# Bilan de clôture\n\n<!-- nodalis:closure-report:v1 -->\n\n~~~json\n";
    private const string Suffix = "\n~~~\n";
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

    /// <summary>Validates all historical event identities and frozen fields.</summary>
    /// <param name="report">Untrusted persisted report.</param>
    public static void Validate(ProjectClosureReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.SchemaVersion != 1 || report.ProjectId == Guid.Empty ||
            report.Reviews is null || report.Reviews.Count > 100)
        {
            throw new InvalidDataException("Bilan de clôture non canonique.");
        }

        HashSet<Guid> ids = [];
        DateTimeOffset previous = DateTimeOffset.MinValue;
        for (int index = 0; index < report.Reviews.Count; index++)
        {
            ProjectClosureReview review = report.Reviews[index];
            if (review is null || review.Id == Guid.Empty ||
                !ids.Add(review.Id) || review.Number != index + 1 ||
                review.RecordedUtc == default || review.RecordedUtc < previous ||
                !ValidLabel(review.Actor, 160) ||
                !ValidNote(review.Rationale, 4000) ||
                !Enum.IsDefined(review.Outcome) ||
                !ValidHash(review.SourceFingerprint) ||
                review.Items is null || review.Items.Count > 12000 ||
                review.Diagnostics is null || review.Diagnostics.Count > 5000)
            {
                throw new InvalidDataException(
                    "Revue de clôture sans identité, auteur ou décision valide.");
            }

            HashSet<Guid> itemIds = [];
            foreach (ProjectClosureReviewItem item in review.Items)
            {
                if (item is null || item.Id == Guid.Empty ||
                    !itemIds.Add(item.Id) ||
                    !ValidLabel(item.Kind, 80) ||
                    !ValidLabel(item.Reference, 100) ||
                    !ValidLabel(item.Title, 240) ||
                    !ValidNote(item.Detail, 1000) ||
                    (item.RelativePath is not null &&
                     !ValidRelativePath(item.RelativePath)))
                {
                    throw new InvalidDataException(
                        "Objet source de clôture manquant ou ambigu.");
                }
            }

            if (review.Diagnostics.Any(item =>
                item is null || item.Length > 1000 || item.Contains('\0')))
            {
                throw new InvalidDataException(
                    "Diagnostic de clôture trop long ou invalide.");
            }

            previous = review.RecordedUtc;
        }
    }

    /// <summary>Formats the complete, versioned human history without mutation.</summary>
    /// <param name="report">Validated canonical source.</param>
    /// <returns>Portable Markdown with a single machine-readable JSON block.</returns>
    public static string Format(ProjectClosureReport report)
    {
        Validate(report);
        string markdown = Prefix + JsonSerializer.Serialize(report, Options) + Suffix;
        if (markdown.Length > MaxCharacters)
        {
            throw new InvalidDataException("Bilan de clôture trop volumineux.");
        }

        return markdown;
    }

    /// <summary>Parses only an exact supported version of this Markdown source.</summary>
    /// <param name="markdown">Complete external file, treated as untrusted.</param>
    /// <returns>Validated immutable closure history.</returns>
    public static ProjectClosureReport Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxCharacters ||
            !markdown.StartsWith(Prefix, StringComparison.Ordinal) ||
            !markdown.EndsWith(Suffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Bilan existant non compatible : aucun écrasement autorisé.");
        }

        string payload = markdown[Prefix.Length..^Suffix.Length];
        ProjectClosureReport report;
        try
        {
            report = JsonSerializer.Deserialize<ProjectClosureReport>(
                payload, Options) ??
                throw new InvalidDataException("Bilan de clôture vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "JSON de clôture invalide.", exception);
        }

        Validate(report);
        if (!string.Equals(Format(report), markdown, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Historique de clôture non canonique ou édité manuellement.");
        }

        return report;
    }

    /// <summary>Checks a stable hex SHA-256 reference.</summary>
    /// <param name="hash">Expected digest.</param>
    /// <returns>True for a 64-character hexadecimal fingerprint.</returns>
    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Checks a short, single-line human field.</summary>
    /// <param name="value">Human string.</param>
    /// <param name="maximum">Maximum supported length.</param>
    /// <returns>Whether input is safe and nonempty.</returns>
    private static bool ValidLabel(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        !value.Any(char.IsControl);

    /// <summary>Checks a required bounded multiline rationale.</summary>
    /// <param name="value">Human string.</param>
    /// <param name="maximum">Maximum supported length.</param>
    /// <returns>Whether the note is nonempty and bounded.</returns>
    private static bool ValidNote(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        !value.Contains('\0');

    /// <summary>Validates a source reference without resolving it after archiving.</summary>
    /// <param name="path">Portable source path.</param>
    /// <returns>Whether path cannot escape via traversal or rooted syntax.</returns>
    private static bool ValidRelativePath(string path)
    {
        try
        {
            ProjectJourneySourcePath.RequireSafe(path);
            return true;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or ArgumentException)
        {
            return false;
        }
    }
}
