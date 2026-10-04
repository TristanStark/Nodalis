using System.IO.Compression;
using System.Text.Json;
using Nodalis.Core.Attachments;
using Nodalis.Core.Decisions;
using Nodalis.Core.Domain;
using Nodalis.Core.Glossary;
using Nodalis.Core.Importing;
using Nodalis.Core.Links;
using Nodalis.Core.Meetings;
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
using Nodalis.Infrastructure.Decisions;
using Nodalis.Infrastructure.Documents;
using Nodalis.Infrastructure.Glossary;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Meetings;
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
    await VerifyMeetingsAsync(root);
    await VerifyDecisionsAsync(root);
    await VerifyDocxImportAsync(root);
    await VerifyDocxImportAnalysisAsync(root);
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

    var simpleProfile = profiles.Profiles.Single(profile =>
        profile.Complexity == ProjectComplexity.Simple);
    var mediumProfile = profiles.Profiles.Single(profile =>
        profile.Complexity == ProjectComplexity.Medium);
    var complexProfile = profiles.Profiles.Single(profile =>
        profile.Complexity == ProjectComplexity.Complex);

    Assert(
        simpleProfile.Sections.Count == 4 &&
        simpleProfile.Sections.Single(section =>
            section.Name == "Technique").TemplateKey == "technical-simple" &&
        simpleProfile.Sections.Single(section =>
            section.Name == "Tests").TemplateKey == "tests-simple",
        "Simple projects must stay minimal and use simple technical/test templates.");

    var mediumNames = mediumProfile.Sections
        .Select(section => section.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(
        mediumNames.IsSupersetOf(
            ["Documentation", "Réunions", "Décisions", "Risques"]) &&
        mediumProfile.Sections.Single(section =>
            section.Name == "Technique").TemplateKey == "technical-medium" &&
        mediumProfile.Sections.Single(section =>
            section.Name == "Tests").TemplateKey == "tests-medium",
        "Medium projects must add project-management sections and richer templates.");

    var complexNames = complexProfile.Sections
        .Select(section => section.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(
        complexNames.IsSupersetOf(
            ["Fonctionnel", "Exploitation", "Déploiement", "Dépendances"]) &&
        complexProfile.Sections.Single(section =>
            section.Name == "Technique").TemplateKey == "technical-complex" &&
        complexProfile.Sections.Single(section =>
            section.Name == "Tests").TemplateKey == "tests-complex",
        "Complex projects must add operational sections and the deepest templates.");

    var variables = MarkdownTemplateRenderer.CreateStandardVariables(
        "Ma note",
        Guid.NewGuid(),
        new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(2)));

    var rendered = await store.RenderAsync("note", variables);

    Assert(rendered.Contains("# Ma note", StringComparison.Ordinal) &&
           rendered.Contains("2026-10-04", StringComparison.Ordinal),
        "Template rendering must substitute standard variables.");

    var complexTests = await store.RenderAsync(
        "tests-complex",
        variables);
    var complexTechnical = await store.RenderAsync(
        "technical-complex",
        variables);

    Assert(
        complexTests.Contains(
            "## Performance / volumétrie",
            StringComparison.Ordinal) &&
        complexTests.Contains(
            "## Résilience / reprise",
            StringComparison.Ordinal) &&
        complexTechnical.Contains(
            "## Observabilité",
            StringComparison.Ordinal) &&
        complexTechnical.Contains(
            "## Déploiement / rollback",
            StringComparison.Ordinal),
        "Complex profile templates must expose advanced test and technical coverage.");

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

static async Task VerifyMeetingsAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Réunions");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
        ?? throw new InvalidDataException(
            "Meeting smoke test application manifest is invalid.");

    var creator = new FileSystemProjectCreator(root);
    var project = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Réunions",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var service = new WorkspaceMeetingService(root);
    var result = await service.CreateAsync(
        project.ProjectDirectory,
        new MeetingDraft
        {
            Title = "Comité projet",
            Date = new DateOnly(2026, 10, 4),
            Participants = "Alice\nBob",
            Context = "Préparer la recette.",
            Agenda = "Planning\nRisques",
            Notes = "Point de vigilance sur le lot 2.",
            Decisions = "Valider l'architecture cible",
            Actions =
                "Préparer recette | Responsable: Alice | Échéance: 2026-10-10\n" +
                "- [ ] Envoyer le compte-rendu",
            AiTranscript = "Transcription locale collée.",
            AiSummary = "Résumé local collé.",
            OutlookContent = "Invitation Outlook copiée."
        });

    Assert(
        Path.GetFileName(
            Path.GetDirectoryName(result.FilePath)) ==
        WorkspaceMeetingService.MeetingsDirectoryName,
        "Meetings must be stored in a dedicated project meeting directory.");

    Assert(
        Path.GetFileName(result.FilePath).StartsWith(
            "2026-10-04 - Comité projet",
            StringComparison.Ordinal),
        "Meeting filenames must use the meeting date and title.");

    var content = await File.ReadAllTextAsync(
        result.FilePath);

    Assert(
        content.Contains(
            "**Projet :** Projet Réunions",
            StringComparison.Ordinal) &&
        content.Contains(
            "- Alice",
            StringComparison.Ordinal) &&
        content.Contains(
            "- Valider l'architecture cible",
            StringComparison.Ordinal) &&
        content.Contains(
            "- [ ] Préparer recette | Responsable: Alice | Échéance: 2026-10-10",
            StringComparison.Ordinal) &&
        content.Contains(
            "## Contenu Outlook",
            StringComparison.Ordinal) &&
        content.Contains(
            "Invitation Outlook copiée.",
            StringComparison.Ordinal),
        "Meeting assistant must persist structured participants, decisions, actions and pasted Outlook content.");

    var scope = await service.ResolveScopeAsync(
        result.FilePath);

    Assert(
        scope is not null &&
        scope.Value.ScopeKind == "Projet" &&
        scope.Value.ScopeName == "Projet Réunions",
        "Meeting context resolution must keep the meeting attached to its project.");

    var tasks = await new WorkspaceTaskService(root)
        .GetTasksAsync(
            project.ProjectDirectory);

    Assert(
        tasks.Any(task =>
            task.Text == "Préparer recette" &&
            task.Owner == "Alice" &&
            task.DueDate == new DateOnly(2026, 10, 10)) &&
        tasks.Any(task =>
            task.Text == "Envoyer le compte-rendu"),
        "Meeting actions must immediately surface as project Markdown tasks.");
}

