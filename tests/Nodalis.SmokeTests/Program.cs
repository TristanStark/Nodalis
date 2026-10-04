using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Navigation;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Settings;
using Nodalis.Core.Templates;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Settings;
using Nodalis.Infrastructure.Templates;

var root = Path.Combine(
    Path.GetTempPath(),
    "Nodalis-SmokeTests",
    Guid.NewGuid().ToString("N"));

try
{
    await VerifyWorkspacePersistenceAsync(root);
    await VerifyWorkspaceNavigationAsync(root);
    await VerifyTemplatesAsync(root);
    await VerifyProjectCreationAsync(root);
    await VerifyApplicationStructureAsync(root);
    VerifyMarkdownParser();
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

    var documentNames = DescendantsAndSelf(project)
        .Where(node => node.Kind == WorkspaceNodeKind.Document)
        .Select(node => node.DisplayName)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(documentNames.Contains("Notes rapides") &&
           documentNames.Contains("Tests couteau"),
        "Navigation must discover Markdown files recursively.");
}

static async Task VerifyTemplatesAsync(string root)
{
    var store = new FileSystemTemplateStore(root);
    await store.InitializeDefaultsAsync();

    var catalog = await store.LoadTemplateCatalogAsync();
    Assert(catalog.Templates.Any(template => template.Key == "note"),
        "The default note template must exist.");
    Assert(catalog.Templates.Any(template => template.Key == "meeting"),
        "The default meeting template must exist.");

    var profiles = await store.LoadProjectProfilesAsync();
    Assert(profiles.Profiles.Count == 3,
        "Simple, Medium and Complex project profiles must exist.");

    foreach (var profile in profiles.Profiles)
    {
        var sectionNames = profile.Sections
            .Select(section => section.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert(
            sectionNames.Contains("Jalons") &&
            sectionNames.Contains("Technique") &&
            sectionNames.Contains("Glossaire") &&
            sectionNames.Contains("Tests"),
            "Every initial project profile must contain the four mandatory sections.");
    }

    var variables = MarkdownTemplateRenderer.CreateStandardVariables(
        "Ma note",
        Guid.NewGuid(),
        new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(2)));

    var rendered = await store.RenderAsync("note", variables);

    Assert(rendered.Contains("# Ma note", StringComparison.Ordinal) &&
           rendered.Contains("2026-10-04", StringComparison.Ordinal),
        "Template rendering must substitute standard variables.");

    AssertThrows<TemplateRenderException>(
        () => MarkdownTemplateRenderer.Render(
            "{{unknown.variable}}",
            variables),
        "Unknown template variables must fail explicitly.");

    var templatePath = Path.Combine(
        root,
        WorkspaceLayout.TemplatesDirectoryName,
        "note.md");

    const string customized = "# Template personnalisé\n";
    await File.WriteAllTextAsync(templatePath, customized);
    await store.InitializeDefaultsAsync();

    Assert(await File.ReadAllTextAsync(templatePath) == customized,
        "Default initialization must never overwrite a customized template.");

    var duplicatePath = Path.Combine(root, "duplicate.md");
    await File.WriteAllTextAsync(duplicatePath, "existing");

    var uniquePath = WindowsPathRules.GetUniqueFilePath(
        root,
        "duplicate.md");

    Assert(
        Path.GetFileName(uniquePath) == "duplicate (2).md",
        "New notes must avoid overwriting files with the same name.");
}

static async Task VerifyProjectCreationAsync(string root)
{
    var applicationId = Guid.NewGuid();
    var applicationPath = Path.Combine(
        root,
        WorkspaceLayout.ApplicationsDirectoryName,
        "Application Project Creator");

    Directory.CreateDirectory(applicationPath);

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    await File.WriteAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName),
        JsonSerializer.Serialize(
            new ApplicationManifest
            {
                Id = applicationId,
                Name = "Application Project Creator"
            },
            jsonOptions));

    var discovery = new ProjectCreationTargetDiscovery();
    var targets = await discovery.DiscoverAsync(root);

    var applicationTarget = targets.Single(
        target => target.ApplicationId == applicationId &&
                  target.ModuleId is null &&
                  target.ParentProjectId is null);

    var creator = new FileSystemProjectCreator(root);
    var result = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Généré",
            Complexity = ProjectComplexity.Medium,
            Target = applicationTarget,
            BusinessLinks =
            [
                "https://outil-interne/projet/123",
                "RTC: WI-456"
            ]
        });

    Assert(Directory.Exists(result.ProjectDirectory),
        "Project creation must materialize a directory.");
    Assert(File.Exists(Path.Combine(
            result.ProjectDirectory,
            WorkspaceLayout.ProjectManifestFileName)),
        "Project creation must persist its manifest.");
    Assert(File.Exists(result.OverviewFilePath),
        "Project creation must generate Présentation.md.");

    var overview = await File.ReadAllTextAsync(
        result.OverviewFilePath);

    Assert(
        overview.Contains("https://outil-interne/projet/123", StringComparison.Ordinal) &&
        overview.Contains("RTC: WI-456", StringComparison.Ordinal),
        "Business links must be copied into the project overview.");

    foreach (var requiredSection in new[] { "Jalons", "Technique", "Glossaire", "Tests" })
    {
        Assert(
            Directory.Exists(Path.Combine(result.ProjectDirectory, requiredSection)),
            $"Project profile must create the '{requiredSection}' section.");
    }

    var refreshedTargets = await discovery.DiscoverAsync(root);
    var parentTarget = refreshedTargets.Single(
        target => target.ParentProjectId == result.Project.Id);

    var child = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Sous-projet Généré",
            Complexity = ProjectComplexity.Simple,
            Target = parentTarget
        });

    Assert(
        child.Project.ParentProjectId == result.Project.Id &&
        Path.GetDirectoryName(child.ProjectDirectory) ==
            Path.Combine(
                result.ProjectDirectory,
                WorkspaceLayout.SubProjectsDirectoryName),
        "Sub-projects must be created under the selected parent project.");
}

