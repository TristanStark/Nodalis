using Nodalis.Core.Composites;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Composites;

/// <summary>Verifies 1.6 transclusion safety and source integrity without dependencies.</summary>
internal static class CompositeDocumentsSmokeTests
{
    /// <summary>Exercises unique headings, ambiguous aliases, fences, cycles and cancellation.</summary>
    /// <returns>Completion of the composite document smoke suite.</returns>
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "Nodalis-Composite-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Guid parentId = Guid.NewGuid();
            Guid childId = Guid.NewGuid();
            Guid anotherId = Guid.NewGuid();
            string parent = Path.Combine(root, "Parent.md");
            string child = Path.Combine(root, "Child.md");
            string secondary = Path.Combine(root, "Other.md");
            string childText = "# Cahier des charges\nIntroduction\n## Tests\nVérifier API\n" +
                "### Sous-tests\nCas limite\n## Dev\nCoder API\n~~~md\n## Fausse section\n~~~\n";
            await File.WriteAllTextAsync(parent, "# Parent\n");
            await File.WriteAllTextAsync(child, childText);
            await File.WriteAllTextAsync(secondary, "# Tiers\n");
            LinkIndexCatalog catalog = new()
            {
                Targets =
                [
                    Document(parentId, "Parent.md", "Parent"),
                    Document(childId, "Child.md", "Child") with { Aliases = ["Ambigu"] },
                    Document(anotherId, "Other.md", "Other") with { Aliases = ["Ambigu"] }
                ]
            };
            CompositeExcerpt section = CompositeMarkdownSections.Extract(childText, "Tests");
            Assert(section.Diagnostic is null && section.Line == 3 &&
                section.Markdown.Contains("Cas limite") &&
                !section.Markdown.Contains("Coder API"),
                "Section stops at the next sibling and preserves nested headings.");
            Assert(CompositeMarkdownSections.Headings(childText).Count == 4,
                "Headings inside fenced examples must not be extracted.");
            Assert(!CompositeMarkdownSyntax.IsSafeTarget("../Child.md") &&
                !CompositeMarkdownSyntax.IsSafeTarget("https://host/file") &&
                CompositeMarkdownSyntax.IsSafeTarget(childId.ToString("D")),
                "Only indexed local names and GUIDs can be resolved.");

            CompositeWorkspaceRenderer renderer = new(root, catalog);
            string directive = CompositeMarkdownSyntax.Format(childId, "Tests");
            CompositeRenderResult resolved = await renderer.RenderAsync(parent,
                "# Parent\n" + directive + "\n");
            Assert(resolved.Diagnostics.Count == 0 &&
                resolved.Edges.Count == 1 && resolved.Edges[0].TargetId == childId &&
                resolved.Markdown.Contains("Vérifier API") &&
                !resolved.Markdown.Contains("Coder API"),
                "GUID-backed transclusion must include only a requested source section.");
            Assert(await File.ReadAllTextAsync(parent) == "# Parent\n",
                "Render must never persist transcluded content into parent Markdown.");
            CompositeRenderResult alias = await renderer.RenderAsync(parent, "![[Ambigu]]");
            Assert(alias.Diagnostics.Count == 1 &&
                alias.Markdown.Contains("ambiguë"),
                "Ambiguous aliases must never silently choose a document.");
            CompositeRenderResult fenced = await renderer.RenderAsync(parent,
                "~~~md\n![[Child]]\n~~~\n![[../Child.md]]");
            Assert(fenced.Edges.Count == 0 && fenced.Diagnostics.Count == 1,
                "Code fences do not execute include directives; traversal remains blocked.");

            await File.WriteAllTextAsync(child,
                "# Child\n" + CompositeMarkdownSyntax.Format(parentId));
            CompositeRenderResult cycle = await renderer.RenderAsync(parent,
                CompositeMarkdownSyntax.Format(childId));
            Assert(cycle.Diagnostics.Any(x => x.Message.Contains("Cycle")) &&
                cycle.Markdown.Contains("Inclusion indisponible"),
                "A recursive inclusion must terminate and visibly explain the cycle.");
            await File.WriteAllTextAsync(child, childText);
            string hundred = string.Join("\n", Enumerable.Repeat(
                CompositeMarkdownSyntax.Format(childId), 120));
            CompositeRenderResult volume = await renderer.RenderAsync(parent, hundred);
            Assert(volume.Diagnostics.Count == 0 &&
                volume.Edges.Count == 120 &&
                volume.Markdown.Split("Contenu inclus", StringSplitOptions.None).Length > 120,
                "More than 100 independent inclusions must render without recursive loops.");
            CompositeRenderResult capped = await renderer.RenderAsync(parent,
                string.Join("\n", Enumerable.Repeat(
                    CompositeMarkdownSyntax.Format(childId), 300)));
            Assert(capped.Diagnostics.Count > 0 &&
                capped.Markdown.Contains("Limite de profondeur ou nombre de fragments"),
                "Oversized composites must give visible bounded-work diagnostics.");

            CompositeInclusionGraph graph = new();
            graph.Replace([
                new CompositeEdge(parentId, childId),
                new CompositeEdge(anotherId, parentId)
            ]);
            IReadOnlyList<Guid> impacted = graph.AffectedParents(childId);
            Assert(impacted.Count == 2 && impacted.Contains(parentId) &&
                impacted.Contains(anotherId),
                "Changed nested source must invalidate each transitive parent.");
            graph.Clear();
            Assert(graph.AffectedParents(childId).Count == 0,
                "Deleting a derived cache must be safe and reconstructible.");

            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            bool stopped = false;
            try
            {
                await renderer.RenderAsync(parent, directive, cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }

            Assert(stopped, "Cancelled render must exit without changing sources.");
            Console.WriteLine("[Composites 1.6] GUIDs, sections, aliases, fences, cycles, cancellation passed.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Builds a registry entry with stable GUID and original path.</summary>
    /// <param name="id">Canonical GUID.</param>
    /// <param name="relative">Workspace-relative Markdown path.</param>
    /// <param name="name">Human-readable name.</param>
    /// <returns>Indexed document target.</returns>
    private static LinkTargetEntry Document(Guid id, string relative, string name) =>
        new()
        {
            Id = id, Kind = LinkTargetKind.Document,
            RelativePath = relative, DisplayName = name, QualifiedName = name
        };

    /// <summary>Fails immediately on any violated release invariant.</summary>
    /// <param name="condition">Assertion result.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
