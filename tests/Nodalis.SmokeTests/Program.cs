using System.Text.Json;
using Nodalis.Core.Attachments;
using Nodalis.Core.Domain;
using Nodalis.Core.Glossary;
using Nodalis.Core.Links;
using Nodalis.Core.Milestones;
using Nodalis.Core.Navigation;
using Nodalis.Core.Tasks;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Settings;
using Nodalis.Core.Templates;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Attachments;
using Nodalis.Infrastructure.Documents;
using Nodalis.Infrastructure.Glossary;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Milestones;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Notes;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Search;
using Nodalis.Infrastructure.Settings;
using Nodalis.Infrastructure.Tasks;
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
    await VerifyQuickNotesAsync(root);
    await VerifySearchAsync(root);
    await VerifyLinksAndBacklinksAsync(root);
    await VerifyGlossaryAsync(root);
    await VerifyAttachmentsAsync(root);
    await VerifyTasksAsync(root);
    await VerifyMilestonesAsync(root);
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

    var movedChildAfterApplicationRename = Path.Combine(
        renamedApplication,
        WorkspaceLayout.ModulesDirectoryName,
        Path.GetFileName(movedChild));

    await service.DeleteModuleAsync(
        movedChildAfterApplicationRename);

    await service.DeleteApplicationAsync(
        renamedApplication);

    Assert(
        !Directory.Exists(renamedApplication),
        "An empty application must be deletable.");
}

static async Task VerifyQuickNotesAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Notes");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var creator = new FileSystemProjectCreator(root);
    var target = new ProjectCreationTarget
    {
        ApplicationId = application.Id,
        ApplicationName = application.Name,
        ParentDirectory = applicationPath,
        DisplayName = application.Name
    };

    var project = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Notes",
            Complexity = ProjectComplexity.Simple,
            Target = target
        });

    var notes = new QuickNotesService();
    var scopes = await notes.ResolveScopesAsync(
        root,
        project.OverviewFilePath);

    Assert(
        scopes.Count == 3 &&
        scopes[0].Kind == Nodalis.Core.Notes.QuickNoteScopeKind.Project &&
        scopes[1].Kind == Nodalis.Core.Notes.QuickNoteScopeKind.Application &&
        scopes[2].Kind == Nodalis.Core.Notes.QuickNoteScopeKind.Global,
        "Quick note scopes must resolve in Project, Application, Global order.");

    await notes.AppendAsync(
        scopes[0],
        "Tester [[Projet Notes]]",
        new DateTimeOffset(
            2026, 10, 4, 14, 0, 0,
            TimeSpan.FromHours(2)));

    var projectNotes = await File.ReadAllTextAsync(
        scopes[0].FilePath);

    Assert(
        projectNotes.Contains(
            "## 2026-10-04 14:00",
            StringComparison.Ordinal) &&
        projectNotes.Contains(
            "[[Projet Notes]]",
            StringComparison.Ordinal),
        "Quick note capture must append a timestamped Markdown entry.");

    var aggregate = await notes.ReadAggregateAsync(
        root,
        project.ProjectDirectory);

    var aggregateMarkdown =
        QuickNotesService.FormatAggregateMarkdown(
            aggregate);

    Assert(
        aggregateMarkdown.Contains(
            "# Projet · Projet Notes",
            StringComparison.Ordinal) &&
        aggregateMarkdown.Contains(
            "# Application · Application Notes",
            StringComparison.Ordinal) &&
        aggregateMarkdown.Contains(
            "# Global",
            StringComparison.Ordinal),
        "Aggregated quick notes must clearly distinguish every relevant scope.");

    var modulePath = await structure.CreateModuleAsync(
        applicationPath,
        application.Id,
        parentModuleId: null,
        "Module Sans Scope");

    Assert(
        !File.Exists(Path.Combine(
            modulePath,
            WorkspaceLayout.GlobalQuickNotesFileName)) &&
        !File.Exists(Path.Combine(
            modulePath,
            WorkspaceLayout.GlobalGlossaryFileName)),
        "Modules must not create quick-note or glossary scopes.");
}

