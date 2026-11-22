using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nodalis.Core.Sfd;

/// <summary>
/// Readable, line-based, versioned C/SC blocks embedded in canonical Markdown.
/// Structure markers carry GUID identity independently of document path/title.
/// No block is generated when an EXU/EXS/EXR draft is first created.
/// </summary>
public static class SfdConditionBlockCodec
{
    private const string Start = "<!-- nodalis-sfd-conditions:v1 -->";
    private const string End = "<!-- /nodalis-sfd-conditions:v1 -->";
    private const string ConditionMarker = "<!-- nodalis-sfd-c:";
    private const string SubconditionMarker = "<!-- nodalis-sfd-sc:";
    private const int MaxBlocks = 2000;

    /// <summary>Checks a human-authored one-line C/SC text without inferring sources.</summary>
    /// <param name="statement">Content entered by the author.</param>
    /// <returns>Normalized one-line content.</returns>
    public static string RequireStatement(string statement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        string normalized = statement.Trim();
        if (normalized.Length > 1000 || normalized.Any(char.IsControl) ||
            normalized.Contains("<!--", StringComparison.Ordinal) ||
            normalized.Contains("-->", StringComparison.Ordinal) ||
            normalized.Contains("[condition]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[action / résultat]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[obligation applicable]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[RÔLE]", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("[ACTION]", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Renseignez une formulation réelle, sur une seule ligne (1000 caractères max).");
        }

        return normalized;
    }

    /// <summary>Checks only the confirmed formalism for this SFD requirement role.</summary>
    /// <param name="kind">SFD EXU/EXS/EXR role.</param>
    /// <param name="statement">Explicit author content.</param>
    /// <param name="isSubcondition">True for SC, otherwise C.</param>
    public static void ValidateFormalism(SfdDocumentKind kind,
        string statement, bool isSubcondition)
    {
        string value = RequireStatement(statement);
        if (isSubcondition)
        {
            if (kind == SfdDocumentKind.UserRequirement &&
                !Regex.IsMatch(value,
                    @"^En tant que .+, je veux .+ afin de .+ sur .+\.$",
                    RegexOptions.CultureInvariant))
            {
                throw new InvalidDataException(
                    "SC EXU : En tant que [RÔLE], je veux [ACTION] afin de [BUT / BÉNÉFICE] sur [CANAL].");
            }

            return;
        }

        string expression = kind switch
        {
            SfdDocumentKind.UserRequirement =>
                @"^Quand .+, l'utilisateur doit pouvoir .+\.$",
            SfdDocumentKind.SystemRequirement =>
                @"^Quand .+, le système doit permettre de .+\.$",
            SfdDocumentKind.RegulatoryRequirement =>
                @"^Quand .+, le système doit respecter .+\.$",
            _ => throw new InvalidDataException(
                "Les blocs C/SC ne peuvent être ajoutés qu'à une exigence SFD.")
        };
        if (!Regex.IsMatch(value, expression, RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException(
                "La condition ne respecte pas le patron confirmé du type d'exigence.");
        }
    }

    /// <summary>Lists authored C/SC blocks; rejects ambiguous or partial structural markers.</summary>
    /// <param name="markdown">Complete authored Markdown document.</param>
    /// <returns>Business blocks with stable nested IDs and RM GUID references.</returns>
    public static IReadOnlyList<SfdConditionBlock> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        string normalized = Normalize(markdown);
        string[] lines = normalized.Split('\n');
        int start = Array.IndexOf(lines, Start);
        int end = Array.IndexOf(lines, End);
        if (start < 0 && end < 0)
        {
            if (lines.Any(line => line.StartsWith(ConditionMarker,
                    StringComparison.Ordinal) ||
                line.StartsWith(SubconditionMarker, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("Marqueurs C/SC SFD sans conteneur versionné.");
            }

            return [];
        }

        if (start < 0 || end <= start || Array.LastIndexOf(lines, Start) != start ||
            Array.LastIndexOf(lines, End) != end ||
            lines.Skip(end + 1).Any(line => line.StartsWith(ConditionMarker,
                StringComparison.Ordinal) ||
                line.StartsWith(SubconditionMarker, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Conteneur C/SC SFD incomplet ou dupliqué.");
        }

        List<SfdConditionBlock> blocks = [];
        SfdConditionBlock? current = null;
        HashSet<Guid> identities = [];
        HashSet<string> cReferences = new(StringComparer.Ordinal);
        int total = 0;
        for (int index = start + 1; index < end; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (++total > MaxBlocks)
            {
                throw new InvalidDataException("Trop de blocs C/SC SFD.");
            }

            if (line.StartsWith(ConditionMarker, StringComparison.Ordinal))
            {
                Guid id = ParseMarkerGuid(line, ConditionMarker);
                string heading = NextLine(lines, ref index, end, "### C-");
                (string reference, string statement) = ParseHeading(heading,
                    "### C-", "C-");
                if (!identities.Add(id) || !cReferences.Add(reference))
                {
                    throw new InvalidDataException("Identifiant ou référence C dupliqué.");
                }

                current = new SfdConditionBlock
                {
                    Id = id, Reference = reference, Statement = statement
                };
                blocks.Add(current);
                continue;
            }

            if (line.StartsWith(SubconditionMarker, StringComparison.Ordinal))
            {
                if (current is null)
                {
                    throw new InvalidDataException("SC orpheline, sans C parente.");
                }

                string payload = line[SubconditionMarker.Length..];
                if (!payload.EndsWith(" -->", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Marqueur SC malformé.");
                }

                payload = payload[..^4];
                string[] parts = payload.Split(";rm=", StringSplitOptions.None);
                if (parts.Length != 2 ||
                    !Guid.TryParseExact(parts[0], "D", out Guid id) ||
                    !identities.Add(id))
                {
                    throw new InvalidDataException("Identifiant SC invalide ou dupliqué.");
                }

                List<Guid> ruleIds = [];
                if (parts[1].Length > 0)
                {
                    foreach (string candidate in parts[1].Split(','))
                    {
                        if (!Guid.TryParseExact(candidate, "D", out Guid ruleId) ||
                            ruleId == Guid.Empty || ruleIds.Contains(ruleId))
                        {
                            throw new InvalidDataException("Référence RM invalide ou dupliquée.");
                        }

                        ruleIds.Add(ruleId);
                    }
                }

                string heading = NextLine(lines, ref index, end, "#### SC-");
                (string reference, string statement) = ParseHeading(heading,
                    "#### SC-", "SC-");
                if (current.Subconditions.Any(sc => sc.Reference == reference))
                {
                    throw new InvalidDataException("Référence SC dupliquée dans une C.");
                }

                current.Subconditions.Add(new SfdSubconditionBlock
                {
                    Id = id,
                    Reference = reference,
                    Statement = statement,
                    BusinessRuleIds = ruleIds
                });
                continue;
            }

            throw new InvalidDataException(
                "Contenu non structuré dans le conteneur C/SC SFD.");
        }

        return blocks;
    }

    /// <summary>
    /// Rewrites only the dedicated C/SC marker region. All other Markdown,
    /// including provenance and manual descriptions, stays untouched.
    /// </summary>
    /// <param name="markdown">Existing source document.</param>
    /// <param name="blocks">Human-provided C/SC state with pre-existing GUID identities.</param>
    /// <returns>Markdown containing a single portable C/SC region.</returns>
    public static string Apply(string markdown, IReadOnlyList<SfdConditionBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(blocks);
        Parse(markdown);
        HashSet<Guid> ids = [];
        HashSet<string> references = new(StringComparer.Ordinal);
        StringBuilder region = new();
        region.AppendLine(Start);
        foreach (SfdConditionBlock block in blocks)
        {
            if (block.Id == Guid.Empty || !ids.Add(block.Id) ||
                !IsReference(block.Reference, "C-") ||
                !references.Add(block.Reference) || block.Subconditions is null ||
                block.Subconditions.Count > MaxBlocks)
            {
                throw new InvalidDataException("Structure des conditions SFD invalide.");
            }

            region.Append(ConditionMarker).Append(block.Id.ToString("D"))
                .AppendLine(" -->")
                .Append("### ").Append(block.Reference).Append(" — ")
                .AppendLine(RequireStatement(block.Statement));
            HashSet<string> scReferences = new(StringComparer.Ordinal);
            foreach (SfdSubconditionBlock subcondition in block.Subconditions)
            {
                if (subcondition.Id == Guid.Empty || !ids.Add(subcondition.Id) ||
                    !IsReference(subcondition.Reference, "SC-") ||
                    !scReferences.Add(subcondition.Reference) ||
                    subcondition.BusinessRuleIds is null ||
                    subcondition.BusinessRuleIds.Any(id => id == Guid.Empty) ||
                    subcondition.BusinessRuleIds.Distinct().Count() !=
                    subcondition.BusinessRuleIds.Count)
                {
                    throw new InvalidDataException("Structure des sous-conditions SFD invalide.");
                }

                region.Append(SubconditionMarker)
                    .Append(subcondition.Id.ToString("D"))
                    .Append(";rm=")
                    .Append(string.Join(",", subcondition.BusinessRuleIds
                        .Select(id => id.ToString("D"))))
                    .AppendLine(" -->")
                    .Append("#### ").Append(subcondition.Reference).Append(" — ")
                    .AppendLine(RequireStatement(subcondition.Statement));
            }
        }

        region.AppendLine(End);
        // StringBuilder.AppendLine uses CRLF on Windows. Normalize the newly
        // rendered region BEFORE restoring the source document's newline style.
        string serializedRegion = Normalize(region.ToString());
        string normalized = Normalize(markdown);
        int start = normalized.IndexOf(Start, StringComparison.Ordinal);
        string result;
        if (start < 0)
        {
            result = normalized.TrimEnd('\n') +
                "\n\n## Blocs C/SC identifiés\n\n" + serializedRegion;
        }
        else
        {
            int end = normalized.IndexOf(End, start, StringComparison.Ordinal) +
                End.Length;
            result = normalized[..start] + serializedRegion.TrimEnd('\n') +
                normalized[end..];
        }

        // Draft hints are accurate only until the first authored C or SC.
        // Leave all other user Markdown untouched, especially sources.
        if (blocks.Count > 0)
        {
            result = result.Replace(
                "_Aucune condition renseignée ; brouillon incomplet._",
                string.Empty, StringComparison.Ordinal);
        }

        if (blocks.Any(block => block.Subconditions.Count > 0))
        {
            result = result.Replace(
                "_Aucune sous-condition renseignée ; brouillon incomplet._",
                string.Empty, StringComparison.Ordinal);
        }

        // Preserve existing Windows editors' CRLF without making the whole
        // document appear modified when only the C/SC region changes.
        return markdown.Contains("\r\n", StringComparison.Ordinal)
            ? result.Replace("\n", "\r\n", StringComparison.Ordinal)
            : result;
    }

    /// <summary>Finds the next unused readable C/SC number without renumbering existing blocks.</summary>
    /// <param name="references">Existing references in the same owning scope.</param>
    /// <param name="prefix">C- or SC-.</param>
    /// <returns>Next stable display code.</returns>
    public static string NextReference(IEnumerable<string> references, string prefix)
    {
        if (prefix is not ("C-" or "SC-"))
        {
            throw new ArgumentException("Préfixe SFD inconnu.", nameof(prefix));
        }

        int last = references.Select(reference =>
            IsReference(reference, prefix)
                ? int.Parse(reference[prefix.Length..], CultureInfo.InvariantCulture)
                : throw new InvalidDataException("Référence C/SC invalide."))
            .DefaultIfEmpty(0).Max();
        if (last == 9999)
        {
            throw new InvalidOperationException("Numérotation C/SC épuisée.");
        }

        return prefix + (last + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Checks canonical C-0001 / SC-0001 codes.</summary>
    /// <param name="reference">Candidate code.</param>
    /// <param name="prefix">Expected prefix.</param>
    /// <returns>True if properly numbered.</returns>
    private static bool IsReference(string reference, string prefix) =>
        reference.StartsWith(prefix, StringComparison.Ordinal) &&
        reference.Length == prefix.Length + 4 &&
        int.TryParse(reference[prefix.Length..], NumberStyles.None,
            CultureInfo.InvariantCulture, out int number) &&
        number is >= 1 and <= 9999 &&
        reference == prefix + number.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>Parses a marker GUID including its required closing delimiter.</summary>
    /// <param name="line">Complete marker.</param>
    /// <param name="prefix">Expected marker prefix.</param>
    /// <returns>Nonempty stable GUID.</returns>
    private static Guid ParseMarkerGuid(string line, string prefix)
    {
        if (!line.EndsWith(" -->", StringComparison.Ordinal) ||
            !Guid.TryParseExact(line[prefix.Length..^4], "D", out Guid id) ||
            id == Guid.Empty)
        {
            throw new InvalidDataException("GUID du bloc SFD invalide.");
        }

        return id;
    }

    /// <summary>Requires an immediately adjacent, role-specific Markdown heading.</summary>
    /// <param name="lines">All source lines.</param>
    /// <param name="index">Index of the marker, advanced to the heading.</param>
    /// <param name="end">Last allowed index.</param>
    /// <param name="prefix">Expected heading opening.</param>
    /// <returns>Heading source.</returns>
    private static string NextLine(string[] lines, ref int index, int end,
        string prefix)
    {
        if (++index >= end || !lines[index].StartsWith(prefix,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("Titre C/SC manquant après son GUID.");
        }

        return lines[index];
    }

    /// <summary>Extracts the stable visible reference and edited free-text statement.</summary>
    /// <param name="heading">Markdown heading.</param>
    /// <param name="headingPrefix">Expected start.</param>
    /// <param name="refPrefix">C-/SC- code.</param>
    /// <returns>Parsed readable code and text.</returns>
    private static (string, string) ParseHeading(string heading,
        string headingPrefix, string refPrefix)
    {
        string rest = heading[headingPrefix.Length..];
        int dash = rest.IndexOf(" — ", StringComparison.Ordinal);
        if (dash <= 0)
        {
            throw new InvalidDataException("En-tête C/SC sans libellé.");
        }

        string reference = refPrefix + rest[..dash];
        if (!IsReference(reference, refPrefix))
        {
            throw new InvalidDataException("Référence C/SC invalide.");
        }

        return (reference, RequireStatement(rest[(dash + 3)..]));
    }

    /// <summary>Unifies line endings only for the versioned structural text parser.</summary>
    /// <param name="text">Source.</param>
    /// <returns>LF-normalized Markdown.</returns>
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
