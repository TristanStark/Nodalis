using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nodalis.Core.Risks;

/// <summary>
/// Strict, portable Markdown/front-matter contract for new v2 risk records.
/// Legacy documents are never interpreted as automatically migrated risks.
/// </summary>
public static class ProjectRiskMarkdownCodec
{
    private const int MetadataMaxLength = 4096;

    /// <summary>Checks one human-assigned numbered risk reference.</summary>
    /// <param name="reference">Reference such as RIS-0001.</param>
    /// <returns>Whether its canonical number lies in 1..9999.</returns>
    public static bool IsReference(string? reference) =>
        reference is { Length: 8 } &&
        reference.StartsWith("RIS-", StringComparison.Ordinal) &&
        int.TryParse(reference[4..], NumberStyles.None,
            CultureInfo.InvariantCulture, out int number) &&
        number is >= 1 and <= 9999 &&
        string.Equals("RIS-" + number.ToString("D4",
            CultureInfo.InvariantCulture), reference, StringComparison.Ordinal);

    /// <summary>Rejects invalid or mismatched metadata and unknown ratings.</summary>
    /// <param name="card">Risk card to verify.</param>
    public static void Validate(ProjectRiskCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.SchemaVersion != ProjectRiskCard.CurrentSchemaVersion ||
            card.Id == Guid.Empty || card.ProjectId == Guid.Empty ||
            !IsReference(card.Reference) ||
            string.IsNullOrWhiteSpace(card.Title) || card.Title.Length > 240 ||
            card.Title.Any(char.IsControl) ||
            card.CreatedUtc == default ||
            !Enum.IsDefined(card.Qualification) ||
            !Enum.IsDefined(card.Lifecycle) ||
            card.Initial is null || card.Residual is null ||
            card.TreatmentHeadId == Guid.Empty)
        {
            throw new InvalidDataException("Fiche de risque invalide.");
        }