static async Task VerifyDecisionsAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Décisions");

    var applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
        ?? throw new InvalidDataException(
            "Decision smoke test application manifest is invalid.");

    var creator = new FileSystemProjectCreator(root);
    var project = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Décisions",
            Complexity = ProjectComplexity.Simple,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var meetingService = new WorkspaceMeetingService(root);
    var meeting = await meetingService.CreateAsync(
        project.ProjectDirectory,
        new MeetingDraft
        {
            Title = "Comité architecture",
            Date = new DateOnly(2026, 10, 5),
            Decisions =
                "Adopter PostgreSQL pour le référentiel\n" +
                "Geler l'API publique avant la recette"
        });

    var decisionService = new WorkspaceDecisionService(root);
    var candidates = await decisionService.ExtractDecisionCandidatesAsync(
        meeting.FilePath);

    Assert(
        candidates.Count == 2 &&
        candidates.Contains(
            "Adopter PostgreSQL pour le référentiel",
            StringComparer.CurrentCultureIgnoreCase),
        "Decision service must extract explicit decisions from a meeting section.");

    var linksBefore = await new WorkspaceLinkIndexService(root)
        .RefreshAsync();

    var meetingRelative = Path.GetRelativePath(
            root,
            meeting.FilePath)
        .Replace(
            Path.DirectorySeparatorChar,
            '/');

    var meetingTarget = linksBefore.Targets.Single(target =>
        target.Kind == LinkTargetKind.Document &&
        string.Equals(
            target.RelativePath,
            meetingRelative,
            StringComparison.OrdinalIgnoreCase));

    var created = await decisionService.CreateAsync(
        meeting.FilePath,
        new DecisionDraft
        {
            Title = "Référentiel PostgreSQL",
            Date = new DateOnly(2026, 10, 5),
            Decision = candidates[0],
            Context = "Le stockage doit rester local et auditable.",
            Justification = "Référentiel relationnel structuré.",
            Impacts = "Adapter le schéma de persistence.",
            Status = "Actée",
            Links = "Documentation technique"
        },
        meeting.FilePath);

    Assert(
        Path.GetFileName(
            Path.GetDirectoryName(created.FilePath)) ==
        WorkspaceDecisionService.DecisionsDirectoryName,
        "Decision Records must be stored in a dedicated decision directory.");

    var content = await File.ReadAllTextAsync(
        created.FilePath);

    Assert(
        content.Contains(
            "# Décision — Référentiel PostgreSQL",
            StringComparison.Ordinal) &&
        content.Contains(
            "**Statut :** Actée",
            StringComparison.Ordinal) &&
        content.Contains(
            $"[[{meetingTarget.QualifiedName}]]",
            StringComparison.Ordinal) &&
        content.Contains(
            "Adopter PostgreSQL pour le référentiel",
            StringComparison.Ordinal),
        "Decision Records must persist readable metadata, content and source link.");

    var listed = await decisionService.GetDecisionsAsync(
        project.ProjectDirectory);

    Assert(
        listed.Count == 1 &&
        listed[0].Title == "Référentiel PostgreSQL" &&
        listed[0].Status == "Actée",
        "Project decision view must list persisted Decision Records.");

    var searched = await decisionService.SearchAsync(
        project.ProjectDirectory,
        "PostgreSQL");

    Assert(
        searched.Count == 1,
        "Decision search must find text across Decision Record content.");

    var linkService = new WorkspaceLinkIndexService(root);
    var catalogWithDecision = await linkService.RefreshAsync();
    var backlinks = await linkService.GetBacklinksAsync(
        meetingTarget.Id);

    Assert(
        backlinks.Any(backlink =>
            backlink.Source.RelativePath.EndsWith(
                Path.GetFileName(created.FilePath),
                StringComparison.OrdinalIgnoreCase)),
        "A Decision Record must create a backlink on its source document.");

    var renamedMeeting = await new DocumentStructureService(root)
        .RenameAsync(
            meeting.FilePath,
            "Comité architecture renommé");

    var afterRename = await linkService.RefreshAsync();
    var oldReferenceResolution = WorkspaceLinkIndexService.Resolve(
        afterRename,
        meetingTarget.QualifiedName);

    Assert(
        oldReferenceResolution.Status == LinkResolutionStatus.Resolved &&
        oldReferenceResolution.Target?.Id == meetingTarget.Id &&
        File.Exists(renamedMeeting),
        "Decision source references must survive source document renames.");
}