static async Task VerifySearchAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Recherche");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var creator = new FileSystemProjectCreator(root);
    var project = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Recherche",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var globalFile = Path.Combine(
        root,
        "Recherche globale.md");

    var applicationFile = Path.Combine(
        applicationPath,
        "Recherche application.md");

    var projectFile = Path.Combine(
        project.ProjectDirectory,
        "Recherche projet.md");

    await File.WriteAllTextAsync(
        globalFile,
        "# Global\n\nsteak global\n");

    await File.WriteAllTextAsync(
        applicationFile,
        "# Application\n\nsteak application\n");

    await File.WriteAllTextAsync(
        projectFile,
        "# Projet\n\nsteak projet\n");

    var search = new WorkspaceSearchService();
    var results = await search.SearchAsync(
        root,
        projectFile,
        "steak");

    Assert(
        results.Project.Any(result =>
            result.FilePath == projectFile),
        "Project search must include current-project documents.");

    Assert(
        results.Application.Any(result =>
            result.FilePath == applicationFile),
        "Application search must include application-level documents.");

    Assert(
        results.Global.Any(result =>
            result.FilePath == globalFile),
        "Global search must include global documents.");

    Assert(
        !results.Application.Any(result =>
            result.FilePath == projectFile) &&
        !results.Global.Any(result =>
            result.FilePath == applicationFile ||
            result.FilePath == projectFile),
        "Search scopes must not duplicate lower-scope matches.");

    Assert(
        results.Project
            .Concat(results.Application)
            .Concat(results.Global)
            .All(result =>
                result.LineNumber > 0 &&
                !string.IsNullOrWhiteSpace(result.Excerpt)),
        "Search results must expose navigable line numbers and excerpts.");
}

static async Task VerifyLinksAndBacklinksAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Liens");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var projectCreator = new FileSystemProjectCreator(root);
    var project = await projectCreator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Liens",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var sourcePath = Path.Combine(
        project.ProjectDirectory,
        "Source.md");

    var targetPath = Path.Combine(
        project.ProjectDirectory,
        "Cible.md");

    await File.WriteAllTextAsync(
        sourcePath,
        "# Source\n\nVoir [[Cible]] puis [[Introuvable]].\n");

    await File.WriteAllTextAsync(
        targetPath,
        "# Cible\n\nContenu stable.\n");

    var indexService = new WorkspaceLinkIndexService(root);
    var initial = await indexService.RefreshAsync();

    var target = initial.Targets.Single(candidate =>
        candidate.Kind == LinkTargetKind.Document &&
        candidate.DisplayName == "Cible" &&
        candidate.RelativePath.EndsWith(
            "Cible.md",
            StringComparison.OrdinalIgnoreCase));

    var source = initial.Targets.Single(candidate =>
        candidate.Kind == LinkTargetKind.Document &&
        candidate.DisplayName == "Source");

    var resolved = WorkspaceLinkIndexService.Resolve(
        initial,
        "Cible");

    Assert(
        resolved.Status == LinkResolutionStatus.Resolved &&
        resolved.Target?.Id == target.Id,
        "A unique [[document]] link must resolve to the indexed target id.");

    var broken = initial.References.Single(reference =>
        reference.SourceId == source.Id &&
        reference.RawTarget == "Introuvable");

    Assert(
        broken.TargetId is null &&
        WorkspaceLinkIndexService.Resolve(
            initial,
            broken.RawTarget).Status == LinkResolutionStatus.Missing,
        "Broken internal links must be represented explicitly in the index.");

    var backlinks = await indexService.GetBacklinksAsync(
        target.Id);

    Assert(
        backlinks.Count == 1 &&
        backlinks[0].Source.Id == source.Id &&
        backlinks[0].LineNumber == 3,
        "Backlinks must point to the source document and occurrence line.");

    var documentStructure = new DocumentStructureService(root);
    var renamedPath = await documentStructure.RenameAsync(
        targetPath,
        "Cible renommée");

    var renamedIndex = await indexService.LoadAsync();

    var renamed = renamedIndex.Targets.Single(candidate =>
        candidate.Kind == LinkTargetKind.Document &&
        candidate.RelativePath.EndsWith(
            "Cible renommée.md",
            StringComparison.OrdinalIgnoreCase));

    Assert(
        renamed.Id == target.Id,
        "Renaming a document must preserve its immutable link target id.");

    var oldNameResolution = WorkspaceLinkIndexService.Resolve(
        renamedIndex,
        "Cible");

    Assert(
        oldNameResolution.Status == LinkResolutionStatus.Resolved &&
        oldNameResolution.Target?.Id == target.Id &&
        oldNameResolution.Target.DisplayName == "Cible renommée",
        "The former document name must remain a resolving alias after rename.");

    var renamedBacklinks = await indexService.GetBacklinksAsync(
        target.Id);

    Assert(
        renamedBacklinks.Count == 1 &&
        renamedBacklinks[0].RawTarget == "Cible",
        "Existing Markdown links must remain valid without rewriting after rename.");

    var renamedApplicationPath = await structure.RenameApplicationAsync(
        applicationPath,
        "Application Liens Renommée");

    var afterParentRename = await indexService.RefreshAsync();

    var targetAfterParentRename = afterParentRename.Targets.Single(candidate =>
        candidate.Id == target.Id);

    Assert(
        targetAfterParentRename.DisplayName == "Cible renommée" &&
        File.Exists(Path.Combine(
            root,
            targetAfterParentRename.RelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar))),
        "Document identity must survive parent application path changes.");

    Assert(
        WorkspaceLinkIndexService.Resolve(
            afterParentRename,
            "Cible").Target?.Id == target.Id,
        "Historical aliases must survive parent folder renames.");
}

