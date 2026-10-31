using Nodalis.Core.Links;
using System.Text.Json.Nodes;
using Nodalis.Core.Markdown;
using Nodalis.Core.Relations;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Reliability;
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
                MarkdownBlockIdentityCodec.FormatMarker(otherId) + "\n" +
                MarkdownRelationParser.Format("vérifie", id, "Criterion") + "\nReady\n");

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

            Assert(cold.Catalog.Relations.Single().SourceBlockId == otherId,
                "Relation source ownership must be projected from the unique marked heading.");

            BlockIncomingRelationResult inbound =
                BlockRelationIndexQuery.GetIncoming(cold.Catalog, id);
            Assert(inbound.Resolution.Status == LinkResolutionStatus.Resolved &&
                inbound.Relations.Count == 1 &&
                inbound.Relations[0].Source.RelativePath == "Tests.md" &&
                inbound.Relations[0].RelationType == "vérifie" &&
                inbound.Relations[0].LineNumber == 4,
                "Typed relations to blocks must be resolved from the existing index.");

            IReadOnlyList<RelationTargetOption> choices =
                RelationTargetIndexQuery.GetSelectable(cold.Catalog,
                    cold.Catalog.Targets.Single(target =>
                        target.RelativePath == "Tests.md").Id);
            Assert(choices.Any(choice => choice.IsBlock && choice.Id == id) &&
                   choices.Any(choice => choice.IsBlock && choice.Id == otherId) &&
                   !choices.Any(choice => !choice.IsBlock &&
                       choice.DisplayName == "Tests"),
                "Document and uniquely identified block targets must be searchable together.");

            BlockOutgoingRelationResult outbound =
                BlockRelationIndexQuery.GetOutgoing(cold.Catalog, otherId);
            Assert(outbound.Source.Status == LinkResolutionStatus.Resolved &&
                outbound.Relations.Count == 1 &&
                outbound.Relations[0].TargetStatus == LinkResolutionStatus.Resolved &&
                outbound.Relations[0].TargetBlock?.Id == id,
                "Typed relations must support outbound block-to-block navigation.");

            LinkIndexCatalog crossKindCollision = cold.Catalog with
            {
                Targets = cold.Catalog.Targets.Append(new LinkTargetEntry
                {
                    Id = id,
                    Kind = LinkTargetKind.Document,
                    RelativePath = "Fake.md",
                    DisplayName = "Fake",
                    QualifiedName = "Fake"
                }).ToList()
            };
            Assert(!RelationTargetIndexQuery.GetSelectable(crossKindCollision)
                    .Any(choice => choice.Id == id),
                "Cross-kind GUID collisions must be hidden from selection.");

            Assert(BlockRelationIndexQuery.GetOutgoing(crossKindCollision, otherId)
                    .Relations.Single().TargetStatus == LinkResolutionStatus.Ambiguous,
                "Cross-kind collisions must remain ambiguous in outgoing relations.");

            Assert(BlockIndexQuery.Resolve(crossKindCollision, id).Status ==
                LinkResolutionStatus.Ambiguous &&
                BlockRelationIndexQuery.GetIncoming(crossKindCollision, id)
                    .Relations.Count == 0,
                "A GUID duplicated between a block and document must be ambiguous.");

            // Simulate the exact v1.9 schema v3 payload: document target IDs
            // must survive while block projections are reconstructed.
            string indexPath = Path.Combine(workspaceRoot, WorkspaceLayout.LinkIndexFileName);
            JsonObject v3 = JsonNode.Parse(await File.ReadAllTextAsync(indexPath))!.AsObject();
            v3["schemaVersion"] = 3;
            v3.Remove("blocks");
            v3.Remove("blockIssues");
            await File.WriteAllTextAsync(indexPath, v3.ToJsonString());
            LinkIndexCatalog migrated = await service.LoadAsync();
            Assert(migrated.SchemaVersion == LinkIndexCatalog.CurrentSchemaVersion &&
                migrated.Blocks.Count == 2 &&
                migrated.Targets.Single(target =>
                    target.RelativePath == "Criteria.md").Id == resolved.Document?.Id,
                "Schema v3 must reconstruct block indexes while retaining document IDs.");

            // A persisted v4 projection lacks sourceBlockId and must be
            // rebuilt once without losing document identities.
            JsonObject v4 = JsonNode.Parse(await File.ReadAllTextAsync(indexPath))!.AsObject();
            v4["schemaVersion"] = 4;
            if (v4["relations"] is JsonArray oldRelations)
            {
                foreach (JsonNode? relation in oldRelations)
                {
                    if (relation is JsonObject relationObject)
                    {
                        relationObject.Remove("sourceBlockId");
                    }
                }
            }

            await File.WriteAllTextAsync(indexPath, v4.ToJsonString());
            LinkIndexCatalog upgraded = await service.LoadAsync();
            Assert(upgraded.SchemaVersion == LinkIndexCatalog.CurrentSchemaVersion &&
                upgraded.Relations.Single().SourceBlockId == otherId &&
                upgraded.Targets.Single(target =>
                    target.RelativePath == "Criteria.md").Id == resolved.Document?.Id,
                "v4-to-v5 migration must rebuild relation sources but preserve document IDs.");

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

            // A two-step approval must never silently change a document.
            string assignmentPath = Path.Combine(workspaceRoot, "Assignment.md");
            await File.WriteAllTextAsync(assignmentPath, "# Assignment\n## New block\n");
            WorkspaceBlockIdentityService assignments = new(workspaceRoot);
            Guid approvedId = Guid.NewGuid();
            BlockIdentityAssignmentPreview plan =
                await assignments.PrepareAsync(assignmentPath, 2, approvedId);
            Assert(await File.ReadAllTextAsync(assignmentPath) ==
                "# Assignment\n## New block\n" &&
                plan.UpdatedMarkdown.Contains(
                    MarkdownBlockIdentityCodec.FormatMarker(approvedId),
                    StringComparison.Ordinal),
                "An approval preview must not mutate the source.");

            bool duplicateRejected = false;
            try
            {
                await assignments.PrepareAsync(assignmentPath, 2, id);
            }
            catch (InvalidOperationException)
            {
                duplicateRejected = true;
            }

            Assert(duplicateRejected, "Previously assigned block IDs must be refused.");

            bool documentIdRejected = false;
            try
            {
                Guid documentId = (await service.LoadAsync()).Targets.Single(target =>
                    target.RelativePath == "Tests.md").Id;
                await assignments.PrepareAsync(assignmentPath, 2, documentId);
            }
            catch (InvalidOperationException)
            {
                documentIdRejected = true;
            }

            Assert(documentIdRejected,
                "Block IDs cannot reuse an existing document target GUID.");
            await File.AppendAllTextAsync(assignmentPath, "External edit\n");
            bool staleRejected = false;
            try
            {
                await assignments.CommitApprovedAsync(plan);
            }
            catch (ExternalModificationException)
            {
                staleRejected = true;
            }

            Assert(staleRejected &&
                !(await File.ReadAllTextAsync(assignmentPath)).Contains(
                    MarkdownBlockIdentityCodec.FormatMarker(approvedId),
                    StringComparison.Ordinal),
                "An external edit must make a previous approval stale.");

            plan = await assignments.PrepareAsync(assignmentPath, 2, approvedId);
            Assert(await assignments.CommitApprovedAsync(plan) &&
                BlockIndexQuery.Resolve(await service.LoadAsync(), approvedId).Document?
                    .RelativePath == "Assignment.md",
                "Explicit approval must atomically save and reindex the new marker.");
            BlockIdentityAssignmentPreview repeat =
                await assignments.PrepareAsync(assignmentPath, 2, approvedId);
            Assert(!await assignments.CommitApprovedAsync(repeat),
                "A repeated approved assignment must be an idempotent no-op.");

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
