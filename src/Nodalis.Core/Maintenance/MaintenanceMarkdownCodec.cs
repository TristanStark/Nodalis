using System.Globalization;
using System.Text;

namespace Nodalis.Core.Maintenance;

/// <summary>A problem (incident) or an action addressing a problem (intervention).</summary>
public enum MaintenanceRecordKind { Incident, Intervention }

/// <summary>A maintenance entry whose stable identity is stored in readable Markdown.</summary>
public sealed record MaintenanceRecord
{
    public required MaintenanceRecordKind Kind { get; init; }
    public required string Id { get; init; }
    public required DateOnly Date { get; init; }
    public required string Summary { get; init; }
    public string? IncidentId { get; init; }
    public IReadOnlyDictionary<string, string> Details { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Source position and explanation of damaged or ambiguous Markdown.</summary>
public sealed record MaintenanceDiagnostic(int LineNumber, string Message);

/// <summary>Valid maintenance entries accompanied by lossless parsing diagnostics.</summary>
public sealed record MaintenanceParseResult(
    IReadOnlyList<MaintenanceRecord> Records,
    IReadOnlyList<MaintenanceDiagnostic> Diagnostics);

/// <summary>
/// Portable incident and intervention codec: the Markdown source remains canonical.
/// </summary>
public static class MaintenanceMarkdownCodec
{
    public const string HistoryHeading = "Historique de maintenance";
    private const string Identifier = "Identifiant";
    private const string Date = "Date";
    private const string Summary = "Résumé";
    private const string Incident = "Incident";

    private readonly record struct SourceLine(int Start, int Number, string Text);
    private readonly record struct PendingBlock(int LineIndex, MaintenanceRecordKind Kind);
    private sealed record ScanResult(MaintenanceParseResult Data, int Sections, int InsertAt);

    /// <summary>Generates a distinct identifier that survives file movement and renaming.</summary>
    /// <param name="kind">Incident or intervention.</param>
    /// <returns>An INC- or INT-prefixed GUID in portable form.</returns>
    public static string NewId(MaintenanceRecordKind kind)
    {
        return (kind == MaintenanceRecordKind.Incident ? "INC-" : "INT-") +
               Guid.NewGuid().ToString("N");
    }

    /// <summary>Reads records without modifying or migrating arbitrary existing Markdown.</summary>
    /// <param name="markdown">Complete file content.</param>
    /// <returns>Valid entries and diagnostics for invalid ones.</returns>
    public static MaintenanceParseResult Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return Scan(markdown).Data;
    }

    /// <summary>
    /// Inserts one record into the existing history, or adds the history at the end.
    /// The original Markdown is preserved byte-for-byte as a subsequence.
    /// </summary>
    /// <param name="markdown">Latest file content, reloaded before saving.</param>
    /// <param name="entry">New entry with its permanent ID.</param>
    /// <returns>Complete new text, without writing any files.</returns>
    public static string Append(string markdown, MaintenanceRecord entry)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(entry);
        Validate(entry);
        ScanResult scan = Scan(markdown);
        if (scan.Sections > 1 || scan.Data.Diagnostics.Count != 0)
        {
            throw new InvalidDataException("Historique ambigu ou endommagé : corrigez le Markdown avant ajout.");
        }

        if (scan.Data.Records.Any(item =>
                string.Equals(item.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Identifiant de maintenance déjà présent.");
        }

        if (entry.IncidentId is not null &&
            !scan.Data.Records.Any(item =>
                item.Kind == MaintenanceRecordKind.Incident &&
                string.Equals(item.Id, entry.IncidentId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("L'incident lié est introuvable.");
        }

        string nl = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        StringBuilder output = new StringBuilder(markdown.Length + 400);
        output.Append(markdown.AsSpan(0, scan.InsertAt));
        EnsureBlankLine(output, nl);
        if (scan.Sections == 0)
        {
            output.Append("## ").Append(HistoryHeading).Append(nl).Append(nl);
        }

        output.Append("### ")
            .Append(entry.Kind == MaintenanceRecordKind.Incident ? "Incident" : "Intervention")
            .Append(nl);
        WriteField(output, Identifier, entry.Id, nl);
        WriteField(output, Date, entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), nl);
        WriteField(output, Summary, entry.Summary.Trim(), nl);
        if (entry.IncidentId is not null)
        {
            WriteField(output, Incident, entry.IncidentId, nl);
        }

        foreach (KeyValuePair<string, string> field in entry.Details)
        {
            WriteField(output, field.Key.Trim(), field.Value.Trim(), nl);
        }

        output.Append(nl).Append(markdown.AsSpan(scan.InsertAt));
        return output.ToString();
    }

    /// <summary>Rejects missing contract fields and generated Markdown structural injection.</summary>
    /// <param name="entry">Entry being inserted.</param>
    private static void Validate(MaintenanceRecord entry)
    {
        if (!ValidId(entry.Id, entry.Kind) || entry.Date == DateOnly.MinValue ||
            string.IsNullOrWhiteSpace(entry.Summary))
        {
            throw new ArgumentException("ID, date et résumé valides requis.", nameof(entry));
        }

        CheckOneLine(entry.Summary);
        if (entry.IncidentId is not null &&
            (entry.Kind != MaintenanceRecordKind.Intervention ||
             !ValidId(entry.IncidentId, MaintenanceRecordKind.Incident)))
        {
            throw new ArgumentException("Référence d'incident invalide.", nameof(entry));
        }

        foreach (KeyValuePair<string, string> field in entry.Details)
        {
            string key = field.Key.Trim();
            if (key.Length == 0 || key.Contains(':') || key.Contains('*') ||
                key.Contains('\n') || key.Contains('\r') || Reserved(key))
            {
                throw new ArgumentException("Champ de maintenance invalide : " + key, nameof(entry));
            }

            CheckOneLine(field.Value);
        }
    }

    /// <summary>Prevents arbitrary values from splitting a generated Markdown field.</summary>
    /// <param name="value">Raw field value.</param>
    private static void CheckOneLine(string value)
    {
        if (value is null || value.Contains('\n') || value.Contains('\r'))
        {
            throw new ArgumentException("Un champ de maintenance doit tenir sur une ligne.");
        }
    }

    /// <summary>Checks the typed and portable identifier format.</summary>
    /// <param name="id">Text identifier.</param>
    /// <param name="kind">Required type.</param>
    /// <returns>True for a prefixed GUID without separators.</returns>
    private static bool ValidId(string? id, MaintenanceRecordKind kind)
    {
        string prefix = kind == MaintenanceRecordKind.Incident ? "INC-" : "INT-";
        return id is not null &&
               id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               Guid.TryParseExact(id[prefix.Length..], "N", out Guid _);
    }

    /// <summary>Tests whether a custom field collides with mandatory properties.</summary>
    /// <param name="key">Custom field name.</param>
    /// <returns>Whether it is reserved by the contract.</returns>
    private static bool Reserved(string key)
    {
        return key.Equals(Identifier, StringComparison.OrdinalIgnoreCase) ||
               key.Equals(Date, StringComparison.OrdinalIgnoreCase) ||
               key.Equals(Summary, StringComparison.OrdinalIgnoreCase) ||
               key.Equals(Incident, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes a plain-text, user-editable Markdown list item.</summary>
    /// <param name="output">Markdown builder.</param>
    /// <param name="key">Visible field label.</param>
    /// <param name="value">Single-line value.</param>
    /// <param name="nl">Line separator.</param>
    private static void WriteField(StringBuilder output, string key, string value, string nl)
    {
        output.Append("- **").Append(key).Append(" :** ").Append(value).Append(nl);
    }

    /// <summary>Separates a new entry from original content with at least one blank line.</summary>
    /// <param name="output">Existing source prefix.</param>
    /// <param name="nl">Original newline style.</param>
    private static void EnsureBlankLine(StringBuilder output, string nl)
    {
        if (output.Length == 0)
        {
            return;
        }

        string prefix = output.ToString();
        if (prefix.EndsWith(nl + nl, StringComparison.Ordinal))
        {
            return;
        }

        if (!prefix.EndsWith(nl, StringComparison.Ordinal))
        {
            output.Append(nl);
        }

        output.Append(nl);
    }

    /// <summary>Finds a unique history and its records outside fenced examples.</summary>
    /// <param name="markdown">Source Markdown.</param>
    /// <returns>Parse diagnostics and non-destructive insertion point.</returns>
    private static ScanResult Scan(string markdown)
    {
        List<SourceLine> lines = ReadLines(markdown);
        List<MaintenanceRecord> records = [];
        List<MaintenanceDiagnostic> problems = [];
        int sections = 0;
        int insertAt = markdown.Length;
        bool inside = false;
        PendingBlock? pending = null;
        char activeFence = '\0';
        int activeLength = 0;

        for (int index = 0; index < lines.Count; index++)
        {
            SourceLine line = lines[index];
            if (TryFence(line.Text, out char marker, out int run, out bool closing))
            {
                if (activeFence == '\0')
                {
                    activeFence = marker;
                    activeLength = run;
                }
                else if (marker == activeFence && run >= activeLength && closing)
                {
                    activeFence = '\0';
                    activeLength = 0;
                }

                continue;
            }

            if (activeFence != '\0' ||
                !TryHeading(line.Text, out int level, out string heading))
            {
                continue;
            }

            if (level == 2)
            {
                if (inside)
                {
                    if (pending is PendingBlock previous)
                    {
                        ReadBlock(lines, previous, index, records, problems);
                        pending = null;
                    }

                    if (sections == 1)
                    {
                        insertAt = line.Start;
                    }

                    inside = false;
                }

                if (heading.Equals(HistoryHeading, StringComparison.OrdinalIgnoreCase))
                {
                    sections++;
                    inside = true;
                    if (sections > 1)
                    {
                        problems.Add(new MaintenanceDiagnostic(line.Number,
                            "Plusieurs historiques de maintenance."));
                    }
                }

                continue;
            }

            if (inside && level == 3)
            {
                if (pending is PendingBlock previous)
                {
                    ReadBlock(lines, previous, index, records, problems);
                    pending = null;
                }

                if (heading.Equals("Incident", StringComparison.OrdinalIgnoreCase))
                {
                    pending = new PendingBlock(index, MaintenanceRecordKind.Incident);
                }
                else if (heading.Equals("Intervention", StringComparison.OrdinalIgnoreCase))
                {
                    pending = new PendingBlock(index, MaintenanceRecordKind.Intervention);
                }
            }
        }

        if (pending is PendingBlock final)
        {
            ReadBlock(lines, final, lines.Count, records, problems);
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> incidents = records
            .Where(item => item.Kind == MaintenanceRecordKind.Incident)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (MaintenanceRecord record in records)
        {
            if (!ids.Add(record.Id))
            {
                problems.Add(new MaintenanceDiagnostic(0, "ID en double : " + record.Id));
            }

            if (record.IncidentId is not null && !incidents.Contains(record.IncidentId))
            {
                problems.Add(new MaintenanceDiagnostic(0, "Incident lié manquant : " + record.IncidentId));
            }
        }

        return new ScanResult(new MaintenanceParseResult(records, problems), sections, insertAt);
    }

    /// <summary>Reads one structured block without stripping free-form Markdown from the file.</summary>
    /// <param name="lines">Original source lines.</param>
    /// <param name="block">Typed heading.</param>
    /// <param name="end">Exclusive next heading index.</param>
    /// <param name="records">Parsed records.</param>
    /// <param name="problems">Diagnostics.</param>
    private static void ReadBlock(
        IReadOnlyList<SourceLine> lines, PendingBlock block, int end,
        ICollection<MaintenanceRecord> records, ICollection<MaintenanceDiagnostic> problems)
    {
        Dictionary<string, string> properties = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        bool duplicate = false;
        char activeFence = '\0';
        int activeLength = 0;
        for (int index = block.LineIndex + 1; index < end; index++)
        {
            string line = lines[index].Text;
            if (TryFence(line, out char marker, out int length, out bool closing))
            {
                if (activeFence == '\0')
                {
                    activeFence = marker;
                    activeLength = length;
                }
                else if (marker == activeFence && length >= activeLength && closing)
                {
                    activeFence = '\0';
                    activeLength = 0;
                }

                continue;
            }

            if (activeFence == '\0' &&
                TryField(line, out string key, out string value) &&
                !properties.TryAdd(key, value))
            {
                duplicate = true;
            }
        }

        int lineNumber = lines[block.LineIndex].Number;
        if (duplicate ||
            !properties.TryGetValue(Identifier, out string? id) ||
            !ValidId(id, block.Kind) ||
            !properties.TryGetValue(Date, out string? dateText) ||
            !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly date) ||
            !properties.TryGetValue(Summary, out string? summary) ||
            string.IsNullOrWhiteSpace(summary))
        {
            problems.Add(new MaintenanceDiagnostic(lineNumber,
                "Bloc incomplet ou invalide (ID/date/résumé)."));
            return;
        }

        properties.TryGetValue(Incident, out string? linkedId);
        if (linkedId is not null &&
            (block.Kind != MaintenanceRecordKind.Intervention ||
             !ValidId(linkedId, MaintenanceRecordKind.Incident)))
        {
            problems.Add(new MaintenanceDiagnostic(lineNumber, "ID de l'incident lié invalide."));
            return;
        }

        Dictionary<string, string> details = properties
            .Where(field => !Reserved(field.Key))
            .ToDictionary(field => field.Key, field => field.Value, StringComparer.OrdinalIgnoreCase);
        records.Add(new MaintenanceRecord
        {
            Kind = block.Kind,
            Id = id!,
            Date = date,
            Summary = summary,
            IncidentId = linkedId,
            Details = details
        });
    }

    /// <summary>Recognizes a bold-label Markdown bullet.</summary>
    /// <param name="line">Raw line.</param>
    /// <param name="key">Property name.</param>
    /// <param name="value">Property value.</param>
    /// <returns>Whether a property was found.</returns>
    private static bool TryField(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        string trimmed = line.TrimStart(' ');
        if (!trimmed.StartsWith("- **", StringComparison.Ordinal))
        {
            return false;
        }

        int separator = trimmed.IndexOf(" :**", 4, StringComparison.Ordinal);
        if (separator < 5)
        {
            return false;
        }

        key = trimmed.Substring(4, separator - 4).Trim();
        value = trimmed[(separator + 4)..].Trim();
        return key.Length > 0;
    }

    /// <summary>Reads an ATX heading and ignores indented code.</summary>
    /// <param name="line">Source line.</param>
    /// <param name="level">Heading depth.</param>
    /// <param name="title">Heading text.</param>
    /// <returns>Whether an ATX heading was found.</returns>
    private static bool TryHeading(string line, out int level, out string title)
    {
        level = 0;
        title = string.Empty;
        int indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        if (indent > 3)
        {
            return false;
        }

        int cursor = indent;
        while (cursor < line.Length && line[cursor] == '#')
        {
            cursor++;
        }

        level = cursor - indent;
        if (level < 1 || level > 6 ||
            (cursor < line.Length && !char.IsWhiteSpace(line[cursor])))
        {
            return false;
        }

        title = line[cursor..].Trim().TrimEnd('#').Trim();
        return true;
    }

    /// <summary>Recognizes code fences, including closing markers without info strings.</summary>
    /// <param name="line">Raw Markdown line.</param>
    /// <param name="marker">Fence character.</param>
    /// <param name="size">Fence run length.</param>
    /// <param name="closing">Whether it can close a block.</param>
    /// <returns>Whether this line contains a fence marker.</returns>
    private static bool TryFence(string line, out char marker, out int size, out bool closing)
    {
        marker = '\0';
        size = 0;
        closing = false;
        int indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        if (indent > 3 || indent == line.Length ||
            (line[indent] != (char)96 && line[indent] != '~'))
        {
            return false;
        }

        marker = line[indent];
        int cursor = indent;
        while (cursor < line.Length && line[cursor] == marker)
        {
            cursor++;
        }

        size = cursor - indent;
        closing = string.IsNullOrWhiteSpace(line[cursor..]);
        return size >= 3;
    }

    /// <summary>Retains exact original offsets, including CRLF or missing final newline.</summary>
    /// <param name="source">Original text.</param>
    /// <returns>Line metadata with one-based line numbers.</returns>
    private static List<SourceLine> ReadLines(string source)
    {
        List<SourceLine> lines = [];
        int start = 0;
        int number = 1;
        while (start < source.Length)
        {
            int newline = source.IndexOf('\n', start);
            int end = newline < 0 ? source.Length : newline;
            if (end > start && source[end - 1] == '\r')
            {
                end--;
            }

            lines.Add(new SourceLine(start, number, source[start..end]));
            start = newline < 0 ? source.Length : newline + 1;
            number++;
        }

        return lines;
    }
}
