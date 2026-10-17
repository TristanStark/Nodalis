using Nodalis.Core.Markdown;

namespace Nodalis.Core.Maintenance;

/// <summary>Explicit human decision on a candidate pair; neither option merges incidents.</summary>
public enum MaintenanceRecurrenceDecision { Link, Ignore }

/// <summary>Precise metadata-only Markdown edit preserving all unrelated user content.</summary>
public static class MaintenanceIncidentReviewEditor
{
    /// <summary>
    /// Stores a decision in only the first incident's structured properties.
    /// Both original block fingerprints are checked against the current document.
    /// </summary>
    /// <param name="markdown">Latest complete maintenance Markdown.</param>
    /// <param name="first">Source incident the person reviewed.</param>
    /// <param name="second">Other incident, from the same source file.</param>
    /// <param name="decision">Link or dismiss the pair.</param>
    /// <returns>Entire source with only decision metadata changed.</returns>
    public static string Apply(string markdown, MaintenanceHistoryItem first,
        MaintenanceHistoryItem second, MaintenanceRecurrenceDecision decision)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Kind != MaintenanceRecordKind.Incident ||
            second.Kind != MaintenanceRecordKind.Incident ||
            first.IsIncomplete || second.IsIncomplete ||
            first.Id is null || second.Id is null ||
            string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(first.SourcePath, second.SourcePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le rapprochement exige deux incidents structurés distincts de la même fiche.");
        }

        MaintenanceParseResult parsed = MaintenanceMarkdownCodec.Parse(markdown);
        if (parsed.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Le fichier contient des anomalies : corrigez-les avant de lier ou ignorer.");
        }

        IReadOnlyList<MaintenanceHistoryItem> latest =
            MaintenanceHistoryIndex.Parse(first.SourcePath, first.ProjectName, markdown);
        MaintenanceHistoryItem? currentFirst = latest.SingleOrDefault(item =>
            item.Kind == MaintenanceRecordKind.Incident &&
            string.Equals(item.Id, first.Id, StringComparison.OrdinalIgnoreCase));
        MaintenanceHistoryItem? currentSecond = latest.SingleOrDefault(item =>
            item.Kind == MaintenanceRecordKind.Incident &&
            string.Equals(item.Id, second.Id, StringComparison.OrdinalIgnoreCase));
        if (currentFirst is null || currentSecond is null ||
            currentFirst.IsIncomplete || currentSecond.IsIncomplete ||
            !string.Equals(currentFirst.SourceBlockHash, first.SourceBlockHash,
                StringComparison.Ordinal) ||
            !string.Equals(currentSecond.SourceBlockHash, second.SourceBlockHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "L'un des incidents a changé depuis la suggestion. Rouvrez les suggestions.");
        }

        IReadOnlyList<MarkdownOutlineEntry> headings = MarkdownOutlineParser.Parse(markdown);
        bool inHistory = false;
        for (int i = 0; i < headings.Count; i++)
        {
            MarkdownOutlineEntry heading = headings[i];
            if (heading.Level <= 2)
            {
                inHistory = heading.Level == 2 &&
                    string.Equals(heading.Title, MaintenanceMarkdownCodec.HistoryHeading,
                        StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inHistory || heading.Level != 3 ||
                !string.Equals(heading.Title, "Incident", StringComparison.OrdinalIgnoreCase) ||
                heading.LineNumber != currentFirst.LineNumber)
            {
                continue;
            }

            int end = markdown.Length;
            for (int next = i + 1; next < headings.Count; next++)
            {
                if (headings[next].Level <= 3)
                {
                    end = headings[next].Offset;
                    break;
                }
            }

            string oldBlock = markdown[heading.Offset..end];
            string newBlock = EditBlock(oldBlock, second.Id, decision);
            if (string.Equals(oldBlock, newBlock, StringComparison.Ordinal))
            {
                return markdown;
            }

            return markdown[..heading.Offset] + newBlock + markdown[end..];
        }

        throw new InvalidDataException("Le bloc incident original est introuvable.");
    }

    /// <summary>Updates only the reviewed-link list fields, preserving every other line.</summary>
    /// <param name="block">Whole source incident block.</param>
    /// <param name="otherId">Other incident ID.</param>
    /// <param name="decision">User decision.</param>
    /// <returns>Edited block with unchanged free-form Markdown.</returns>
    private static string EditBlock(string block, string otherId,
        MaintenanceRecurrenceDecision decision)
    {
        const string linksKey = "Récurrences liées";
        const string ignoredKey = "Récurrences ignorées";
        string[] lines = block.Split('\n');
        Dictionary<string, (int Index, HashSet<string> Values)> fields = new(
            StringComparer.OrdinalIgnoreCase);
        bool fenced = false;
        for (int i = 1; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
                trimmed.StartsWith(new string((char)96, 3), StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (fenced)
            {
                continue;
            }

            foreach (string key in new[] { linksKey, ignoredKey })
            {
                string prefix = "- **" + key + " :** ";
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (fields.ContainsKey(key))
                {
                    throw new InvalidDataException("Champs de rapprochement répétés.");
                }

                string raw = trimmed[prefix.Length..];
                HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in raw.Split(',', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
                {
                    if (!id.StartsWith("INC-", StringComparison.OrdinalIgnoreCase) ||
                        !Guid.TryParseExact(id[4..], "N", out Guid _))
                    {
                        throw new InvalidDataException(
                            "Référence de rapprochement invalide : " + id);
                    }

                    values.Add(id);
                }

                fields[key] = (i, values);
            }
        }

        HashSet<string> linked = fields.TryGetValue(linksKey, out
            (int Index, HashSet<string> Values) link)
            ? link.Values : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> ignored = fields.TryGetValue(ignoredKey, out
            (int Index, HashSet<string> Values) dismissal)
            ? dismissal.Values : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // An explicit user decision supersedes a previous conflicting decision.
        linked.Remove(otherId);
        ignored.Remove(otherId);
        if (decision == MaintenanceRecurrenceDecision.Link)
        {
            linked.Add(otherId);
        }
        else
        {
            ignored.Add(otherId);
        }

        List<string> output = new List<string>(lines);
        (string Key, HashSet<string> Values)[] decisions =
        [
            (linksKey, linked), (ignoredKey, ignored)
        ];

        // Replace pre-existing fields at their original indices FIRST.
        // Inserting a new field earlier would shift those indices and erase
        // the wrong line when changing a prior "ignore" into a "link".
        foreach ((string key, HashSet<string> values) in decisions)
        {
            if (!fields.TryGetValue(key, out (int Index, HashSet<string> Values) info))
            {
                continue;
            }

            string cr = output[info.Index].EndsWith('\r') ? "\r" : string.Empty;
            output[info.Index] = values.Count == 0
                ? string.Empty
                : "- **" + key + " :** " +
                    string.Join(", ", values.OrderBy(value => value,
                        StringComparer.OrdinalIgnoreCase)) + cr;
        }

        foreach ((string key, HashSet<string> values) in decisions)
        {
            if (values.Count == 0 || fields.ContainsKey(key))
            {
                continue;
            }

            string cr = block.Contains("\r\n", StringComparison.Ordinal) ? "\r" : string.Empty;
            output.Insert(1, "- **" + key + " :** " +
                string.Join(", ", values.OrderBy(value => value,
                    StringComparer.OrdinalIgnoreCase)) + cr);
        }

        return string.Join("\n", output);
    }
}
