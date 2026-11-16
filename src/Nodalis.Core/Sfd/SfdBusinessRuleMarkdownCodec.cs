using System.Globalization;
using System.Text;

namespace Nodalis.Core.Sfd;

/// <summary>
/// Canonical shared business-rule Markdown catalog. Only the one catalog owns
/// RM wording; C/SC blocks store GUID references, never copied definitions.
/// </summary>
public static class SfdBusinessRuleMarkdownCodec
{
    private const string Start = "<!-- nodalis-sfd-rules:v1 -->";
    private const string End = "<!-- /nodalis-sfd-rules:v1 -->";
    private const string Marker = "<!-- nodalis-sfd-rm:";
    private const int MaxRules = 5000;

    /// <summary>Reads only explicitly identified RM declarations from the Markdown catalog.</summary>
    /// <param name="markdown">Canonical full RM catalog source.</param>
    /// <returns>Validated rules; empty means an untouched catalog.</returns>
    public static IReadOnlyList<SfdBusinessRule> Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > 1024 * 1024)
        {
            throw new InvalidDataException("Catalogue RM trop volumineux.");
        }

        string[] lines = Normalize(markdown).Split('\n');
        int start = Array.IndexOf(lines, Start);
        int end = Array.IndexOf(lines, End);
        if (start < 0 && end < 0)
        {
            if (lines.Any(line =>
                line.StartsWith(Marker, StringComparison.Ordinal) ||
                line.StartsWith("## RM-", StringComparison.Ordinal)))
            {
                throw new InvalidDataException(
                    "Règles RM non identifiées : vérifiez le catalogue avant toute création.");
            }

            return [];
        }

        if (start < 0 || end <= start || Array.LastIndexOf(lines, Start) != start ||
            Array.LastIndexOf(lines, End) != end ||
            lines.Skip(end + 1).Any(line => line.StartsWith(Marker,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Bloc de catalogue RM absent ou dupliqué.");
        }

        List<SfdBusinessRule> rules = [];
        HashSet<Guid> ids = [];
        HashSet<string> references = new(StringComparer.Ordinal);
        for (int index = start + 1; index < end; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (++index + 1 >= end || !line.StartsWith(Marker,
                StringComparison.Ordinal) ||
                !line.EndsWith(" -->", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Entrée RM sans marqueur et titre valides.");
            }

            string payload = line[Marker.Length..^4];
            string[] parts = payload.Split(";certainty=", StringSplitOptions.None);
            if (parts.Length != 2 ||
                !Guid.TryParseExact(parts[0], "D", out Guid id) ||
                id == Guid.Empty ||
                !Enum.TryParse(parts[1], false, out SfdAssertionCertainty certainty) ||
                !Enum.IsDefined(certainty) ||
                !ids.Add(id))
            {
                throw new InvalidDataException("Métadonnées RM invalides ou dupliquées.");
            }

            string heading = lines[index];
            if (!heading.StartsWith("## RM-", StringComparison.Ordinal))
            {
                throw new InvalidDataException("En-tête RM manquant.");
            }

            string remainder = heading["## ".Length..];
            int delimiter = remainder.IndexOf(" — ", StringComparison.Ordinal);
            if (delimiter <= 0)
            {
                throw new InvalidDataException("Libellé RM manquant.");
            }

            string reference = remainder[..delimiter];
            string statement = SfdConditionBlockCodec.RequireStatement(
                remainder[(delimiter + 3)..]);
            if (!IsReference(reference) || !references.Add(reference) ||
                !lines[index + 1].StartsWith("Source :", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Référence ou source RM non conforme.");
            }

            string source = lines[index + 1]["Source :".Length..].Trim();
            SfdBusinessRule rule = new()
            {
                Id = id,
                Reference = reference,
                Statement = statement,
                Certainty = certainty,
                Source = source
            };
            Validate(rule);
            rules.Add(rule);
            if (rules.Count > MaxRules)
            {
                throw new InvalidDataException("Catalogue RM trop volumineux.");
            }

            index++;
        }

        return rules;
    }

    /// <summary>Updates only the identified RM region and preserves unrelated Markdown.</summary>
    /// <param name="markdown">Original source text.</param>
    /// <param name="rules">Complete human-authored canonical RM set.</param>
    /// <returns>One versioned Markdown catalog.</returns>
    public static string Apply(string markdown, IReadOnlyList<SfdBusinessRule> rules)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(rules);
        Parse(markdown);
        HashSet<Guid> ids = [];
        HashSet<string> references = new(StringComparer.Ordinal);
        StringBuilder content = new();
        content.AppendLine(Start);
        foreach (SfdBusinessRule rule in rules)
        {
            Validate(rule);
            if (!ids.Add(rule.Id) || !references.Add(rule.Reference) ||
                rules.Count > MaxRules)
            {
                throw new InvalidDataException("Règle RM dupliquée ou catalogue excessif.");
            }

            content.Append(Marker).Append(rule.Id.ToString("D"))
                .Append(";certainty=").Append(rule.Certainty)
                .AppendLine(" -->")
                .Append("## ").Append(rule.Reference).Append(" — ")
                .AppendLine(rule.Statement)
                .Append("Source : ").AppendLine(rule.Source);
        }

        content.AppendLine(End);
        // Avoid mixing StringBuilder's Windows CRLF with normalized LF before
        // applying the original file's line-ending convention exactly once.
        string serializedRegion = Normalize(content.ToString());
        string normalized = Normalize(markdown);
        int start = normalized.IndexOf(Start, StringComparison.Ordinal);
        string result;
        if (start >= 0)
        {
            int end = normalized.IndexOf(End, start, StringComparison.Ordinal) +
                End.Length;
            result = normalized[..start] + serializedRegion.TrimEnd('\n') +
                normalized[end..];
        }
        else
        {
            string baseMarkdown = normalized.Replace(
                "_Aucune règle métier définie._", string.Empty,
                StringComparison.Ordinal).TrimEnd('\n');
            result = baseMarkdown + "\n\n" + serializedRegion;
        }

        return markdown.Contains("\r\n", StringComparison.Ordinal)
            ? result.Replace("\n", "\r\n", StringComparison.Ordinal)
            : result;
    }

    /// <summary>Ensures one rule is canonical without assuming business approval.</summary>
    /// <param name="rule">Explicit RM declaration.</param>
    public static void Validate(SfdBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.Id == Guid.Empty || !IsReference(rule.Reference) ||
            !Enum.IsDefined(rule.Certainty) ||
            (rule.Certainty == SfdAssertionCertainty.Confirmed &&
                string.IsNullOrWhiteSpace(rule.Source)))
        {
            throw new InvalidDataException("Identité, certitude ou source RM invalide.");
        }

        SfdConditionBlockCodec.RequireStatement(rule.Statement);
        if (rule.Source.Length > 500 || rule.Source.Any(char.IsControl) ||
            rule.Source.Contains("<!--", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Source RM invalide ou trop longue.");
        }
    }

    /// <summary>Finds the next unused display number, leaving old identities stable.</summary>
    /// <param name="rules">Existing canonical RM list.</param>
    /// <returns>New RM-nnnn reference.</returns>
    public static string NextReference(IEnumerable<SfdBusinessRule> rules)
    {
        int last = rules.Select(rule =>
            IsReference(rule.Reference)
                ? int.Parse(rule.Reference[3..], CultureInfo.InvariantCulture)
                : throw new InvalidDataException("Référence RM non conforme."))
            .DefaultIfEmpty(0).Max();
        if (last == 9999)
        {
            throw new InvalidOperationException("Numérotation RM épuisée.");
        }

        return "RM-" + (last + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    /// <summary>Validates a user-facing canonical RM code.</summary>
    /// <param name="reference">RM-0001..RM-9999.</param>
    /// <returns>True when the code is well-formed.</returns>
    private static bool IsReference(string reference) =>
        reference.StartsWith("RM-", StringComparison.Ordinal) &&
        reference.Length == 7 &&
        int.TryParse(reference[3..], NumberStyles.None,
            CultureInfo.InvariantCulture, out int number) &&
        number is >= 1 and <= 9999 &&
        reference == "RM-" + number.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>Normalizes platform newline variants for marker handling.</summary>
    /// <param name="text">Markdown source.</param>
    /// <returns>LF text.</returns>
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
