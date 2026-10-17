using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Search;

/// <summary>
/// Defines one deterministic generated workspace fixture.
/// </summary>
internal sealed record WorkspaceRegressionFixtureDefinition
{
    public required string Name { get; init; }

    public required int DocumentCount { get; init; }

    public required int BodyCharacters { get; init; }
}

/// <summary>
/// Exercises representative Small, Medium, and Large workspaces with no third-party test dependency.
/// </summary>
internal static class WorkspaceRegressionFixtureSmokeTests
{
    /// <summary>
    /// Materializes every versioned fixture profile and verifies persistence, navigation, search, links, and filesystem refresh behavior.
    /// </summary>
    /// <param name="root">The main smoke-test root.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string definitionPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "workspace-fixtures.json");

        Assert(
            File.Exists(definitionPath),
            "Regression fixture definitions must be copied next to the test executable.");

        string definitionJson = await File.ReadAllTextAsync(
            definitionPath);

        WorkspaceRegressionFixtureDefinition[] definitions =
            JsonSerializer.Deserialize<WorkspaceRegressionFixtureDefinition[]>(
                definitionJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidDataException(
                "Regression fixture definitions are invalid.");

        HashSet<string> fixtureNames = definitions
            .Select(definition => definition.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert(
            definitions.Length == 3 &&
            fixtureNames.SetEquals(
                new[]
                {
                    "Small",
                    "Medium",
                    "Large"
                }),
            "Regression fixtures must define exactly Small, Medium, and Large profiles.");

        foreach (WorkspaceRegressionFixtureDefinition definition in definitions)
        {
            Assert(
                definition.DocumentCount > 0 &&
                definition.BodyCharacters >= 0,
                $"Regression fixture {definition.Name} has invalid sizing.");

            await RunFixtureAsync(
                root,
                definition);

            Console.WriteLine(
                $"Regression fixture {definition.Name}: {definition.DocumentCount} documents verified.");
        }
    }

    /// <summary>
    /// Creates and validates one isolated generated workspace fixture.
    /// </summary>
    /// <param name="root">The main smoke-test root.</param>
    /// <param name="definition">The fixture sizing definition.</param>
    /// <returns>A task representing the verification.</returns>
    private static async Task RunFixtureAsync(
            string root,
            WorkspaceRegressionFixtureDefinition definition)
    {
        string fixtureRoot = Path.Combine(
            root,
            "RegressionFixtures",
            definition.Name);

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                fixtureRoot);

        WorkspaceManifest created =
            await store.InitializeAsync(
                $"Regression Fixture {definition.Name}");

        WorkspaceManifest loaded =
            await store.LoadAsync();

        Assert(
            created.Id == loaded.Id,
            $"Regression fixture {definition.Name} must survive manifest persistence.");

        ApplicationStructureService structure =
            new ApplicationStructureService(
                fixtureRoot);

        string applicationPath =
            await structure.CreateApplicationAsync(
                $"Application {definition.Name}");

        string applicationJson =
            await File.ReadAllTextAsync(
                Path.Combine(
                    applicationPath,
                    WorkspaceLayout.ApplicationManifestFileName));

        ApplicationManifest application =
            JsonSerializer.Deserialize<ApplicationManifest>(
                applicationJson,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
            ?? throw new InvalidDataException(
                $"Regression fixture {definition.Name} application manifest is invalid.");

        FileSystemProjectCreator creator =
            new FileSystemProjectCreator(
                fixtureRoot);

        ProjectCreationResult project =
            await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name =
                        $"Projet {definition.Name}",
                    Complexity =
                        ProjectComplexity.Simple,
                    Target =
                        new ProjectCreationTarget
                        {
                            ApplicationId =
                                application.Id,
                            ApplicationName =
                                application.Name,
                            ParentDirectory =
                                applicationPath,
                            DisplayName =
                                application.Name
                        }
                });

        string notesDirectory =
            Path.Combine(
                project.ProjectDirectory,
                "Regression");

        Directory.CreateDirectory(
            notesDirectory);

        string fixtureToken =
            $"regression-fixture-{definition.Name.ToLowerInvariant()}";

        string payload =
            new string(
                'x',
                definition.BodyCharacters);

        List<string> documentPaths =
            new List<string>(
                definition.DocumentCount);

