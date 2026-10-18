using System.Text;
using Nodalis.Core.Views;
using Nodalis.Infrastructure.Views;

/// <summary>Verifies v1.5 view definitions without touching source Markdown or results.</summary>
internal static class WorkspaceViewStoreSmokeTests
{
    /// <summary>Exercises portable/private storage, recovery, conflicts and schema compatibility.</summary>
    /// <param name="root">Existing test workspace root.</param>
    /// <returns>Completion of persistence tests.</returns>
    public static async Task RunAsync(string root)
    {
        Guid workspaceId = Guid.NewGuid();
        string privateDirectory = Path.Combine(Path.GetTempPath(),
            "Nodalis-Views-Private", Guid.NewGuid().ToString("N"));
        FileSystemWorkspaceViewStore store = new(root, workspaceId, privateDirectory);
        WorkspaceViewDefinitionSnapshot missing = await store.LoadAsync(
            WorkspaceViewVisibility.Workspace);
        Assert(missing.Views.Count == 0 && missing.Revision == string.Empty &&
            !File.Exists(store.FilePath(WorkspaceViewVisibility.Workspace)),
            "Loading an existing workspace must not create a views JSON file.");

        WorkspaceViewDefinition definition = new()
        {
            Id = Guid.NewGuid(),
            Name = "À traiter cette semaine",
            Query = new WorkspaceViewQuery
            {
                Kinds = [WorkspaceViewItemKind.Task],
                SortBy = "date",
                GroupBy = "project",
                Filter = new WorkspaceViewFilter
                {
                    Predicates = [new WorkspaceViewPredicate
                    {
                        Field = "completed",
                        Operator = WorkspaceViewOperator.Equals,
                        Boolean = false
                    }]
                }
            },
            Layout = WorkspaceViewLayout.Table,
            Columns = ["title", "date", "priority", "project"],
            Pinned = true,
            Order = 0
        };
        string before = Path.Combine(root, "source-intact.md");
        await File.WriteAllTextAsync(before, "# Ne jamais modifier", Encoding.UTF8);
        WorkspaceViewDefinitionSnapshot saved = await store.SaveAsync(
            WorkspaceViewVisibility.Workspace, [definition], missing.Revision);
        WorkspaceViewDefinitionSnapshot read = await store.LoadAsync(
            WorkspaceViewVisibility.Workspace);
        Assert(read.Views.Count == 1 &&
            read.Views[0].Name == definition.Name &&
            read.Views[0].Query.Kinds.Single() == WorkspaceViewItemKind.Task &&
            read.Views[0].Layout == WorkspaceViewLayout.Table &&
            read.Views[0].Pinned &&
            read.Revision == saved.Revision,
            "View definitions must survive JSON save/reload without executing queries.");
        string json = await File.ReadAllTextAsync(
            store.FilePath(WorkspaceViewVisibility.Workspace));
        Assert(json.Contains("\"schemaVersion\"", StringComparison.Ordinal) &&
            !json.Contains("source-intact", StringComparison.OrdinalIgnoreCase) &&
            !json.Contains("results", StringComparison.OrdinalIgnoreCase) &&
            await File.ReadAllTextAsync(before) == "# Ne jamais modifier",
            "Saved JSON must only contain view definitions, never source data or results.");

        bool conflict = false;
        try
        {
            await store.SaveAsync(WorkspaceViewVisibility.Workspace,
                [definition with { Name = "Concurrent edit" }], missing.Revision);
        }
        catch (WorkspaceViewDefinitionConflictException)
        {
            conflict = true;
        }

        Assert(conflict && await File.ReadAllTextAsync(
                store.FilePath(WorkspaceViewVisibility.Workspace)) == json,
            "A stale revision must never overwrite an externally updated JSON.");

        WorkspaceViewDefinition second = definition with
        {
            Id = Guid.NewGuid(), Name = "Maintenance", Order = 1,
            Layout = WorkspaceViewLayout.Cards
        };
        WorkspaceViewDefinitionSnapshot updated = await store.SaveAsync(
            WorkspaceViewVisibility.Workspace, [definition, second], read.Revision);
        Assert((await store.LoadAsync(WorkspaceViewVisibility.Workspace)).Views.Count == 2,
            "Duplicating a definition must preserve two distinct view IDs.");

        WorkspaceViewDefinitionSnapshot privateRead = await store.LoadAsync(
            WorkspaceViewVisibility.Private);
        await store.SaveAsync(WorkspaceViewVisibility.Private,
            [definition with { Id = Guid.NewGuid(), Name = "Mes notes privées" }],
            privateRead.Revision);
        string privatePath = store.FilePath(WorkspaceViewVisibility.Private);
        Assert(File.Exists(privatePath) &&
            !privatePath.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) &&
            (await store.LoadAsync(WorkspaceViewVisibility.Workspace)).Views.Count == 2,
            "Private saved views must not leak into workspace portable definitions.");

        string portablePath = store.FilePath(WorkspaceViewVisibility.Workspace);
        string original = await File.ReadAllTextAsync(portablePath);
        await File.WriteAllTextAsync(portablePath, "{ invalid json");
        bool invalid = false;
        try
        {
            await store.LoadAsync(WorkspaceViewVisibility.Workspace);
        }
        catch (InvalidDataException)
        {
            invalid = true;
        }

        Assert(invalid && await File.ReadAllTextAsync(portablePath) == "{ invalid json",
            "Corruption must be reported without deleting or resetting the JSON source.");
        await File.WriteAllTextAsync(portablePath,
            original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 123",
                StringComparison.Ordinal));
        WorkspaceViewDefinitionSnapshot future = await store.LoadAsync(
            WorkspaceViewVisibility.Workspace);
        Assert(future.IsReadOnly, "Future view schemas must not be downgraded silently.");
        invalid = false;
        try
        {
            await store.SaveAsync(WorkspaceViewVisibility.Workspace,
                [definition], future.Revision);
        }
        catch (InvalidDataException)
        {
            invalid = true;
        }

        Assert(invalid, "A future schema must be protected from old-version writes.");
        await File.WriteAllTextAsync(portablePath, original);

        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        bool wasCancelled = false;
        try
        {
            await store.SaveAsync(WorkspaceViewVisibility.Workspace, [definition],
                updated.Revision, cancelled.Token);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }

        Assert(wasCancelled && await File.ReadAllTextAsync(portablePath) == original,
            "Cancellation before a write must preserve existing definitions.");
        Directory.Delete(privateDirectory, recursive: true);
        Console.WriteLine("[Views 1.5] portable/private definitions and conflict recovery passed.");
    }

    /// <summary>Checks a persistence invariant.</summary>
    /// <param name="passed">Whether it holds.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool passed, string message)
    {
        if (!passed)
        {
            throw new InvalidOperationException(message);
        }
    }
}
