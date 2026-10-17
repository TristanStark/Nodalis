using Nodalis.Core.Impact;
using Nodalis.Core.Links;
using Nodalis.Core.Relations;

/// <summary>
/// Exercises the v1.3 graph contracts without writing or requiring a workspace migration.
/// </summary>
internal static class ImpactAnalysisSmokeTests
{
    /// <summary>
    /// Verifies typed directions, textual context, cycles, bounds and reconstruction determinism.
    /// </summary>
    public static void Run()
    {
        VerifyRelationTruthTable();
        VerifyChainsCyclesAndDiamonds();
        VerifyBacklinksAndUnknownTypes();
        VerifyScopesAndLimits();
        VerifyBrokenTargetsAndReconstruction();
    }

    /// <summary>Checks both directions of every built-in relation against the documented policy.</summary>
    private static void VerifyRelationTruthTable()
    {
        Guid author = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid destination = Guid.Parse("00000000-0000-0000-0000-000000000002");
        foreach (ImpactRelationRule rule in ImpactRelationPolicy.KnownRules)
        {
            LinkIndexCatalog catalog = Catalog(
                [Target(author, "A"), Target(destination, "B")],
                [Relation(author, destination, rule.RelationType)]);
            ImpactGraphIndex graph = ImpactGraphIndex.Build(catalog);
            ImpactAnalysisResult fromB = ImpactAnalyzer.Analyze(graph, destination);
            ImpactAnalysisResult fromA = ImpactAnalyzer.Analyze(graph, author);

            if (rule.Propagation == ImpactPropagation.TargetToSource)
            {
                Assert(fromB.Findings.Count == 1 &&
                    fromB.Findings[0].Target.Id == author &&
                    fromB.Findings[0].Evidence == ImpactEvidence.Explicit &&
                    fromB.Findings[0].Depth == 1,
                    $"Reverse propagation expected for '{rule.RelationType}'.");
                Assert(fromA.Findings.Count == 0,
                    $"Forward propagation forbidden for '{rule.RelationType}'.");
            }
            else
            {
                Assert(fromB.Findings.Count == 1 &&
                    fromB.Findings[0].Evidence == ImpactEvidence.Suggested &&
                    fromA.Findings.Count == 1 &&
                    fromA.Findings[0].Evidence == ImpactEvidence.Suggested,
                    $"Context-only relation must not assert a dependency: '{rule.RelationType}'.");
                Assert(ImpactAnalyzer.Analyze(graph, destination,
                    new ImpactAnalysisOptions { IncludeSuggested = false }).Findings.Count == 0,
                    "Disabling suggestions must eliminate all contextual results.");
            }
        }
    }

    /// <summary>Checks shortest paths in chains and diamonds and nonrepeating cycles.</summary>
    private static void VerifyChainsCyclesAndDiamonds()
    {
        Guid a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid c = Guid.Parse("00000000-0000-0000-0000-000000000003");
        Guid d = Guid.Parse("00000000-0000-0000-0000-000000000004");
        LinkIndexCatalog catalog = Catalog(
            [Target(a, "A"), Target(b, "B"), Target(c, "C"), Target(d, "D")],
            [
                Relation(a, b, "dépend de"),
                Relation(b, c, "dépend de"),
                Relation(a, d, "implémente"),
                Relation(d, c, "documente"),
                Relation(c, a, "teste")
            ]);
        ImpactGraphIndex graph = ImpactGraphIndex.Build(catalog);
        ImpactAnalysisResult result = ImpactAnalyzer.Analyze(graph, c);
        Assert(result.Findings.Count == 3 &&
            result.Findings.Single(item => item.Target.Id == b).Depth == 1 &&
            result.Findings.Single(item => item.Target.Id == d).Depth == 1 &&
            result.Findings.Single(item => item.Target.Id == a).Depth == 2 &&
            !result.IsTruncated,
            "Diamond must deduplicate A, preserve its shortest path, and terminate despite the cycle.");
        ImpactFinding finding = result.Findings.Single(item => item.Target.Id == a);
        Assert(finding.Path[0].TargetId == c &&
            finding.Path[1].SourceId == a,
            "Explanatory path must lead from the changed C to A.");
        Assert(ImpactAnalyzer.Analyze(graph, c).Findings.Select(item => item.Target.Id)
                .SequenceEqual(result.Findings.Select(item => item.Target.Id)),
            "Same graph and source must give deterministic ordering.");

        using CancellationTokenSource source = new();
        source.Cancel();
        bool cancelled = false;
        try
        {
            ImpactAnalyzer.Analyze(graph, c, cancellationToken: source.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert(cancelled, "Cancelled traversal must stop without side effects.");
    }

    /// <summary>Checks that plain backlinks and unknown relation types never become firm dependencies.</summary>
    private static void VerifyBacklinksAndUnknownTypes()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        LinkIndexCatalog catalog = Catalog(
            [Target(a, "A"), Target(b, "B"), Target(c, "C")],
            [Relation(a, b, "mon type inconnu"), Relation(c, a, "dépend de")]);
        catalog.References.Add(new LinkReferenceEntry
        {
            SourceId = c, TargetId = b, RawTarget = "B",
            LineNumber = 4, Excerpt = "[[B]]"
        });
        ImpactGraphIndex graph = ImpactGraphIndex.Build(catalog);
        ImpactAnalysisResult result = ImpactAnalyzer.Analyze(graph, b);
        Assert(result.Findings.Count == 2 &&
            result.Findings.All(item => item.Evidence == ImpactEvidence.Suggested) &&
            !result.Findings.Any(item => item.Depth > 1),
            "Textual links and custom relations cannot propagate through a dependency chain.");
        Assert(ImpactAnalyzer.Analyze(graph, c).Findings.Count == 0,
            "Reverse backlink must not produce a speculative impact.");
    }

    /// <summary>Verifies stable scope boundaries and truncation options.</summary>
    private static void VerifyScopesAndLimits()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid c = Guid.NewGuid();
        LinkIndexCatalog catalog = Catalog(
            [Target(a, "A", "project:one"), Target(b, "B", "project:two"),
                Target(c, "C", "project:two")],
            [Relation(a, b, "dépend de"), Relation(b, c, "dépend de")]);
        ImpactGraphIndex graph = ImpactGraphIndex.Build(catalog);
        Assert(ImpactAnalyzer.Analyze(graph, c).Findings.Count == 1,
            "Default analysis must not cross an application/project boundary.");
        Assert(ImpactAnalyzer.Analyze(graph, c,
            new ImpactAnalysisOptions { IncludeCrossScope = true }).Findings.Count == 2,
            "Cross-workspace option must allow intentional scope transitions.");
        ImpactAnalysisResult shallow = ImpactAnalyzer.Analyze(graph, c,
            new ImpactAnalysisOptions { IncludeCrossScope = true, MaxDepth = 1 });
        Assert(shallow.Findings.Count == 1 && shallow.IsTruncated,
            "Depth limit must report incomplete results.");
        ImpactAnalysisResult bounded = ImpactAnalyzer.Analyze(graph, c,
            new ImpactAnalysisOptions { IncludeCrossScope = true, MaxVisitedNodes = 1 });
        Assert(bounded.IsTruncated,
            "Expansion budget exhaustion must be visible.");
    }