static async Task VerifyDocxImportAsync(string root)
{
    var sourceDirectory = Path.Combine(
        Path.GetTempPath(),
        "Nodalis-Docx-Source",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        sourceDirectory);

    try
    {
        var sourcePath = Path.Combine(
            sourceDirectory,
            "specification.docx");

        CreateSyntheticDocx(
            sourcePath);

        var originalBytes = await File.ReadAllBytesAsync(
            sourcePath);
        var originalWriteTime = File.GetLastWriteTimeUtc(
            sourcePath);

        var service = new WorkspaceDocxImportService(root);
        var result = await service.ImportAsync(
            sourcePath);

        Assert(
            File.Exists(result.SourceCopyPath) &&
            !string.Equals(
                sourcePath,
                result.SourceCopyPath,
                StringComparison.OrdinalIgnoreCase) &&
            result.SourceCopyPath.Contains(
                Path.Combine(
                    WorkspaceLayout.ImportsDirectoryName,
                    WorkspaceLayout.ImportSourcesDirectoryName),
                StringComparison.OrdinalIgnoreCase),
            "DOCX import must parse a dedicated workspace copy.");

        var sourceBytesAfterImport = await File.ReadAllBytesAsync(
            sourcePath);

        Assert(
            originalBytes.SequenceEqual(
                sourceBytesAfterImport) &&
            File.GetLastWriteTimeUtc(sourcePath) ==
            originalWriteTime,
            "DOCX import must never modify the original source file.");

        var document = result.Document;

        Assert(
            document.Metadata.Title == "Spécification synthétique" &&
            document.Metadata.Creator == "Nodalis Smoke Tests" &&
            document.Metadata.CreatedUtc is not null,
            "DOCX parser must extract useful core metadata.");

        Assert(
            document.Headers.Count == 1 &&
            document.Headers[0].Contains(
                "Application: Application Import DOCX",
                StringComparison.Ordinal) &&
            document.Headers[0].Contains(
                "Projet: Projet Import DOCX",
                StringComparison.Ordinal),
            "DOCX parser must expose header text for target detection.");

        Assert(
            document.Blocks.Count == 4 &&
            document.Blocks[0].Kind == DocxBlockKind.Paragraph &&
            document.Blocks[0].Paragraph?.Text == "Documentation technique" &&
            document.Blocks[0].Paragraph?.HeadingLevel == 1 &&
            document.Blocks[0].Paragraph?.StyleName == "Titre 1",
            "DOCX parser must preserve block order and resolve heading styles.");

        var linkParagraph = document.Blocks[1].Paragraph;

        Assert(
            linkParagraph is not null &&
            linkParagraph.Text.Contains(
                "documentation externe",
                StringComparison.OrdinalIgnoreCase) &&
            linkParagraph.Hyperlinks.Count == 1 &&
            linkParagraph.Hyperlinks[0].Target ==
            "https://example.test/documentation",
            "DOCX parser must extract hyperlink relationships.");

        var listParagraph = document.Blocks[2].Paragraph;

        Assert(
            listParagraph is not null &&
            listParagraph.IsListItem &&
            listParagraph.NumberingId == 1 &&
            listParagraph.ListLevel == 0,
            "DOCX parser must expose list numbering metadata.");

        var table = document.Blocks[3].Table;

        Assert(
            table is not null &&
            table.Rows.Count == 2 &&
            table.Rows[0].Cells.Count == 2 &&
            table.Rows[0].Cells[0].Text == "Clé" &&
            table.Rows[1].Cells[1].Text == "Valeur 1",
            "DOCX parser must extract simple table rows and cells.");

        Assert(
            document.Relationships.Any(relationship =>
                relationship.Id == "rId1" &&
                relationship.IsExternal) &&
            document.Relationships.Any(relationship =>
                relationship.Type.EndsWith(
                    "/image",
                    StringComparison.OrdinalIgnoreCase)),
            "DOCX parser must retain useful document relationships.");

        var invalidPath = Path.Combine(
            sourceDirectory,
            "invalide.docx");

        await File.WriteAllTextAsync(
            invalidPath,
            "ceci n'est pas une archive DOCX");

        var copiesBeforeFailure = Directory.GetFiles(
            Path.Combine(
                root,
                WorkspaceLayout.ImportsDirectoryName,
                WorkspaceLayout.ImportSourcesDirectoryName))
            .Length;

        await AssertThrowsAsync<InvalidDataException>(
            () => service.ImportAsync(
                invalidPath),
            "Invalid DOCX files must fail explicitly.");

        var copiesAfterFailure = Directory.GetFiles(
            Path.Combine(
                root,
                WorkspaceLayout.ImportsDirectoryName,
                WorkspaceLayout.ImportSourcesDirectoryName))
            .Length;

        Assert(
            copiesAfterFailure == copiesBeforeFailure,
            "Failed DOCX imports must clean up their working copy.");
    }
    finally
    {
        if (Directory.Exists(sourceDirectory))
        {
            Directory.Delete(
                sourceDirectory,
                recursive: true);
        }
    }
}

