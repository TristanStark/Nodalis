using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Core.Relations;

namespace Nodalis.Core.Maintenance;

/// <summary>
/// Explicit intervention/incident relation derived directly from source Markdown.
/// A missing target is retained, never silently resolved by its mutable label.
/// </summary>
public sealed record MaintenanceDocumentReference
{
    public required string SourcePath { get; init; }
    public required string RecordId { get; init; }
    public required MaintenanceRecordKind SourceKind { get; init; }
    public string? IncidentId { get; init; }
    public required string RelationType { get; init; }
    public Guid? TargetId { get; init; }
    public required string RawTargetId { get; init; }
    public required string TargetLabel { get; init; }
    public required int LineNumber { get; init; }
    public string? TargetRelativePath { get; init; }
    public string? TargetQualifiedName { get; init; }
    public bool IsBroken { get; init; }

    /// <summary>Presentation label explicitly indicating a missing or renamed target.</summary>
    public string DisplayLabel => (IsBroken ? "⚠ Référence cassée · " : "") +
        RelationType + " → " + (TargetQualifiedName ?? TargetLabel);
    /// <summary>Source context, including the stable maintenance record identity.</summary>
    public string OriginLabel => SourceKind + " · " + RecordId + " · ligne " + LineNumber;
}

/// <summary>
/// Read-only maintenance relation projection for the future impact-analysis engine.
/// It consumes existing typed Markdown relations and the current identity registry.
/// </summary>
public static class MaintenanceRelationIndex
{
    /// <summary>
    /// Reads explicit relations contained in level-three maintenance incident and
    /// intervention blocks. Other Markdown relations are deliberately ignored.
    /// </summary>
    /// <param name="sourcePath">Physical source document.</param>
    /// <param name="markdown">Authoritative Markdown text.</param>
    /// <param name="catalog">Current identity targets, normally from a refreshed link index.</param>
    /// <returns>Typed records with stable IDs, line positions and broken-link status.</returns>
    public static IReadOnlyList<MaintenanceDocumentReference> Parse(
        string sourcePath, string markdown, LinkIndexCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(catalog);
        IReadOnlyList<MaintenanceHistoryItem> records =
            MaintenanceHistoryIndex.Parse(sourcePath, string.Empty, markdown);
        Dictionary<int, MaintenanceHistoryItem> recordsByLine = records
            .Where(record => record.Id is not null)
            .ToDictionary(record => record.LineNumber);
        Dictionary<Guid, LinkTargetEntry> targets = catalog.Targets
            .Where(target => target.Kind == LinkTargetKind.Document)
            .GroupBy(target => target.Id)
            .ToDictionary(group => group.Key, group => group.First());
        IReadOnlyList<MarkdownOutlineEntry> headings = MarkdownOutlineParser.Parse(markdown);
        List<MaintenanceDocumentReference> relations = [];
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
                !recordsByLine.TryGetValue(heading.LineNumber, out MaintenanceHistoryItem? record))
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

            string block = markdown[heading.Offset..end];
            foreach (MarkdownRelationEntry relation in MarkdownRelationParser.Parse(block))
            {
                LinkTargetEntry? target = relation.TargetId is Guid id &&
                    targets.TryGetValue(id, out LinkTargetEntry? candidate)
                    ? candidate : null;
                relations.Add(new MaintenanceDocumentReference
                {
                    SourcePath = sourcePath,
                    RecordId = record.Id!,
                    SourceKind = record.Kind,
                    IncidentId = record.Kind == MaintenanceRecordKind.Incident
                        ? record.Id : record.IncidentId,
                    RelationType = relation.RelationType,
                    TargetId = relation.TargetId,
                    RawTargetId = relation.RawTargetId,
                    TargetLabel = relation.TargetLabel ?? relation.RawTargetId,
                    TargetQualifiedName = target?.QualifiedName,
                    TargetRelativePath = target?.RelativePath,
                    IsBroken = target is null,
                    LineNumber = heading.LineNumber + relation.LineNumber - 1
                });
            }
        }

        return relations;
    }

    /// <summary>
    /// Returns only declared relations on the incident or on interventions
    /// explicitly pointing to that incident, within the same Markdown file.
    /// No inferred dependency or reciprocal link is created.
    /// </summary>
    /// <param name="incident">Incident whose references are being inspected.</param>
    /// <param name="relations">All parsed outgoing maintenance relations.</param>
    /// <returns>Incident and associated intervention references.</returns>
    public static IReadOnlyList<MaintenanceDocumentReference> ForIncident(
        MaintenanceHistoryItem incident,
        IEnumerable<MaintenanceDocumentReference> relations)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(relations);
        if (incident.Kind != MaintenanceRecordKind.Incident || incident.Id is null)
        {
            return [];
        }

        return relations.Where(relation =>
            string.Equals(relation.SourcePath, incident.SourcePath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(relation.IncidentId, incident.Id,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(relation => relation.LineNumber).ToArray();
    }

    /// <summary>Gets only the relations explicitly declared by one source block.</summary>
    /// <param name="record">Incident or intervention.</param>
    /// <param name="relations">Parsed relations.</param>
    /// <returns>Direct relations in source order.</returns>
    public static IReadOnlyList<MaintenanceDocumentReference> ForRecord(
        MaintenanceHistoryItem record,
        IEnumerable<MaintenanceDocumentReference> relations)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(relations);
        return record.Id is null ? [] : relations.Where(relation =>
            string.Equals(relation.SourcePath, record.SourcePath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(relation.RecordId, record.Id,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(relation => relation.LineNumber).ToArray();
    }
}
