using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;

/// <summary>
/// Index integration regression tests for persisted Markdown block identity projections.
/// </summary>
internal static class BlockIndexSmokeTests
{
    /// <summary>
    /// Verifies a cold build, warm reuse, targeted change, file move and collision.
    /// </summary>
    /// <param name="root">Isolated parent test workspace.</param>
    /// <returns>Asynchronous regression task.</returns>
    public static async Task RunAsync(string root)
    {
        string workspaceRoot = Path.Combine(root, "Block-index-workspace");
        await new FileSystemWorkspaceStore(workspaceRoot)
            .InitializeAsync("Block Index Fixture");

        try
        {
            Guid id = Guid.NewGuid();
            Guid otherId = Guid.NewGuid();
            string first = Path.Combine(workspaceRoot, "Criteria.md");
            string second = Path.Combine(workspaceRoot, "Tests.md");
            string markdown = "# Specification\n## Criterion\n" +
                MarkdownBlockIdentityCodec.FormatMarker(id) + "\nValidated\n";
            await File.WriteAllTextAsync(first, markdown);
            await File.WriteAllTextAsync(second, "# Tests\n## Case\n" +
                MarkdownBlockIdentityCodec.FormatMarker(otherId) + "\nReady\n");

            WorkspaceLinkIndexService service = new WorkspaceLinkIndexService(workspaceRoot);
            LinkIndexRefreshResult cold = await service.RefreshWithMetricsAsync();

            Assert(cold.Catalog.SchemaVersion == LinkIndexCatalog.CurrentSchemaVersion &&
                cold.Catalog.Blocks.Count == 2 &&
                cold.Catalog.BlockIssues.Count == 0,
                "Cold index must project only the explicit identities.");
            MarkdownBlockIndexResolution resolved = BlockIndexQuery.Resolve(cold.Catalog, id);
            Assert(resolved.Status == LinkResolutionStatus.Resolved &&
                resolved.Document?.RelativePath == "Criteria.md" &&
                resolved.Block?.HeadingLine == 2,
                "Block resolution must return its stable owning document.");

            LinkIndexRefreshResult warm = await service.RefreshWithMetricsAsync();
            Assert(warm.Metrics.ParsedDocuments == 0 &&
                !warm.Metrics.IndexFileWritten &&
                warm.Catalog.Blocks.Count == 2,
                "Warm refresh must reuse block projections without parsing.");

            string moved = Path.Combine(workspaceRoot, "Renamed Criteria.md");
            File.Move(first, moved);
            LinkIndexRefreshResult relocated = await service.RefreshWithMetricsAsync();
            MarkdownBlockIndexResolution afterMove = BlockIndexQuery.Resolve(relocated.Catalog, id);
            Assert(afterMove.Status == LinkResolutionStatus.Resolved &&
                afterMove.Document?.RelativePath == "Renamed Criteria.md" &&
                afterMove.Document.Id == resolved.Document?.Id,
                "Moving a document must preserve its block identity via document GUID.");

            await File.WriteAllTextAsync(moved, markdown.Replace(
                "## Criterion", "## Updated criterion", StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(moved, DateTime.UtcNow.AddSeconds(2));
            LinkIndexRefreshResult changed = await service.RefreshDocumentAsync(moved);
            MarkdownBlockIndexResolution afterEdit = BlockIndexQuery.Resolve(changed.Catalog, id);
            Assert(changed.Metrics.ParsedDocuments == 1 &&
                afterEdit.Status == LinkResolutionStatus.Resolved &&
                afterEdit.Block?.HeadingTitle == "Updated criterion",
                "A changed document must rebuild only its own block entries.");

            await File.WriteAllTextAsync(second, "# Tests\n## Case\n" +
                MarkdownBlockIdentityCodec.FormatMarker(id) + "\nReady\n");
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(3));
            LinkIndexRefreshResult conflict = await service.RefreshDocumentAsync(second);
            Assert(BlockIndexQuery.Resolve(conflict.Catalog, id).Status ==
                LinkResolutionStatus.Ambiguous &&
                BlockIndexQuery.FindAmbiguousIds(conflict.Catalog).SequenceEqual([id]),
                "Cross-document duplicates must never resolve to the first match.");

            await File.WriteAllTextAsync(second, "# Tests\n## Invalid\n" +
                "<!-- nodalis:block:invalid -->\n");
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(4));
            LinkIndexRefreshResult malformed = await service.RefreshDocumentAsync(second);
            Assert(malformed.Catalog.BlockIssues.Count == 1 &&
                malformed.Catalog.BlockIssues[0].Kind ==
                    MarkdownBlockIdentityIssueKind.MalformedMarker &&
                BlockIndexQuery.Resolve(malformed.Catalog, id).Status ==
                    LinkResolutionStatus.Resolved &&
                BlockIndexQuery.Resolve(malformed.Catalog, otherId).Status ==
                    LinkResolutionStatus.Missing,
                "Malformed source markers must be diagnosed and deleted IDs removed.");

            File.Delete(moved);
            LinkIndexRefreshResult deleted = await service.RefreshWithMetricsAsync();
            Assert(BlockIndexQuery.Resolve(deleted.Catalog, id).Status ==
                LinkResolutionStatus.Missing,
                "Deleted source documents must not leave ghost blocks.");
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    /// <summary>Fails when a block-index invariant is violated.</summary>
    /// <param name="condition">Required invariant.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