static async Task VerifyApplicationStructureAsync(string root)
{
    var service = new ApplicationStructureService(root);

    var applicationPath = await service.CreateApplicationAsync(
        "Application Gestion");

    Assert(
        File.Exists(Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName)),
        "Creating an application must persist its manifest.");

    Assert(
        File.Exists(Path.Combine(
            applicationPath,
            WorkspaceLayout.GlobalQuickNotesFileName)) &&
        File.Exists(Path.Combine(
            applicationPath,
            WorkspaceLayout.GlobalGlossaryFileName)),
        "Creating an application must initialize its singleton notes.");

    var appJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        appJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var modulePath = await service.CreateModuleAsync(
        applicationPath,
        application.Id,
        parentModuleId: null,
        "Module Racine");

    var module = await service.LoadModuleAsync(modulePath);

    var childPath = await service.CreateModuleAsync(
        modulePath,
        application.Id,
        module.Id,
        "Sous-module");

    var child = await service.LoadModuleAsync(childPath);

    Assert(
        child.ParentModuleId == module.Id,
        "Nested modules must persist their parent module id.");

    var renamedChild = await service.RenameModuleAsync(
        childPath,
        "Sous-module renommé");

    Assert(
        Directory.Exists(renamedChild) &&
        Path.GetFileName(renamedChild).Contains(
            "Sous-module renommé",
            StringComparison.Ordinal),
        "Renaming a module must rename its directory.");

    await AssertThrowsAsync<DomainValidationException>(
        () => service.DeleteModuleAsync(modulePath),
        "A module containing another module must not be deleted.");

    var movedChild = await service.MoveModuleAsync(
        renamedChild,
        applicationPath,
        newParentModuleId: null);

    var movedManifest = await service.LoadModuleAsync(movedChild);

    Assert(
        movedManifest.ParentModuleId is null &&
        Path.GetDirectoryName(movedChild) ==
            Path.Combine(
                applicationPath,
                WorkspaceLayout.ModulesDirectoryName),
        "Moving a module to the application root must update path and metadata.");

    await service.DeleteModuleAsync(modulePath);
    Assert(
        !Directory.Exists(modulePath),
        "An empty module must be deletable.");

    var renamedApplication = await service.RenameApplicationAsync(
        applicationPath,
        "Application Gestion Renommée");

    Assert(
        Directory.Exists(renamedApplication),
        "Renaming an application must rename its directory.");

    await AssertThrowsAsync<DomainValidationException>(
        () => service.DeleteApplicationAsync(renamedApplication),
        "An application containing a module must not be deleted.");

    await service.DeleteModuleAsync(movedChild);
    await service.DeleteApplicationAsync(renamedApplication);

    Assert(
        !Directory.Exists(renamedApplication),
        "An empty application must be deletable.");
}

static void VerifyMarkdownParser()
{
    var fence = new string((char)96, 3);
    var markdown =
        "# Titre\n\n" +
        "Texte **gras** et *italique* avec [[Projet Patate|le projet]].\n\n" +
        "- [x] Action terminée\n" +
        "- Élément\n" +
        "1. Premier\n\n" +
        "> Citation\n\n" +
        "| Col A | Col B |\n" +
        "| --- | --- |\n" +
        "| A | B |\n\n" +
        fence + "csharp\n" +
        "Console.WriteLine(1);\n" +
        fence + "\n";

    var blocks = MarkdownDocumentParser.Parse(markdown);

    Assert(blocks.Any(block =>
            block.Kind == MarkdownBlockKind.Heading &&
            block.Level == 1 &&
            block.Text == "Titre"),
        "Markdown headings must be parsed.");

    Assert(blocks.Any(block =>
            block.Kind == MarkdownBlockKind.ChecklistItem &&
            block.IsChecked == true),
        "Markdown checkboxes must be parsed.");

    Assert(blocks.Any(block =>
            block.Kind == MarkdownBlockKind.Table &&
            block.TableRows.Count == 2),
        "Markdown tables must be parsed.");

    Assert(blocks.Any(block =>
            block.Kind == MarkdownBlockKind.CodeBlock &&
            block.Language == "csharp" &&
            block.Text.Contains("Console.WriteLine", StringComparison.Ordinal)),
        "Fenced code blocks must be parsed.");

    var inlines = MarkdownInlineParser.Parse(
        "A **bold** *italic* [[Target|Alias]] [link](file.md)");

    Assert(inlines.Any(inline =>
            inline.Kind == MarkdownInlineKind.Bold &&
            inline.Text == "bold"),
        "Bold inline Markdown must be parsed.");

    Assert(inlines.Any(inline =>
            inline.Kind == MarkdownInlineKind.InternalLink &&
            inline.Target == "Target" &&
            inline.Text == "Alias"),
        "Internal links with aliases must be parsed.");

    Assert(inlines.Any(inline =>
            inline.Kind == MarkdownInlineKind.Link &&
            inline.Target == "file.md"),
        "Standard Markdown links must be parsed.");
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
    WorkspaceNavigationNode node)
{
    yield return node;

    foreach (var child in node.Children)
    {
        foreach (var descendant in DescendantsAndSelf(child))
        {
            yield return descendant;
        }
    }
}
