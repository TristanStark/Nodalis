using System.Globalization;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Markdown;

namespace Nodalis.Core.Sfd;

/// <summary>
/// Strict, role-specific SFD Markdown identity with draft-only functional
/// templates. Do not interpret legacy or unversioned Markdown as SFD objects.
/// </summary>
public static class SfdDocumentMarkdownCodec
{
    private const int MaxSourceCharacters = 1024 * 1024;

    /// <summary>Returns the stable visible code prefix for an authored SFD role.</summary>
    /// <param name="kind">SFD role.</param>
    /// <returns>Human-readable code prefix.</returns>
    public static string Prefix(SfdDocumentKind kind) => kind switch
    {
        SfdDocumentKind.Process => "PM",
        SfdDocumentKind.Activity => "ACT",
        SfdDocumentKind.UserRequirement => "EXU",
        SfdDocumentKind.SystemRequirement => "EXS",
        SfdDocumentKind.RegulatoryRequirement => "EXR",
        SfdDocumentKind.Functionality => "FCT",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Composes the compact visible ACT code for either historical or new PM labels.</summary>
    /// <param name="processReference">Verified process reference (PM-0001 or PM001).</param>
    /// <param name="sequence">Sequence inside this single business process.</param>
    /// <returns>Compact identifier such as PM001-ACT002.</returns>
    public static string ActivityReference(string processReference, int sequence)
    {
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(processReference,
                @"^PM-?([0-9]{3,4})$");
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int processNumber) ||
            processNumber is < 1 or > 9999 ||
            sequence is < 1 or > 9999)
        {
            throw new InvalidDataException("Référence PM ou numéro ACT invalide.");
        }

