using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nodalis.Core.Requirements;

/// <summary>Requirement classification; no category is inferred from a title.</summary>
public enum ProjectRequirementKind
{
    Unclassified,
    Functional,
    NonFunctional,
    Business,
    Technical,
    Security,
    Operations,
    Constraint
}

/// <summary>Human validation, distinct from implementation and verification.</summary>
public enum ProjectRequirementValidation
{
    Draft,
    ToClarify,
    Approved,
    Rejected,
    Obsolete
}

/// <summary>Implementation is never inferred from a linked task.</summary>
public enum ProjectRequirementImplementation
{
    NotStarted,
    InProgress,
    Implemented
}

/// <summary>Coverage by a test alone does not establish verification.</summary>
public enum ProjectRequirementVerification
{
    NotVerified,
    ToRequalify,
    Verified,
    Failed
}

/// <summary>
/// One portable EXI requirement identity. The three independent statuses
/// are intentionally never updated by parsing linked tasks or test results.
/// </summary>
public sealed record ProjectRequirementCard
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required Guid Id { get; init; }

    public required Guid ProjectId { get; init; }

    public required string Reference { get; init; }

    public required string Title { get; init; }

    public required DateTimeOffset CreatedUtc { get; init; }

    public ProjectRequirementKind Kind { get; init; } =
        ProjectRequirementKind.Unclassified;

    public ProjectRequirementValidation Validation { get; init; } =
        ProjectRequirementValidation.Draft;

    public ProjectRequirementImplementation Implementation { get; init; } =
        ProjectRequirementImplementation.NotStarted;

    public ProjectRequirementVerification Verification { get; init; } =
        ProjectRequirementVerification.NotVerified;
}

/// <summary>
/// Fail-closed Markdown/front-matter contract. Legacy documents are never
/// migrated or interpreted as v2 requirements on read.
/// </summary>
public static class ProjectRequirementMarkdownCodec
{
    private const int MaxSourceCharacters = 1024 * 1024;
    private const int MaxMetadataCharacters = 4096;

    /// <summary>Checks canonical, positive EXI-0001..EXI-9999 references.</summary>
    /// <param name="reference">Readable reference.</param>
    /// <returns>Whether the reference is canonical.</returns>
    public static bool IsReference(string? reference) =>
        reference is { Length: 8 } &&
        reference.StartsWith("EXI-", StringComparison.Ordinal) &&
        int.TryParse(reference[4..], NumberStyles.None,
            CultureInfo.InvariantCulture, out int number) &&
        number is >= 1 and <= 9999 &&
        string.Equals("EXI-" + number.ToString("D4",
            CultureInfo.InvariantCulture), reference, StringComparison.Ordinal);