static void CreateSyntheticDocx(string path)
{
    Directory.CreateDirectory(
        Path.GetDirectoryName(path)!);

    using var archive = ZipFile.Open(
        path,
        ZipArchiveMode.Create);

    WriteZipEntry(
        archive,
        "[Content_Types].xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
          <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
          <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
        </Types>
        """);

    WriteZipEntry(
        archive,
        "word/styles.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:style w:type="paragraph" w:styleId="Heading1">
            <w:name w:val="Titre 1"/>
            <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
          </w:style>
        </w:styles>
        """);

    WriteZipEntry(
        archive,
        "word/header1.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
          <w:p><w:r><w:t>Application: Application Import DOCX</w:t></w:r></w:p>
          <w:p><w:r><w:t>Projet: Projet Import DOCX</w:t></w:r></w:p>
        </w:hdr>
        """);

    WriteZipEntry(
        archive,
        "word/_rels/document.xml.rels",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.test/documentation" TargetMode="External"/>
          <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/image1.png"/>
        </Relationships>
        """);

    WriteZipEntry(
        archive,
        "docProps/core.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <cp:coreProperties
            xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties"
            xmlns:dc="http://purl.org/dc/elements/1.1/"
            xmlns:dcterms="http://purl.org/dc/terms/">
          <dc:title>Spécification synthétique</dc:title>
          <dc:creator>Nodalis Smoke Tests</dc:creator>
          <dcterms:created>2026-10-04T12:00:00Z</dcterms:created>
          <dcterms:modified>2026-10-04T13:00:00Z</dcterms:modified>
        </cp:coreProperties>
        """);

    WriteZipEntry(
        archive,
        "word/document.xml",
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document
            xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <w:body>
            <w:p>
              <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
              <w:r><w:t>Documentation technique</w:t></w:r>
            </w:p>
            <w:p>
              <w:r><w:t>Voir la </w:t></w:r>
              <w:hyperlink r:id="rId1">
                <w:r><w:t>documentation externe</w:t></w:r>
              </w:hyperlink>
              <w:r><w:t>.</w:t></w:r>
            </w:p>
            <w:p>
              <w:pPr>
                <w:numPr>
                  <w:ilvl w:val="0"/>
                  <w:numId w:val="1"/>
                </w:numPr>
              </w:pPr>
              <w:r><w:t>Premier point</w:t></w:r>
            </w:p>
            <w:tbl>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Clé</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Valeur</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Champ 1</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>Valeur 1</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            <w:sectPr/>
          </w:body>
        </w:document>
        """);
}

