using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nodalis.Core.Sfd;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Narrow, structure-preserving SFD field adapter. Only the canonical title
/// and the dedicated AI-draft description section are editable in this slice.
/// It never recreates the rest of a Markdown document or its C/SC blocks.
/// </summary>
internal static class SfdDocumentThreeWayCodec
{
    private const string DescriptionHeading =
        "## Description proposée par IA — à qualifier";
    private static readonly Regex DescriptionHeadingPattern = new(
        @"(?m)^## Description proposée par IA — à qualifier[ \t]*\r?\n",
        RegexOptions.CultureInvariant);
    private static readonly Regex NextHeadingPattern = new(
        @"(?m)^#{1,2} ",
        RegexOptions.CultureInvariant);

    /// <summary>Reads just the AI-controlled description section, if present.</summary>
    /// <param name="markdown">Unmodified canonical Markdown.</param>
    /// <returns>Existing AI draft description or empty string.</returns>
    public static string Description(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        Match match = DescriptionHeadingPattern.Match(markdown);
        if (!match.Success)
        {
            return string.Empty;
        }

        Match next = NextHeadingPattern.Match(markdown,
            match.Index + match.Length);
        int end = next.Success ? next.Index : markdown.Length;
        string segment = markdown[(match.Index + match.Length)..end].Trim();
        if (segment.Length == 0)
        {
            return string.Empty;
        }

        string[] lines = segment.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n');
        // New AI descriptions are inert blockquotes; older ones remain plain
        // Markdown from previous controlled imports.
        if (lines.All(line => line.StartsWith("> ", StringComparison.Ordinal)
            || line == ">"))
        {
            return string.Join("\n", lines.Select(line =>
                (line == ">" ? "" : line[2..])
                .Replace("&lt;", "<", StringComparison.Ordinal)
                .Replace("&gt;", ">", StringComparison.Ordinal))).Trim();
        }

        return segment;
    }

    /// <summary>Compares two source states and one explicitly proposed field value.</summary>
    /// <param name="name">Stable title/description key.</param>
    /// <param name="before">Immutable baseline field.</param>
    /// <param name="current">Current on-disk field.</param>
    /// <param name="proposed">External suggestion.</param>
    /// <returns>Conflict-aware three-way field projection.</returns>
    public static SfdThreeWayField Compare(string name, string before,
        string current, string proposed)
    {
        SfdThreeWayFieldStatus status;
        if (string.Equals(current, proposed, StringComparison.Ordinal))
        {
            status = string.Equals(before, current, StringComparison.Ordinal)
                ? SfdThreeWayFieldStatus.Unchanged
                : SfdThreeWayFieldStatus.AlreadyCurrent;
        }
        else if (string.Equals(before, current, StringComparison.Ordinal))
        {
            status = SfdThreeWayFieldStatus.ProposedChange;
        }
        else
        {
            status = string.Equals(proposed, before, StringComparison.Ordinal)
                ? SfdThreeWayFieldStatus.KeptHumanChange
                : SfdThreeWayFieldStatus.Conflict;
        }

        return new SfdThreeWayField(name, before, current, proposed, status);
    }

    /// <summary>
    /// Replaces selected safe fields, leaving all unrelated Markdown (including
    /// human content and C/SC/RM markers) byte-for-byte unmodified.
    /// </summary>
    /// <param name="markdown">Exact current UTF-8 source.</param>
    /// <param name="card">Validated immutable identity.</param>
    /// <param name="title">Chosen new title; null means preserve.</param>
    /// <param name="description">Chosen AI draft description; null means preserve.</param>
    /// <returns>Updated UTF-8 Markdown source.</returns>
    public static string Apply(string markdown, SfdDocumentCard card,
        string? title, string? description)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        SfdDocumentMarkdownCodec.Validate(card);
        string updated = markdown;
        if (title is not null && title != card.Title)
        {
            string oldFront = "title: " + JsonSerializer.Serialize(card.Title);
            string newFront = "title: " + JsonSerializer.Serialize(title);
            updated = ReplaceExactLine(updated, oldFront, newFront);
            updated = ReplaceExactLine(updated,
                "# " + card.Reference + " — " + card.Title,
                "# " + card.Reference + " — " + title);
        }

        if (description is not null &&
            !string.Equals(description, Description(updated),
                StringComparison.Ordinal))
        {
            Match match = DescriptionHeadingPattern.Match(updated);
            string contents = Quote(description);
            if (!match.Success)
            {
                updated = updated.TrimEnd('\r', '\n') + "\n\n" +
                    DescriptionHeading + "\n\n" + contents + "\n";
            }
            else
            {
                Match next = NextHeadingPattern.Match(updated,
                    match.Index + match.Length);
                int end = next.Success ? next.Index : updated.Length;
                string before = updated[..(match.Index + match.Length)];
                string after = updated[end..];
                updated = before + "\n" + contents + "\n\n" + after;
            }
        }

        SfdDocumentCard verified = SfdDocumentMarkdownCodec.Parse(updated);
        if (verified.Id != card.Id || verified.ProjectId != card.ProjectId ||
            verified.Reference != card.Reference || verified.Kind != card.Kind ||
            verified.ParentId != card.ParentId ||
            verified.FunctionalityMode != card.FunctionalityMode ||
            verified.Certainty != card.Certainty ||
            verified.CreatedUtc != card.CreatedUtc ||
            (title is not null && verified.Title != title))
        {
            throw new InvalidDataException(
                "La mise à jour SFD a modifié un identifiant ou une propriété protégée.");
        }

        return updated;
    }

    /// <summary>Replaces exactly one complete Markdown line without moving folders.</summary>
    /// <param name="markdown">Existing source.</param>
    /// <param name="oldLine">Required exact existing line.</param>
    /// <param name="newLine">Reviewed replacement.</param>
    /// <returns>Updated source with remaining content preserved.</returns>
    private static string ReplaceExactLine(string markdown,
        string oldLine, string newLine)
    {
        string[] lines = markdown.Split('\n');
        int match = -1;
        for (int index = 0; index < lines.Length; index++)
        {
            if (lines[index].TrimEnd('\r') != oldLine)
            {
                continue;
            }

            if (match != -1)
            {
                throw new InvalidDataException(
                    "Ligne SFD à modifier ambiguë : mise à jour suspendue.");
            }

            match = index;
        }

        if (match == -1)
        {
            throw new InvalidDataException(
                "Titre de la fiche SFD non conforme au format canonique.");
        }

        lines[match] = newLine +
            (lines[match].EndsWith("\r", StringComparison.Ordinal) ? "\r" : "");
        return string.Join("\n", lines);
    }

    /// <summary>Encodes untrusted prose as inert Markdown blockquotes.</summary>
    /// <param name="description">Reviewed external text.</param>
    /// <returns>One safe, non-heading block.</returns>
    private static string Quote(string description)
    {
        string[] lines = description.Replace("\r\n", "\n",
            StringComparison.Ordinal).Replace('\r', '\n').Trim().Split('\n');
        StringBuilder result = new();
        foreach (string line in lines)
        {
            result.Append("> ").AppendLine(
                line.Replace("<", "&lt;", StringComparison.Ordinal)
                    .Replace(">", "&gt;", StringComparison.Ordinal));
        }

        return result.ToString().TrimEnd('\r', '\n');
    }
}
