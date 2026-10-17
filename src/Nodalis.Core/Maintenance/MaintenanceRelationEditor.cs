using Nodalis.Core.Markdown;
using Nodalis.Core.Relations;

namespace Nodalis.Core.Maintenance;

/// <summary>
/// Inserts existing Nodalis typed-relation syntax inside one stable maintenance
/// block without rewriting any other note or manufacturing inferred links.
/// </summary>
public static class MaintenanceRelationEditor
{
    /// <summary>Appends one GUID-based typed relation with a snapshot check.</summary>
    /// <param name="markdown">Latest maintenance Markdown.</param>
    /// <param name="snapshot">History record selected before the user opened the picker.</param>
    /// <param name="type">Existing or custom relation type.</param>
    /// <param name="targetId">Stable target GUID from the identity registry.</param>
    /// <param name="label">Human label, never used to resolve identity.</param>
    /// <returns>Original text plus one canonical relation line, or unchanged for a duplicate.</returns>
    public static string Add(string markdown, MaintenanceHistoryItem snapshot,
        string type, Guid targetId, string label)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Id is null || snapshot.IsIncomplete ||
            targetId == Guid.Empty || string.IsNullOrWhiteSpace(type) ||
            string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidDataException(
                "Sélectionnez un incident ou une intervention structurée et un document valide.");
        }

        MaintenanceParseResult parsed = MaintenanceMarkdownCodec.Parse(markdown);
        if (parsed.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "La fiche contient des anomalies de maintenance : corrigez-les avant d'ajouter une relation.");
        }

        MaintenanceHistoryItem? current = MaintenanceHistoryIndex.Parse(
                snapshot.SourcePath, snapshot.ProjectName, markdown)
            .SingleOrDefault(record =>
                string.Equals(record.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase) &&
                record.Kind == snapshot.Kind);
        if (current is null || current.IsIncomplete ||
            !string.Equals(snapshot.SourceBlockHash, current.SourceBlockHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Le bloc sélectionné a été modifié depuis l'ouverture : rechargez l'historique.");
        }

        IReadOnlyList<MarkdownOutlineEntry> headings = MarkdownOutlineParser.Parse(markdown);
        MarkdownOutlineEntry? heading = headings.FirstOrDefault(candidate =>
            candidate.Level == 3 && candidate.LineNumber == current.LineNumber);
        if (heading is null)
        {
            throw new InvalidDataException("Bloc d'intervention introuvable.");
        }

        int end = markdown.Length;
        foreach (MarkdownOutlineEntry following in headings)
        {
            if (following.Offset > heading.Offset && following.Level <= 3)
            {
                end = following.Offset;
                break;
            }
        }

        string block = markdown[heading.Offset..end];
        if (MarkdownRelationParser.Parse(block).Any(relation =>
                relation.TargetId == targetId &&
                string.Equals(relation.RelationType, type.Trim(),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return markdown;
        }

        string line = MarkdownRelationParser.Format(type, targetId, label);
        int headingEnd = markdown.IndexOf('\n', heading.Offset);
        if (headingEnd < 0 || headingEnd >= end)
        {
            throw new InvalidDataException("En-tête du bloc de maintenance incomplet.");
        }

        string nl = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return markdown.Insert(headingEnd + 1, line + nl);
    }
}
