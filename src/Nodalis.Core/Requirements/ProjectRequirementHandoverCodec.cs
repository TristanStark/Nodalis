using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Tasks;

namespace Nodalis.Core.Requirements;

/// <summary>
/// Pure in-memory EXI reassignment/succession. This codec does not publish a
/// source tombstone, move a card or imply that existing links are rewired.
/// </summary>
public static class ProjectRequirementHandoverCodec
{
    /// <summary>
    /// Prepares a canonical destination card while retaining the exact
    /// human-authored Markdown body. For B, the new identity starts at Draft,
    /// NotStarted and NotVerified; existing CA blocks forbid blind cloning.
    /// </summary>
    /// <param name="originalMarkdown">Exact versioned EXI source.</param>
    /// <param name="source">Expected canonical source metadata.</param>
    /// <param name="targetProjectId">New owner of A or successor owner B.</param>
    /// <param name="targetReference">Unreserved EXI code in destination.</param>
    /// <param name="targetId">Original GUID A or independent GUID B.</param>
    /// <param name="intentId">Existing approved human intent.</param>
    /// <param name="method">Reviewed A/B intent.</param>
    /// <param name="approvedUtc">Stable creation time of the human-reviewed B intent.</param>
    /// <returns>Canonical prospective destination Markdown; never saved.</returns>
    public static string Prepare(
        string originalMarkdown, ProjectRequirementCard source,
        Guid targetProjectId, string targetReference, Guid targetId,
        Guid intentId, ProjectHandoverMethod method,
        DateTimeOffset? approvedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(originalMarkdown);
        ProjectRequirementMarkdownCodec.Validate(source);
        if (targetProjectId == Guid.Empty || targetProjectId == source.ProjectId ||
            targetId == Guid.Empty || intentId == Guid.Empty ||
            approvedUtc == default && approvedUtc.HasValue ||
            !Enum.IsDefined(method) ||
            !ProjectRequirementMarkdownCodec.IsReference(targetReference) ||
            (method == ProjectHandoverMethod.MoveOriginal &&
             targetId != source.Id) ||
            (method == ProjectHandoverMethod.LinkedSuccessor &&
             targetId == source.Id))
        {
            throw new InvalidDataException(
                "Identité, référence ou stratégie EXI incompatible.");
        }

        ProjectRequirementCard actual =
            ProjectRequirementMarkdownCodec.Parse(originalMarkdown);
        if (actual != source)
        {
            throw new InvalidDataException(
                "L'exigence a changé depuis l'identification de la source.");
        }

        ProjectRequirementCriterionScan criteria =
            ProjectRequirementCriterionCodec.Inspect(originalMarkdown);
        MarkdownBlockIdentityScan blocks =
            MarkdownBlockIdentityCodec.Inspect(originalMarkdown);
        TaskPersistentIdentityScan tasks =
            TaskPersistentIdentityCodec.Inspect(
                originalMarkdown.Replace("\r\n", "\n",
                    StringComparison.Ordinal).Split('\n'));
        bool embeddedLinksOrTasks = tasks.Entries.Count > 0 ||
            tasks.Issues.Count > 0 ||
            originalMarkdown.Contains("[[", StringComparison.Ordinal) ||
            originalMarkdown.Contains("](", StringComparison.Ordinal);
        bool hasCriteria = criteria.Criteria.Count > 0;
        bool allCriterionMarkers = hasCriteria &&
            criteria.Diagnostics.Count == 0 &&
            blocks.Issues.Count == 0 &&
            criteria.Criteria.All(item =>
                item.Id.HasValue && item.LocallyIdentified) &&
            blocks.Blocks.Where(item => item.Id.HasValue).All(item =>
                criteria.Criteria.Any(criterion => criterion.Id == item.Id &&
                    criterion.HeadingLine == item.HeadingLine)) &&
            System.Text.RegularExpressions.Regex.Matches(
                originalMarkdown, "<!-- nodalis:block:",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count ==
                criteria.Criteria.Count &&
            !HasUnscopedAcceptanceContent(originalMarkdown);
        bool plainWithoutCriteria = !hasCriteria &&
            criteria.Diagnostics.Count == 0 &&
            blocks.Issues.Count == 0 &&
            !blocks.Blocks.Any(item => item.Id.HasValue) &&
            !HasAcceptanceContent(originalMarkdown);
        if (embeddedLinksOrTasks ||
            method == ProjectHandoverMethod.LinkedSuccessor &&
            !plainWithoutCriteria ||
            method == ProjectHandoverMethod.MoveOriginal &&
            !plainWithoutCriteria && !allCriterionMarkers)
        {
            throw new InvalidDataException(
                "Passation EXI : critères CA sans GUID stable, blocs " +
                "non reliés, tâches ou liens Markdown à requalifier. " +
                "Le mode A accepte uniquement les CA complètement identifiés ; " +
                "le mode B reste limité aux fiches sans CA.");
        }

        if (originalMarkdown.StartsWith('\uFEFF') ||
            originalMarkdown.Replace("\r\n", string.Empty,
                StringComparison.Ordinal).Contains('\r'))
        {
            throw new InvalidDataException(
                "Markdown EXI avec BOM ou fin de ligne CR isolée : refus.");
        }

        // Standard EXI files are rendered with AppendLine(), which emits
        // CRLF on Windows. Preserve the user's original line endings instead
        // of rejecting all cards generated by Nodalis on Windows.
        string newline = originalMarkdown.Contains("\r\n",
            StringComparison.Ordinal) ? "\r\n" : "\n";
        string[] lines = originalMarkdown.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n');
        if (lines.Length < 15 || lines[0] != "---")
        {
            throw new InvalidDataException("En-tête EXI incomplet.");
        }

        int closing = Array.IndexOf(lines, "---", 1);
        if (closing < 0 || closing >= 30 ||
            closing + 1 >= lines.Length)
        {
            throw new InvalidDataException(
                "Sections de la fiche EXI non reconnues.");
        }

        string expectedHeading = "# " + source.Reference + " — " + source.Title;
        if (lines[closing + 1] != expectedHeading)
        {
            throw new InvalidDataException(
                "Titre Markdown et référence EXI incohérents.");
        }

        ProjectRequirementCard destination = source with
        {
            Id = targetId,
            ProjectId = targetProjectId,
            Reference = targetReference,
            CreatedUtc = method == ProjectHandoverMethod.MoveOriginal
                ? source.CreatedUtc : approvedUtc ?? source.CreatedUtc,
            Validation = method == ProjectHandoverMethod.MoveOriginal
                ? source.Validation : ProjectRequirementValidation.Draft,
            Implementation = method == ProjectHandoverMethod.MoveOriginal
                ? source.Implementation :
                    ProjectRequirementImplementation.NotStarted,
            Verification = method == ProjectHandoverMethod.MoveOriginal
                ? source.Verification :
                    ProjectRequirementVerification.NotVerified
        };

        ReplaceField(lines, closing, "requirement-id",
            destination.Id.ToString("D"));
        ReplaceField(lines, closing, "project-id",
            destination.ProjectId.ToString("D"));
        ReplaceField(lines, closing, "reference",
            destination.Reference);
        ReplaceField(lines, closing, "created-utc",
            destination.CreatedUtc.ToUniversalTime().ToString(
                "O", System.Globalization.CultureInfo.InvariantCulture));
        ReplaceField(lines, closing, "validation",
            destination.Validation.ToString());
        ReplaceField(lines, closing, "implementation",
            destination.Implementation.ToString());
        ReplaceField(lines, closing, "verification",
            destination.Verification.ToString());
        lines[closing + 1] = "# " + destination.Reference + " — " +
            destination.Title;

        string result = string.Join(newline, lines);
        string provenance = newline + newline + "> Passation " +
            (method == ProjectHandoverMethod.MoveOriginal ? "A" : "B") +
            " · source " + source.ProjectId.ToString("D") +
            " / " + source.Id.ToString("D") +
            " / " + source.Reference +
            " · intention " + intentId.ToString("D") + newline;
        result = result.TrimEnd('\r', '\n') + provenance;
        ProjectRequirementCard parsed =
            ProjectRequirementMarkdownCodec.Parse(result);
        if (parsed != destination)
        {
            throw new InvalidDataException(
                "Reconstruction du nouveau propriétaire EXI invalide.");
        }

        return result;
    }

    /// <summary>
    /// Detects any human-authored text or heading beneath acceptance criteria,
    /// including free-form text with no canonical CA identity. The default
    /// generated empty section is the only safe B clone.
    /// </summary>
    /// <param name="markdown">Original requirement source.</param>
    /// <returns>True if acceptance content must be requalified.</returns>
    private static bool HasAcceptanceContent(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n');
        bool inside = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inside = string.Equals(
                    line.Replace('’', '\''),
                    "## Critères d'acceptation",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inside && !string.IsNullOrWhiteSpace(line))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refuses unowned acceptance prose before the first stable CA heading.
    /// Existing criterion bodies remain untouched in a mode-A migration.
    /// </summary>
    /// <param name="markdown">Full original Markdown.</param>
    /// <returns>True when acceptance text is not attributable to a CA.</returns>
    private static bool HasUnscopedAcceptanceContent(string markdown)
    {
        bool inAcceptance = false;
        bool hasFirstCriterion = false;
        foreach (string line in markdown.Replace("\r\n", "\n",
            StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inAcceptance = string.Equals(
                    line[3..].Trim().Replace('’', '\''),
                    "Critères d'acceptation",
                    StringComparison.OrdinalIgnoreCase);
                hasFirstCriterion = false;
                continue;
            }

            if (!inAcceptance)
            {
                continue;
            }

            if (line.StartsWith("### CA-", StringComparison.Ordinal))
            {
                hasFirstCriterion = true;
            }
            else if (!hasFirstCriterion && !string.IsNullOrWhiteSpace(line))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Replaces exactly one front-matter key without editing the body.</summary>
    /// <param name="lines">Mutable copy of original Markdown lines.</param>
    /// <param name="closing">Front-matter boundary.</param>
    /// <param name="field">Canonical metadata key.</param>
    /// <param name="value">Replacement scalar.</param>
    private static void ReplaceField(
        string[] lines, int closing, string field, string value)
    {
        string prefix = field + ": ";
        int[] indices = Enumerable.Range(1, closing - 1).Where(index =>
            lines[index].StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        if (indices.Length != 1)
        {
            throw new InvalidDataException(
                "Métadonnées EXI manquantes ou ambiguës : " + field);
        }

        lines[indices[0]] = prefix + value;
    }
}