    /// <summary>Checks broken targets, identical reconstruction, and target rename identity stability.</summary>
    private static void VerifyBrokenTargetsAndReconstruction()
    {
        Guid a = Guid.NewGuid();
        Guid b = Guid.NewGuid();
        Guid removed = Guid.NewGuid();
        LinkIndexCatalog catalog = Catalog(
            [Target(a, "A"), Target(b, "B")],
            [Relation(a, b, "dépend de"), Relation(a, removed, "teste")]);
        ImpactGraphIndex first = ImpactGraphIndex.Build(catalog);
        ImpactGraphIndex rebuilt = ImpactGraphIndex.Build(catalog);
        Assert(first.Diagnostics.Count == 1 &&
            first.Edges.Select(edge => edge.Id).SequenceEqual(rebuilt.Edges.Select(edge => edge.Id)),
            "Broken target must be diagnosed; rebuilding must preserve derived edge identities.");
        LinkIndexCatalog renamed = Catalog(
            [Target(a, "A"), Target(b, "B renamed")],
            [Relation(a, b, "dépend de")]);
        Assert(ImpactAnalyzer.Analyze(ImpactGraphIndex.Build(renamed), b)
            .Findings.Single().Target.Id == a,
            "Renaming a target must not change the GUID-based relation.");
        Assert(catalog.Relations.Count == 2 &&
            catalog.Targets.Single(target => target.Id == b).DisplayName == "B",
            "Building and querying must not mutate the source catalog.");
    }

    /// <summary>Creates a small in-memory link catalog for smoke fixtures.</summary>
    /// <param name="targets">Stable objects.</param>
    /// <param name="relations">Typed Markdown relations.</param>
    /// <returns>A mutable fixture catalog.</returns>
    private static LinkIndexCatalog Catalog(
        IEnumerable<LinkTargetEntry> targets,
        IEnumerable<TypedRelationEntry> relations)
    {
        return new LinkIndexCatalog
        {
            Targets = targets.ToList(),
            Relations = relations.ToList()
        };
    }

    /// <summary>Creates a document with stable identity and scope.</summary>
    /// <param name="id">Stable identifier.</param>
    /// <param name="name">Human-readable document name.</param>
    /// <param name="scope">Stable scope identity.</param>
    /// <returns>Indexed document target.</returns>
    private static LinkTargetEntry Target(
        Guid id, string name, string scope = "project:shared")
    {
        return new LinkTargetEntry
        {
            Id = id, Kind = LinkTargetKind.Document,
            DisplayName = name, QualifiedName = name,
            RelativePath = name + ".md", ScopeIdentity = scope
        };
    }

    /// <summary>Creates a typed relation resembling the existing JSON/Markdown index.</summary>
    /// <param name="source">Declaring document.</param>
    /// <param name="target">Stable target ID.</param>
    /// <param name="type">Relation type.</param>
    /// <returns>Indexed relation fixture.</returns>
    private static TypedRelationEntry Relation(Guid source, Guid target, string type)
    {
        return new TypedRelationEntry
        {
            SourceId = source, TargetId = target, RawTargetId = target.ToString("D"),
            RelationType = type, LineNumber = 1, Excerpt = "Relation de test"
        };
    }

    /// <summary>Fails with the scenario explanation when a regression is detected.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