static async Task VerifyGlossaryAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Glossaire");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var projectCreator = new FileSystemProjectCreator(root);
    var project = await projectCreator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Glossaire",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var glossary = new GlossaryService();
    var scopes = await glossary.ResolveScopesAsync(
        root,
        project.OverviewFilePath);

    Assert(
        scopes.Count == 3 &&
        scopes[0].Kind == GlossaryScopeKind.Project &&
        scopes[1].Kind == GlossaryScopeKind.Application &&
        scopes[2].Kind == GlossaryScopeKind.Global,
        "Glossary scopes must resolve in Project, Application, Global priority order.");

    await glossary.AppendAsync(
        scopes[2],
        new GlossaryEntryDraft
        {
            Term = "Steak",
            Definition = "Définition globale",
            Synonyms = ["Bifteck"],
            Acronyms = ["STK"],
            Links = ["[[Présentation]]"]
        });

    await glossary.AppendAsync(
        scopes[1],
        new GlossaryEntryDraft
        {
            Term = "Steak",
            Definition = "Définition application"
        });

    await glossary.AppendAsync(
        scopes[0],
        new GlossaryEntryDraft
        {
            Term = "Steak",
            Definition = "Définition projet"
        });

    var projectResolution = await glossary.ResolveAsync(
        root,
        project.ProjectDirectory,
        "Steak");

    Assert(
        projectResolution.Primary?.Scope.Kind == GlossaryScopeKind.Project &&
        projectResolution.Primary.Definition == "Définition projet" &&
        projectResolution.Alternatives.Count == 2 &&
        projectResolution.Alternatives[0].Scope.Kind == GlossaryScopeKind.Application &&
        projectResolution.Alternatives[1].Scope.Kind == GlossaryScopeKind.Global,
        "Glossary resolution must prioritize Project over Application over Global.");

    var synonymResolution = await glossary.ResolveAsync(
        root,
        project.ProjectDirectory,
        "bifteck");

    Assert(
        synonymResolution.Primary?.Scope.Kind == GlossaryScopeKind.Global &&
        synonymResolution.Primary.Term == "Steak",
        "Glossary synonyms must resolve case-insensitively.");

    var acronymResolution = await glossary.ResolveAsync(
        root,
        project.ProjectDirectory,
        "stk");

    Assert(
        acronymResolution.Primary?.Scope.Kind == GlossaryScopeKind.Global &&
        acronymResolution.Primary.Term == "Steak",
        "Glossary acronyms must resolve case-insensitively.");

    var applicationResolution = await glossary.ResolveAsync(
        root,
        applicationPath,
        "Steak");

    Assert(
        applicationResolution.Primary?.Scope.Kind == GlossaryScopeKind.Application &&
        applicationResolution.Alternatives.Count == 1 &&
        applicationResolution.Alternatives[0].Scope.Kind == GlossaryScopeKind.Global,
        "Application context must not include project-local glossary definitions.");

    await AssertThrowsAsync<InvalidOperationException>(
        () => glossary.AppendAsync(
            scopes[0],
            new GlossaryEntryDraft
            {
                Term = "Steak",
                Definition = "Doublon interdit dans le même scope"
            }),
        "A glossary must reject duplicate terms inside the same scope.");

    var parsed = GlossaryService.Parse(
        "## API\n\n**Définition :** Interface\n\n**Synonymes :** service\n\n**Acronymes :** API\n\n**Liens :** [[Technique]]\n",
        scopes[0]);

    Assert(
        parsed.Count == 1 &&
        parsed[0].Term == "API" &&
        parsed[0].Synonyms.Single() == "service" &&
        parsed[0].Acronyms.Single() == "API" &&
        parsed[0].Links.Single() == "[[Technique]]",
        "Structured glossary Markdown must round-trip term metadata.");

    var matcherScope = scopes[0];
    var matcherEntries = new List<GlossaryEntry>
    {
        new()
        {
            Term = "Base de données",
            Definition = "Terme long",
            Scope = matcherScope
        },
        new()
        {
            Term = "Base",
            Definition = "Terme court",
            Scope = matcherScope
        },
        new()
        {
            Term = "Steak",
            Definition = "Terme",
            Synonyms = ["Bifteck"],
            Scope = matcherScope
        }
    };

    const string sourceText =
        "Base de données, STEAK et bifteck. Steakhouse ne doit pas matcher.";

    var textMatches = GlossaryTextMatcher.Match(
        sourceText,
        matcherEntries);

    Assert(
        textMatches.Count(match =>
            match.Entry.Term == "Base de données") == 1 &&
        textMatches.All(match =>
            !(match.Entry.Term == "Base" &&
              match.Start == 0)),
        "Glossary matching must prefer the longest overlapping expression.");

    Assert(
        textMatches.Count(match =>
            match.Entry.Term == "Steak") == 2,
        "Glossary matching must resolve case and synonyms.");

    Assert(
        !textMatches.Any(match =>
            match.MatchedText.Contains(
                "Steakhouse",
                StringComparison.CurrentCultureIgnoreCase)),
        "Glossary matching must respect punctuation and word boundaries.");

    Assert(
        sourceText ==
        "Base de données, STEAK et bifteck. Steakhouse ne doit pas matcher.",
        "Glossary annotation matching must never mutate Markdown content.");
}

