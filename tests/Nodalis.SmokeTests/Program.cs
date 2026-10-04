using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Navigation;
using Nodalis.Core.Settings;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Settings;

var root = Path.Combine(
    Path.GetTempPath(),
    "Nodalis-SmokeTests",
    Guid.NewGuid().ToString("N"));

try
{
    await VerifyWorkspacePersistenceAsync(root);
    await VerifyWorkspaceNavigationAsync(root);
    await VerifyUserPreferencesAsync(root);
    await VerifyDocumentReliabilityAsync(root);
    VerifyDomainCatalog();

    Console.WriteLine("Nodalis smoke tests passed.");
    return;
}
finally
{
    if (Directory.Exists(root))
    {
        Directory.Delete(root, recursive: true);
    }
}

static async Task VerifyWorkspacePersistenceAsync(string root)
{
    var store = new FileSystemWorkspaceStore(root);
    var created = await store.InitializeAsync("Smoke Test Workspace");
    var loaded = await store.LoadAsync();

    Assert(created.Id == loaded.Id, "Workspace identity must survive persistence.");
    Assert(File.Exists(Path.Combine(root, WorkspaceLayout.GlobalQuickNotesFileName)),
        "Global quick notes must be created.");
    Assert(File.Exists(Path.Combine(root, WorkspaceLayout.GlobalGlossaryFileName)),
        "Global glossary must be created.");
    Assert(WindowsPathRules.SanitizeSegment("CON") == "_CON",
        "Reserved Windows names must be escaped.");
}

static async Task VerifyWorkspaceNavigationAsync(string root)
{
    var applicationId = Guid.NewGuid();
    var projectId = Guid.NewGuid();

    var applicationPath = Path.Combine(
        root,
        WorkspaceLayout.ApplicationsDirectoryName,
        "Application A");

    var projectPath = Path.Combine(
        applicationPath,
        WorkspaceLayout.ProjectsDirectoryName,
        "Projet Patate");

    var testsPath = Path.Combine(
        projectPath,
        "Tests",
        "Unitaires");

    Directory.CreateDirectory(testsPath);

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    await File.WriteAllTextAsync(
        Path.Combine(applicationPath, WorkspaceLayout.ApplicationManifestFileName),
        JsonSerializer.Serialize(
            new ApplicationManifest
            {
                Id = applicationId,
                Name = "Application A"
            },
            jsonOptions));

    await File.WriteAllTextAsync(
        Path.Combine(projectPath, WorkspaceLayout.ProjectManifestFileName),
        JsonSerializer.Serialize(
            new ProjectManifest
            {
                Id = projectId,
                Name = "Projet Patate",
                ApplicationId = applicationId,
                InitialComplexity = ProjectComplexity.Medium
            },
            jsonOptions));

    await File.WriteAllTextAsync(
        Path.Combine(projectPath, WorkspaceLayout.GlobalQuickNotesFileName),
        "# Notes projet\n");

    await File.WriteAllTextAsync(
        Path.Combine(testsPath, "Tests couteau.md"),
        "# Tests couteau\n");

    var builder = new WorkspaceNavigationBuilder();
    var navigation = await builder.BuildAsync(root);

    var applications = navigation.Children.Single(
        node => node.Kind == WorkspaceNodeKind.ApplicationsRoot);

    var application = applications.Children.Single();
    Assert(application.Id == applicationId,
        "Navigation must use the application manifest identity.");

    var projects = application.Children.Single(
        node => node.Kind == WorkspaceNodeKind.ProjectsRoot);

    var project = projects.Children.Single();
    Assert(project.Id == projectId,
        "Navigation must use the project manifest identity.");

    var documentNames = project
        .DescendantsAndSelf()
        .Where(node => node.Kind == WorkspaceNodeKind.Document)
        .Select(node => node.DisplayName)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(documentNames.Contains("Notes rapides") &&
           documentNames.Contains("Tests couteau"),
        "Navigation must discover Markdown files recursively.");
}

static async Task VerifyUserPreferencesAsync(string root)
{
    var preferencesPath = Path.Combine(root, "User", "preferences.json");
    var store = new UserPreferencesStore(preferencesPath);

    var defaults = await store.LoadAsync();
    Assert(defaults.IsContextPanelOpen,
        "Missing preferences must return safe defaults.");

    var projectId = Guid.NewGuid();
    var preferences = defaults with
    {
        WorkspaceRootPath = Path.Combine(root, "Workspace"),
        NavigationPanelWidth = 50,
        ContextPanelWidth = 5_000,
        ExpandedNodeIds = [projectId],
        Favorites =
        [
            new UserItemReference
            {
                Kind = "project",
                Key = projectId.ToString("D"),
                DisplayName = "Projet Patate"
            }
        ],
        RecentItems =
        [
            new RecentItemReference
            {
                Item = new UserItemReference
                {
                    Kind = "document",
                    Key = "Applications/Application A/Notes rapides.md"
                }
            }
        ],
        Editor = defaults.Editor with
        {
            FontSize = 100,
            AutosaveDelayMilliseconds = 20
        }
    };

    await store.SaveAsync(preferences);
    var loaded = await store.LoadAsync();

    Assert(loaded.WorkspaceRootPath == preferences.WorkspaceRootPath,
        "Preferences must persist the local workspace path.");
    Assert(loaded.NavigationPanelWidth == 180,
        "Navigation width must be normalized.");
    Assert(loaded.ContextPanelWidth == 800,
        "Context width must be normalized.");
    Assert(loaded.Editor.FontSize == 48,
        "Editor font size must be normalized.");
    Assert(loaded.Editor.AutosaveDelayMilliseconds == 100,
        "Autosave delay must be normalized.");
    Assert(loaded.Favorites.Count == 1 && loaded.ExpandedNodeIds.Contains(projectId),
        "Favorites and expanded nodes must survive persistence.");

    await File.WriteAllTextAsync(preferencesPath, "{ definitely not valid json");
    var recovered = await store.LoadAsync();

    Assert(recovered.WorkspaceRootPath is null &&
           recovered.Favorites.Count == 0 &&
           recovered.RecentItems.Count == 0 &&
           recovered.NavigationPanelWidth == 280 &&
           recovered.ContextPanelWidth == 300,
        "Corrupted preferences must safely return defaults.");
}

