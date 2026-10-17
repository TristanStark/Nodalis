using System.Diagnostics;
using Nodalis.Core.Impact;
using Nodalis.Core.Links;
using Nodalis.Core.Relations;

/// <summary>
/// Qualification fixtures for small, medium and large derived graphs.
/// </summary>
internal static class ImpactReleaseQualificationSmokeTests
{
    /// <summary>
    /// Verifies rebuild stability, bounds, cancellation and prints non-gating performance samples.
    /// </summary>
    public static void Run()
    {
        VerifyFixture("Small", 16);
        VerifyFixture("Medium", 300);
        VerifyFixture("Large", 1800);
    }

    /// <summary>
    /// Reconstructs the same fixture twice and compares all graph edges and impact results.
    /// </summary>
    /// <param name="name">Fixture label in CI logs.</param>
    /// <param name="documentCount">Number of generated stable-ID documents.</param>
    private static void VerifyFixture(string name, int documentCount)
    {
        List<LinkTargetEntry> targets = [];
        List<TypedRelationEntry> relations = [];
        List<LinkReferenceEntry> references = [];

        for (int index = 0; index < documentCount; index++)
        {
            Guid id = Identifier(index);
            targets.Add(new LinkTargetEntry
            {
                Id = id,
                Kind = LinkTargetKind.Document,
                DisplayName = $"Document {index:D5}",
                QualifiedName = $"Fixture / {name} / Document {index:D5}",
                RelativePath = $"Technique/Document-{index:D5}.md",
                ScopeIdentity = "project:impact-release",
                Aliases = [$"Document ancien {index:D5}"]
            });

            if (index > 0)
            {
                Guid dependency = Identifier(index - 1);
                relations.Add(new TypedRelationEntry
                {
                    SourceId = id,
                    TargetId = dependency,
                    RawTargetId = dependency.ToString("D"),
                    RelationType = index % 3 == 0 ? "teste" : "dépend de",
                    LineNumber = 3,
                    Excerpt = "Dépendance explicite dans le Markdown"
                });
            }
        }

        references.Add(new LinkReferenceEntry
        {
            SourceId = Identifier(documentCount - 1),
            TargetId = Identifier(0),
            RawTarget = "Document 00000",
            LineNumber = 5,
            Excerpt = "[[Document 00000]]"
        });

        relations.Add(new TypedRelationEntry
        {
            SourceId = Identifier(0),
            TargetId = Guid.NewGuid(),
            RawTargetId = "référence-supprimée",
            RelationType = "teste",
            LineNumber = 7,
            Excerpt = "Relation vers une cible disparue"
        });

        LinkIndexCatalog catalog = new()
        {
            Targets = targets,
            Relations = relations,
            References = references
        };
        ImpactAnalysisOptions options = new()
        {
            MaxDepth = 8,
            MaxResults = 500,
            MaxVisitedNodes = 1200,
            IncludeSuggested = true
        };

        ImpactGraphIndex first = ImpactGraphIndex.Build(catalog);
        ImpactGraphIndex rebuilt = ImpactGraphIndex.Build(catalog);
        ImpactAnalysisResult firstResult = ImpactAnalyzer.Analyze(first, Identifier(0), options);
        ImpactAnalysisResult rebuiltResult = ImpactAnalyzer.Analyze(rebuilt, Identifier(0), options);

        Assert(first.Targets.Count == documentCount &&
            first.Edges.Count == documentCount &&
            first.Diagnostics.Count == 1,
            $"{name}: unexpected graph shape or missing broken-target warning.");
        Assert(first.Edges.Select(edge => edge.Id)
                .SequenceEqual(rebuilt.Edges.Select(edge => edge.Id)) &&
            firstResult.Findings.Select(item => (item.Target.Id, item.Evidence, item.Depth))
                .SequenceEqual(rebuiltResult.Findings.Select(item => (item.Target.Id, item.Evidence, item.Depth))),
            $"{name}: graph reconstruction differs from the first pass.");
        Assert(firstResult.Findings.Count >= Math.Min(8, documentCount - 1) &&
            firstResult.IsTruncated,
            $"{name}: deep chain must stop at the configured depth and declare truncation.");
        Assert(firstResult.Findings.All(item =>
                item.Target.Id != Identifier(0) &&
                item.Path.Count <= options.MaxDepth),
            $"{name}: loop, self-impact or unbounded path detected.");

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        bool stopped = false;
        try
        {
            ImpactGraphIndex.Build(catalog, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        Assert(stopped, $"{name}: graph reconstruction did not stop on cancellation.");

        // Measurements are recorded but not hard-gated: hosted CI runners are not stable benchmarks.
        double[] samples = new double[5];
        for (int iteration = 0; iteration < samples.Length; iteration++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            ImpactGraphIndex graph = ImpactGraphIndex.Build(catalog);
            ImpactAnalyzer.Analyze(graph, Identifier(0), options);
            watch.Stop();
            samples[iteration] = watch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        Console.WriteLine(
            $"[Impact v1.3] {name}: documents={documentCount}, edges={first.Edges.Count}, " +
            $"diagnostics={first.Diagnostics.Count}, p50={samples[2]:F2} ms, p95={samples[4]:F2} ms");
    }

    /// <summary>
    /// Creates a reproducible GUID for a fixture's document slot.
    /// </summary>
    /// <param name="index">Zero-based slot.</param>
    /// <returns>Stable fixture identifier.</returns>
    private static Guid Identifier(int index)
    {
        return Guid.Parse($"{index + 1:x8}-0000-0000-0000-000000000000");
    }

    /// <summary>Fails with a useful test explanation when a regression is detected.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Diagnostic message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
