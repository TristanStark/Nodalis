using Nodalis.Core.Markdown;

namespace Nodalis.Core.Requirements;

/// <summary>
/// One CA-xx heading derived from a requirement's Markdown body.
/// The GUID comes from the shared, stable block-marker syntax.
/// </summary>
/// <param name="Reference">Local CA-01 reference, never a global identity.</param>
/// <param name="Title">Current human-readable criterion text.</param>
/// <param name="Id">Stable block GUID when present.</param>
/// <param name="HeadingLine">One-based source line for navigation.</param>
/// <param name="LocallyIdentified">Only local identity is unambiguous; workspace-wide collisions still need index validation.</param>
public sealed record ProjectRequirementCriterion(
    string Reference, string Title, Guid? Id, int HeadingLine, bool LocallyIdentified);

/// <summary>
/// Computed criterion inventory with warnings. Does not persist or repair IDs.
/// </summary>
/// <param name="Criteria">All syntactically recognized CA headings.</param>
/// <param name="Diagnostics">Missing or ambiguous identification warnings.</param>
public sealed record ProjectRequirementCriterionScan(
    IReadOnlyList<ProjectRequirementCriterion> Criteria,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Finds acceptance criteria only under the canonical section of a requirement.
/// Existing block IDs stay stable when a heading is renamed or reordered.
/// </summary>
public static class ProjectRequirementCriterionCodec
{
    /// <summary>Accepts canonical CA-01..CA-99 references.</summary>
    /// <param name="reference">Human-readable local criterion reference.</param>
    /// <returns>True for one nonzero two-digit criterion reference.</returns>
    public static bool IsReference(string? reference) =>
        reference is { Length: 5 } &&
        reference.StartsWith("CA-", StringComparison.Ordinal) &&
        reference[3] is >= '0' and <= '9' &&
        reference[4] is >= '0' and <= '9' &&
        reference != "CA-00";

    /// <summary>
    /// Performs read-only criterion discovery using the shared fenced-code
    /// aware Markdown heading and block-ID parser.
    /// </summary>
    /// <param name="markdown">Canonical requirement Markdown source.</param>
    /// <returns>Criteria, link eligibility and diagnostics.</returns>
    public static ProjectRequirementCriterionScan Inspect(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        MarkdownBlockIdentityScan scan = MarkdownBlockIdentityCodec.Inspect(
            markdown);
        List<(string Reference, string Title, Guid? Id, int Line, bool Invalid)> items = [];
        List<string> diagnostics = [];
        bool insideAcceptance = false;
        bool seenAcceptance = false;

        HashSet<Guid> duplicatedBlockIds = scan.Blocks
            .Where(block => block.Id.HasValue)
            .GroupBy(block => block.Id!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();

        foreach (MarkdownBlockIdentityEntry heading in scan.Blocks)
        {
            if (heading.Level <= 2)
            {
                insideAcceptance = heading.Level == 2 &&
                    string.Equals(
                        heading.Title.Replace('’', '\''),
                        "Critères d'acceptation",
                        StringComparison.OrdinalIgnoreCase);
                if (insideAcceptance)
                {
                    if (seenAcceptance)
                    {
                        diagnostics.Add("Section de critères répétée, ligne " +
                            heading.HeadingLine + ".");
                    }

                    seenAcceptance = true;
                }

                continue;
            }

            if (!insideAcceptance || heading.Level != 3)
            {
                continue;
            }

            string fullTitle = heading.Title.Trim();
            if (fullTitle.Length < 5 ||
                !IsReference(fullTitle[..5]) ||
                fullTitle.Length > 5 && !char.IsWhiteSpace(fullTitle[5]) &&
                fullTitle[5] is not ('-' or '—' or '–' or ':'))
            {
                diagnostics.Add("Critère sans référence CA-xx valide, ligne " +
                    heading.HeadingLine + ".");
                continue;
            }

            string label = fullTitle[5..].Trim();
            label = label.TrimStart('-', '—', '–', ':').Trim();
            bool invalid = string.IsNullOrWhiteSpace(label);
            if (invalid)
            {
                diagnostics.Add("Critère sans description, ligne " +
                    heading.HeadingLine + ".");
            }

            if (heading.Id is null)
            {
                diagnostics.Add("Critère sans GUID stable, ligne " +
                    heading.HeadingLine + ".");
            }

            if (heading.Id is Guid id && duplicatedBlockIds.Contains(id))
            {
                diagnostics.Add("Identifiant de bloc dupliqué, ligne " +
                    heading.HeadingLine + ".");
                invalid = true;
            }

            if (scan.Issues.Any(issue =>
                issue.HeadingLine == heading.HeadingLine))
            {
                invalid = true;
            }

            items.Add((fullTitle[..5], label, heading.Id,
                heading.HeadingLine, invalid));
        }

        HashSet<string> repeatedReferences = items.GroupBy(
            item => item.Reference, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (string reference in repeatedReferences.OrderBy(
            value => value, StringComparer.Ordinal))
        {
            diagnostics.Add("Référence " + reference +
                " dupliquée : liens ambigus.");
        }

        ProjectRequirementCriterion[] criteria = items.Select(item =>
            new ProjectRequirementCriterion(
                item.Reference, item.Title, item.Id, item.Line,
                !item.Invalid && item.Id.HasValue &&
                !repeatedReferences.Contains(item.Reference))).ToArray();
        return new ProjectRequirementCriterionScan(criteria, diagnostics);
    }
}