static async Task VerifyAttachmentsAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Pièces Jointes");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var projectCreator = new FileSystemProjectCreator(root);
    var project = await projectCreator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Pièces Jointes",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var notePath = Path.Combine(
        project.ProjectDirectory,
        "Note pièces jointes.md");

    await File.WriteAllTextAsync(
        notePath,
        "# Note\n");

    var linkIndex = new WorkspaceLinkIndexService(root);
    await linkIndex.RefreshAsync();

    var externalRoot = Path.Combine(
        Path.GetTempPath(),
        "Nodalis-Attachment-Source",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        externalRoot);

    var sourcePath = Path.Combine(
        externalRoot,
        "specification.bin");

    var sourceBytes = Enumerable
        .Range(0, 256)
        .Select(value => (byte)value)
        .ToArray();

    await File.WriteAllBytesAsync(
        sourcePath,
        sourceBytes);

    try
    {
        var attachments = new AttachmentService(root);

        var copied = await attachments.CopyIntoWorkspaceAsync(
            sourcePath,
            notePath);

        Assert(
            copied.StorageMode == AttachmentStorageMode.CopiedIntoWorkspace &&
            copied.Exists &&
            copied.FullPath.StartsWith(
                Path.Combine(
                    root,
                    WorkspaceLayout.AttachmentsDirectoryName),
                StringComparison.OrdinalIgnoreCase),
            "Copied attachments must live under the workspace Attachments directory.");

        Assert(
            (await File.ReadAllBytesAsync(copied.FullPath))
                .SequenceEqual(sourceBytes),
            "Attachment copy must preserve binary content exactly.");

        Assert(
            !Path.IsPathRooted(copied.MarkdownTarget),
            "Copied attachments must use a relative Markdown target.");

        var resolvedCopy = attachments.Resolve(
            copied.MarkdownTarget,
            notePath);

        Assert(
            resolvedCopy.Exists &&
            string.Equals(
                resolvedCopy.FullPath,
                copied.FullPath,
                StringComparison.OrdinalIgnoreCase),
            "Relative attachment targets must resolve from their owner document.");

        var external = attachments.CreateExternalReference(
            sourcePath,
            notePath);

        Assert(
            external.StorageMode == AttachmentStorageMode.ExternalReference &&
            Path.IsPathRooted(external.FullPath) &&
            external.Exists,
            "External attachments must keep an explicit absolute local reference.");

        var resolvedExternal = attachments.Resolve(
            external.MarkdownTarget,
            notePath);

        Assert(
            resolvedExternal.Exists &&
            resolvedExternal.StorageMode == AttachmentStorageMode.ExternalReference,
            "External local references must resolve without copying the file.");

        File.Delete(
            copied.FullPath);

        var missing = attachments.Resolve(
            copied.MarkdownTarget,
            notePath);

        Assert(
            !missing.Exists,
            "Deleted attachment files must be detected as missing.");

        var secondCopy = await attachments.CopyIntoWorkspaceAsync(
            sourcePath,
            notePath);

        var thirdCopy = await attachments.CopyIntoWorkspaceAsync(
            sourcePath,
            notePath);

        Assert(
            !string.Equals(
                secondCopy.FullPath,
                thirdCopy.FullPath,
                StringComparison.OrdinalIgnoreCase),
            "Copying the same attachment twice must never overwrite an existing file.");
    }
    finally
    {
        if (Directory.Exists(externalRoot))
        {
            Directory.Delete(
                externalRoot,
                recursive: true);
        }
    }
}