    /// <summary>Validates one canonical requirement without changing it.</summary>
    /// <param name="card">Requirement metadata.</param>
    public static void Validate(ProjectRequirementCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.SchemaVersion != ProjectRequirementCard.CurrentSchemaVersion ||
            card.Id == Guid.Empty || card.ProjectId == Guid.Empty ||
            !IsReference(card.Reference) ||
            string.IsNullOrWhiteSpace(card.Title) || card.Title.Length > 240 ||
            card.Title.Any(char.IsControl) ||
            card.CreatedUtc == default ||
            !Enum.IsDefined(card.Kind) ||
            !Enum.IsDefined(card.Validation) ||
            !Enum.IsDefined(card.Implementation) ||
            !Enum.IsDefined(card.Verification))
        {
            throw new InvalidDataException("Fiche d'exigence v2 invalide.");
        }
    }

    /// <summary>Builds a draft Markdown card in memory, with no side effects.</summary>
    /// <param name="card">Explicitly created metadata.</param>
    /// <returns>Portable Markdown document.</returns>
    public static string Format(ProjectRequirementCard card)
    {
        Validate(card);
        StringBuilder markdown = new();
        markdown.AppendLine("---")
            .AppendLine("nodalis-requirement-schema: 1")
            .Append("requirement-id: ").AppendLine(card.Id.ToString("D"))
            .Append("project-id: ").AppendLine(card.ProjectId.ToString("D"))
            .Append("reference: ").AppendLine(card.Reference)
            .Append("title: ").AppendLine(JsonSerializer.Serialize(card.Title))
            .Append("created-utc: ").AppendLine(card.CreatedUtc.ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture))
            .Append("kind: ").AppendLine(card.Kind.ToString())
            .Append("validation: ").AppendLine(card.Validation.ToString())
            .Append("implementation: ").AppendLine(card.Implementation.ToString())
            .Append("verification: ").AppendLine(card.Verification.ToString())
            .AppendLine("---")
            .Append("# ").Append(card.Reference).Append(" — ")
            .AppendLine(card.Title)
            .AppendLine()
            .AppendLine("## Description vérifiable")
            .AppendLine()
            .AppendLine("## Origine, hypothèses et ambiguïtés")
            .AppendLine()
            .AppendLine("## Critères d'acceptation")
            .AppendLine()
            .AppendLine("## Dépendances et preuves");
        return markdown.ToString();
    }

    /// <summary>Reads only well-formed, explicitly versioned v2 metadata.</summary>
    /// <param name="markdown">Canonical Markdown, never modified.</param>
    /// <returns>Validated requirement identity and independent states.</returns>
    public static ProjectRequirementCard Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxSourceCharacters)
        {
            throw new InvalidDataException("Fiche d'exigence trop volumineuse.");
        }

        string[] lines = markdown.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        if (lines.Length < 4 || lines[0] != "---")
        {
            throw new InvalidDataException(
                "En-tête v2 d'exigence absent : aucune migration implicite.");
        }

        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        int metadataSize = 0;
        bool closed = false;
        for (int index = 1; index < lines.Length && index < 30; index++)
        {
            string line = lines[index];
            metadataSize += line.Length;
            if (metadataSize > MaxMetadataCharacters)
            {
                throw new InvalidDataException("Métadonnées d'exigence trop longues.");
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
                    "Champ d'exigence malformé ou dupliqué.");
            }
        }

        string[] allowedKeys =
        [
            "nodalis-requirement-schema", "requirement-id", "project-id",
            "reference", "title", "created-utc", "kind",
            "validation", "implementation", "verification"
        ];
        if (!closed || fields.Count != allowedKeys.Length ||
            fields.Keys.Any(key => !allowedKeys.Contains(key,
                StringComparer.Ordinal)) ||
            !fields.TryGetValue("nodalis-requirement-schema",
                out string? schema) || schema != "1")
        {
            throw new InvalidDataException(
                "Métadonnées d'exigence v2 incomplètes ou incompatibles.");
        }

        try
        {
            string title = JsonSerializer.Deserialize<string>(
                fields["title"]) ?? throw new InvalidDataException(
                    "Titre d'exigence absent.");
            if (!DateTimeOffset.TryParseExact(fields["created-utc"], "O",
                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out DateTimeOffset created) ||
                !Enum.TryParse(fields["kind"], false,
                    out ProjectRequirementKind kind) ||
                !Enum.TryParse(fields["validation"], false,
                    out ProjectRequirementValidation validation) ||
                !Enum.TryParse(fields["implementation"], false,
                    out ProjectRequirementImplementation implementation) ||
                !Enum.TryParse(fields["verification"], false,
                    out ProjectRequirementVerification verification))
            {
                throw new InvalidDataException(
                    "Date ou état d'exigence non conforme.");
            }

            ProjectRequirementCard card = new()
            {
                Id = Guid.ParseExact(fields["requirement-id"], "D"),
                ProjectId = Guid.ParseExact(fields["project-id"], "D"),
                Reference = fields["reference"],
                Title = title,
                CreatedUtc = created,
                Kind = kind,
                Validation = validation,
                Implementation = implementation,
                Verification = verification
            };
            Validate(card);
            return card;
        }
        catch (Exception exception) when (
            exception is FormatException or KeyNotFoundException or
            ArgumentException or JsonException)
        {
            throw new InvalidDataException(
                "Métadonnées d'exigence v2 non conformes.", exception);
        }
    }
}
