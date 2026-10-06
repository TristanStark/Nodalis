using System.Text.Json;
using Nodalis.Core.Compatibility;
using Nodalis.Core.Domain;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Compatibility;
using Nodalis.Infrastructure.Persistence;

internal static class WorkspaceCompatibilitySmokeTests
{
    /// <summary>
    /// Verifies the compatibility matrix and isolated fallback reader.
    /// </summary>
    /// <param name="root">The main smoke-test root.</param>
    public static async Task RunAsync(
            string root)
    {
        VerifyCompatibilityMatrix();

        string sourceRoot =
            root + "-FutureSchema";

        try
        {
            await VerifyFutureSchemaFallbackAsync(
                sourceRoot);
        }
        finally
        {
            if (Directory.Exists(
                    sourceRoot))
            {
                Directory.Delete(
                    sourceRoot,
                    recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies deterministic decisions for legacy, current and future schemas.
    /// </summary>
    private static void VerifyCompatibilityMatrix()
    {
        global::Nodalis.Core.Compatibility.WorkspaceCompatibilityDecision legacy =
            WorkspaceCompatibilityPolicy.Evaluate(
                0);
        global::Nodalis.Core.Compatibility.WorkspaceCompatibilityDecision current =
            WorkspaceCompatibilityPolicy.Evaluate(
                WorkspaceManifest.CurrentSchemaVersion);
        global::Nodalis.Core.Compatibility.WorkspaceCompatibilityDecision future =
            WorkspaceCompatibilityPolicy.Evaluate(
                WorkspaceManifest.CurrentSchemaVersion + 7);

        Assert(
            legacy.AccessMode == WorkspaceAccessMode.MigrationRequired &&
            legacy.CanRead &&
            !legacy.CanWrite &&
            legacy.CanMigrate,
            "Legacy schema 0 must be readable but require migration before writing.");

        Assert(
            current.AccessMode == WorkspaceAccessMode.ReadWrite &&
            current.CanRead &&
            current.CanWrite &&
            !current.CanMigrate,
            "The current schema must support normal read/write access.");

        Assert(
            future.AccessMode == WorkspaceAccessMode.ReadOnlyFallback &&
            future.CanRead &&
            !future.CanWrite &&
            !future.CanMigrate,
            "A newer schema must never become writable, but may use metadata-agnostic fallback reading.");
    }

    /// <summary>
    /// Verifies that fallback access reads an isolated snapshot and leaves the newer source untouched.
    /// </summary>
    /// <param name="sourceRoot">The future-schema workspace root.</param>
    private static async Task VerifyFutureSchemaFallbackAsync(
            string sourceRoot)
    {
        Directory.CreateDirectory(
            Path.Combine(
                sourceRoot,
                "Unknown future structure",
                "Nested"));

        int futureVersion =
            WorkspaceManifest.CurrentSchemaVersion + 7;
        string manifestPath = Path.Combine(
            sourceRoot,
            WorkspaceLayout.WorkspaceManifestFileName);
        string notePath = Path.Combine(
            sourceRoot,
            "Unknown future structure",
            "Nested",
            "future-note.md");

        string manifest =
            "{\n" +
            $"  \"id\": \"{Guid.NewGuid():D}\",\n" +
            "  \"name\": \"Future Workspace\",\n" +
            $"  \"schemaVersion\": {futureVersion},\n" +
            "  \"unknownFutureContract\": { \"doNotInterpret\": true }\n" +
            "}\n";

        await File.WriteAllTextAsync(
            manifestPath,
            manifest);
        await File.WriteAllTextAsync(
            notePath,
            "# Future note\n\nContenu lisible.\n");

        global::Nodalis.Infrastructure.Compatibility.WorkspaceCompatibilityService service =
            new WorkspaceCompatibilityService(
                sourceRoot);
        global::Nodalis.Core.Compatibility.WorkspaceCompatibilityDecision decision =
            await service.EvaluateAsync();

        Assert(
            decision.WorkspaceSchemaVersion == futureVersion &&
            decision.AccessMode == WorkspaceAccessMode.ReadOnlyFallback &&
            !decision.CanWrite,
            "Future workspace detection must choose fallback read-only mode.");

        string originalManifest =
            await File.ReadAllTextAsync(
                manifestPath);
        string originalNote =
            await File.ReadAllTextAsync(
                notePath);

        string snapshotPath;

        using (global::Nodalis.Infrastructure.Compatibility.ReadOnlyWorkspaceSnapshot snapshot =
               await service.CreateReadOnlySnapshotAsync())
        {
            snapshotPath =
                snapshot.SnapshotRoot;

            global::Nodalis.Infrastructure.Compatibility.ReadOnlyWorkspaceNavigationBuilder builder =
                new ReadOnlyWorkspaceNavigationBuilder();
            global::Nodalis.Core.Navigation.WorkspaceNavigationNode navigation =
                await builder.BuildAsync(
                    snapshot.SnapshotRoot,
                    "Future Workspace");

            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Navigation.WorkspaceNavigationNode> documents =
                DescendantsAndSelf(
                        navigation)
                    .Where(
                        node => node.Kind == WorkspaceNodeKind.Document)
                    .ToArray();

            Assert(
                documents.Count == 1 &&
                documents[0].DisplayName == "future-note",
                "Fallback navigation must discover Markdown without parsing newer manifests.");

            await File.WriteAllTextAsync(
                documents[0].FullPath,
                "# Changed only in snapshot\n");
            File.Delete(
                Path.Combine(
                    snapshot.SnapshotRoot,
                    WorkspaceLayout.WorkspaceManifestFileName));

            Assert(
                await File.ReadAllTextAsync(
                    manifestPath) == originalManifest &&
                await File.ReadAllTextAsync(
                    notePath) == originalNote,
                "Writes performed inside the isolated fallback snapshot must never alter the future source workspace.");
        }

        Assert(
            !Directory.Exists(
                snapshotPath),
            "The temporary read-only snapshot must be removed when the session ends.");
    }

    /// <summary>
    /// Enumerates a fallback navigation tree.
    /// </summary>
    /// <param name="node">The current node.</param>
    /// <returns>The node and all descendants.</returns>
    private static IEnumerable<WorkspaceNavigationNode> DescendantsAndSelf(
            WorkspaceNavigationNode node)
    {
        yield return node;

        foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode child in node.Children)
        {
            foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode descendant in DescendantsAndSelf(
                         child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Throws when a smoke-test condition is false.
    /// </summary>
    /// <param name="condition">The condition to verify.</param>
    /// <param name="message">The failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