static async Task VerifyTasksAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Tâches");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var projectCreator = new FileSystemProjectCreator(root);
    var project = await projectCreator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Tâches",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var meetingPath = Path.Combine(
        project.ProjectDirectory,
        "Réunion tâches.md");

    await File.WriteAllTextAsync(
        meetingPath,
        "# Réunion\n\n" +
        "- [ ] Préparer recette | Responsable: Alice | Échéance: 2026-10-10\n" +
        "- [x] Action déjà terminée\n");

    var applicationTaskPath = Path.Combine(
        applicationPath,
        "Action application.md");

    await File.WriteAllTextAsync(
        applicationTaskPath,
        "- [ ] Vérifier application | Owner: Bob | Due: 2026-11-01\n");

    var globalTaskPath = Path.Combine(
        root,
        "Action globale.md");

    await File.WriteAllTextAsync(
        globalTaskPath,
        "- [ ] Action globale\n");

    var service = new WorkspaceTaskService(root);

    var projectTasks = await service.GetTasksAsync(
        project.ProjectDirectory);

    Assert(
        projectTasks.Count == 1 &&
        projectTasks[0].Text == "Préparer recette" &&
        projectTasks[0].Owner == "Alice" &&
        projectTasks[0].DueDate == new DateOnly(2026, 10, 10) &&
        projectTasks[0].ProjectId == project.Project.Id,
        "Project task view must extract open checkboxes, metadata and project context.");

    var applicationTasks = await service.GetTasksAsync(
        applicationPath);

    Assert(
        applicationTasks.Count == 2 &&
        applicationTasks.Any(task =>
            task.Text == "Préparer recette") &&
        applicationTasks.Any(task =>
            task.Text == "Vérifier application"),
        "Application task view must include application-level and project tasks.");

    var globalTasks = await service.GetTasksAsync(
        root);

    Assert(
        globalTasks.Count >= 3 &&
        globalTasks.Any(task =>
            task.Text == "Action globale"),
        "Global task view must include tasks from every scope.");

    var allProjectTasks = await service.GetTasksAsync(
        project.ProjectDirectory,
        includeCompleted: true);

    Assert(
        allProjectTasks.Count == 2 &&
        allProjectTasks.Any(task =>
            task.IsCompleted &&
            task.Text == "Action déjà terminée"),
        "Completed tasks must be available when explicitly requested.");

    var taskToMove = projectTasks.Single();

    var originalContent = await File.ReadAllTextAsync(
        meetingPath);

    await File.WriteAllTextAsync(
        meetingPath,
        "Ligne ajoutée avant\n" + originalContent);

    await service.SetCompletedAsync(
        taskToMove,
        completed: true);

    var updatedContent = await File.ReadAllTextAsync(
        meetingPath);

    Assert(
        updatedContent.Contains(
            "- [x] Préparer recette | Responsable: Alice | Échéance: 2026-10-10",
            StringComparison.Ordinal),
        "Task toggle must relocate a uniquely moved source line safely.");

    var ambiguousPath = Path.Combine(
        project.ProjectDirectory,
        "Tâches ambiguës.md");

    const string duplicateTask =
        "- [ ] Même tâche\n";

    await File.WriteAllTextAsync(
        ambiguousPath,
        duplicateTask + duplicateTask);

    var refreshed = await service.GetTasksAsync(
        project.ProjectDirectory);

    var ambiguous = refreshed.First(task =>
        task.SourceRelativePath.EndsWith(
            "Tâches ambiguës.md",
            StringComparison.OrdinalIgnoreCase));

    await File.WriteAllTextAsync(
        ambiguousPath,
        "Décalage\n" + duplicateTask + duplicateTask);

    await AssertThrowsAsync<TaskSourceConflictException>(
        () => service.SetCompletedAsync(
            ambiguous,
            completed: true),
        "Task toggle must refuse ambiguous moved source lines.");
}

