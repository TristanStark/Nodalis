using System.Globalization;
using System.Text;
using Nodalis.Core.Markdown;

namespace Nodalis.Core.Maintenance;

/// <summary>A projected history item; the Markdown file remains the only source of truth.</summary>
public sealed record MaintenanceHistoryItem
{
    public required string SourcePath { get; init; }
    public required string ProjectName { get; init; }
    public required int LineNumber { get; init; }
    public required MaintenanceRecordKind Kind { get; init; }
    public string? Id { get; init; }
    public DateOnly? Date { get; init; }
    public required string Summary { get; init; }
    public string Resolution { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Tags { get; init; } = string.Empty;
    public string? IncidentId { get; init; }
    public bool IsIncomplete { get; init; }
    public string Warning { get; init; } = string.Empty;
    public required string SearchText { get; init; }

    /// <summary>User-facing label that does not conceal missing dates.</summary>
    public string DateLabel => Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Date absente";
    /// <summary>Visible type.</summary>
    public string KindLabel => Kind == MaintenanceRecordKind.Incident ? "Incident" : "Intervention";
    /// <summary>Project and source line for actionable context.</summary>
    public string SourceLabel => ProjectName + " · ligne " + LineNumber;
    /// <summary>Warnings displayed alongside incomplete source blocks.</summary>
    public string WarningLabel => IsIncomplete ? "⚠ " + Warning : string.Empty;
}

/// <summary>In-memory search parameters; nothing is saved outside Markdown.</summary>
public sealed record MaintenanceHistoryFilter
{
    public string Search { get; init; } = string.Empty;
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
}

/// <summary>
/// Creates a read-only navigable projection from mixed historical and structured
/// Markdown, including incomplete entries that the strict write codec rejects.
/// </summary>
public static class MaintenanceHistoryIndex
{
    /// <summary>Projects a single Markdown maintenance file without changing its contents.</summary>
    /// <param name="sourcePath">Absolute file path used for navigation.</param>
    /// <param name="projectName">Friendly project label.</param>
    /// <param name="markdown">Full Markdown source.</param>
    /// <returns>Incidents and interventions with source line and warnings.</returns>
    public static IReadOnlyList<MaintenanceHistoryItem> Parse(
        string sourcePath, string projectName, string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(projectName);
        ArgumentNullException.ThrowIfNull(markdown);
        IReadOnlyList<MarkdownOutlineEntry> headings = MarkdownOutlineParser.Parse(markdown);
        List<MaintenanceHistoryItem> entries = [];
        bool inHistory = false;
        for (int index = 0; index < headings.Count; index++)
        {
            MarkdownOutlineEntry heading = headings[index];
            if (heading.Level <= 2)
            {
                inHistory = heading.Level == 2 &&
                    string.Equals(heading.Title, MaintenanceMarkdownCodec.HistoryHeading,
                        StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inHistory || heading.Level != 3)
            {
                continue;
            }

            MaintenanceRecordKind? kind = heading.Title.Equals("Incident",
                    StringComparison.OrdinalIgnoreCase)
                ? MaintenanceRecordKind.Incident
                : heading.Title.Equals("Intervention", StringComparison.OrdinalIgnoreCase)
                    ? MaintenanceRecordKind.Intervention : null;
            if (kind is null)
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

            string body = markdown[(heading.Offset +
                ReadHeadingLength(markdown, heading.Offset))..end];
            Dictionary<string, string> properties = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            bool duplicate = ReadFields(body, properties);
            properties.TryGetValue("Identifiant", out string? id);
            properties.TryGetValue("Date", out string? dateText);
            properties.TryGetValue("Résumé", out string? summary);
            properties.TryGetValue("Résultat", out string? resolution);
            properties.TryGetValue("Statut", out string? status);
            properties.TryGetValue("Catégorie", out string? category);
            properties.TryGetValue("Tags", out string? tags);
            properties.TryGetValue("Incident", out string? incidentId);
            bool dateOk = DateOnly.TryParseExact(dateText, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date);
            string prefix = kind == MaintenanceRecordKind.Incident ? "INC-" : "INT-";
            bool idOk = id is not null && id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(id[prefix.Length..], "N", out Guid _);
            bool summaryOk = !string.IsNullOrWhiteSpace(summary);
            bool relationOk = kind == MaintenanceRecordKind.Intervention ||
                string.IsNullOrWhiteSpace(incidentId);
            List<string> warnings = [];
            if (!dateOk)
            {
                warnings.Add("date manquante ou invalide");
            }

            if (!idOk)
            {
                warnings.Add("identifiant absent ou invalide");
            }

            if (!summaryOk)
            {
                warnings.Add("résumé absent");
            }

            if (duplicate)
            {
                warnings.Add("propriétés répétées");
            }

            if (!relationOk)
            {
                warnings.Add("référence d'incident invalide");
            }

            entries.Add(new MaintenanceHistoryItem
            {
                SourcePath = sourcePath,
                ProjectName = projectName,
                LineNumber = heading.LineNumber,
                Kind = kind.Value,
                Id = id,
                Date = dateOk ? date : null,
                Summary = summaryOk ? summary! : "(Résumé non renseigné)",
                Resolution = resolution ?? string.Empty,
                Status = status ?? string.Empty,
                Category = category ?? string.Empty,
                Tags = tags ?? string.Empty,
                IncidentId = incidentId,
                IsIncomplete = warnings.Count > 0,
                Warning = string.Join(", ", warnings),
                SearchText = string.Join(" ", properties.Select(p => p.Key + " " + p.Value)) +
                    " " + body + " " + projectName
            });
        }

        HashSet<string> incidentIds = entries
            .Where(item => item.Kind == MaintenanceRecordKind.Incident && item.Id is not null)
            .Select(item => item.Id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < entries.Count; index++)
        {
            MaintenanceHistoryItem item = entries[index];
            List<string> warnings = [];
            if (item.Id is not null && !seenIds.Add(item.Id))
            {
                warnings.Add("identifiant répété");
            }

            if (item.IncidentId is not null && !incidentIds.Contains(item.IncidentId))
            {
                warnings.Add("incident lié introuvable");
            }

            if (warnings.Count > 0)
            {
                entries[index] = item with
                {
                    IsIncomplete = true,
                    Warning = string.IsNullOrWhiteSpace(item.Warning)
                        ? string.Join(", ", warnings)
                        : item.Warning + ", " + string.Join(", ", warnings)
                };
            }
        }

        return entries;
    }

    /// <summary>
    /// Filters a memory-only history. Undated records remain visible even when
    /// a date interval is requested, with an explicit warning on each row.
    /// </summary>
    /// <param name="entries">Projected records.</param>
    /// <param name="filter">Search and filter values.</param>
    /// <returns>Newest first, with undated records retained at the end.</returns>
    public static IReadOnlyList<MaintenanceHistoryItem> Search(
        IEnumerable<MaintenanceHistoryItem> entries, MaintenanceHistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(filter);
        return entries.Where(item =>
                (string.IsNullOrWhiteSpace(filter.Search) ||
                 item.SearchText.Contains(filter.Search.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(filter.Status) ||
                 item.Status.Contains(filter.Status.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(filter.Category) ||
                 item.Category.Contains(filter.Category.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(filter.Tag) ||
                 item.Tags.Split(',', StringSplitOptions.TrimEntries |
                     StringSplitOptions.RemoveEmptyEntries)
                     .Any(tag => tag.Contains(filter.Tag.Trim(), StringComparison.OrdinalIgnoreCase))) &&
                (!item.Date.HasValue ||
                 ((!filter.From.HasValue || item.Date.Value >= filter.From.Value) &&
                  (!filter.To.HasValue || item.Date.Value <= filter.To.Value))))
            .OrderBy(item => !item.Date.HasValue)
            .ThenByDescending(item => item.Date)
            .ThenBy(item => item.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.LineNumber)
            .ToArray();
    }

    /// <summary>Gets length through the first line break, keeping source offsets exact.</summary>
    /// <param name="markdown">Original document.</param>
    /// <param name="start">Heading start.</param>
    /// <returns>Number of heading characters including its line break.</returns>
    private static int ReadHeadingLength(string markdown, int start)
    {
        int end = markdown.IndexOf('\n', start);
        return end < 0 ? markdown.Length - start : end - start + 1;
    }

    /// <summary>Extracts bold-label bullets, ignoring fenced Markdown examples.</summary>
    /// <param name="body">Markdown between headings.</param>
    /// <param name="properties">Extracted fields.</param>
    /// <returns>Whether any field key was duplicated.</returns>
    private static bool ReadFields(string body, Dictionary<string, string> properties)
    {
        char fence = '\0';
        int fenceLength = 0;
        bool duplicate = false;
        foreach (string rawLine in body.Split('\n'))
        {
            string line = rawLine.Trim();
            int offset = 0;
            while (offset < line.Length && (line[offset] == (char)96 || line[offset] == '~'))
            {
                if (offset > 0 && line[offset] != line[0])
                {
                    break;
                }

                offset++;
            }

            if (offset >= 3 && (line[0] == (char)96 || line[0] == '~'))
            {
                if (fence == '\0')
                {
                    fence = line[0];
                    fenceLength = offset;
                }
                else if (fence == line[0] && offset >= fenceLength &&
                    string.IsNullOrWhiteSpace(line[offset..]))
                {
                    fence = '\0';
                }

                continue;
            }

            if (fence != '\0' || !line.StartsWith("- **", StringComparison.Ordinal))
            {
                continue;
            }

            int separator = line.IndexOf(" :**", 4, StringComparison.Ordinal);
            if (separator < 5)
            {
                continue;
            }

            string key = line[4..separator].Trim();
            string value = line[(separator + 4)..].Trim();
            if (key.Length > 0 && !properties.TryAdd(key, value))
            {
                duplicate = true;
            }
        }

        return duplicate;
    }
}
