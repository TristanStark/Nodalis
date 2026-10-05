namespace Nodalis.Core.Links;

/// <summary>
/// Queries the derived link catalog for incoming references without touching the workspace.
/// </summary>
public static class ReferenceIndexQuery
{
    /// <summary>
    /// Returns internal links and typed relations that point to one stable target identifier.
    /// </summary>
    /// <param name="catalog">The current derived link catalog.</param>
    /// <param name="targetId">The target identifier.</param>
    /// <returns>Incoming references ordered by source, line and type.</returns>
    public static IReadOnlyList<ReferenceSearchEntry> GetIncoming(
            LinkIndexCatalog catalog,
            Guid targetId)
    {
        ArgumentNullException.ThrowIfNull(
            catalog);

        LinkTargetEntry? target =
            catalog.Targets.FirstOrDefault(candidate =>
                candidate.Id == targetId);

        if (target is null)
        {
            return [];
        }

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Links.LinkTargetEntry> targets =
            catalog.Targets.ToDictionary(candidate =>
                candidate.Id);

        global::System.Collections.Generic.List<global::Nodalis.Core.Links.ReferenceSearchEntry> result =
            new List<ReferenceSearchEntry>();

        foreach (LinkReferenceEntry reference in catalog.References.Where(candidate =>
                     candidate.TargetId == targetId))
        {
            if (!targets.TryGetValue(
                    reference.SourceId,
                    out LinkTargetEntry? source))
            {
                continue;
            }

            result.Add(
                new ReferenceSearchEntry
                {
                    Source =
                        source,
                    LineNumber =
                        reference.LineNumber,
                    Excerpt =
                        reference.Excerpt,
                    TypeLabel =
                        "Lien interne",
                    RelationType =
                        null,
                    IsSameScope =
                        string.Equals(
                            source.ScopeIdentity,
                            target.ScopeIdentity,
                            StringComparison.OrdinalIgnoreCase)
                });
        }

        foreach (global::Nodalis.Core.Relations.TypedRelationEntry relation in catalog.Relations.Where(candidate =>
                     candidate.TargetId == targetId))
        {
            if (!targets.TryGetValue(
                    relation.SourceId,
                    out LinkTargetEntry? source))
            {
                continue;
            }

            result.Add(
                new ReferenceSearchEntry
                {
                    Source =
                        source,
                    LineNumber =
                        relation.LineNumber,
                    Excerpt =
                        relation.Excerpt,
                    TypeLabel =
                        $"Relation · {relation.RelationType}",
                    RelationType =
                        relation.RelationType,
                    IsSameScope =
                        string.Equals(
                            source.ScopeIdentity,
                            target.ScopeIdentity,
                            StringComparison.OrdinalIgnoreCase)
                });
        }

        return result
            .OrderBy(
                entry => entry.Source.QualifiedName,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry =>
                entry.LineNumber)
            .ThenBy(
                entry => entry.TypeLabel,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