static async Task VerifyMilestonesAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Jalons");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    var projectCreator = new FileSystemProjectCreator(root);
    var project = await projectCreator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Jalons",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var service = new WorkspaceMilestoneService(root);

    var first = await service.AddAsync(
        project.ProjectDirectory,
        new MilestoneDraft
        {
            Name = "Recette longue",
            TargetDate = new DateOnly(2026, 10, 10),
            Status = "À faire",
            Description = "Passage A | puis B",
            Link = "outil://jalon/123"
        });

    await service.AddAsync(
        project.ProjectDirectory,
        new MilestoneDraft
        {
            Name = "Jalon terminé",
            TargetDate = new DateOnly(2026, 10, 8),
            Status = "Terminé",
            Description = "Déjà traité"
        });

    await service.AddAsync(
        project.ProjectDirectory,
        new MilestoneDraft
        {
            Name = "Ancien jalon",
            TargetDate = new DateOnly(2026, 9, 30),
            Status = "À faire"
        });

    var projectMilestones = await service.GetMilestonesAsync(
        project.ProjectDirectory);

    Assert(
        projectMilestones.Count == 3 &&
        projectMilestones.Any(item =>
            item.Name == "Recette longue" &&
            item.TargetDate == new DateOnly(2026, 10, 10) &&
            item.Description == "Passage A | puis B" &&
            item.Link == "outil://jalon/123"),
        "Milestone tables must persist dates, description, links and escaped pipes.");

    var updated = await service.UpdateAsync(
        first,
        new MilestoneDraft
        {
            Name = "Recette longue",
            TargetDate = new DateOnly(2026, 10, 12),
            Status = "En cours",
            Description = "Décalée après revue",
            Link = "outil://jalon/123"
        });

    Assert(
        updated.TargetDate == new DateOnly(2026, 10, 12) &&
        updated.Status == "En cours",
        "Editing a milestone must update local Markdown metadata.");

    var upcoming = await service.GetUpcomingAsync(
        new DateOnly(2026, 10, 4),
        forwardDays: 30);

    Assert(
        upcoming.Any(item =>
            item.ProjectId == project.Project.Id &&
            item.Name == "Recette longue") &&
        !upcoming.Any(item =>
            item.Name == "Jalon terminé") &&
        !upcoming.Any(item =>
            item.Name == "Ancien jalon"),
        "Upcoming milestones must exclude completed and past milestones.");

    var sourcePath = Path.Combine(
        root,
        updated.SourceRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));

    var current = await File.ReadAllTextAsync(
        sourcePath);

    await File.WriteAllTextAsync(
        sourcePath,
        "Note avant la table\n" + current);

    var relocated = await service.UpdateAsync(
        updated,
        new MilestoneDraft
        {
            Name = "Recette longue",
            TargetDate = new DateOnly(2026, 10, 13),
            Status = "En cours",
            Description = "Ligne déplacée",
            Link = "outil://jalon/123"
        });

    Assert(
        relocated.TargetDate == new DateOnly(2026, 10, 13),
        "Milestone editing must relocate a uniquely moved source row safely.");

    var duplicateRow = relocated.RawLine;
    var duplicatedContent = await File.ReadAllTextAsync(
        sourcePath);

    await File.WriteAllTextAsync(
        sourcePath,
        "Décalage supplémentaire\n" +
        duplicatedContent +
        duplicateRow +
        "\n");

    await AssertThrowsAsync<MilestoneSourceConflictException>(
        () => service.UpdateAsync(
            relocated,
            new MilestoneDraft
            {
                Name = "Recette longue",
                TargetDate = new DateOnly(2026, 10, 14),
                Status = "En cours"
            }),
        "Milestone editing must refuse ambiguous moved source rows.");
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