        ProjectRiskMatrix.Evaluate(card.Initial);
        ProjectRiskMatrix.Evaluate(card.Residual);
    }

    /// <summary>
    /// Creates one Markdown document in memory; no file or folder is touched.
    /// Human evaluation remains explicitly unassessed at initial creation.
    /// </summary>
    /// <param name="card">Validated human-approved metadata.</param>
    /// <returns>Portable Markdown with JSON-escaped YAML scalar title.</returns>
    public static string Format(ProjectRiskCard card)
    {
        Validate(card);
        StringBuilder markdown = new();
        markdown.AppendLine("---")
            .AppendLine("nodalis-risk-schema: 1")
            .Append("risk-id: ").AppendLine(card.Id.ToString("D"))
            .Append("project-id: ").AppendLine(card.ProjectId.ToString("D"))
            .Append("reference: ").AppendLine(card.Reference)
            .Append("title: ").AppendLine(JsonSerializer.Serialize(card.Title))
            .Append("created-utc: ").AppendLine(card.CreatedUtc.ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture))
            .Append("qualification: ").AppendLine(card.Qualification.ToString())
            .Append("lifecycle: ").AppendLine(card.Lifecycle.ToString())
            .Append("initial-probability: ").AppendLine(
                card.Initial.Probability?.ToString(CultureInfo.InvariantCulture) ??
                "null")
            .Append("initial-impact: ").AppendLine(
                card.Initial.Impact?.ToString(CultureInfo.InvariantCulture) ??
                "null")
            .Append("residual-probability: ").AppendLine(
                card.Residual.Probability?.ToString(CultureInfo.InvariantCulture) ??
                "null")
            .Append("residual-impact: ").AppendLine(
                card.Residual.Impact?.ToString(CultureInfo.InvariantCulture) ??
                "null")
            .Append("treatment-head-id: ").AppendLine(
                card.TreatmentHeadId?.ToString("D") ?? "null")
            .AppendLine("---")
            .Append("# ").Append(card.Reference).Append(" — ")
            .AppendLine(card.Title)
            .AppendLine()
            .AppendLine("## Contexte et conséquences")
            .AppendLine()
            .AppendLine("## Mesures de prévention et de réduction")
            .AppendLine()
            .AppendLine("## Plan de contingence");
        return markdown.ToString();
    }

    /// <summary>
    /// Parses only explicit and valid v2 metadata. The rest of the Markdown
    /// remains user-owned and need not be rewritten for read-only discovery.
    /// </summary>
    /// <param name="markdown">Existing canonical Markdown.</param>
    /// <returns>One validated metadata record.</returns>
    public static ProjectRiskCard Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > 1024 * 1024)
        {
            throw new InvalidDataException("Fiche de risque trop volumineuse.");
        }

        string[] lines = markdown.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        if (lines.Length < 4 || lines[0] != "---")
        {
            throw new InvalidDataException(
                "Fichier sans en-tête de risque v2 : aucune migration implicite.");
        }

        Dictionary<string, string> fields =
            new(StringComparer.Ordinal);
        int size = 0;
        bool closed = false;
        for (int index = 1; index < lines.Length && index < 32; index++)
        {
            string line = lines[index];
            size += line.Length;
            if (size > MetadataMaxLength)
            {
                throw new InvalidDataException("Métadonnées de risque trop longues.");
            }

            if (line == "---")
            {
                closed = true;
                break;
            }

            int separator = line.IndexOf(':');
            if (separator < 1 ||
                !fields.TryAdd(line[..separator], line[(separator + 1)..].TrimStart()))
            {
                throw new InvalidDataException(
                    "Champ de risque malformé ou dupliqué.");
            }
        }

        if (!closed || fields.Count is not (12 or 13) ||
            fields.Count == 13 && !fields.ContainsKey("treatment-head-id") ||
            !fields.TryGetValue("nodalis-risk-schema", out string? schema) ||
            schema != "1")
        {
            throw new InvalidDataException(
                "En-tête de risque v2 manquant ou incompatible.");
        }

        try
        {
            string titleJson = fields["title"];
            string title = JsonSerializer.Deserialize<string>(titleJson) ??
                throw new InvalidDataException("Titre de risque vide.");
            if (!DateTimeOffset.TryParseExact(fields["created-utc"], "O",
                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out DateTimeOffset created) ||
                !Enum.TryParse(fields["qualification"], false,
                    out ProjectRiskQualification qualification) ||
                !Enum.TryParse(fields["lifecycle"], false,
                    out ProjectRiskLifecycle lifecycle))
            {
                throw new InvalidDataException("États ou date de risque invalides.");
            }

            ProjectRiskCard card = new()
            {
                Id = Guid.ParseExact(fields["risk-id"], "D"),
                ProjectId = Guid.ParseExact(fields["project-id"], "D"),
                Reference = fields["reference"],
                Title = title,
                CreatedUtc = created,
                Qualification = qualification,
                Lifecycle = lifecycle,
                Initial = new ProjectRiskRating(
                    ReadRatingPart(fields["initial-probability"]),
                    ReadRatingPart(fields["initial-impact"])),
                Residual = new ProjectRiskRating(
                    ReadRatingPart(fields["residual-probability"]),
                    ReadRatingPart(fields["residual-impact"])),
                TreatmentHeadId = fields.TryGetValue("treatment-head-id",
                    out string? headText) && headText != "null"
                    ? Guid.ParseExact(headText!, "D") : null
            };
            Validate(card);
            return card;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or
            FormatException or JsonException or ArgumentException)
        {
            throw new InvalidDataException(
                "Métadonnées de risque v2 non conformes.", exception);
        }
    }

    /// <summary>Decodes an explicit assessment number or unknown marker.</summary>
    /// <param name="text">Null literal or an integer.</param>
    /// <returns>Parsed value or null for unassessed.</returns>
    private static int? ReadRatingPart(string text)
    {
        if (text == "null")
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.None,
            CultureInfo.InvariantCulture, out int value))
        {
            throw new InvalidDataException("Valeur de criticité invalide.");
        }

        return value;
    }
}
