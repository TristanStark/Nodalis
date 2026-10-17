using System.Globalization;
using System.Text;
using Nodalis.Core.Markdown;

namespace Nodalis.Core.Maintenance;

/// <summary>Non-destructive editing of structured interventions in an existing Markdown file.</summary>
public static class MaintenanceMarkdownEditor
{
    /// <summary>
    /// Updates one existing intervention. Unknown free-form paragraphs and other sections survive.
    /// A concurrently changed entry is rejected rather than overwritten.
    /// </summary>
    /// <param name="markdown">Latest complete Markdown source.</param>
    /// <param name="original">The entry as it appeared when editing began.</param>
    /// <param name="replacement">Edited values, retaining the original ID.</param>
    /// <returns>Updated complete Markdown with no file-system side effects.</returns>
    public static string Replace(
        string markdown, MaintenanceRecord original, MaintenanceRecord replacement)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        if (original.Kind != MaintenanceRecordKind.Intervention ||
            replacement.Kind != MaintenanceRecordKind.Intervention ||
            !string.Equals(original.Id, replacement.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La modification doit conserver l'identité de l'intervention.");
        }

        MaintenanceParseResult parsed = MaintenanceMarkdownCodec.Parse(markdown);
        if (parsed.Diagnostics.Count != 0)
        {
            throw new InvalidDataException("Historique incohérent : correction manuelle requise.");
        }

        MaintenanceRecord? current = parsed.Records.FirstOrDefault(item =>
            item.Kind == MaintenanceRecordKind.Intervention &&
            string.Equals(item.Id, original.Id, StringComparison.OrdinalIgnoreCase));
        if (current is null || !SameFields(current, original))
        {
            throw new InvalidDataException(
                "L'intervention a été modifiée ou supprimée ailleurs. Rechargez la fiche avant d'éditer.");
        }

        // Validate mandatory fields and any optional field names without writing to a file.
        string availableHistory = markdown;
        MaintenanceMarkdownCodec.Append(availableHistory,
            replacement with { Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention) });

        IReadOnlyList<MarkdownOutlineEntry> headings = MarkdownOutlineParser.Parse(markdown);
        bool inHistory = false;
        for (int index = 0; index < headings.Count; index++)
        {
            MarkdownOutlineEntry heading = headings[index];
            if (heading.Level <= 2)
            {
                inHistory = heading.Level == 2 &&
                    heading.Title.Equals(MaintenanceMarkdownCodec.HistoryHeading,
                        StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inHistory || heading.Level != 3 ||
                !heading.Title.Equals("Intervention", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int end = markdown.Length;
            for (int next = index + 1; next < headings.Count; next++)
            {
                if (headings[next].Level <= 3)
                {
                    end = headings[next].Offset;
                    break;
                }
            }

            string oldBlock = markdown[heading.Offset..end];
            if (!HasIdentityField(oldBlock, original.Id))
            {
                continue;
            }

            string nl = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            StringBuilder edited = new StringBuilder();
            edited.Append("### Intervention").Append(nl);
            WriteField(edited, "Identifiant", replacement.Id, nl);
            WriteField(edited, "Date",
                replacement.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), nl);
            WriteField(edited, "Résumé", replacement.Summary.Trim(), nl);
            if (replacement.IncidentId is not null)
            {
                WriteField(edited, "Incident", replacement.IncidentId, nl);
            }

            foreach (KeyValuePair<string, string> field in replacement.Details)
            {
                WriteField(edited, field.Key.Trim(), field.Value.Trim(), nl);
            }

            // Keep arbitrary Markdown paragraphs, comments, fenced code and whitespace.
            // Only structured property bullets outside fences are regenerated.
            string remainder = PreserveUnstructuredLines(oldBlock, nl);
            edited.Append(remainder);
            return markdown[..heading.Offset] + edited + markdown[end..];
        }

        throw new InvalidDataException("Bloc de l'intervention introuvable dans la fiche.");
    }

    /// <summary>Compares editable values, including unknown optional properties.</summary>
    /// <param name="left">Current record.</param>
    /// <param name="right">Snapshot from the form opening.</param>
    /// <returns>True when no structured field changed.</returns>
    private static bool SameFields(MaintenanceRecord left, MaintenanceRecord right)
    {
        return left.Kind == right.Kind &&
            string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase) &&
            left.Date == right.Date &&
            string.Equals(left.Summary, right.Summary, StringComparison.Ordinal) &&
            string.Equals(left.IncidentId, right.IncidentId, StringComparison.OrdinalIgnoreCase) &&
            left.Details.Count == right.Details.Count &&
            left.Details.All(item =>
                right.Details.TryGetValue(item.Key, out string? value) &&
                string.Equals(item.Value, value, StringComparison.Ordinal));
    }

    /// <summary>Matches a generated identity bullet, not a mention in prose.</summary>
    /// <param name="block">Source heading and fields.</param>
    /// <param name="id">The expected identifier.</param>
    /// <returns>Whether this block contains the expected ID field.</returns>
    private static bool HasIdentityField(string block, string id)
    {
        string expected = "- **Identifiant :** " + id;
        bool fenced = false;
        foreach (string line in block.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
                trimmed.StartsWith(new string((char)96, 3), StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced && trimmed.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Retains every free-form line while removing replaceable field bullets.</summary>
    /// <param name="block">Original intervention block.</param>
    /// <param name="newLine">Preferred line break.</param>
    /// <returns>Unstructured Markdown with original spacing preserved.</returns>
    private static string PreserveUnstructuredLines(string block, string newLine)
    {
        int headingEnd = block.IndexOf('\n');
        if (headingEnd < 0)
        {
            return newLine;
        }

        string remainder = block[(headingEnd + 1)..];
        StringBuilder output = new StringBuilder();
        bool fenced = false;
        int cursor = 0;
        while (cursor < remainder.Length)
        {
            int lf = remainder.IndexOf('\n', cursor);
            int end = lf < 0 ? remainder.Length : lf + 1;
            string fullLine = remainder[cursor..end];
            string trimmed = fullLine.Trim();
            if (trimmed.StartsWith("~~~", StringComparison.Ordinal) ||
                trimmed.StartsWith(new string((char)96, 3), StringComparison.Ordinal))
            {
                fenced = !fenced;
            }

            bool isProperty = !fenced && trimmed.StartsWith("- **", StringComparison.Ordinal) &&
                              trimmed.Contains(" :**", StringComparison.Ordinal);
            if (!isProperty)
            {
                output.Append(fullLine);
            }

            cursor = end;
        }

        return output.Length == 0 ? newLine : output.ToString();
    }

    /// <summary>Renders an editable Markdown bullet without proprietary metadata.</summary>
    /// <param name="builder">Output text.</param>
    /// <param name="key">Field name.</param>
    /// <param name="value">Field text.</param>
    /// <param name="newLine">Line ending.</param>
    private static void WriteField(StringBuilder builder, string key, string value, string newLine)
    {
        builder.Append("- **").Append(key).Append(" :** ").Append(value).Append(newLine);
    }
}