static async Task VerifyDocumentReliabilityAsync(string root)
{
    var documentPath = Path.Combine(root, "Documents", "note.md");
    await AtomicFileWriter.WriteAllTextAsync(documentPath, "# Version 1\n");

    var session = await TextDocumentSession.OpenAsync(documentPath);
    Assert(session.Content == "# Version 1\n",
        "A text session must load the document content.");

    await session.SaveAsync("# Version 2\n");
    Assert(await File.ReadAllTextAsync(documentPath) == "# Version 2\n",
        "A session save must update the file.");

    await File.WriteAllTextAsync(documentPath, "# External change\n");

    Assert(await session.HasExternalChangesAsync(),
        "External modifications must be detected.");

    await AssertThrowsAsync<ExternalModificationException>(
        () => session.SaveAsync("# Must not overwrite\n"),
        "Saving over an external modification must be rejected.");

    Assert(await File.ReadAllTextAsync(documentPath) == "# External change\n",
        "Conflict detection must preserve the external file.");

    await session.ReloadAsync();

    await using var autosave = new DocumentAutosaveController(
        session,
        TimeSpan.FromSeconds(30));

    autosave.Schedule("# Autosaved\n");
    await autosave.FlushAsync();

    Assert(await File.ReadAllTextAsync(documentPath) == "# Autosaved\n",
        "Flushing autosave must persist pending content.");

    var conflictRaised = false;
    autosave.ConflictDetected += (_, _) => conflictRaised = true;

    await File.WriteAllTextAsync(documentPath, "# External again\n");
    autosave.Schedule("# Pending local edit\n");
    await autosave.FlushAsync();

    Assert(conflictRaised,
        "Autosave must surface an external modification conflict.");
    Assert(await File.ReadAllTextAsync(documentPath) == "# External again\n",
        "Autosave conflicts must never overwrite external changes.");

    var temporaryFiles = Directory
        .EnumerateFiles(
            Path.GetDirectoryName(documentPath)!,
            "*.tmp",
            SearchOption.TopDirectoryOnly)
        .ToArray();

    Assert(temporaryFiles.Length == 0,
        "Atomic writes must not leave temporary files behind.");
}

static void VerifyDomainCatalog()
{
    var catalog = new WorkspaceCatalog();

    var application = catalog.CreateApplication("Application A");
    var module = catalog.CreateModule(application.Id, "Module A1");

    AssertThrows<DomainValidationException>(
        () => catalog.MoveModule(module.Id, module.Id),
        "A module cannot become its own parent.");

    var project = catalog.CreateProject(
        application.Id,
        "Projet Patate",
        ProjectComplexity.Simple,
        module.Id);

    var glossary = catalog.AddSection(
        project.Id,
        "Glossaire",
        isSingleton: true,
        templateKey: "glossary");

    catalog.AddSection(project.Id, "Technique", isSingleton: false);

    AssertThrows<DomainValidationException>(
        () => catalog.AddSection(project.Id, "Glossaire", isSingleton: true),
        "A singleton section cannot be duplicated.");

    catalog.RenameSection(project.Id, glossary.Id, "Glossaire projet");

    var child = catalog.CreateProject(
        application.Id,
        "Sous-projet Cuisson",
        ProjectComplexity.Simple,
        module.Id,
        project.Id);

    Assert(child.ParentProjectId == project.Id,
        "A sub-project must retain its parent identity.");

    AssertThrows<DomainValidationException>(
        () => catalog.DeleteProject(project.Id),
        "A project with sub-projects cannot be deleted.");

    AssertThrows<DomainValidationException>(
        () => catalog.DeleteApplication(application.Id),
        "An application with content cannot be deleted.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static async Task AssertThrowsAsync<TException>(
    Func<Task> action,
    string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static IEnumerable<WorkspaceNavigationNode> DescendantsAndSelf(
    this WorkspaceNavigationNode node)
{
    yield return node;

    foreach (var child in node.Children)
    {
        foreach (var descendant in child.DescendantsAndSelf())
        {
            yield return descendant;
        }
    }
}
