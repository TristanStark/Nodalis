using Nodalis.Core.Markdown;

namespace Nodalis.Core.Requirements;

/// <summary>
/// Pure, conservative rewrite of one explicitly identified CA heading.
/// It never creates, removes or changes stable block IDs or test verdicts.
/// </summary>
public static class ProjectRequirementCriterionTitleCodec
{
    private const int MaxDocumentCharacters = 1024 * 1024;

    /// <summary>
    /// Rewrites exactly one canonical CA heading, preserving the adjacent
    /// nodalis:block GUID and the remaining Markdown character-for-character.
    /// This method has no file-system or external AI side effects.
    /// </summary>
    /// <param name="markdown">Canonical source, already SHA-pinned in review.</param>
    /// <param name="card">Reviewed EXI identity and independent statuses.</param>
    /// <param name="criterionId">Existing unique CA block identity.</param>
    /// <param name="expectedTitle">Reviewed local criterion title.</param>
    /// <param name="approvedTitle">Human-approved replacement title.</param>
    /// <returns>New in-memory Markdown document with one CA title modified.</returns>
    public static string RewriteTitle(string markdown, ProjectRequirementCard card,
        Guid criterionId, string expectedTitle, string approvedTitle)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(expectedTitle);
        ArgumentNullException.ThrowIfNull(approvedTitle);
        if (criterionId == Guid.Empty || markdown.Length > MaxDocumentCharacters ||
            string.IsNullOrWhiteSpace(approvedTitle) ||
            approvedTitle != approvedTitle.Trim() ||
            approvedTitle.Length > 240 ||
            approvedTitle.Any(char.IsControl) ||
            approvedTitle.StartsWith('#') || approvedTitle.EndsWith('#') ||
            approvedTitle.Contains("<!--", StringComparison.Ordinal) ||
            ProjectRequirementMarkdownCodec.Parse(markdown) != card)
        {
            throw new InvalidDataException(
                "Titre CA, identité ou document d'exigence non conforme.");
        }

        ProjectRequirementCriterionScan scan =
            ProjectRequirementCriterionCodec.Inspect(markdown);
        if (scan.Diagnostics.Any(item => item.StartsWith(
            "Section de critères répétée", StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Section CA dupliquée : réécriture automatique refusée.");
        }

        ProjectRequirementCriterion[] matches = scan.Criteria.Where(item =>
            item.Id == criterionId).ToArray();
        if (matches.Length != 1 || !matches[0].LocallyIdentified ||
            matches[0].Title != expectedTitle)
        {
            throw new InvalidDataException(
                "Critère CA absent, en conflit ou sans GUID stable unique.");
        }

        MarkdownBlockIdentityScan identity =
            MarkdownBlockIdentityCodec.Inspect(markdown);
        if (identity.Blocks.Count(item => item.Id == criterionId) != 1)
        {
            throw new InvalidDataException(
                "GUID de bloc CA réutilisé par un autre titre Markdown.");
        }

        ProjectRequirementCriterion criterion = matches[0];
        string[] lines = markdown.Split('\n');
        int row = criterion.HeadingLine - 1;
        if (row < 0 || row >= lines.Length)
        {
            throw new InvalidDataException("Position du critère CA invalide.");
        }

        string expectedHeading = "### " + criterion.Reference + " — " +
            expectedTitle;
        string originalLine = lines[row];
        if (originalLine.TrimEnd('\r') != expectedHeading)
        {
            throw new InvalidDataException(
                "Titre CA non canonique : une requalification est requise.");
        }

        string replaced = "### " + criterion.Reference + " — " +
            approvedTitle + (originalLine.EndsWith('\r') ? "\r" : "");
        lines[row] = replaced;
        string updated = string.Join("\n", lines);
        if (updated.Length > MaxDocumentCharacters ||
            ProjectRequirementMarkdownCodec.Parse(updated) != card)
        {
            throw new InvalidDataException(
                "La mise à jour CA modifierait des métadonnées EXI.");
        }

        ProjectRequirementCriterionScan after =
            ProjectRequirementCriterionCodec.Inspect(updated);
        ProjectRequirementCriterion[] confirmed = after.Criteria.Where(item =>
            item.Id == criterionId).ToArray();
        if (confirmed.Length != 1 || !confirmed[0].LocallyIdentified ||
            confirmed[0].Reference != criterion.Reference ||
            confirmed[0].Title != approvedTitle ||
            confirmed[0].HeadingLine != criterion.HeadingLine ||
            after.Criteria.Count != scan.Criteria.Count)
        {
            throw new InvalidDataException(
                "La mise à jour CA modifierait la structure du registre.");
        }

        return updated;
    }
}
