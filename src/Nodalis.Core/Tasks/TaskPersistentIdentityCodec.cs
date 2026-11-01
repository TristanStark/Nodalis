using System.Text.RegularExpressions;

namespace Nodalis.Core.Tasks;

/// <summary>
/// One original checkbox line and its opt-in identity, unrelated to line
/// numbers or the Markdown document ID.
/// </summary>
/// <param name="LineNumber">One-based line in the source document.</param>
/// <param name="Id">Explicit persistent identity, or null for legacy checkboxes.</param>
/// <param name="RawLine">Complete original Markdown checkbox.</param>
public sealed record TaskPersistentIdentityEntry(
    int LineNumber, Guid? Id, string RawLine);

/// <summary>Source diagnostic: never silently invent or repair an identity.</summary>
/// <param name="LineNumber">Affected original source line.</param>
/// <param name="Message">Human-readable reason identity is not usable.</param>
public sealed record TaskPersistentIdentityIssue(
    int LineNumber, string Message);

/// <summary>Read-only list of detected IDs and identity defects.</summary>
/// <param name="Entries">Checkboxes outside fenced code blocks.</param>
/// <param name="Issues">Malformed and duplicate marker diagnostics.</param>
public sealed record TaskPersistentIdentityScan(
    IReadOnlyList<TaskPersistentIdentityEntry> Entries,
    IReadOnlyList<TaskPersistentIdentityIssue> Issues);

/// <summary>
/// Portable, opt-in checkbox identifiers. The HTML comment lives at the end
/// of the checkbox line and survives document moves, line insertion, sorting,
/// completion and changes of readable metadata.
/// </summary>
public static class TaskPersistentIdentityCodec
{
    private const string Prefix = "<!-- nodalis:task:";
    private const string Suffix = " -->";
    private static readonly Regex Checkbox = new(
        @"^[ \t]*[-*+]\s+\[[ xX]\]\s+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Builds the one canonical hidden inline task identity marker.</summary>
    /// <param name="id">Fresh, non-empty persistent task GUID.</param>
    /// <returns>Markdown HTML comment with a stable GUID.</returns>
    public static string FormatMarker(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifiant de tâche vide.", nameof(id));
        }

        return Prefix + id.ToString("D") + Suffix;
    }

    /// <summary>
    /// Parses a real checkbox's optional exact suffix marker, rejecting
    /// partial or duplicate markers instead of treating them as unassigned.
    /// </summary>
    /// <param name="line">Complete Markdown checkbox line.</param>
    /// <param name="cleanLine">Line with the marker removed, or unchanged.</param>
    /// <returns>Persistent GUID when present; null for legacy content.</returns>
    public static Guid? ReadMarker(string line, out string cleanLine)
    {
        ArgumentNullException.ThrowIfNull(line);
        cleanLine = line;
        if (!Checkbox.IsMatch(line))
        {
            return null;
        }

        int position = line.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
        if (position < 0)
        {
            return null;
        }

        string candidate = line[position..].TrimEnd();
        if (!candidate.StartsWith(Prefix, StringComparison.Ordinal) ||
            !candidate.EndsWith(Suffix, StringComparison.Ordinal) ||
            candidate.IndexOf(Prefix, Prefix.Length,
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidDataException(
                "Marqueur d'identité de tâche invalide ou répété.");
        }

        string raw = candidate[Prefix.Length..^Suffix.Length];
        if (!Guid.TryParseExact(raw, "D", out Guid id) ||
            id == Guid.Empty || raw != id.ToString("D"))
        {
            throw new InvalidDataException(
                "GUID de tâche invalide : aucune migration implicite.");
        }

        cleanLine = line[..position].TrimEnd();
        if (!Checkbox.IsMatch(cleanLine))
        {
            throw new InvalidDataException(
                "Identité de tâche dépourvue de checkbox lisible.");
        }

        return id;
    }

    /// <summary>
    /// Appends one stable marker only to an existing checkbox. Existing
    /// identities cannot be replaced; repeated application is idempotent.
    /// </summary>
    /// <param name="line">Exact source checkbox.</param>
    /// <param name="id">Stable ID assigned by explicit user action.</param>
    /// <returns>Updated checkbox preserving all existing visible text.</returns>
    public static string InsertMarker(string line, Guid id)
    {
        string marker = FormatMarker(id);
        if (!Checkbox.IsMatch(line))
        {
            throw new InvalidDataException(
                "La source sélectionnée n'est plus une checkbox Markdown.");
        }

        Guid? existing = ReadMarker(line, out string cleanLine);
        if (existing == id)
        {
            return line;
        }

        if (existing is not null)
        {
            throw new InvalidOperationException(
                "Identité de tâche déjà attribuée : remplacement interdit.");
        }

        return cleanLine + " " + marker;
    }

    /// <summary>
    /// Retains the original checkbox ID when metadata rewrites a line,
    /// rejecting untrusted malformed markers before an editor can erase them.
    /// </summary>
    /// <param name="previous">Exact old checkbox source.</param>
    /// <param name="updated">Rebuilt checkbox with new readable metadata.</param>
    /// <returns>Rebuilt checkbox with its original persistent ID.</returns>
    public static string PreserveMarker(string previous, string updated)
    {
        Guid? id = ReadMarker(previous, out string _);
        return id is Guid stable ? InsertMarker(updated, stable) : updated;
    }

    /// <summary>
    /// Inspects checkboxes outside common fenced Markdown examples without
    /// modifying any source or adding missing IDs. Duplicates are diagnosed.
    /// </summary>
    /// <param name="lines">Original Markdown lines.</param>
    /// <returns>Eligible task checkboxes and explicit identity diagnostics.</returns>
    public static TaskPersistentIdentityScan Inspect(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        List<TaskPersistentIdentityEntry> entries = [];
        List<TaskPersistentIdentityIssue> issues = [];
        HashSet<Guid> seen = [];
        char fenceChar = '\0';
        int fenceSize = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            string stripped = line.TrimStart();
            int indent = line.Length - stripped.Length;
            if (indent <= 3 && stripped.Length >= 3 &&
                (stripped[0] is '`' or '~'))
            {
                char candidate = stripped[0];
                int length = 0;
                while (length < stripped.Length && stripped[length] == candidate)
                {
                    length++;
                }

                if (length >= 3 && fenceChar == '\0')
                {
                    fenceChar = candidate;
                    fenceSize = length;
                    continue;
                }

                if (fenceChar == candidate && length >= fenceSize)
                {
                    fenceChar = '\0';
                    fenceSize = 0;
                    continue;
                }
            }

            if (fenceChar != '\0' || !Checkbox.IsMatch(line))
            {
                continue;
            }

            try
            {
                Guid? id = ReadMarker(line, out string _);
                entries.Add(new TaskPersistentIdentityEntry(index + 1, id, line));
                if (id is Guid stable && !seen.Add(stable))
                {
                    issues.Add(new TaskPersistentIdentityIssue(
                        index + 1, "GUID de tâche dupliqué dans le document."));
                }
            }
            catch (InvalidDataException exception)
            {
                entries.Add(new TaskPersistentIdentityEntry(
                    index + 1, null, line));
                issues.Add(new TaskPersistentIdentityIssue(
                    index + 1, exception.Message));
            }
        }

        return new TaskPersistentIdentityScan(entries, issues);
    }
}
