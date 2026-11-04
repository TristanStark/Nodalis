using Nodalis.Core.Markdown;

namespace Nodalis.Core.Requirements;

/// <summary>
/// Bounded read-only CA-body projection and lossless, purely in-memory rewrite.
/// The heading and adjacent nodalis:block GUID are not part of the body.
/// </summary>
public static class ProjectRequirementCriterionBodyCodec
{
    private const int MaxDocumentCharacters = 1024 * 1024;
    private const int MaxBodyCharacters = 8000;

    /// <summary>
    /// Extracts safe, uniquely identified CA bodies in ONE pass per EXI.
    /// Null/absent entries are intentionally not treated as empty descriptions.
    /// </summary>
    /// <param name="markdown">Canonical EXI snapshot.</param>
    /// <returns>Body text indexed only by locally unambiguous CA block GUID.</returns>
    public static IReadOnlyDictionary<Guid, string> ExtractAll(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        Dictionary<Guid, string> result = [];
        if (markdown.Length > MaxDocumentCharacters)
        {
            return result;
        }

        ProjectRequirementCriterionScan scan =
            ProjectRequirementCriterionCodec.Inspect(markdown);
        if (scan.Diagnostics.Any(item => item.StartsWith(
            "Section de critères répétée", StringComparison.Ordinal)))
        {
            return result;
        }

        MarkdownBlockIdentityScan identities =
            MarkdownBlockIdentityCodec.Inspect(markdown);
        MarkdownOutlineEntry[] headings = MarkdownOutlineParser.Parse(markdown)
            .ToArray();
        foreach (ProjectRequirementCriterion criterion in scan.Criteria)
        {
            if (!criterion.LocallyIdentified ||
                criterion.Id is not Guid id ||
                identities.Blocks.Count(item => item.Id == id) != 1)
            {
                continue;
            }

            (int Start, int End)? bounds = FindBounds(
                markdown, id, criterion, identities, headings);
            if (bounds is null)
            {
                continue;
            }

            string value = NormalizeNewlines(markdown[
                bounds.Value.Start..bounds.Value.End]).Trim('\n');
            if (value.Length <= MaxBodyCharacters)
            {
                result.Add(id, value);
            }
        }

        return result;
    }

    /// <summary>Reads the body for one existing, locally identified CA.</summary>
    /// <param name="markdown">Exact EXI Markdown.</param>
    /// <param name="criterionId">Stable existing block GUID.</param>
    /// <returns>LF-only body, or null if not safely identifiable.</returns>
    public static string? Extract(string markdown, Guid criterionId) =>
        ExtractAll(markdown).TryGetValue(criterionId, out string? text)
            ? text : null;

    /// <summary>
    /// Replaces only the Markdown between the stable CA marker and the next
    /// H1/H2/H3 heading. Supports H4-H6 and fenced code in the body; it
    /// rejects additional high-level headings, identity comments and
    /// unfinished fences instead of changing the surrounding EXI structure.
    /// This method performs no I/O and never approves a test or verdict.
    /// </summary>
    /// <param name="markdown">Source pinned by SHA at human review.</param>
    /// <param name="card">Authoritative parent EXI identity.</param>
    /// <param name="criterionId">Existing stable CA GUID.</param>
    /// <param name="expectedCurrent">Human-reviewed current CA text.</param>
    /// <param name="approved">Explicitly accepted/edited LF-only body.</param>
    /// <returns>Full replacement Markdown snapshot, still unwritten.</returns>
    public static string Rewrite(string markdown, ProjectRequirementCard card,
        Guid criterionId, string expectedCurrent, string approved)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(expectedCurrent);
        ArgumentNullException.ThrowIfNull(approved);
        if (criterionId == Guid.Empty ||
            markdown.Length > MaxDocumentCharacters ||
            approved.Length > MaxBodyCharacters ||
            approved != approved.Trim('\n') ||
            approved.Contains('\r') || approved.Contains('\0') ||
            approved.Contains('\uFEFF') ||
            approved.Contains("<!-- nodalis:block:",
                StringComparison.OrdinalIgnoreCase) ||
            MarkdownOutlineParser.Parse(approved).Any(entry => entry.Level <= 3) ||
            !ClosedFences(approved) ||
            ProjectRequirementMarkdownCodec.Parse(markdown) != card)
        {
            throw new InvalidDataException(
                "Corps CA invalide : 8000 caractères, structure EXI ou " +
                "identifiants de bloc protégés.");
        }

