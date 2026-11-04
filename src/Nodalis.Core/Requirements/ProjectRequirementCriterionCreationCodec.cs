using Nodalis.Core.Markdown;

namespace Nodalis.Core.Requirements;

/// <summary>
/// Pure, bounded creation of one new CA heading inside an existing EXI.
/// Nodalis, never the external model, assigns the CA code and stable block ID.
/// </summary>
public static class ProjectRequirementCriterionCreationCodec
{
    private const int MaxDocumentCharacters = 1024 * 1024;

    /// <summary>
    /// Selects the first unused CA-01..CA-99 in the containing requirement.
    /// Even legacy or unidentified headings reserve their reference.
    /// Duplicate CA sections, malformed markers and ambiguous CA references
    /// require manual remediation before adding another criterion.
    /// </summary>
    /// <param name="markdown">Canonical local EXI Markdown.</param>
    /// <returns>Next free CA-xx code, or null when all 99 are occupied.</returns>
    public static string? NextReference(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        if (markdown.Length > MaxDocumentCharacters)
        {
            throw new InvalidDataException("Fiche EXI trop volumineuse.");
        }

        ProjectRequirementCriterionScan inventory =
            ProjectRequirementCriterionCodec.Inspect(markdown);
        if (inventory.Diagnostics.Any(item =>
            !item.StartsWith("Critère sans GUID stable", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Critères CA ambigus : résoudre le registre avant d'en créer.");
        }

        string[] headings = MarkdownOutlineParser.Parse(markdown)
            .Where(entry => entry.Level == 2 &&
                string.Equals(entry.Title.Replace('’', '\''),
                    "Critères d'acceptation", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Title).ToArray();
        if (headings.Length != 1)
        {
            throw new InvalidDataException(
                "Section de critères d'acceptation absente ou répétée.");
        }

        HashSet<string> occupied = inventory.Criteria
            .Select(item => item.Reference).ToHashSet(StringComparer.Ordinal);
        for (int number = 1; number <= 99; number++)
        {
            string reference = "CA-" + number.ToString("D2",
                System.Globalization.CultureInfo.InvariantCulture);
            if (!occupied.Contains(reference))
            {
                return reference;
            }
        }

        return null;
    }

    /// <summary>
    /// Inserts precisely one CA heading and its adjacent persistent marker.
    /// All other character ranges, EXI metadata and existing CA identities
    /// remain unchanged. No file is written by this method.
    /// </summary>
    /// <param name="markdown">Exact SHA-pinned canonical EXI source.</param>
    /// <param name="card">Authoritative existing EXI metadata.</param>
    /// <param name="reference">Next reference computed from the same source.</param>
    /// <param name="title">Explicitly approved single-line text.</param>
    /// <param name="criterionId">Fresh locally generated stable block GUID.</param>
    /// <returns>New in-memory Markdown for CAS publication.</returns>
    public static string Insert(string markdown, ProjectRequirementCard card,
        string reference, string title, Guid criterionId)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(card);
        if (criterionId == Guid.Empty ||
            !ProjectRequirementCriterionCodec.IsReference(reference) ||
            string.IsNullOrWhiteSpace(title) ||
            title != title.Trim() || title.Length > 240 ||
            title.Any(char.IsControl) ||
            title.StartsWith('#') || title.EndsWith('#') ||
            title.Contains("<!--", StringComparison.Ordinal) ||
            markdown.Length > MaxDocumentCharacters ||
            ProjectRequirementMarkdownCodec.Parse(markdown) != card ||
            NextReference(markdown) != reference)
        {
            throw new InvalidDataException(
                "Création CA non canonique ou code déjà occupé.");
        }

        MarkdownBlockIdentityScan before =
            MarkdownBlockIdentityCodec.Inspect(markdown);
        if (before.Blocks.Any(item => item.Id == criterionId) ||
            card.Id == criterionId || card.ProjectId == criterionId)
        {
            throw new InvalidDataException("Le nouveau GUID CA est déjà utilisé.");
        }

        MarkdownOutlineEntry[] headings =
            MarkdownOutlineParser.Parse(markdown).ToArray();
        MarkdownOutlineEntry section = headings.Single(entry =>
            entry.Level == 2 && string.Equals(
                entry.Title.Replace('’', '\''), "Critères d'acceptation",
                StringComparison.OrdinalIgnoreCase));
        int sectionEnd = headings.Where(entry =>
            entry.HeadingOffset > section.HeadingOffset && entry.Level <= 2)
            .Select(entry => entry.HeadingOffset).DefaultIfEmpty(
                markdown.Length).First();
        int headingEnd = markdown.IndexOf('\n', section.HeadingOffset);
        if (headingEnd < 0 || sectionEnd <= headingEnd)
        {
            throw new InvalidDataException("Section CA sans corps Markdown sûr.");
        }

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string addition = newline + "### " + reference + " — " + title +
            newline + MarkdownBlockIdentityCodec.FormatMarker(criterionId) +
            newline + newline;
        string result = markdown.Insert(sectionEnd, addition);
        if (result.Length > MaxDocumentCharacters ||
            ProjectRequirementMarkdownCodec.Parse(result) != card)
        {
            throw new InvalidDataException(
                "La création CA modifierait les métadonnées de l'exigence.");
        }

        ProjectRequirementCriterionScan previous =
            ProjectRequirementCriterionCodec.Inspect(markdown);
        ProjectRequirementCriterionScan after =
            ProjectRequirementCriterionCodec.Inspect(result);
        ProjectRequirementCriterion[] inserted = after.Criteria.Where(item =>
            item.Id == criterionId).ToArray();
        if (after.Criteria.Count != previous.Criteria.Count + 1 ||
            inserted.Length != 1 || !inserted[0].LocallyIdentified ||
            inserted[0].Reference != reference ||
            inserted[0].Title != title ||
            NextReference(result) == reference)
        {
            throw new InvalidDataException(
                "Critère ajouté non identifiable ou structure CA ambigüe.");
        }

        foreach (ProjectRequirementCriterion existing in previous.Criteria)
        {
            if (after.Criteria.Count(item =>
                item.Id == existing.Id &&
                item.Reference == existing.Reference &&
                item.Title == existing.Title) != 1)
            {
                throw new InvalidDataException(
                    "La création CA changerait un ancien critère.");
            }
        }

        return result;
    }
}