        for (int index = 0;
             index < definition.DocumentCount;
             index++)
        {
            string title =
                $"Fixture {definition.Name} {index:D4}";

            string nextTitle =
                $"Fixture {definition.Name} {(index + 1) % definition.DocumentCount:D4}";

            string documentPath =
                Path.Combine(
                    notesDirectory,
                    title + ".md");

            string content =
                "---\n" +
                "status: active\n" +
                $"tags: regression, {definition.Name.ToLowerInvariant()}\n" +
                "---\n" +
                $"# {title}\n\n" +
                fixtureToken +
                "\n\n" +
                $"[[{nextTitle}]]\n\n" +
                payload +
                "\n";

            await File.WriteAllTextAsync(
                documentPath,
                content);

            documentPaths.Add(
                documentPath);
        }

        WorkspaceNavigationBuilder navigationBuilder =
            new WorkspaceNavigationBuilder();

        WorkspaceNavigationNode navigation =
            await navigationBuilder.BuildAsync(
                fixtureRoot);

        string normalizedNotesDirectory =
            Path.GetFullPath(
                notesDirectory) +
            Path.DirectorySeparatorChar;

        int navigationDocumentCount =
            DescendantsAndSelf(
                    navigation)
                .Count(node =>
                    node.Kind ==
                        WorkspaceNodeKind.Document &&
                    Path.GetFullPath(
                            node.FullPath)
                        .StartsWith(
                            normalizedNotesDirectory,
                            StringComparison.OrdinalIgnoreCase));

        Assert(
            navigationDocumentCount ==
                definition.DocumentCount,
            $"Regression fixture {definition.Name} must expose every generated document in navigation.");

        WorkspaceSearchService search =
            new WorkspaceSearchService();

        global::Nodalis.Core.Search.SearchResultSet searchResults =
            await search.SearchAsync(
                fixtureRoot,
                documentPaths[0],
                fixtureToken);

        int matchedDocuments =
            searchResults.Project
                .Select(result =>
                    result.FilePath)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        Assert(
            matchedDocuments ==
                definition.DocumentCount,
            $"Regression fixture {definition.Name} search must find every generated project document.");

        WorkspaceLinkIndexService linkIndex =
            new WorkspaceLinkIndexService(
                fixtureRoot);

        LinkIndexCatalog links =
            await linkIndex.RefreshAsync();

        string linkPrefix =
            $"Fixture {definition.Name} ";

        int fixtureReferenceCount =
            links.References.Count(reference =>
                !string.IsNullOrWhiteSpace(
                    reference.RawTarget) &&
                reference.RawTarget.StartsWith(
                    linkPrefix,
                    StringComparison.Ordinal));

        Assert(
            fixtureReferenceCount >=
                definition.DocumentCount,
            $"Regression fixture {definition.Name} must index the generated link ring.");

        await File.AppendAllTextAsync(
            documentPaths[0],
            "\nregression-filesystem-mutation\n");

        global::Nodalis.Core.Search.SearchResultSet mutationResults =
            await search.SearchAsync(
                fixtureRoot,
                documentPaths[0],
                "regression-filesystem-mutation");

        Assert(
            mutationResults.Project.Any(result =>
                string.Equals(
                    result.FilePath,
                    documentPaths[0],
                    StringComparison.OrdinalIgnoreCase)),
            $"Regression fixture {definition.Name} must observe filesystem content changes.");

        string removedDocumentPath =
            documentPaths[
                documentPaths.Count - 1];

        File.Delete(
            removedDocumentPath);

        global::Nodalis.Core.Search.SearchResultSet afterDeleteResults =
            await search.SearchAsync(
                fixtureRoot,
                documentPaths[0],
                fixtureToken);

        int matchedAfterDelete =
            afterDeleteResults.Project
                .Select(result =>
                    result.FilePath)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count();

        Assert(
            matchedAfterDelete ==
                definition.DocumentCount - 1,
            $"Regression fixture {definition.Name} must stop returning deleted documents.");
    }

    /// <summary>
    /// Enumerates a navigation tree recursively.
    /// </summary>
    /// <param name="node">The root node.</param>
    /// <returns>The node and every descendant.</returns>
    private static IEnumerable<WorkspaceNavigationNode> DescendantsAndSelf(
            WorkspaceNavigationNode node)
    {
        yield return node;

        foreach (WorkspaceNavigationNode child in node.Children)
        {
            foreach (WorkspaceNavigationNode descendant in DescendantsAndSelf(
                         child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Throws when a regression invariant is not satisfied.
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