        ProjectRequirementCriterionScan scan =
            ProjectRequirementCriterionCodec.Inspect(markdown);
        ProjectRequirementCriterion[] current = scan.Criteria
            .Where(item => item.Id == criterionId && item.LocallyIdentified)
            .ToArray();
        if (current.Length != 1 ||
            scan.Diagnostics.Any(item => item.StartsWith(
                "Section de critères répétée", StringComparison.Ordinal)) ||
            !string.Equals(Extract(markdown, criterionId), expectedCurrent,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Critère CA ou texte local différent de la revue humaine.");
        }

        MarkdownBlockIdentityScan identities =
            MarkdownBlockIdentityCodec.Inspect(markdown);
        (int Start, int End)? bounds = FindBounds(markdown, criterionId,
            current[0], identities, MarkdownOutlineParser.Parse(markdown)
                .ToArray());
        if (bounds is null)
        {
            throw new InvalidDataException("Identité ou limites CA ambiguës.");
        }

        // Nested H4+ headings can carry their own stable block GUIDs. A body
        // replacement may not silently remove their IDs or break backlinks.
        // Such a document requires deliberate manual editing/relinking first.
        if (markdown[bounds.Value.Start..bounds.Value.End].Contains(
            "<!-- nodalis:block:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le corps CA contient des identifiants de sous-blocs : " +
                "édition manuelle requise pour préserver les liens stables.");
        }

        string newline = markdown.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string replacement = newline +
            approved.Replace("\n", newline, StringComparison.Ordinal) +
            newline + newline;
        string rewritten = markdown[..bounds.Value.Start] + replacement +
            markdown[bounds.Value.End..];
        if (rewritten.Length > MaxDocumentCharacters ||
            ProjectRequirementMarkdownCodec.Parse(rewritten) != card ||
            !string.Equals(Extract(rewritten, criterionId), approved,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Corps CA proposé incompatible avec la fiche EXI.");
        }

        ProjectRequirementCriterionScan after =
            ProjectRequirementCriterionCodec.Inspect(rewritten);
        if (after.Criteria.Count != scan.Criteria.Count ||
            scan.Criteria.Where((item, index) =>
                item.Id != after.Criteria[index].Id ||
                item.Reference != after.Criteria[index].Reference ||
                item.Title != after.Criteria[index].Title ||
                item.LocallyIdentified != after.Criteria[index].LocallyIdentified)
                .Any())
        {
            throw new InvalidDataException(
                "La mise à jour changerait les critères existants.");
        }

        return rewritten;
    }

    /// <summary>
    /// Resolves the raw source character bounds only if a CA heading and its
    /// adjacent marker are canonical, unique and within acceptance criteria.
    /// </summary>
    /// <param name="markdown">Original Markdown.</param>
    /// <param name="criterionId">Unique existing CA block GUID.</param>
    /// <param name="criterion">Already-validated CA inventory entry.</param>
    /// <param name="identities">Fenced-code-aware heading identity scan.</param>
    /// <param name="headings">Ordered Markdown headings.</param>
    /// <returns>Body offsets (excluding marker), or null if unsafe.</returns>
    private static (int Start, int End)? FindBounds(string markdown,
        Guid criterionId, ProjectRequirementCriterion criterion,
        MarkdownBlockIdentityScan identities,
        IReadOnlyList<MarkdownOutlineEntry> headings)
    {
        MarkdownBlockIdentityEntry[] blocks = identities.Blocks
            .Where(item => item.Id == criterionId).ToArray();
        if (blocks.Length != 1 || blocks[0].Level != 3 ||
            blocks[0].HeadingLine != criterion.HeadingLine ||
            blocks[0].MarkerLine != criterion.HeadingLine + 1)
        {
            return null;
        }

        MarkdownBlockIdentityEntry block = blocks[0];
        MarkdownOutlineEntry? parent = headings
            .Where(item => item.Offset < block.HeadingOffset &&
                item.Level <= 2)
            .LastOrDefault();
        if (parent is null || parent.Level != 2 ||
            !string.Equals(parent.Title.Replace('’', '\''),
                "Critères d'acceptation", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int markerLineStart = markdown.IndexOf('\n', block.HeadingOffset) + 1;
        if (markerLineStart <= 0 || markerLineStart >= markdown.Length)
        {
            return null;
        }

        int markerEnd = markdown.IndexOf('\n', markerLineStart);
        if (markerEnd < 0)
        {
            return null;
        }

        string marker = markdown[markerLineStart..markerEnd].TrimEnd('\r');
        if (!string.Equals(marker,
            MarkdownBlockIdentityCodec.FormatMarker(criterionId),
            StringComparison.Ordinal))
        {
            return null;
        }

        int start = markerEnd + 1;
        int end = headings.Where(item =>
            item.Offset > block.HeadingOffset && item.Level <= 3)
            .Select(item => item.Offset).DefaultIfEmpty(markdown.Length)
            .First();
        if (start > end || !ClosedFences(markdown[start..end]))
        {
            return null;
        }

        return (start, end);
    }

    /// <summary>Recognizes complete Markdown code fences in the bounded body.</summary>
    /// <param name="body">LF or CRLF Markdown.</param>
    /// <returns>False for an unterminated fence that could mask later headings.</returns>
    private static bool ClosedFences(string body)
    {
        bool open = false;
        char marker = '\0';
        int length = 0;
        foreach (string raw in body.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            string content = line.TrimStart(' ');
            if (line.Length - content.Length > 3 || content.Length < 3 ||
                content[0] is not ('~' or (char)96))
            {
                continue;
            }

            int run = 0;
            while (run < content.Length && content[run] == content[0])
            {
                run++;
            }

            if (!open && run >= 3)
            {
                open = true;
                marker = content[0];
                length = run;
            }
            else if (open && content[0] == marker && run >= length &&
                string.IsNullOrWhiteSpace(content[run..]))
            {
                open = false;
            }
        }

        return !open;
    }

    /// <summary>Normalizes only field text for comparison; not the source.</summary>
    /// <param name="text">Bounded CA body.</param>
    /// <returns>LF-only body.</returns>
    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
