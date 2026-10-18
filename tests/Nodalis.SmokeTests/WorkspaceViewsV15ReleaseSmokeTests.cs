using System.Diagnostics;
using Nodalis.Core.Views;
using Nodalis.Infrastructure.Views;

/// <summary>Protects the release gate for cross-project views, recovery and repeated cancellation.</summary>
internal static class WorkspaceViewsV15ReleaseSmokeTests
{
    /// <summary>Exercises compatibility and source safety without depending on a WPF desktop.</summary>
    /// <returns>Completion of the v1.5 view qualification fixture.</returns>
    public static async Task RunAsync()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "Nodalis-Views-Release-" + Guid.NewGuid().ToString("N"));
        string privateRoot = Path.Combine(Path.GetTempPath(),
            "Nodalis-Views-Release-Private-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string manifest = Path.Combine(root, ".workspace.json");
            string source = Path.Combine(root, "Existing.md");
            await File.WriteAllTextAsync(manifest, "{\"schemaVersion\":1}");
            await File.WriteAllTextAsync(source, "# Ancienne note\n\n- [ ] À faire");
            FileSystemWorkspaceViewStore store = new(root, Guid.NewGuid(), privateRoot);
            WorkspaceViewDefinition definition = new()
            {
                Id = Guid.NewGuid(),
                Name = "Multi-projets",
                Pinned = true,
                Query = new WorkspaceViewQuery
                {
                    Kinds = [WorkspaceViewItemKind.Task],
                    SortBy = "date",
                    Limit = 100,
                    MaxScannedItems = 25000
                }
            };
            WorkspaceViewDefinitionSnapshot empty = await store.LoadAsync(
                WorkspaceViewVisibility.Workspace);
            await store.SaveAsync(WorkspaceViewVisibility.Workspace, [definition],
                empty.Revision);
            WorkspaceViewDefinitionSnapshot reopened = await store.LoadAsync(
                WorkspaceViewVisibility.Workspace);
            Assert(reopened.Views.Single().Id == definition.Id &&
                reopened.Views.Single().Pinned,
                "An old workspace must reopen pinned views with the same GUID.");
            Assert(await File.ReadAllTextAsync(manifest) == "{\"schemaVersion\":1}" &&
                await File.ReadAllTextAsync(source) == "# Ancienne note\n\n- [ ] À faire",
                "Views must not migrate the workspace schema or change canonical Markdown.");

            // A workspace backup restores the JSON bytes, not projected result copies.
            string portable = store.FilePath(WorkspaceViewVisibility.Workspace);
            byte[] backup = await File.ReadAllBytesAsync(portable);
            await File.WriteAllTextAsync(portable, "{ damaged");
            bool corruptionDetected = false;
            try
            {
                await store.LoadAsync(WorkspaceViewVisibility.Workspace);
            }
            catch (InvalidDataException)
            {
                corruptionDetected = true;
            }

            Assert(corruptionDetected, "Corrupted definitions must be isolated, never reset.");
            await File.WriteAllBytesAsync(portable, backup);
            WorkspaceViewDefinitionSnapshot restored = await store.LoadAsync(
                WorkspaceViewVisibility.Workspace);
            Assert(restored.Views.Single().Id == definition.Id,
                "Restoring a backed-up views definition must retain saved identities.");

            const int rowCount = 12000;
            WorkspaceViewItem[] items = Enumerable.Range(0, rowCount)
                .Select(index => Item(index, completed: false)).ToArray();
            WorkspaceViewQuery query = new()
            {
                Kinds = [WorkspaceViewItemKind.Task],
                SortBy = "date",
                Limit = 100,
                MaxScannedItems = 25000,
                Filter = new WorkspaceViewFilter
                {
                    Predicates = [new WorkspaceViewPredicate
                    {
                        Field = "completed",
                        Operator = WorkspaceViewOperator.Equals,
                        Boolean = false
                    }]
                }
            };
            Stopwatch watch = Stopwatch.StartNew();
            WorkspaceViewQueryResult initial = WorkspaceViewQueryEngine.Execute(items, query);
            watch.Stop();
            Assert(initial.TotalMatches == rowCount && initial.Items.Count == 100 &&
                !initial.IsTruncated,
                "Large cross-project queries must report accurate counts and bounded pages.");
            WorkspaceViewQueryResult secondPage = WorkspaceViewQueryEngine.Execute(
                items, query with { Offset = 100 });
            Assert(secondPage.Items.Count == 100 &&
                initial.Items[^1].Key != secondPage.Items[0].Key,
                "Pagination must be stable and contain no duplicate source identity.");

            WorkspaceViewItem[] edited =
                items.Select(item => item.Key == "task:000000"
                    ? item with
                    {
                        Fields = new Dictionary<string, WorkspaceViewField>
                        {
                            ["completed"] = WorkspaceViewField.OfBoolean(true)
                        }
                    }
                    : item).ToArray();
            WorkspaceViewQueryResult afterEdit = WorkspaceViewQueryEngine.Execute(
                edited, query);
            Assert(afterEdit.TotalMatches == rowCount - 1,
                "Completing a task must remove it from open-task views without restarting.");
            WorkspaceViewQueryResult afterDelete = WorkspaceViewQueryEngine.Execute(
                edited.Where(item => item.Key != "task:000001"), query);
            Assert(afterDelete.TotalMatches == rowCount - 2,
                "A deleted source must not remain visible in a rebuilt projection.");
            WorkspaceViewQueryResult missing = WorkspaceViewQueryEngine.Execute(items,
                new WorkspaceViewQuery
                {
                    Filter = new WorkspaceViewFilter
                    {
                        Predicates = [new WorkspaceViewPredicate
                        {
                            Field = "status",
                            Operator = WorkspaceViewOperator.IsEmpty
                        }]
                    }
                });
            Assert(missing.TotalMatches == rowCount,
                "Null or missing fields must remain identifiable without fake values.");

            for (int attempt = 0; attempt < 50; attempt++)
            {
                using CancellationTokenSource request = new();
                request.Cancel();
                try
                {
                    WorkspaceViewQueryEngine.Execute(items, query, request.Token);
                    throw new InvalidOperationException("Cancelled query unexpectedly completed.");
                }
                catch (OperationCanceledException)
                {
                    // Expected; repeated cancellations must be harmless.
                }
            }

            Assert(await File.ReadAllTextAsync(source) == "# Ancienne note\n\n- [ ] À faire",
                "Query and cancellation must never write to the original source.");
            Console.WriteLine("[Views 1.5 release] portability, backup restore, 12k items, " +
                "pagination, edits, deletion and 50 cancellations passed (" +
                watch.ElapsedMilliseconds + " ms initial query).");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }

            if (Directory.Exists(privateRoot))
            {
                Directory.Delete(privateRoot, recursive: true);
            }
        }
    }

    /// <summary>Creates a typed fixture shared by numerous project identities.</summary>
    /// <param name="index">Stable fixture index.</param>
    /// <param name="completed">Whether the item is completed.</param>
    /// <returns>One canonical-source-shaped item.</returns>
    private static WorkspaceViewItem Item(int index, bool completed)
    {
        return new WorkspaceViewItem
        {
            Key = "task:" + index.ToString("D6"),
            Kind = WorkspaceViewItemKind.Task,
            Title = "Tâche " + index,
            ScopeIdentity = "project:" + (index % 200).ToString("D3"),
            Source = new WorkspaceViewSource(Guid.NewGuid(),
                "Project-" + (index % 200) + "/Tasks.md", index + 1, null),
            Fields = new Dictionary<string, WorkspaceViewField>
            {
                ["completed"] = WorkspaceViewField.OfBoolean(completed),
                ["date"] = WorkspaceViewField.OfDate(
                    new DateOnly(2026, 10, 18).AddDays(index % 365))
            }
        };
    }

    /// <summary>Fails immediately with an actionable release regression message.</summary>
    /// <param name="condition">Fixture invariant.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