static void WriteZipEntry(
    ZipArchive archive,
    string name,
    string content)
{
    var entry = archive.CreateEntry(
        name,
        CompressionLevel.Fastest);

    using var stream = entry.Open();
    using var writer = new StreamWriter(
        stream,
        new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false));

    writer.Write(
        content.Trim());
}

static async Task VerifyDocxImportAnalysisAsync(string root)
{
    var structure = new ApplicationStructureService(root);
    var applicationPath = await structure.CreateApplicationAsync(
        "Application Import DOCX");

    var applicationManifestText = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    var application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationManifestText,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
        ?? throw new InvalidDataException(
            "DOCX analysis application manifest is invalid.");

    var creator = new FileSystemProjectCreator(root);
    var project = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Import DOCX",
            Complexity = ProjectComplexity.Medium,
            Target = new ProjectCreationTarget
            {
                ApplicationId = application.Id,
                ApplicationName = application.Name,
                ParentDirectory = applicationPath,
                DisplayName = application.Name
            }
        });

    var sourceDirectory = Path.Combine(
        Path.GetTempPath(),
        "Nodalis-Docx-Analysis",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        sourceDirectory);

    try
    {
        var sourcePath = Path.Combine(
            sourceDirectory,
            "SPEC_Application_Import_DOCX.docx");

        CreateSyntheticDocx(
            sourcePath);

        var imported = await new WorkspaceDocxImportService(root)
            .ImportAsync(
                sourcePath);

        var analyzer = new DocxImportAnalyzer(root);
        var analysis = await analyzer.AnalyzeAsync(
            imported.Document,
            Path.GetFileName(sourcePath));

        Assert(
            analysis.ApplicationCandidates.Count == 1 &&
            analysis.ApplicationCandidates[0].Id == application.Id &&
            analysis.ApplicationCandidates[0].Confidence >= 90,
            "Explicit application labels in DOCX headers must resolve one strong application candidate.");

        Assert(
            analysis.ProjectCandidates.Count == 1 &&
            analysis.ProjectCandidates[0].Id == project.Project.Id &&
            analysis.ProjectCandidates[0].Confidence >= 90,
            "Explicit project labels must resolve inside the detected application.");

        var technical = analysis.MappedSections.Single(section =>
            section.TargetSection == "Technique");

        Assert(
            technical.SourceHeading == "Documentation technique" &&
            technical.Blocks.Count == 3 &&
            technical.Blocks.Any(block =>
                block.Kind == DocxBlockKind.Table),
            "Configurable heading rules must map Word content while preserving block order.");

        var previewService =
            new WorkspaceDocxImportService(root);

        var preview = await previewService.PreparePreviewAsync(
            sourcePath);

        Assert(
            File.Exists(
                preview.StagedImport.StagedCopyPath) &&
            preview.SuggestedApplicationId ==
                application.Id &&
            preview.SuggestedProjectId ==
                project.Project.Id &&
            preview.Sections.Any(section =>
                section.SuggestedTargetSection ==
                "Technique"),
            "DOCX preview must stage a disposable copy and expose detected targets and sections before validation.");

        var technicalPreview = preview.Sections.Single(section =>
            section.SuggestedTargetSection ==
            "Technique");

        var committed = await previewService.CommitAsync(
            preview,
            new DocxImportCommitRequest
            {
                ApplicationId = application.Id,
                ProjectId = project.Project.Id,
                Sections =
                [
                    new DocxImportSectionSelection
                    {
                        SectionIndex =
                            technicalPreview.Index,
                        Include = true,
                        TargetSection = "Tests"
                    }
                ]
            });

        Assert(
            !File.Exists(
                preview.StagedImport.StagedCopyPath) &&
            File.Exists(
                committed.SourceCopyPath) &&
            committed.GeneratedFiles.Count == 1 &&
            committed.GeneratedFiles[0].Contains(
                Path.Combine(
                    project.ProjectDirectory,
                    "Tests"),
                StringComparison.OrdinalIgnoreCase),
            "Validated DOCX imports must consume the staged copy and honor an explicit section remapping.");

        var importedMarkdown = await File.ReadAllTextAsync(
            committed.GeneratedFiles[0]);

        Assert(
            importedMarkdown.Contains(
                "documentation externe",
                StringComparison.OrdinalIgnoreCase) &&
            importedMarkdown.Contains(
                "https://example.test/documentation",
                StringComparison.OrdinalIgnoreCase) &&
            importedMarkdown.Contains(
                "| Clé | Valeur |",
                StringComparison.Ordinal),
            "Validated DOCX imports must persist converted Markdown content without losing links or tables.");

        var proposedDocument = new ParsedDocxDocument
        {
            Headers =
            [
                "Application: Application Import DOCX\n" +
                "Projet: Nouveau Projet DOCX"
            ],
            Blocks =
            [
                new DocxBlock
                {
                    Kind = DocxBlockKind.Paragraph,
                    Paragraph = new DocxParagraph
                    {
                        Text = "Tests / Recette",
                        HeadingLevel = 1
                    }
                },
                new DocxBlock
                {
                    Kind = DocxBlockKind.Paragraph,
                    Paragraph = new DocxParagraph
                    {
                        Text = "Cas nominal"
                    }
                }
            ]
        };

        var proposed = await analyzer.AnalyzeAsync(
            proposedDocument,
            "nouveau-projet.docx");

        Assert(
            proposed.ApplicationCandidates.Count == 1 &&
            proposed.ApplicationCandidates[0].Id == application.Id &&
            proposed.ProjectCandidates.Count == 0 &&
            proposed.ProposedProjectName == "Nouveau Projet DOCX" &&
            proposed.RequiresProjectCreation &&
            proposed.MappedSections.Single().TargetSection == "Tests",
            "A missing explicitly named project must be proposed for creation, never created silently.");

        var otherApplicationPath = await structure.CreateApplicationAsync(
            "Application Import Ambiguë");

        var otherManifestText = await File.ReadAllTextAsync(
            Path.Combine(
                otherApplicationPath,
                WorkspaceLayout.ApplicationManifestFileName));

        var otherApplication = JsonSerializer.Deserialize<ApplicationManifest>(
            otherManifestText,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidDataException(
                "DOCX ambiguity application manifest is invalid.");

        await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet Import Ambigu",
                Complexity = ProjectComplexity.Simple,
                Target = new ProjectCreationTarget
                {
                    ApplicationId = application.Id,
                    ApplicationName = application.Name,
                    ParentDirectory = applicationPath,
                    DisplayName = application.Name
                }
            });

        await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet Import Ambigu",
                Complexity = ProjectComplexity.Simple,
                Target = new ProjectCreationTarget
                {
                    ApplicationId = otherApplication.Id,
                    ApplicationName = otherApplication.Name,
                    ParentDirectory = otherApplicationPath,
                    DisplayName = otherApplication.Name
                }
            });

        var ambiguous = await analyzer.AnalyzeAsync(
            new ParsedDocxDocument
            {
                Headers =
                [
                    "Projet: Projet Import Ambigu"
                ]
            },
            "ambigu.docx");

        Assert(
            ambiguous.ApplicationCandidates.Count == 0 &&
            ambiguous.ProjectCandidates.Count == 2 &&
            ambiguous.HasAmbiguousProject,
            "A project name shared by multiple applications must remain explicitly ambiguous.");
    }
    finally
    {
        if (Directory.Exists(sourceDirectory))
        {
            Directory.Delete(
                sourceDirectory,
                recursive: true);
        }
    }
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
