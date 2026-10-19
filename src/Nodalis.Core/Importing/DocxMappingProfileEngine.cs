using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nodalis.Core.Importing;

/// <summary>
/// Safe, local and fully deterministic DOCX heading-to-target mapping.
/// </summary>
public static class DocxMappingProfileEngine
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(70);

    /// <summary>
    /// Validates a user-editable profile without touching any workspace.
    /// </summary>
    /// <param name="profile">Profile to validate.</param>
    public static void Validate(DocxMappingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) ||
            profile.Name.Length > 100 || profile.Name.Any(char.IsControl) ||
            profile.Rules.Count > 150 || profile.ManualOverrides.Count > 500)
        {
            throw new InvalidDataException("Identité, nom ou nombre de règles du profil DOCX invalide.");
        }

        HashSet<Guid> ids = [];
        foreach (DocxMappingProfileRule rule in profile.Rules)
        {
            if (rule.Id == Guid.Empty || !ids.Add(rule.Id) ||
                !Enum.IsDefined(rule.Action) ||
                (rule.HeadingLevel is not null and (< 1 or > 9)) ||
                new[] { rule.HeadingText, rule.WordStyleId, rule.ParentPathContains, rule.TextPattern }
                    .All(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException("Règle de profil DOCX vide, en doublon ou invalide.");
            }

            foreach (string? field in new[]
            {
                rule.HeadingText, rule.WordStyleId, rule.ParentPathContains, rule.TextPattern
            })
            {
                if (field is { Length: > 160 } || field?.Any(char.IsControl) == true)
                {
                    throw new InvalidDataException("Les motifs de profil doivent être bornés à 160 caractères.");
                }
            }

            ValidateAction(rule.Action, rule.TargetSection);

            if (!string.IsNullOrWhiteSpace(rule.TextPattern))
            {
                try
                {
                    Regex pattern = new Regex(rule.TextPattern,
                        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                        PatternTimeout);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidDataException("Expression régulière DOCX invalide.", exception);
                }
            }
        }

        HashSet<string> overrideKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DocxMappingManualOverride entry in profile.ManualOverrides)
        {
            if (string.IsNullOrWhiteSpace(entry.HeadingText) ||
                entry.HeadingText.Length > 160 || entry.ParentPath.Length > 300 ||
                !overrideKeys.Add(MakeOverrideKey(entry.HeadingText, entry.ParentPath)))
            {
                throw new InvalidDataException("Override manuel de profil DOCX vide ou ambigu.");
            }

            ValidateAction(entry.Action, entry.TargetSection);
        }
    }

    /// <summary>
    /// Returns a proposal and the IDs of matching rules without performing any write.
    /// Manual overrides take priority; incompatible matching rules become a blocking diagnostic.
    /// </summary>
    /// <param name="profile">A validated, selected profile.</param>
    /// <param name="source">Heading metadata from the source Word.</param>
    /// <returns>A safe explainable decision.</returns>
    public static DocxMappingDecision Evaluate(
        DocxMappingProfile profile,
        DocxMappingSourceSection source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(profile);
        if (!profile.Enabled)
        {
            return new DocxMappingDecision();
        }

        string key = MakeOverrideKey(source.Heading, source.ParentPath);
        DocxMappingManualOverride? manual = profile.ManualOverrides
            .FirstOrDefault(entry =>
                string.Equals(MakeOverrideKey(entry.HeadingText, entry.ParentPath),
                    key, StringComparison.Ordinal));

        if (manual is not null)
        {
            return new DocxMappingDecision
            {
                Matched = true,
                IsManualOverride = true,
                Action = manual.Action,
                TargetSection = manual.TargetSection,
                Reasons = ["Choix manuel enregistré : priorité sur toutes les règles automatiques."]
            };
        }

        List<DocxMappingProfileRule> matching = [];
        foreach (DocxMappingProfileRule rule in profile.Rules.Where(rule => rule.Enabled))
        {
            if (!Matches(rule, source))
            {
                continue;
            }

            matching.Add(rule);
        }

        if (matching.Count == 0)
        {
            return new DocxMappingDecision();
        }

        bool conflict = matching.Skip(1).Any(rule =>
            rule.Action != matching[0].Action ||
            !string.Equals(rule.TargetSection?.Trim(),
                matching[0].TargetSection?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (conflict)
        {
            return new DocxMappingDecision
            {
                Matched = true,
                HasConflict = true,
                MatchingRuleIds = matching.Select(rule => rule.Id).ToList(),
                Reasons = ["Règles contradictoires : aucun mapping proposé avant arbitrage manuel."]
            };
        }

        DocxMappingProfileRule winner = matching[0];
        return new DocxMappingDecision
        {
            Matched = true,
            Action = winner.Action,
            TargetSection = winner.TargetSection,
            MatchingRuleIds = matching.Select(rule => rule.Id).ToList(),
            Reasons =
            [
                "Règle prioritaire #" + (profile.Rules.IndexOf(winner) + 1) +
                " : " + DescribeRule(winner) +
                (matching.Count > 1 ? " (autres règles compatibles : " + (matching.Count - 1) + ")" : "")
            ]
        };
    }

    /// <summary>
    /// Produces a readable explanation of all predicates in a mapping rule.
    /// </summary>
    public static string DescribeRule(DocxMappingProfileRule rule)
    {
        List<string> predicates = [];
        if (!string.IsNullOrWhiteSpace(rule.HeadingText))
        {
            predicates.Add("titre = " + rule.HeadingText);
        }

        if (!string.IsNullOrWhiteSpace(rule.WordStyleId))
        {
            predicates.Add("style = " + rule.WordStyleId);
        }

        if (rule.HeadingLevel is int level)
        {
            predicates.Add("niveau = " + level);
        }

        if (!string.IsNullOrWhiteSpace(rule.ParentPathContains))
        {
            predicates.Add("parent contient " + rule.ParentPathContains);
        }

        if (!string.IsNullOrWhiteSpace(rule.TextPattern))
        {
            predicates.Add("motif = " + rule.TextPattern);
        }

        return string.Join(" ET ", predicates) + " → " +
            (rule.Action == DocxMappingAction.Ignore ? "Ignorer" : rule.TargetSection);
    }

    /// <summary>
    /// Stores an explicit user override without modifying the original instance.
    /// </summary>
    public static DocxMappingProfile SaveOverride(
        DocxMappingProfile profile,
        DocxMappingSourceSection source,
        DocxMappingAction action,
        string? destination)
    {
        Validate(profile);
        ValidateAction(action, destination);
        if (string.IsNullOrWhiteSpace(source.Heading))
        {
            throw new InvalidDataException("Titre de section absent : override manuel impossible.");
        }

        string key = MakeOverrideKey(source.Heading, source.ParentPath);
        List<DocxMappingManualOverride> entries = profile.ManualOverrides
            .Where(entry => !string.Equals(
                MakeOverrideKey(entry.HeadingText, entry.ParentPath), key, StringComparison.Ordinal))
            .ToList();

        entries.Add(new DocxMappingManualOverride
        {
            HeadingText = source.Heading,
            ParentPath = source.ParentPath,
            Action = action,
            TargetSection = destination
        });

        DocxMappingProfile updated = profile with { ManualOverrides = entries };
        Validate(updated);
        return updated;
    }

    /// <summary>
    /// Simplifies whitespace/diacritics for stable, case-insensitive heading matching.
    /// </summary>
    public static string Normalize(string input)
    {
        StringBuilder result = new StringBuilder();
        bool separating = false;
        foreach (char c in (input ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (!char.IsLetterOrDigit(c))
            {
                separating = result.Length > 0;
                continue;
            }

            if (separating)
            {
                result.Append(' ');
            }

            separating = false;
            result.Append(char.ToUpperInvariant(c));
        }

        return result.ToString();
    }

    /// <summary>Checks each bounded predicate against a single Word heading.</summary>
    /// <param name="rule">The candidate rule.</param>
    /// <param name="source">The document section metadata.</param>
    /// <returns>True if all explicitly supplied predicates match.</returns>
    private static bool Matches(DocxMappingProfileRule rule, DocxMappingSourceSection source)
    {
        if (!string.IsNullOrWhiteSpace(rule.HeadingText) &&
            Normalize(rule.HeadingText) != Normalize(source.Heading))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.WordStyleId) &&
            !string.Equals(rule.WordStyleId, source.WordStyleId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rule.HeadingLevel is int level && level != source.HeadingLevel)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.ParentPathContains) &&
            !Normalize(source.ParentPath).Contains(Normalize(rule.ParentPathContains), StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.TextPattern))
        {
            return true;
        }

        try
        {
            return Regex.IsMatch(source.Heading[..Math.Min(source.Heading.Length, 4096)],
                rule.TextPattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                PatternTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            // Slow patterns are treated as nonmatches, never as permission to map.
            return false;
        }
    }

    /// <summary>Creates a normalized key from source heading and parent context.</summary>
    /// <param name="heading">Source section heading.</param>
    /// <param name="parent">Source parent path.</param>
    /// <returns>The stable heading/context key.</returns>
    private static string MakeOverrideKey(string heading, string parent) =>
        Normalize(parent) + "|" + Normalize(heading);

    /// <summary>Rejects invalid rule actions and unsafe destination section names.</summary>
    /// <param name="action">Mapping operation.</param>
    /// <param name="destination">Optional destination name.</param>
    private static void ValidateAction(DocxMappingAction action, string? destination)
    {
        if (!Enum.IsDefined(action))
        {
            throw new InvalidDataException("Action de mapping inconnue.");
        }

        if (action == DocxMappingAction.Ignore)
        {
            if (!string.IsNullOrWhiteSpace(destination))
            {
                throw new InvalidDataException("Une règle Ignorer ne peut pas créer une destination.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(destination) ||
            destination.Length > 100 ||
            destination.Trim() != destination ||
            destination.StartsWith('.') ||
            destination.Contains('/') || destination.Contains('\\'))
        {
            throw new InvalidDataException("Nom de destination Nodalis absent ou dangereux.");
        }

        ImportDocumentPlanBuilder.ValidateDestination(destination + ".md");
    }
}
