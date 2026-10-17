using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;

namespace Nodalis.Core.Impact;

/// <summary>
/// A maintenance record explicitly referring to a stable indexed document.
/// This is traceable context, not a new dependency edge in the impact graph.
/// </summary>
public sealed record ImpactMaintenanceContext(
    LinkTargetEntry SourceDocument,
    LinkTargetEntry RelatedDocument,
    MaintenanceHistoryItem Record,
    MaintenanceDocumentReference Reference)
{
    /// <summary>Gets an explicit, noncausal explanation for the user interface.</summary>
    public string Explanation => "Relation déclarée : " + Reference.RelationType +
        " · aucune propagation causale présumée";
}

/// <summary>
/// Projects source Markdown maintenance records without creating synthetic graph identities.
/// </summary>
public static class ImpactMaintenanceQuery
{
    /// <summary>
    /// Parses a canonical maintenance sheet and returns only records explicitly
    /// linked by GUID to an active document in the current identity registry.
    /// </summary>
    /// <param name="source">Indexed maintenance Markdown document.</param>
    /// <param name="markdown">Source content read without changes.</param>
    /// <param name="catalog">Current stable identity registry.</param>
    /// <returns>Linked incidents/interventions with their original source line.</returns>
    public static IReadOnlyList<ImpactMaintenanceContext> Parse(
        LinkTargetEntry source, string markdown, LinkIndexCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(catalog);
        if (source.Kind != LinkTargetKind.Document)
        {
            return [];
        }

        IReadOnlyList<MaintenanceHistoryItem> records = MaintenanceHistoryIndex.Parse(
            source.RelativePath, source.QualifiedName, markdown);
        IReadOnlyList<MaintenanceDocumentReference> references =
            MaintenanceRelationIndex.Parse(source.RelativePath, markdown, catalog);
        Dictionary<string, MaintenanceHistoryItem> byId = records
            .Where(item => item.Id is not null && !item.IsIncomplete)
            .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<Guid, LinkTargetEntry> targets = catalog.Targets
            .Where(item => item.Kind == LinkTargetKind.Document)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        List<ImpactMaintenanceContext> contexts = [];
        foreach (MaintenanceDocumentReference reference in references)
        {
            if (reference.IsBroken || reference.TargetId is not Guid targetId ||
                !targets.TryGetValue(targetId, out LinkTargetEntry? target) ||
                !byId.TryGetValue(reference.RecordId, out MaintenanceHistoryItem? record))
            {
                continue;
            }

            contexts.Add(new ImpactMaintenanceContext(source, target, record, reference));
        }

        return contexts.OrderBy(item => item.SourceDocument.QualifiedName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Reference.LineNumber)
            .ThenBy(item => item.Record.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Filters maintenance references to one analyzed origin or its actual graph findings.
    /// No context link is promoted to a traversable graph edge.
    /// </summary>
    /// <param name="contexts">Explicitly parsed maintenance links.</param>
    /// <param name="documentIds">Document identifiers currently visible in the result.</param>
    /// <returns>Only maintenance records directly referencing those documents.</returns>
    public static IReadOnlyList<ImpactMaintenanceContext> Filter(
        IEnumerable<ImpactMaintenanceContext> contexts, IEnumerable<Guid> documentIds)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(documentIds);
        HashSet<Guid> ids = documentIds.ToHashSet();
        return contexts.Where(item => ids.Contains(item.RelatedDocument.Id))
            .OrderBy(item => item.RelatedDocument.QualifiedName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceDocument.QualifiedName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Reference.LineNumber).ToArray();
    }
}
