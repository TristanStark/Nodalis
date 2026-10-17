using System.Diagnostics;
using Nodalis.Core.Impact;
using Nodalis.Core.Links;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Relations;

/// <summary>Exercises the v1.4 diagram, maintenance integration and cache invariants.</summary>
internal static class ImpactV14SmokeTests
{
    /// <summary>Executes all v1.4-specific zero-dependency smoke cases.</summary>
    public static void Run()
    {
        VerifyDiagramBoundaries();
        VerifyMaintenanceProvenance();
        VerifyCacheAndInvalidation();
        VerifyIncrementalRebuildEquivalence();
    }

    /// <summary>Checks arrow direction, graph bounds and context-only labeling.</summary>
    private static void VerifyDiagramBoundaries()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        LinkIndexCatalog catalog = new()
        {
            Targets = [Target(a, "A"), Target(b, "B"), Target(c, "C")],
            Relations =
            [
                Relation(a, b, "dépend de"),
                Relation(c, b, "est lié à")
            ]
        };
        ImpactGraphIndex graph = ImpactGraphIndex.Build(catalog);
        ImpactAnalysisResult analysis = ImpactAnalyzer.Analyze(graph, b);
        ImpactDiagram diagram = ImpactDiagramProjection.Create(
            catalog.Targets[1], analysis.Findings, graph, maxNodes: 3);
        Assert(diagram.Nodes.Count == 3 && diagram.Edges.Count == 2 &&
            diagram.Edges.Any(edge => edge.FromId == b && edge.ToId == a &&
                edge.Evidence == ImpactEvidence.Explicit) &&
            diagram.Edges.Any(edge => edge.FromId == b && edge.ToId == c &&
                edge.Evidence == ImpactEvidence.Suggested),
            "Diagram must distinguish directional dependencies from context.");
        ImpactDiagram bounded = ImpactDiagramProjection.Create(
            catalog.Targets[1], analysis.Findings, graph, maxNodes: 1);
        Assert(bounded.Nodes.Count == 1 && bounded.Edges.Count == 0 && bounded.IsTruncated,
            "A too-large graph must fall back to the complete accessible list.");

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        bool interrupted = false;
        try
        {
            ImpactDiagramProjection.Create(catalog.Targets[1],
                analysis.Findings, graph, 20, cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
        }

        Assert(interrupted, "Graph projection must be cancellable.");
    }