        return "PM" + processNumber.ToString("D3", CultureInfo.InvariantCulture) +
            "-ACT" + sequence.ToString("D3", CultureInfo.InvariantCulture);
    }

    /// <summary>Recognizes new flat ACT references without affecting historical IDs.</summary>
    /// <param name="reference">Document reference to inspect.</param>
    /// <returns>True when the code follows PM001-ACT002.</returns>
    public static bool IsCompactActivityReference(string reference) =>
        !string.IsNullOrEmpty(reference) &&
        System.Text.RegularExpressions.Regex.IsMatch(reference,
            @"^PM[0-9]{3,4}-ACT[0-9]{3,4}$");

    /// <summary>Extracts the numeric ACT sequence from compact and historical codes.</summary>
    /// <param name="reference">An ACT code in one of the supported formats.</param>
    /// <returns>Sequence number local to the process for parent-scoped references.</returns>
    public static int ActivitySequence(string reference)
    {
        System.Text.RegularExpressions.Match match =
            System.Text.RegularExpressions.Regex.Match(reference,
                @"(?:ACT-?)([0-9]{3,4})$");
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int sequence) ||
            sequence is < 1 or > 9999)
        {
            throw new InvalidDataException("Numéro d'activité SFD invalide.");
        }

        return sequence;
    }

    /// <summary>Checks a complete SFD identity without imposing completion cardinalities on drafts.</summary>
    /// <param name="card">Versioned document identity.</param>
    public static void Validate(SfdDocumentCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.SchemaVersion != SfdDocumentCard.CurrentSchemaVersion ||
            card.Id == Guid.Empty || card.ProjectId == Guid.Empty ||
            !Enum.IsDefined(card.Kind) ||
            !Enum.IsDefined(card.Certainty) ||
            card.CreatedUtc == default ||
            string.IsNullOrWhiteSpace(card.Title) ||
            card.Title.Length > 240 ||
            card.Title.Any(char.IsControl))
        {
            throw new InvalidDataException("Fiche SFD invalide.");
        }

        string prefix = Prefix(card.Kind) + "-";
        bool legacyReference = System.Text.RegularExpressions.Regex.IsMatch(
            card.Reference, "^" + prefix + @"[0-9]{4}$");
        bool parentScopedActivity = card.Kind == SfdDocumentKind.Activity &&
            (System.Text.RegularExpressions.Regex.IsMatch(card.Reference,
                @"^PM-[0-9]{3,4}-ACT-[0-9]{3,4}$") ||
             IsCompactActivityReference(card.Reference));
        int lastSeparator = card.Reference.LastIndexOf('-');
        bool positiveSuffix = lastSeparator >= 0 &&
            (card.Kind == SfdDocumentKind.Activity &&
                parentScopedActivity
                ? ActivitySequence(card.Reference) is >= 1 and <= 9999
                : int.TryParse(card.Reference[(lastSeparator + 1)..],
                    NumberStyles.None, CultureInfo.InvariantCulture,
                    out int number) && number is >= 1 and <= 9999);
        bool positiveParent = true;
        if (parentScopedActivity)
        {
            System.Text.RegularExpressions.Match processMatch =
                System.Text.RegularExpressions.Regex.Match(card.Reference,
                    @"^PM-?([0-9]{3,4})-ACT-?[0-9]{3,4}$");
            positiveParent = processMatch.Success &&
                int.TryParse(processMatch.Groups[1].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int processNumber) &&
                processNumber is >= 1 and <= 9999;
        }

        if ((!legacyReference && !parentScopedActivity) || !positiveSuffix ||
            !positiveParent)
        {
            throw new InvalidDataException("Référence SFD invalide.");
        }

        bool requiresParent = card.Kind is SfdDocumentKind.Activity or
            SfdDocumentKind.UserRequirement or SfdDocumentKind.SystemRequirement or
            SfdDocumentKind.RegulatoryRequirement;
        if (requiresParent != card.ParentId.HasValue ||
            card.ParentId == Guid.Empty ||
            (card.Kind == SfdDocumentKind.Functionality) != card.FunctionalityMode.HasValue ||
            (card.FunctionalityMode.HasValue && !Enum.IsDefined(card.FunctionalityMode.Value)))
        {
            throw new InvalidDataException("Parent ou catégorie de fonctionnalité SFD invalide.");
        }
    }

    /// <summary>Produces a distinct, editable Markdown draft without invented C, SC or RM.</summary>
    /// <param name="card">Validated authored identity.</param>
    /// <returns>Portable Markdown containing stable metadata and role-specific fields.</returns>
    public static string Format(SfdDocumentCard card)
    {
        Validate(card);
        StringBuilder text = new();
        text.AppendLine("---")
            .AppendLine("nodalis-sfd-schema: 1")
            .Append("sfd-id: ").AppendLine(card.Id.ToString("D"))
            .Append("project-id: ").AppendLine(card.ProjectId.ToString("D"))
            .Append("sfd-kind: ").AppendLine(card.Kind.ToString())
            .Append("reference: ").AppendLine(card.Reference)
            .Append("title: ").AppendLine(JsonSerializer.Serialize(card.Title))
            .Append("created-utc: ").AppendLine(card.CreatedUtc.ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture))
            .Append("certainty: ").AppendLine(card.Certainty.ToString());
        if (card.ParentId is Guid parentId)
        {
            text.Append("parent-id: ").AppendLine(parentId.ToString("D"));
        }

        if (card.FunctionalityMode is SfdFunctionalityMode mode)
        {
            text.Append("functionality-mode: ").AppendLine(mode.ToString());
        }

        text.AppendLine("---")
            .Append("# ").Append(card.Reference).Append(" — ").AppendLine(card.Title)
            .AppendLine()
            .AppendLine("## Sources et provenance")
            .AppendLine("_À confirmer : renseigner les sources métier, sans inventer de preuve._")
            .AppendLine();

        switch (card.Kind)
        {
            case SfdDocumentKind.Process:
                text.AppendLine("## Finalité et périmètre")
                    .AppendLine().AppendLine("## Déclencheur et acteurs")
                    .AppendLine().AppendLine("## Entrées et résultats")
                    .AppendLine().AppendLine("## Déroulement nominal et alternatives")
                    .AppendLine().AppendLine("## Activités");
                break;
            case SfdDocumentKind.Activity:
                text.AppendLine("## Objectif et processus parent")
                    .AppendLine().AppendLine("## Déclencheur et acteurs")
                    .AppendLine().AppendLine("## Entrées, décisions et résultat")
                    .AppendLine().AppendLine("## Activités suivantes")
                    .AppendLine().AppendLine("## Synthèse réutilisable pour les fonctionnalités")
                    .AppendLine("_Aucune exigence dans la fiche canonique de l'activité._");
                break;
            case SfdDocumentKind.UserRequirement:
                AppendRequirement(text,
                    "Quand [condition], l'utilisateur doit pouvoir [action / résultat].",
                    "En tant que [RÔLE], je veux [ACTION] afin de [BUT / BÉNÉFICE] sur [CANAL À CONFIRMER].");
                break;
            case SfdDocumentKind.SystemRequirement:
                AppendRequirement(text,
                    "Quand [condition], le système doit permettre de [action / résultat].",
                    null);
                break;
            case SfdDocumentKind.RegulatoryRequirement:
                text.AppendLine("## Source réglementaire applicable")
                    .AppendLine("_À confirmer : ne pas considérer une obligation comme établie sans source._")
                    .AppendLine();
                AppendRequirement(text,
                    "Quand [condition], le système doit respecter [obligation applicable].",
                    null);
                break;
            case SfdDocumentKind.Functionality:
                text.AppendLine("## Capacité de la solution")
                    .AppendLine().AppendLine("## Activités liées (GUID)")
                    .AppendLine("_À associer explicitement ; une fonctionnalité peut couvrir plusieurs activités._")
                    .AppendLine().AppendLine("## Opérations, données et contrôles")
                    .AppendLine().AppendLine("## Entrées, sorties et erreurs")
                    .AppendLine().AppendLine("## Exigences de mise en œuvre (EMO)")
                    .AppendLine("_Le placement documentaire des EMO reste à arbitrer._");
                break;
        }

        return text.ToString();
    }

    /// <summary>Reads an explicitly versioned SFD identity without altering the Markdown body.</summary>
    /// <param name="markdown">Existing document source.</param>
    /// <returns>Validated card, or an error for ambiguous/unversioned documents.</returns>
    public static SfdDocumentCard Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxSourceCharacters)
        {
            throw new InvalidDataException("Fiche SFD trop volumineuse.");
        }

        string[] lines = markdown.TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n');
        if (lines.Length < 4 || lines[0] != "---")
        {
            throw new InvalidDataException("Métadonnées SFD absentes.");
        }

        HashSet<string> keys = new(StringComparer.Ordinal);
        bool closed = false;
        for (int index = 1; index < lines.Length && index < 25; index++)
        {
            if (lines[index] == "---")
            {
                closed = true;
                break;
            }

            int colon = lines[index].IndexOf(':');
            if (colon < 1 || !keys.Add(lines[index][..colon]))
            {
                throw new InvalidDataException("Métadonnées SFD dupliquées ou malformées.");
            }
        }

        if (!closed)
        {
            throw new InvalidDataException("En-tête SFD non fermé.");
        }

        MarkdownFrontMatterDocument parsed = MarkdownFrontMatterParser.Parse(markdown);
        if (!parsed.HasFrontMatter || !int.TryParse(
                Required(parsed.Properties, "nodalis-sfd-schema"),
                NumberStyles.None, CultureInfo.InvariantCulture, out int schema) ||
            schema != SfdDocumentCard.CurrentSchemaVersion ||
            !Guid.TryParseExact(Required(parsed.Properties, "sfd-id"), "D", out Guid id) ||
            !Guid.TryParseExact(Required(parsed.Properties, "project-id"), "D", out Guid projectId) ||
            !Enum.TryParse(Required(parsed.Properties, "sfd-kind"), false,
                out SfdDocumentKind kind) ||
            !Enum.TryParse(Required(parsed.Properties, "certainty"), false,
                out SfdAssertionCertainty certainty) ||
            !DateTimeOffset.TryParse(Required(parsed.Properties, "created-utc"),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out DateTimeOffset createdUtc))
        {
            throw new InvalidDataException("Métadonnées SFD non conformes.");
        }

        string? title;
        try
        {
            title = JsonSerializer.Deserialize<string>(Required(parsed.Properties, "title"));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Titre SFD invalide.", exception);
        }

        Guid? parentId = null;
        if (parsed.Properties.TryGetValue("parent-id", out string? parentRaw))
        {
            if (!Guid.TryParseExact(parentRaw, "D", out Guid parent))
            {
                throw new InvalidDataException("Référence parent SFD invalide.");
            }

            parentId = parent;
        }

        SfdFunctionalityMode? mode = null;
        if (parsed.Properties.TryGetValue("functionality-mode", out string? modeRaw))
        {
            if (!Enum.TryParse(modeRaw, false, out SfdFunctionalityMode parsedMode))
            {
                throw new InvalidDataException("Catégorie IHM SFD invalide.");
            }

            mode = parsedMode;
        }

        SfdDocumentCard card = new()
        {
            Id = id,
            ProjectId = projectId,
            Kind = kind,
            Reference = Required(parsed.Properties, "reference"),
            Title = title ?? string.Empty,
            CreatedUtc = createdUtc,
            Certainty = certainty,
            ParentId = parentId,
            FunctionalityMode = mode
        };
        Validate(card);
        return card;
    }

    /// <summary>Returns one mandatory metadata field, failing closed if missing.</summary>
    /// <param name="properties">Parsed front matter fields.</param>
    /// <param name="key">Required metadata name.</param>
    /// <returns>Unmodified value.</returns>
    private static string Required(IReadOnlyDictionary<string, string> properties, string key) =>
        properties.TryGetValue(key, out string? value)
            ? value
            : throw new InvalidDataException("Métadonnée SFD manquante : " + key);

    /// <summary>Adds required textual patterns as guidance, not actual fake conditions.</summary>
    /// <param name="text">Output Markdown.</param>
    /// <param name="condition">Confirmed condition formalism.</param>
    /// <param name="subcondition">Optional confirmed EXU sub-condition formalism.</param>
    private static void AppendRequirement(StringBuilder text, string condition,
        string? subcondition)
    {
        text.AppendLine("## Besoin métier et résultat attendu")
            .AppendLine().AppendLine("## Conditions (C)")
            .Append("> Patron de rédaction : ").AppendLine(condition)
            .AppendLine("_Aucune condition renseignée ; brouillon incomplet._")
            .AppendLine().AppendLine("## Sous-conditions (SC)");
        if (subcondition is not null)
        {
            text.Append("> Patron de rédaction : ").AppendLine(subcondition);
        }
        else
        {
            text.AppendLine("_Décrire un comportement précis et vérifiable ; pas de patron imposé._");
        }

        text.AppendLine("_Aucune sous-condition renseignée ; brouillon incomplet._")
            .AppendLine().AppendLine("## Références de règles métiers (RM)")
            .AppendLine("_Référencer les identifiants du catalogue DOCUMENTATION/liste des règles métiers.md ; ne pas copier les règles._");
    }
}