    /// <summary>
    /// Verifies that incident/intervention references retain the actual source line
    /// and record ID and never invent propagation edges.
    /// </summary>
    private static void VerifyMaintenanceProvenance()
    {
        Guid maintenanceId = Guid.NewGuid();
        Guid specificationId = Guid.NewGuid();
        LinkTargetEntry sheet = Target(maintenanceId, "Fiche de Maintenance.md");
        LinkTargetEntry spec = Target(specificationId, "Specification.md");
        LinkIndexCatalog catalog = new() { Targets = [sheet, spec] };
        MaintenanceRecord incident = new()
        {
            Kind = MaintenanceRecordKind.Incident,
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident),
            Date = new DateOnly(2026, 10, 17),
            Summary = "Erreur API"
        };
        string markdown = MaintenanceMarkdownCodec.Append("# Maintenance\r\n", incident);
        markdown += Environment.NewLine +
            MarkdownRelationParser.Format("documente", specificationId, spec.DisplayName) +
            Environment.NewLine;
        IReadOnlyList<ImpactMaintenanceContext> matched =
            ImpactMaintenanceQuery.Parse(sheet, markdown, catalog);
        Assert(matched.Count == 1 &&
            matched[0].Record.Id == incident.Id &&
            matched[0].Reference.LineNumber > matched[0].Record.LineNumber &&
            matched[0].RelatedDocument.Id == specificationId &&
            matched[0].Explanation.Contains("aucune propagation", StringComparison.Ordinal),
            "Maintenance context must retain explicit GUID, provenance and noncausal semantics.");
        Assert(ImpactMaintenanceQuery.Filter(matched, [specificationId]).Count == 1 &&
            ImpactMaintenanceQuery.Filter(matched, [Guid.NewGuid()]).Count == 0,
            "Maintenance filtering must not infer relations via text resemblance.");
        Assert(ImpactGraphIndex.Build(catalog).Edges.Count == 0,
            "Maintenance context must not fabricate a graph dependency.");
        LinkIndexCatalog removed = new() { Targets = [sheet] };
        Assert(ImpactMaintenanceQuery.Parse(sheet, markdown, removed).Count == 0,
            "A deleted document's relation must never be redirected to a homonym.");
    }

    /// <summary>Verifies cache reuse, targeted index changes and equivalence with full rebuilds.</summary>
    private static void VerifyCacheAndInvalidation()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        LinkIndexCatalog catalog = new()
        {
            Targets = [Target(a, "A"), Target(b, "B")],
            Relations = [Relation(a, b, "dépend de")]
        };
        ImpactGraphSnapshotCache cache = new();
        ImpactGraphIndex first = cache.GetOrBuild(catalog);
        ImpactGraphIndex warm = cache.GetOrBuild(catalog);
        Assert(ReferenceEquals(first, warm) &&
            cache.RebuildCount == 1 && cache.CacheHitCount == 1,
            "Unchanged derived indexes must reuse the graph without rebuilding.");

        catalog.Targets[1] = catalog.Targets[1] with { DisplayName = "Renamed B" };
        ImpactGraphIndex renamed = cache.GetOrBuild(catalog);
        Assert(!ReferenceEquals(first, renamed) &&
            renamed.Targets[b].DisplayName == "Renamed B" &&
            renamed.Edges.Select(edge => edge.Id)
                .SequenceEqual(ImpactGraphIndex.Build(catalog).Edges.Select(edge => edge.Id)),
            "A rename must invalidate cached labels but preserve stable-ID topology.");

        catalog.Relations.Clear();
        ImpactGraphIndex removed = cache.GetOrBuild(catalog);
        Assert(removed.Edges.Count == 0 && cache.RebuildCount == 3,
            "Removing a relation must invalidate stale graph edges.");
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        bool interrupted = false;
        try
        {
            cache.GetOrBuild(catalog, cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
        }

        Assert(interrupted && cache.RebuildCount == 3,
            "Cancellation must not mutate cache state or underlying documents.");
        cache.Clear();
        Assert(cache.GetOrBuild(catalog).Edges.Count == 0 &&
            cache.RebuildCount == 4,
            "A cleared cache must reconstruct the same graph from canonical index data.");

        Stopwatch watch = Stopwatch.StartNew();
        for (int count = 0; count < 25; count++)
        {
            cache.GetOrBuild(catalog);
        }
        watch.Stop();
        Console.WriteLine("[Impact v1.4] warm graph snapshot x25: " +
            watch.Elapsed.TotalMilliseconds.ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture) + " ms");
    }


    /// <summary>
    /// Compares source-targeted invalidation against full reconstruction after
    /// changed relations, deleted/restored documents and unresolved references.
    /// </summary>
    private static void VerifyIncrementalRebuildEquivalence()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        LinkIndexCatalog catalog = new()
        {
            Targets = [Target(a, "A"), Target(b, "B"), Target(c, "C")],
            Relations =
            [
                Relation(a, b, "dépend de"),
                Relation(c, b, "teste")
            ]
        };
        ImpactGraphSnapshotCache cache = new();
        cache.GetOrBuild(catalog);
        catalog.Relations[0] = Relation(a, c, "implémente");
        ImpactGraphIndex changed = cache.GetOrBuild(catalog);
        Assert(cache.PartialRebuildCount == 1 &&
            cache.LastReindexedSourceCount == 1,
            "A single edited relation should only rebuild its declaring source.");
        Equivalent(changed, ImpactGraphIndex.Build(catalog));

        // Missing GUID destinations must be diagnosed without stale ghost edges.
        catalog.Targets.RemoveAll(item => item.Id == c);
        ImpactGraphIndex deleted = cache.GetOrBuild(catalog);
        Equivalent(deleted, ImpactGraphIndex.Build(catalog));
        Assert(deleted.Diagnostics.Any(item => item.SourceId == a),
            "Deleting a referenced target must produce a broken-link diagnosis.");
        catalog.Targets.Add(Target(c, "C restored"));
        ImpactGraphIndex restored = cache.GetOrBuild(catalog);
        Equivalent(restored, ImpactGraphIndex.Build(catalog));
        Assert(!restored.Diagnostics.Any(item => item.SourceId == a),
            "Restoring a target must recompute previously broken relations.");

        catalog.References.Add(new LinkReferenceEntry
        {
            SourceId = a, TargetId = null, RawTarget = "Inconnu",
            LineNumber = 11, Excerpt = "[[Inconnu]]"
        });
        Equivalent(cache.GetOrBuild(catalog), ImpactGraphIndex.Build(catalog));
    }

    /// <summary>Compares exact IDs, relation semantics and diagnostics for two projections.</summary>
    /// <param name="actual">Incrementally updated result.</param>
    /// <param name="expected">Fully rebuilt reference.</param>
    private static void Equivalent(ImpactGraphIndex actual, ImpactGraphIndex expected)
    {
        Assert(actual.Targets.Keys.OrderBy(id => id)
                .SequenceEqual(expected.Targets.Keys.OrderBy(id => id)) &&
            actual.Edges.Select(item => (item.Id, item.SourceId, item.TargetId,
                    item.RelationType, item.Evidence, item.Propagation))
                .SequenceEqual(expected.Edges.Select(item => (item.Id, item.SourceId,
                    item.TargetId, item.RelationType, item.Evidence, item.Propagation))) &&
            actual.Diagnostics.Select(item => (item.SourceId, item.LineNumber,
                    item.RawTarget, item.Message))
                .SequenceEqual(expected.Diagnostics.Select(item =>
                    (item.SourceId, item.LineNumber, item.RawTarget, item.Message))),
            "Incremental graph differs from a complete reconstruction.");
    }

    /// <summary>Creates a stable document fixture.</summary>
    /// <param name="id">Identity.</param>
    /// <param name="name">Displayed name.</param>
    /// <returns>Indexed Markdown target.</returns>
    private static LinkTargetEntry Target(Guid id, string name)
    {
        return new LinkTargetEntry
        {
            Id = id, Kind = LinkTargetKind.Document, DisplayName = name,
            QualifiedName = "Projet / " + name,
            RelativePath = "Projet/" + name, ScopeIdentity = "project:test"
        };
    }

    /// <summary>Creates a typed relation fixture.</summary>
    /// <param name="from">Source.</param>
    /// <param name="to">Destination.</param>
    /// <param name="type">Declared type.</param>
    /// <returns>Valid typed reference.</returns>
    private static TypedRelationEntry Relation(Guid from, Guid to, string type)
    {
        return new TypedRelationEntry
        {
            SourceId = from, TargetId = to, RawTargetId = to.ToString("D"),
            RelationType = type, LineNumber = 3, Excerpt = type
        };
    }

    /// <summary>Throws on regression.</summary>
    /// <param name="result">Invariant.</param>
    /// <param name="message">Explanation.</param>
    private static void Assert(bool result, string message)
    {
        if (!result)
        {
            throw new InvalidOperationException(message);
        }
    }
}
