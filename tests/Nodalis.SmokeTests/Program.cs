using System.IO.Compression;
using System.Text.Json;
using Nodalis.Core.Attachments;
using Nodalis.Core.Backups;
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
using Nodalis.Core.Trash;
using Nodalis.Core.Validation;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Attachments;
using Nodalis.Infrastructure.Backups;
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
using Nodalis.Infrastructure.Trash;

string root = Path.Combine(
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
    await VerifyTrashAsync(root);
    await VerifyWorkspaceBackupsAsync(root);
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
    VerifyMarkdownOutlineParser();
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
    global::Nodalis.Infrastructure.Persistence.FileSystemWorkspaceStore store = new FileSystemWorkspaceStore(root);
    global::Nodalis.Core.Domain.WorkspaceManifest created = await store.InitializeAsync("Smoke Test Workspace");
    global::Nodalis.Core.Domain.WorkspaceManifest loaded = await store.LoadAsync();

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
    global::System.Guid applicationId = Guid.NewGuid();
    global::System.Guid projectId = Guid.NewGuid();

    string applicationPath = Path.Combine(
        root,
        WorkspaceLayout.ApplicationsDirectoryName,
        "Application A");

    string projectPath = Path.Combine(
        applicationPath,
        WorkspaceLayout.ProjectsDirectoryName,
        "Projet Patate");

    string testsPath = Path.Combine(
        projectPath,
        "Tests",
        "Unitaires");

    Directory.CreateDirectory(testsPath);

    global::System.Text.Json.JsonSerializerOptions jsonOptions = new JsonSerializerOptions
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

    global::Nodalis.Infrastructure.Navigation.WorkspaceNavigationBuilder builder = new WorkspaceNavigationBuilder();
    global::Nodalis.Core.Navigation.WorkspaceNavigationNode navigation = await builder.BuildAsync(root);

    global::Nodalis.Core.Navigation.WorkspaceNavigationNode applications = navigation.Children.Single(
        node => node.Kind == WorkspaceNodeKind.ApplicationsRoot);

    global::Nodalis.Core.Navigation.WorkspaceNavigationNode application = applications.Children.Single();
    Assert(application.Id == applicationId,
        "Navigation must use the application manifest identity.");

    global::Nodalis.Core.Navigation.WorkspaceNavigationNode projects = application.Children.Single(
        node => node.Kind == WorkspaceNodeKind.ProjectsRoot);

    global::Nodalis.Core.Navigation.WorkspaceNavigationNode project = projects.Children.Single();
    Assert(project.Id == projectId,
        "Navigation must use the project manifest identity.");

    global::System.Collections.Generic.HashSet<string> documentNames = DescendantsAndSelf(project)
        .Where(node => node.Kind == WorkspaceNodeKind.Document)
        .Select(node => node.DisplayName)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    Assert(documentNames.Contains("Notes rapides") &&
           documentNames.Contains("Tests couteau"),
        "Navigation must discover Markdown files recursively.");
}

static async Task VerifyTemplatesAsync(string root)
{
    global::Nodalis.Infrastructure.Templates.FileSystemTemplateStore store = new FileSystemTemplateStore(root);
    await store.InitializeDefaultsAsync();

    global::Nodalis.Core.Templates.TemplateCatalog catalog = await store.LoadTemplateCatalogAsync();
    Assert(catalog.Templates.Any(template => template.Key == "note"),
        "The default note template must exist.");
    Assert(catalog.Templates.Any(template => template.Key == "meeting"),
        "The default meeting template must exist.");

    global::Nodalis.Core.Templates.ProjectProfileCatalog profiles = await store.LoadProjectProfilesAsync();
    Assert(profiles.Profiles.Count == 3,
        "Simple, Medium and Complex project profiles must exist.");

    foreach (global::Nodalis.Core.Templates.ProjectProfileDefinition profile in profiles.Profiles)
    {
        global::System.Collections.Generic.HashSet<string> sectionNames = profile.Sections
            .Select(section => section.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert(
            sectionNames.Contains("Jalons") &&
            sectionNames.Contains("Technique") &&
            sectionNames.Contains("Glossaire") &&
            sectionNames.Contains("Tests"),
            "Every initial project profile must contain the four mandatory sections.");
    }

    global::Nodalis.Core.Templates.ProjectProfileDefinition simpleProfile = profiles.Profiles.Single(profile =>
        profile.Complexity == ProjectComplexity.Simple);
    global::Nodalis.Core.Templates.ProjectProfileDefinition mediumProfile = profiles.Profiles.Single(profile =>
        profile.Complexity == ProjectComplexity.Medium);
    global::Nodalis.Core.Templates.ProjectProfileDefinition complexProfile = profiles.Profiles.Single(profile =>
        profile.Complexity == ProjectComplexity.Complex);

    Assert(
        simpleProfile.Sections.Count == 4 &&
        simpleProfile.Sections.Single(section =>
            section.Name == "Technique").TemplateKey == "technical-simple" &&
        simpleProfile.Sections.Single(section =>
            section.Name == "Tests").TemplateKey == "tests-simple",
        "Simple projects must stay minimal and use simple technical/test templates.");

    global::System.Collections.Generic.HashSet<string> mediumNames = mediumProfile.Sections
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

    global::System.Collections.Generic.HashSet<string> complexNames = complexProfile.Sections
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

    global::System.Collections.Generic.Dictionary<string, string> variables = MarkdownTemplateRenderer.CreateStandardVariables(
        "Ma note",
        Guid.NewGuid(),
        new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.FromHours(2)));

    string rendered = await store.RenderAsync("note", variables);

    Assert(rendered.Contains("# Ma note", StringComparison.Ordinal) &&
           rendered.Contains("2026-10-04", StringComparison.Ordinal),
        "Template rendering must substitute standard variables.");

    string complexTests = await store.RenderAsync(
        "tests-complex",
        variables);
    string complexTechnical = await store.RenderAsync(
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

    string templatePath = Path.Combine(
        root,
        WorkspaceLayout.TemplatesDirectoryName,
        "note.md");

    const string customized = "# Template personnalisé\n";
    await File.WriteAllTextAsync(templatePath, customized);
    await store.InitializeDefaultsAsync();

    Assert(await File.ReadAllTextAsync(templatePath) == customized,
        "Default initialization must never overwrite a customized template.");

    string duplicatePath = Path.Combine(root, "duplicate.md");
    await File.WriteAllTextAsync(duplicatePath, "existing");

    string uniquePath = WindowsPathRules.GetUniqueFilePath(
        root,
        "duplicate.md");

    Assert(
        Path.GetFileName(uniquePath) == "duplicate (2).md",
        "New notes must avoid overwriting files with the same name.");
}

static async Task VerifyProjectCreationAsync(string root)
{
    global::System.Guid applicationId = Guid.NewGuid();
    string applicationPath = Path.Combine(
        root,
        WorkspaceLayout.ApplicationsDirectoryName,
        "Application Project Creator");

    Directory.CreateDirectory(applicationPath);

    global::System.Text.Json.JsonSerializerOptions jsonOptions = new JsonSerializerOptions
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

    global::Nodalis.Infrastructure.Projects.ProjectCreationTargetDiscovery discovery = new ProjectCreationTargetDiscovery();
    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Projects.ProjectCreationTarget> targets = await discovery.DiscoverAsync(root);

    global::Nodalis.Core.Projects.ProjectCreationTarget applicationTarget = targets.Single(
        target => target.ApplicationId == applicationId &&
                  target.ModuleId is null &&
                  target.ParentProjectId is null);

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult result = await creator.CreateAsync(
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

    string overview = await File.ReadAllTextAsync(
        result.OverviewFilePath);

    Assert(
        overview.Contains("https://outil-interne/projet/123", StringComparison.Ordinal) &&
        overview.Contains("RTC: WI-456", StringComparison.Ordinal),
        "Business links must be copied into the project overview.");

    foreach (string requiredSection in new[] { "Jalons", "Technique", "Glossaire", "Tests" })
    {
        Assert(
            Directory.Exists(Path.Combine(result.ProjectDirectory, requiredSection)),
            $"Project profile must create the '{requiredSection}' section.");
    }

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Projects.ProjectCreationTarget> refreshedTargets = await discovery.DiscoverAsync(root);
    global::Nodalis.Core.Projects.ProjectCreationTarget parentTarget = refreshedTargets.Single(
        target => target.ParentProjectId == result.Project.Id);

    global::Nodalis.Core.Projects.ProjectCreationResult child = await creator.CreateAsync(
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService service = new ApplicationStructureService(root);

    string applicationPath = await service.CreateApplicationAsync(
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

    string appJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        appJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    string modulePath = await service.CreateModuleAsync(
        applicationPath,
        application.Id,
        parentModuleId: null,
        "Module Racine");

    global::Nodalis.Core.Domain.ModuleManifest module = await service.LoadModuleAsync(modulePath);

    string childPath = await service.CreateModuleAsync(
        modulePath,
        application.Id,
        module.Id,
        "Sous-module");

    global::Nodalis.Core.Domain.ModuleManifest child = await service.LoadModuleAsync(childPath);

    Assert(
        child.ParentModuleId == module.Id,
        "Nested modules must persist their parent module id.");

    string renamedChild = await service.RenameModuleAsync(
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

    string movedChild = await service.MoveModuleAsync(
        renamedChild,
        applicationPath,
        newParentModuleId: null);

    global::Nodalis.Core.Domain.ModuleManifest movedManifest = await service.LoadModuleAsync(movedChild);

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

    string renamedApplication = await service.RenameApplicationAsync(
        applicationPath,
        "Application Gestion Renommée");

    Assert(
        Directory.Exists(renamedApplication),
        "Renaming an application must rename its directory.");

    await AssertThrowsAsync<DomainValidationException>(
        () => service.DeleteApplicationAsync(renamedApplication),
        "An application containing a module must not be deleted.");

    string movedChildAfterApplicationRename = Path.Combine(
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

static async Task VerifyTrashAsync(string root)
{
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    global::Nodalis.Infrastructure.Trash.WorkspaceTrashService trash = new WorkspaceTrashService(root);

    string applicationPath = await structure.CreateApplicationAsync(
        "Application Corbeille");
    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));
    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    string documentPath = Path.Combine(
        applicationPath,
        "Note supprimable.md");
    await File.WriteAllTextAsync(
        documentPath,
        "# Note supprimable\n\nContenu conservé.\n");

    Guid documentId = Guid.NewGuid();
    global::Nodalis.Core.Trash.TrashEntry documentEntry = await trash.MoveToTrashAsync(
        documentPath,
        TrashItemKind.Document,
        documentId);

    Assert(
        !File.Exists(documentPath),
        "Moving a document to the trash must remove it from its original location.");

    global::Nodalis.Infrastructure.Search.WorkspaceSearchService search = new WorkspaceSearchService();
    global::Nodalis.Core.Search.SearchResultSet deletedSearch = await search.SearchAsync(
        root,
        root,
        "Contenu conservé");

    Assert(
        deletedSearch.Project.Count == 0 &&
        deletedSearch.Application.Count == 0 &&
        deletedSearch.Global.Count == 0,
        "Content moved to the trash must not remain visible in workspace search.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Trash.TrashEntry> entries = await trash.ListAsync();

    Assert(
        entries.Any(item =>
            item.EntryId == documentEntry.EntryId &&
            item.ItemId == documentId &&
            item.Kind == TrashItemKind.Document),
        "The trash must persist type, deletion metadata and the supplied item id.");

    await File.WriteAllTextAsync(
        documentPath,
        "collision");

    await AssertThrowsAsync<TrashRestoreCollisionException>(
        () => trash.RestoreAsync(documentEntry.EntryId),
        "Restore must refuse an occupied original path before moving the trash payload.");

    Assert(
        (await trash.ListAsync()).Any(item =>
            item.EntryId == documentEntry.EntryId),
        "A collision must leave the trash entry untouched.");

    File.Delete(documentPath);

    global::Nodalis.Core.Trash.TrashEntry applicationEntry = await trash.MoveToTrashAsync(
        applicationPath,
        TrashItemKind.Application,
        application.Id);

    Assert(
        !Directory.Exists(applicationPath),
        "Moving an application to the trash must move its complete subtree.");

    await AssertThrowsAsync<DirectoryNotFoundException>(
        () => trash.RestoreAsync(documentEntry.EntryId),
        "A child item must not recreate a missing structured parent during restore.");

    Assert(
        (await trash.ListAsync()).Any(item =>
            item.EntryId == documentEntry.EntryId),
        "A missing restore parent must leave the child trash entry untouched.");

    string restoredApplication = await trash.RestoreAsync(
        applicationEntry.EntryId);

    string restoredApplicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            restoredApplication,
            WorkspaceLayout.ApplicationManifestFileName));
    global::Nodalis.Core.Domain.ApplicationManifest restoredManifest = JsonSerializer.Deserialize<ApplicationManifest>(
        restoredApplicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    Assert(
        restoredManifest.Id == application.Id,
        "Restoring a structured item must preserve its stable manifest id.");

    string restoredDocument = await trash.RestoreAsync(
        documentEntry.EntryId);

    Assert(
        restoredDocument == documentPath &&
        File.Exists(restoredDocument) &&
        (await File.ReadAllTextAsync(restoredDocument)).Contains(
            "Contenu conservé.",
            StringComparison.Ordinal),
        "Restoring a document after its parent must put the unchanged payload back at its original path.");

    global::Nodalis.Core.Trash.TrashEntry finalEntry = await trash.MoveToTrashAsync(
        restoredDocument,
        TrashItemKind.Document,
        documentId);

    Assert(
        (await trash.ListAsync()).Any(item =>
            item.EntryId == finalEntry.EntryId),
        "The final trash test entry must be visible before emptying the trash.");

    await trash.EmptyAsync();

    Assert(
        (await trash.ListAsync()).Count == 0 &&
        !File.Exists(restoredDocument),
        "Emptying the trash must permanently remove all trash payloads.");
}

static async Task VerifyWorkspaceBackupsAsync(string root)
{
    string backupDirectory = root + "-Backups";
    string restoreDirectory = root + "-Restored";
    string occupiedRestoreDirectory = root + "-OccupiedRestore";

    try
    {
        await File.WriteAllTextAsync(
            Path.Combine(
                root,
                "Backup probe.md"),
            "# Backup probe\n\nContenu sauvegardé.\n");

        global::Nodalis.Infrastructure.Backups.WorkspaceBackupService service = new WorkspaceBackupService(root);

        await AssertThrowsAsync<InvalidOperationException>(
            () => service.CreateBackupAsync(
                Path.Combine(
                    root,
                    "Backups"),
                retentionCount: 2),
            "Backup destinations inside the workspace must be rejected.");

        global::Nodalis.Core.Backups.WorkspaceBackupInfo first = await service.CreateBackupAsync(
            backupDirectory,
            retentionCount: 2);

        Assert(
            first.Status == BackupValidationStatus.Valid &&
            File.Exists(first.ArchivePath) &&
            first.SizeBytes > 0,
            "A manual workspace backup must produce a validated standard ZIP archive.");

        using (global::System.IO.Compression.ZipArchive archive = ZipFile.OpenRead(first.ArchivePath))
        {
            Assert(
                archive.Entries.Any(entry =>
                    entry.FullName == ".workspace.json") &&
                archive.Entries.Any(entry =>
                    entry.FullName == "Backup probe.md"),
                "The backup ZIP must remain readable with the standard ZIP API.");
        }

        global::Nodalis.Core.Backups.WorkspaceBackupInfo second = await service.CreateBackupAsync(
            backupDirectory,
            retentionCount: 2);
        global::Nodalis.Core.Backups.WorkspaceBackupInfo third = await service.CreateBackupAsync(
            backupDirectory,
            retentionCount: 2);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Backups.WorkspaceBackupInfo> retained = await service.ListBackupsAsync(
            backupDirectory);

        Assert(
            retained.Count == 2 &&
            retained.All(item =>
                item.Status == BackupValidationStatus.Valid) &&
            !File.Exists(first.ArchivePath),
            "Retention must keep only the configured number of newest backups.");

        bool dueImmediately = await service.IsBackupDueAsync(
            backupDirectory,
            intervalMinutes: 60,
            nowUtc: DateTimeOffset.UtcNow);

        Assert(
            !dueImmediately,
            "A recent backup must suppress an automatic backup until its cadence expires.");

        string restored = await service.RestoreBackupAsync(
            third.ArchivePath,
            restoreDirectory);

        Assert(
            File.Exists(Path.Combine(
                restored,
                WorkspaceLayout.WorkspaceManifestFileName)) &&
            (await File.ReadAllTextAsync(
                Path.Combine(
                    restored,
                    "Backup probe.md"))).Contains(
                "Contenu sauvegardé.",
                StringComparison.Ordinal),
            "A verified backup must restore into a separate directory.");

        Directory.CreateDirectory(
            occupiedRestoreDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                occupiedRestoreDirectory,
                "existing.txt"),
            "do not overwrite");

        await AssertThrowsAsync<BackupRestoreCollisionException>(
            () => service.RestoreBackupAsync(
                second.ArchivePath,
                occupiedRestoreDirectory),
            "Restore must refuse a non-empty destination without overwriting it.");

        string corruptArchive = Path.Combine(
            backupDirectory,
            "corrupt.zip");
        await File.WriteAllBytesAsync(
            corruptArchive,
            [0x50, 0x4B, 0x03, 0x04, 0x00]);

        global::Nodalis.Core.Backups.WorkspaceBackupInfo corrupt = await service.ValidateBackupAsync(
            corruptArchive);

        Assert(
            corrupt.Status == BackupValidationStatus.Invalid,
            "A truncated ZIP must be reported as invalid before restoration.");
    }
    finally
    {
        foreach (string path in new[]
                 {
                     backupDirectory,
                     restoreDirectory,
                     occupiedRestoreDirectory
                 })
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
    }
}

static async Task VerifyQuickNotesAsync(string root)
{
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Notes");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationTarget target = new ProjectCreationTarget
    {
        ApplicationId = application.Id,
        ApplicationName = application.Name,
        ParentDirectory = applicationPath,
        DisplayName = application.Name
    };

    global::Nodalis.Core.Projects.ProjectCreationResult project = await creator.CreateAsync(
        new ProjectCreationRequest
        {
            Name = "Projet Notes",
            Complexity = ProjectComplexity.Simple,
            Target = target
        });

    global::Nodalis.Infrastructure.Notes.QuickNotesService notes = new QuickNotesService();
    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Notes.QuickNoteScope> scopes = await notes.ResolveScopesAsync(
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

    string projectNotes = await File.ReadAllTextAsync(
        scopes[0].FilePath);

    Assert(
        projectNotes.Contains(
            "## 2026-10-04 14:00",
            StringComparison.Ordinal) &&
        projectNotes.Contains(
            "[[Projet Notes]]",
            StringComparison.Ordinal),
        "Quick note capture must append a timestamped Markdown entry.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Notes.QuickNotesSnapshot> aggregate = await notes.ReadAggregateAsync(
        root,
        project.ProjectDirectory);

    string aggregateMarkdown =
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

    string modulePath = await structure.CreateModuleAsync(
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Recherche");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await creator.CreateAsync(
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

    string globalFile = Path.Combine(
        root,
        "Recherche globale.md");

    string applicationFile = Path.Combine(
        applicationPath,
        "Recherche application.md");

    string projectFile = Path.Combine(
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

    global::Nodalis.Infrastructure.Search.WorkspaceSearchService search = new WorkspaceSearchService();
    global::Nodalis.Core.Search.SearchResultSet results = await search.SearchAsync(
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Liens");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator projectCreator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await projectCreator.CreateAsync(
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

    string sourcePath = Path.Combine(
        project.ProjectDirectory,
        "Source.md");

    string targetPath = Path.Combine(
        project.ProjectDirectory,
        "Cible.md");

    await File.WriteAllTextAsync(
        sourcePath,
        "# Source\n\nVoir [[Cible]] puis [[Introuvable]].\n");

    await File.WriteAllTextAsync(
        targetPath,
        "# Cible\n\nContenu stable.\n");

    global::Nodalis.Infrastructure.Links.WorkspaceLinkIndexService indexService = new WorkspaceLinkIndexService(root);
    global::Nodalis.Core.Links.LinkIndexCatalog initial = await indexService.RefreshAsync();

    global::Nodalis.Core.Links.LinkTargetEntry target = initial.Targets.Single(candidate =>
        candidate.Kind == LinkTargetKind.Document &&
        candidate.DisplayName == "Cible" &&
        candidate.RelativePath.EndsWith(
            "Cible.md",
            StringComparison.OrdinalIgnoreCase));

    global::Nodalis.Core.Links.LinkTargetEntry source = initial.Targets.Single(candidate =>
        candidate.Kind == LinkTargetKind.Document &&
        candidate.DisplayName == "Source");

    global::Nodalis.Core.Links.LinkResolution resolved = WorkspaceLinkIndexService.Resolve(
        initial,
        "Cible");

    Assert(
        resolved.Status == LinkResolutionStatus.Resolved &&
        resolved.Target?.Id == target.Id,
        "A unique [[document]] link must resolve to the indexed target id.");

    global::Nodalis.Core.Links.LinkReferenceEntry broken = initial.References.Single(reference =>
        reference.SourceId == source.Id &&
        reference.RawTarget == "Introuvable");

    Assert(
        broken.TargetId is null &&
        WorkspaceLinkIndexService.Resolve(
            initial,
            broken.RawTarget).Status == LinkResolutionStatus.Missing,
        "Broken internal links must be represented explicitly in the index.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Links.BacklinkEntry> backlinks = await indexService.GetBacklinksAsync(
        target.Id);

    Assert(
        backlinks.Count == 1 &&
        backlinks[0].Source.Id == source.Id &&
        backlinks[0].LineNumber == 3,
        "Backlinks must point to the source document and occurrence line.");

    global::Nodalis.Infrastructure.Documents.DocumentStructureService documentStructure = new DocumentStructureService(root);
    string renamedPath = await documentStructure.RenameAsync(
        targetPath,
        "Cible renommée");

    global::Nodalis.Core.Links.LinkIndexCatalog renamedIndex = await indexService.LoadAsync();

    global::Nodalis.Core.Links.LinkTargetEntry renamed = renamedIndex.Targets.Single(candidate =>
        candidate.Kind == LinkTargetKind.Document &&
        candidate.RelativePath.EndsWith(
            "Cible renommée.md",
            StringComparison.OrdinalIgnoreCase));

    Assert(
        renamed.Id == target.Id,
        "Renaming a document must preserve its immutable link target id.");

    global::Nodalis.Core.Links.LinkResolution oldNameResolution = WorkspaceLinkIndexService.Resolve(
        renamedIndex,
        "Cible");

    Assert(
        oldNameResolution.Status == LinkResolutionStatus.Resolved &&
        oldNameResolution.Target?.Id == target.Id &&
        oldNameResolution.Target.DisplayName == "Cible renommée",
        "The former document name must remain a resolving alias after rename.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Links.BacklinkEntry> renamedBacklinks = await indexService.GetBacklinksAsync(
        target.Id);

    Assert(
        renamedBacklinks.Count == 1 &&
        renamedBacklinks[0].RawTarget == "Cible",
        "Existing Markdown links must remain valid without rewriting after rename.");

    string renamedApplicationPath = await structure.RenameApplicationAsync(
        applicationPath,
        "Application Liens Renommée");

    global::Nodalis.Core.Links.LinkIndexCatalog afterParentRename = await indexService.RefreshAsync();

    global::Nodalis.Core.Links.LinkTargetEntry targetAfterParentRename = afterParentRename.Targets.Single(candidate =>
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Glossaire");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator projectCreator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await projectCreator.CreateAsync(
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

    global::Nodalis.Infrastructure.Glossary.GlossaryService glossary = new GlossaryService();
    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryScope> scopes = await glossary.ResolveScopesAsync(
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

    global::Nodalis.Core.Glossary.GlossaryResolution projectResolution = await glossary.ResolveAsync(
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

    global::Nodalis.Core.Glossary.GlossaryResolution synonymResolution = await glossary.ResolveAsync(
        root,
        project.ProjectDirectory,
        "bifteck");

    Assert(
        synonymResolution.Primary?.Scope.Kind == GlossaryScopeKind.Global &&
        synonymResolution.Primary.Term == "Steak",
        "Glossary synonyms must resolve case-insensitively.");

    global::Nodalis.Core.Glossary.GlossaryResolution acronymResolution = await glossary.ResolveAsync(
        root,
        project.ProjectDirectory,
        "stk");

    Assert(
        acronymResolution.Primary?.Scope.Kind == GlossaryScopeKind.Global &&
        acronymResolution.Primary.Term == "Steak",
        "Glossary acronyms must resolve case-insensitively.");

    global::Nodalis.Core.Glossary.GlossaryResolution applicationResolution = await glossary.ResolveAsync(
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

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryEntry> parsed = GlossaryService.Parse(
        "## API\n\n**Définition :** Interface\n\n**Synonymes :** service\n\n**Acronymes :** API\n\n**Liens :** [[Technique]]\n",
        scopes[0]);

    Assert(
        parsed.Count == 1 &&
        parsed[0].Term == "API" &&
        parsed[0].Synonyms.Single() == "service" &&
        parsed[0].Acronyms.Single() == "API" &&
        parsed[0].Links.Single() == "[[Technique]]",
        "Structured glossary Markdown must round-trip term metadata.");

    global::Nodalis.Core.Glossary.GlossaryScope matcherScope = scopes[0];
    global::System.Collections.Generic.List<global::Nodalis.Core.Glossary.GlossaryEntry> matcherEntries = new List<GlossaryEntry>
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

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Glossary.GlossaryTextMatch> textMatches = GlossaryTextMatcher.Match(
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Pièces Jointes");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator projectCreator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await projectCreator.CreateAsync(
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

    string notePath = Path.Combine(
        project.ProjectDirectory,
        "Note pièces jointes.md");

    await File.WriteAllTextAsync(
        notePath,
        "# Note\n");

    global::Nodalis.Infrastructure.Links.WorkspaceLinkIndexService linkIndex = new WorkspaceLinkIndexService(root);
    await linkIndex.RefreshAsync();

    string externalRoot = Path.Combine(
        Path.GetTempPath(),
        "Nodalis-Attachment-Source",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        externalRoot);

    string sourcePath = Path.Combine(
        externalRoot,
        "specification.bin");

    byte[] sourceBytes = Enumerable
        .Range(0, 256)
        .Select(value => (byte)value)
        .ToArray();

    await File.WriteAllBytesAsync(
        sourcePath,
        sourceBytes);

    try
    {
        global::Nodalis.Infrastructure.Attachments.AttachmentService attachments = new AttachmentService(root);

        global::Nodalis.Core.Attachments.AttachmentReference copied = await attachments.CopyIntoWorkspaceAsync(
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

        global::Nodalis.Core.Attachments.AttachmentReference resolvedCopy = attachments.Resolve(
            copied.MarkdownTarget,
            notePath);

        Assert(
            resolvedCopy.Exists &&
            string.Equals(
                resolvedCopy.FullPath,
                copied.FullPath,
                StringComparison.OrdinalIgnoreCase),
            "Relative attachment targets must resolve from their owner document.");

        global::Nodalis.Core.Attachments.AttachmentReference external = attachments.CreateExternalReference(
            sourcePath,
            notePath);

        Assert(
            external.StorageMode == AttachmentStorageMode.ExternalReference &&
            Path.IsPathRooted(external.FullPath) &&
            external.Exists,
            "External attachments must keep an explicit absolute local reference.");

        global::Nodalis.Core.Attachments.AttachmentReference resolvedExternal = attachments.Resolve(
            external.MarkdownTarget,
            notePath);

        Assert(
            resolvedExternal.Exists &&
            resolvedExternal.StorageMode == AttachmentStorageMode.ExternalReference,
            "External local references must resolve without copying the file.");

        File.Delete(
            copied.FullPath);

        global::Nodalis.Core.Attachments.AttachmentReference missing = attachments.Resolve(
            copied.MarkdownTarget,
            notePath);

        Assert(
            !missing.Exists,
            "Deleted attachment files must be detected as missing.");

        global::Nodalis.Core.Attachments.AttachmentReference secondCopy = await attachments.CopyIntoWorkspaceAsync(
            sourcePath,
            notePath);

        global::Nodalis.Core.Attachments.AttachmentReference thirdCopy = await attachments.CopyIntoWorkspaceAsync(
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Tâches");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator projectCreator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await projectCreator.CreateAsync(
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

    string meetingPath = Path.Combine(
        project.ProjectDirectory,
        "Réunion tâches.md");

    await File.WriteAllTextAsync(
        meetingPath,
        "# Réunion\n\n" +
        "- [ ] Préparer recette | Responsable: Alice | Échéance: 2026-10-10\n" +
        "- [x] Action déjà terminée\n");

    string applicationTaskPath = Path.Combine(
        applicationPath,
        "Action application.md");

    await File.WriteAllTextAsync(
        applicationTaskPath,
        "- [ ] Vérifier application | Owner: Bob | Due: 2026-11-01\n");

    string globalTaskPath = Path.Combine(
        root,
        "Action globale.md");

    await File.WriteAllTextAsync(
        globalTaskPath,
        "- [ ] Action globale\n");

    global::Nodalis.Infrastructure.Tasks.WorkspaceTaskService service = new WorkspaceTaskService(root);

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> projectTasks = await service.GetTasksAsync(
        project.ProjectDirectory);

    Assert(
        projectTasks.Count == 1 &&
        projectTasks[0].Text == "Préparer recette" &&
        projectTasks[0].Owner == "Alice" &&
        projectTasks[0].DueDate == new DateOnly(2026, 10, 10) &&
        projectTasks[0].ProjectId == project.Project.Id,
        "Project task view must extract open checkboxes, metadata and project context.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> applicationTasks = await service.GetTasksAsync(
        applicationPath);

    Assert(
        applicationTasks.Count == 2 &&
        applicationTasks.Any(task =>
            task.Text == "Préparer recette") &&
        applicationTasks.Any(task =>
            task.Text == "Vérifier application"),
        "Application task view must include application-level and project tasks.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> globalTasks = await service.GetTasksAsync(
        root);

    Assert(
        globalTasks.Count >= 3 &&
        globalTasks.Any(task =>
            task.Text == "Action globale"),
        "Global task view must include tasks from every scope.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> allProjectTasks = await service.GetTasksAsync(
        project.ProjectDirectory,
        includeCompleted: true);

    Assert(
        allProjectTasks.Count == 2 &&
        allProjectTasks.Any(task =>
            task.IsCompleted &&
            task.Text == "Action déjà terminée"),
        "Completed tasks must be available when explicitly requested.");

    global::Nodalis.Core.Tasks.TaskItem taskToMove = projectTasks.Single();

    string originalContent = await File.ReadAllTextAsync(
        meetingPath);

    await File.WriteAllTextAsync(
        meetingPath,
        "Ligne ajoutée avant\n" + originalContent);

    await service.SetCompletedAsync(
        taskToMove,
        completed: true);

    string updatedContent = await File.ReadAllTextAsync(
        meetingPath);

    Assert(
        updatedContent.Contains(
            "- [x] Préparer recette | Responsable: Alice | Échéance: 2026-10-10",
            StringComparison.Ordinal),
        "Task toggle must relocate a uniquely moved source line safely.");

    string ambiguousPath = Path.Combine(
        project.ProjectDirectory,
        "Tâches ambiguës.md");

    const string duplicateTask =
        "- [ ] Même tâche\n";

    await File.WriteAllTextAsync(
        ambiguousPath,
        duplicateTask + duplicateTask);

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> refreshed = await service.GetTasksAsync(
        project.ProjectDirectory);

    global::Nodalis.Core.Tasks.TaskItem ambiguous = refreshed.First(task =>
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Jalons");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator projectCreator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await projectCreator.CreateAsync(
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

    global::Nodalis.Infrastructure.Milestones.WorkspaceMilestoneService service = new WorkspaceMilestoneService(root);

    global::Nodalis.Core.Milestones.MilestoneItem first = await service.AddAsync(
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

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> projectMilestones = await service.GetMilestonesAsync(
        project.ProjectDirectory);

    Assert(
        projectMilestones.Count == 3 &&
        projectMilestones.Any(item =>
            item.Name == "Recette longue" &&
            item.TargetDate == new DateOnly(2026, 10, 10) &&
            item.Description == "Passage A | puis B" &&
            item.Link == "outil://jalon/123"),
        "Milestone tables must persist dates, description, links and escaped pipes.");

    global::Nodalis.Core.Milestones.MilestoneItem updated = await service.UpdateAsync(
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

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> upcoming = await service.GetUpcomingAsync(
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

    string sourcePath = Path.Combine(
        root,
        updated.SourceRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));

    string current = await File.ReadAllTextAsync(
        sourcePath);

    await File.WriteAllTextAsync(
        sourcePath,
        "Note avant la table\n" + current);

    global::Nodalis.Core.Milestones.MilestoneItem relocated = await service.UpdateAsync(
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

    string duplicateRow = relocated.RawLine;
    string duplicatedContent = await File.ReadAllTextAsync(
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Réunions");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
        ?? throw new InvalidDataException(
            "Meeting smoke test application manifest is invalid.");

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await creator.CreateAsync(
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

    global::Nodalis.Infrastructure.Meetings.WorkspaceMeetingService service = new WorkspaceMeetingService(root);
    global::Nodalis.Core.Meetings.MeetingCreationResult result = await service.CreateAsync(
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

    string content = await File.ReadAllTextAsync(
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

    (string ScopeKind, string ScopeName)? scope = await service.ResolveScopeAsync(
        result.FilePath);

    Assert(
        scope is not null &&
        scope.Value.ScopeKind == "Projet" &&
        scope.Value.ScopeName == "Projet Réunions",
        "Meeting context resolution must keep the meeting attached to its project.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Tasks.TaskItem> tasks = await new WorkspaceTaskService(root)
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
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Décisions");

    string applicationJson = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationJson,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
        ?? throw new InvalidDataException(
            "Decision smoke test application manifest is invalid.");

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await creator.CreateAsync(
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

    global::Nodalis.Infrastructure.Meetings.WorkspaceMeetingService meetingService = new WorkspaceMeetingService(root);
    global::Nodalis.Core.Meetings.MeetingCreationResult meeting = await meetingService.CreateAsync(
        project.ProjectDirectory,
        new MeetingDraft
        {
            Title = "Comité architecture",
            Date = new DateOnly(2026, 10, 5),
            Decisions =
                "Adopter PostgreSQL pour le référentiel\n" +
                "Geler l'API publique avant la recette"
        });

    global::Nodalis.Infrastructure.Decisions.WorkspaceDecisionService decisionService = new WorkspaceDecisionService(root);
    global::System.Collections.Generic.IReadOnlyList<string> candidates = await decisionService.ExtractDecisionCandidatesAsync(
        meeting.FilePath);

    Assert(
        candidates.Count == 2 &&
        candidates.Contains(
            "Adopter PostgreSQL pour le référentiel",
            StringComparer.CurrentCultureIgnoreCase),
        "Decision service must extract explicit decisions from a meeting section.");

    global::Nodalis.Core.Links.LinkIndexCatalog linksBefore = await new WorkspaceLinkIndexService(root)
        .RefreshAsync();

    string meetingRelative = Path.GetRelativePath(
            root,
            meeting.FilePath)
        .Replace(
            Path.DirectorySeparatorChar,
            '/');

    global::Nodalis.Core.Links.LinkTargetEntry meetingTarget = linksBefore.Targets.Single(target =>
        target.Kind == LinkTargetKind.Document &&
        string.Equals(
            target.RelativePath,
            meetingRelative,
            StringComparison.OrdinalIgnoreCase));

    global::Nodalis.Core.Decisions.DecisionCreationResult created = await decisionService.CreateAsync(
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

    string content = await File.ReadAllTextAsync(
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

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Decisions.DecisionRecord> listed = await decisionService.GetDecisionsAsync(
        project.ProjectDirectory);

    Assert(
        listed.Count == 1 &&
        listed[0].Title == "Référentiel PostgreSQL" &&
        listed[0].Status == "Actée",
        "Project decision view must list persisted Decision Records.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Decisions.DecisionRecord> searched = await decisionService.SearchAsync(
        project.ProjectDirectory,
        "PostgreSQL");

    Assert(
        searched.Count == 1,
        "Decision search must find text across Decision Record content.");

    global::Nodalis.Infrastructure.Links.WorkspaceLinkIndexService linkService = new WorkspaceLinkIndexService(root);
    global::Nodalis.Core.Links.LinkIndexCatalog catalogWithDecision = await linkService.RefreshAsync();
    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Links.BacklinkEntry> backlinks = await linkService.GetBacklinksAsync(
        meetingTarget.Id);

    Assert(
        backlinks.Any(backlink =>
            backlink.Source.RelativePath.EndsWith(
                Path.GetFileName(created.FilePath),
                StringComparison.OrdinalIgnoreCase)),
        "A Decision Record must create a backlink on its source document.");

    string renamedMeeting = await new DocumentStructureService(root)
        .RenameAsync(
            meeting.FilePath,
            "Comité architecture renommé");

    global::Nodalis.Core.Links.LinkIndexCatalog afterRename = await linkService.RefreshAsync();
    global::Nodalis.Core.Links.LinkResolution oldReferenceResolution = WorkspaceLinkIndexService.Resolve(
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
    string sourceDirectory = Path.Combine(
        Path.GetTempPath(),
        "Nodalis-Docx-Source",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        sourceDirectory);

    try
    {
        string sourcePath = Path.Combine(
            sourceDirectory,
            "specification.docx");

        CreateSyntheticDocx(
            sourcePath);

        byte[] originalBytes = await File.ReadAllBytesAsync(
            sourcePath);
        global::System.DateTime originalWriteTime = File.GetLastWriteTimeUtc(
            sourcePath);

        global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService service = new WorkspaceDocxImportService(root);
        global::Nodalis.Core.Importing.DocxImportResult result = await service.ImportAsync(
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

        byte[] sourceBytesAfterImport = await File.ReadAllBytesAsync(
            sourcePath);

        Assert(
            originalBytes.SequenceEqual(
                sourceBytesAfterImport) &&
            File.GetLastWriteTimeUtc(sourcePath) ==
            originalWriteTime,
            "DOCX import must never modify the original source file.");

        global::Nodalis.Core.Importing.ParsedDocxDocument document = result.Document;

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

        global::Nodalis.Core.Importing.DocxParagraph? linkParagraph = document.Blocks[1].Paragraph;

        Assert(
            linkParagraph is not null &&
            linkParagraph.Text.Contains(
                "documentation externe",
                StringComparison.OrdinalIgnoreCase) &&
            linkParagraph.Hyperlinks.Count == 1 &&
            linkParagraph.Hyperlinks[0].Target ==
            "https://example.test/documentation",
            "DOCX parser must extract hyperlink relationships.");

        global::Nodalis.Core.Importing.DocxParagraph? listParagraph = document.Blocks[2].Paragraph;

        Assert(
            listParagraph is not null &&
            listParagraph.IsListItem &&
            listParagraph.NumberingId == 1 &&
            listParagraph.ListLevel == 0,
            "DOCX parser must expose list numbering metadata.");

        global::Nodalis.Core.Importing.DocxTable? table = document.Blocks[3].Table;

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

        string invalidPath = Path.Combine(
            sourceDirectory,
            "invalide.docx");

        await File.WriteAllTextAsync(
            invalidPath,
            "ceci n'est pas une archive DOCX");

        int copiesBeforeFailure = Directory.GetFiles(
            Path.Combine(
                root,
                WorkspaceLayout.ImportsDirectoryName,
                WorkspaceLayout.ImportSourcesDirectoryName))
            .Length;

        await AssertThrowsAsync<InvalidDataException>(
            () => service.ImportAsync(
                invalidPath),
            "Invalid DOCX files must fail explicitly.");

        int copiesAfterFailure = Directory.GetFiles(
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

    using global::System.IO.Compression.ZipArchive archive = ZipFile.Open(
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
    global::System.IO.Compression.ZipArchiveEntry entry = archive.CreateEntry(
        name,
        CompressionLevel.Fastest);

    using global::System.IO.Stream stream = entry.Open();
    using global::System.IO.StreamWriter writer = new StreamWriter(
        stream,
        new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false));

    writer.Write(
        content.Trim());
}

static async Task VerifyDocxImportAnalysisAsync(string root)
{
    global::Nodalis.Infrastructure.Applications.ApplicationStructureService structure = new ApplicationStructureService(root);
    string applicationPath = await structure.CreateApplicationAsync(
        "Application Import DOCX");

    string applicationManifestText = await File.ReadAllTextAsync(
        Path.Combine(
            applicationPath,
            WorkspaceLayout.ApplicationManifestFileName));

    global::Nodalis.Core.Domain.ApplicationManifest application = JsonSerializer.Deserialize<ApplicationManifest>(
        applicationManifestText,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
        ?? throw new InvalidDataException(
            "DOCX analysis application manifest is invalid.");

    global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
    global::Nodalis.Core.Projects.ProjectCreationResult project = await creator.CreateAsync(
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

    string sourceDirectory = Path.Combine(
        Path.GetTempPath(),
        "Nodalis-Docx-Analysis",
        Guid.NewGuid().ToString("N"));

    Directory.CreateDirectory(
        sourceDirectory);

    try
    {
        string sourcePath = Path.Combine(
            sourceDirectory,
            "SPEC_Application_Import_DOCX.docx");

        CreateSyntheticDocx(
            sourcePath);

        global::Nodalis.Core.Importing.DocxImportResult imported = await new WorkspaceDocxImportService(root)
            .ImportAsync(
                sourcePath);

        global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer analyzer = new DocxImportAnalyzer(root);
        global::Nodalis.Core.Importing.DocxImportAnalysis analysis = await analyzer.AnalyzeAsync(
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

        global::Nodalis.Core.Importing.DocxMappedSection technical = analysis.MappedSections.Single(section =>
            section.TargetSection == "Technique");

        Assert(
            technical.SourceHeading == "Documentation technique" &&
            technical.Blocks.Count == 3 &&
            technical.Blocks.Any(block =>
                block.Kind == DocxBlockKind.Table),
            "Configurable heading rules must map Word content while preserving block order.");

        global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService previewService =
            new WorkspaceDocxImportService(root);

        global::Nodalis.Core.Importing.DocxImportPreview preview = await previewService.PreparePreviewAsync(
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

        global::Nodalis.Core.Importing.DocxImportSectionPreview technicalPreview = preview.Sections.Single(section =>
            section.SuggestedTargetSection ==
            "Technique");

        string manifestPath = Path.Combine(
            project.ProjectDirectory,
            WorkspaceLayout.ProjectManifestFileName);

        string manifestBeforePlan = await File.ReadAllTextAsync(
            manifestPath);

        global::Nodalis.Core.Importing.DocxImportCommitRequest request = new DocxImportCommitRequest
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
                    TargetSection = "Section DOCX"
                }
            ]
        };

        global::Nodalis.Core.Importing.DocxImportPlan plan = await previewService.BuildPlanAsync(
            preview,
            request);

        Assert(
            !plan.CreatesProject &&
            plan.TargetProjectRelativePath.Replace(
                '\\',
                '/')
                .EndsWith(
                    project.Project.Name,
                    StringComparison.CurrentCultureIgnoreCase) &&
            plan.Changes.Any(change =>
                change.Action == "Modifier" &&
                change.RelativePath.EndsWith(
                    WorkspaceLayout.ProjectManifestFileName,
                    StringComparison.OrdinalIgnoreCase)) &&
            plan.Changes.Any(change =>
                change.RelativePath.Contains(
                    "/Section DOCX/",
                    StringComparison.OrdinalIgnoreCase) &&
                change.RelativePath.EndsWith(
                    ".md",
                    StringComparison.OrdinalIgnoreCase)) &&
            plan.Changes.Any(change =>
                change.Action == "Copier" &&
                change.RelativePath.Contains(
                    "/" + WorkspaceLayout.ImportSourcesDirectoryName + "/",
                    StringComparison.OrdinalIgnoreCase)),
            "DOCX plan must expose the exact project metadata, Markdown and source-copy operations before validation.");

        Assert(
            File.Exists(
                preview.StagedImport.StagedCopyPath) &&
            string.Equals(
                manifestBeforePlan,
                await File.ReadAllTextAsync(
                    manifestPath),
                StringComparison.Ordinal) &&
            !Directory.Exists(
                Path.Combine(
                    project.ProjectDirectory,
                    "Section DOCX")),
            "Building a DOCX plan must not perform any business write.");

        global::Nodalis.Core.Importing.DocxImportCommitResult committed = await previewService.CommitAsync(
            preview,
            request);

        Assert(
            !File.Exists(
                preview.StagedImport.StagedCopyPath) &&
            File.Exists(
                committed.SourceCopyPath) &&
            committed.GeneratedFiles.Count == 1 &&
            committed.GeneratedFiles[0].Contains(
                Path.Combine(
                    project.ProjectDirectory,
                    "Section DOCX"),
                StringComparison.OrdinalIgnoreCase),
            "Validated DOCX imports must consume the staged copy and honor an explicit section remapping.");

        string importedMarkdown = await File.ReadAllTextAsync(
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

        global::Nodalis.Core.Importing.ParsedDocxDocument proposedDocument = new ParsedDocxDocument
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

        global::Nodalis.Core.Importing.DocxImportAnalysis proposed = await analyzer.AnalyzeAsync(
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

        string otherApplicationPath = await structure.CreateApplicationAsync(
            "Application Import Ambiguë");

        string otherManifestText = await File.ReadAllTextAsync(
            Path.Combine(
                otherApplicationPath,
                WorkspaceLayout.ApplicationManifestFileName));

        global::Nodalis.Core.Domain.ApplicationManifest otherApplication = JsonSerializer.Deserialize<ApplicationManifest>(
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

        global::Nodalis.Core.Importing.DocxImportAnalysis ambiguous = await analyzer.AnalyzeAsync(
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
    string fence = new string((char)96, 3);
    string markdown =
        "# Titre\n\n" +
        "Texte **gras** et *italique* avec [[Projet Patate|le projet]].\n\n" +
        "- [x] Action terminée\n" +
        "- Élément\n" +
        "1. Premier\n" +
        "2. Deuxième\n" +
        "3. Troisième\n\n" +
        "> Citation\n\n" +
        "| Col A | Col B |\n" +
        "| --- | --- |\n" +
        "| A | B |\n\n" +
        fence + "csharp\n" +
        "Console.WriteLine(1);\n" +
        fence + "\n";

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Markdown.MarkdownBlock> blocks = MarkdownDocumentParser.Parse(markdown);

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

    global::Nodalis.Core.Markdown.MarkdownBlock[] orderedItems = blocks
        .Where(block =>
            block.Kind == MarkdownBlockKind.OrderedListItem)
        .ToArray();

    Assert(
        orderedItems.Length == 3 &&
        orderedItems[0].OrderedListNumber == 1 &&
        orderedItems[1].OrderedListNumber == 2 &&
        orderedItems[2].OrderedListNumber == 3,
        "Markdown ordered-list markers must preserve their numeric values.");

    Assert(blocks.Any(block =>
            block.Kind == MarkdownBlockKind.CodeBlock &&
            block.Language == "csharp" &&
            block.Text.Contains("Console.WriteLine", StringComparison.Ordinal)),
        "Fenced code blocks must be parsed.");

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Markdown.MarkdownInline> inlines = MarkdownInlineParser.Parse(
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
    string preferencesPath = Path.Combine(root, "User", "preferences.json");
    global::Nodalis.Infrastructure.Settings.UserPreferencesStore store = new UserPreferencesStore(preferencesPath);

    global::Nodalis.Core.Settings.UserPreferences defaults = await store.LoadAsync();
    Assert(defaults.IsContextPanelOpen,
        "Missing preferences must return safe defaults.");

    global::System.Guid projectId = Guid.NewGuid();
    global::Nodalis.Core.Settings.UserPreferences preferences = defaults with
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
        OpenDocumentTabs =
        [
            new OpenDocumentTabReference
            {
                DocumentId = projectId,
                RelativePath = "Applications/Application A/Notes rapides.md",
                CaretIndex = 42,
                SelectionStart = 40,
                SelectionLength = 2
            }
        ],
        ActiveDocumentTabId = projectId,
        Editor = defaults.Editor with
        {
            FontSize = 100,
            AutosaveDelayMilliseconds = 20,
            SplitMode = EditorSplitMode.Horizontal,
            SplitRatio = 0.95,
            SecondaryDocumentTabId = projectId
        },
        Backup = defaults.Backup with
        {
            AutomaticEnabled = true,
            DestinationDirectory = Path.Combine(root, "Backups"),
            IntervalMinutes = 1,
            RetentionCount = 500
        }
    };

    await store.SaveAsync(preferences);
    global::Nodalis.Core.Settings.UserPreferences loaded = await store.LoadAsync();

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
    Assert(
        loaded.Editor.SplitMode == EditorSplitMode.Horizontal &&
        loaded.Editor.SplitRatio == 0.8 &&
        loaded.Editor.SecondaryDocumentTabId == projectId,
        "Editor split layout must persist locally and normalize its ratio.");
    Assert(
        loaded.Backup.AutomaticEnabled &&
        loaded.Backup.IntervalMinutes == 15 &&
        loaded.Backup.RetentionCount == 100 &&
        loaded.Backup.DestinationDirectory == Path.GetFullPath(Path.Combine(root, "Backups")),
        "Backup preferences must persist and normalize their safe bounds.");
    Assert(loaded.Favorites.Count == 1 && loaded.ExpandedNodeIds.Contains(projectId),
        "Favorites and expanded nodes must survive persistence.");
    Assert(
        loaded.OpenDocumentTabs.Count == 1 &&
        loaded.OpenDocumentTabs[0].DocumentId == projectId &&
        loaded.OpenDocumentTabs[0].CaretIndex == 42 &&
        loaded.OpenDocumentTabs[0].SelectionStart == 40 &&
        loaded.OpenDocumentTabs[0].SelectionLength == 2 &&
        loaded.ActiveDocumentTabId == projectId,
        "Open document tabs and their editor positions must survive preference persistence.");

    await File.WriteAllTextAsync(preferencesPath, "{ definitely not valid json");
    global::Nodalis.Core.Settings.UserPreferences recovered = await store.LoadAsync();

    Assert(recovered.WorkspaceRootPath is null &&
           recovered.Favorites.Count == 0 &&
           recovered.RecentItems.Count == 0 &&
           recovered.NavigationPanelWidth == 280 &&
           recovered.ContextPanelWidth == 300,
        "Corrupted preferences must safely return defaults.");
}

static async Task VerifyDocumentReliabilityAsync(string root)
{
    string documentPath = Path.Combine(root, "Documents", "note.md");
    await AtomicFileWriter.WriteAllTextAsync(documentPath, "# Version 1\n");

    global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(documentPath);
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

    await using global::Nodalis.Infrastructure.Reliability.DocumentAutosaveController autosave = new DocumentAutosaveController(
        session,
        TimeSpan.FromSeconds(30));

    autosave.Schedule("# Autosaved\n");
    await autosave.FlushAsync();

    Assert(await File.ReadAllTextAsync(documentPath) == "# Autosaved\n",
        "Flushing autosave must persist pending content.");

    bool conflictRaised = false;
    autosave.ConflictDetected += (_, _) => conflictRaised = true;

    await File.WriteAllTextAsync(documentPath, "# External again\n");
    autosave.Schedule("# Pending local edit\n");
    await autosave.FlushAsync();

    Assert(conflictRaised,
        "Autosave must surface an external modification conflict.");
    Assert(await File.ReadAllTextAsync(documentPath) == "# External again\n",
        "Autosave conflicts must never overwrite external changes.");

    string[] temporaryFiles = Directory
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
    global::Nodalis.Core.Domain.WorkspaceCatalog catalog = new WorkspaceCatalog();

    global::Nodalis.Core.Domain.ApplicationManifest application = catalog.CreateApplication("Application A");
    global::Nodalis.Core.Domain.ModuleManifest module = catalog.CreateModule(application.Id, "Module A1");

    AssertThrows<DomainValidationException>(
        () => catalog.MoveModule(module.Id, module.Id),
        "A module cannot become its own parent.");

    global::Nodalis.Core.Domain.ProjectManifest project = catalog.CreateProject(
        application.Id,
        "Projet Patate",
        ProjectComplexity.Simple,
        module.Id);

    global::Nodalis.Core.Domain.SectionManifest glossary = catalog.AddSection(
        project.Id,
        "Glossaire",
        isSingleton: true,
        templateKey: "glossary");

    catalog.AddSection(project.Id, "Technique", isSingleton: false);

    AssertThrows<DomainValidationException>(
        () => catalog.AddSection(project.Id, "Glossaire", isSingleton: true),
        "A singleton section cannot be duplicated.");

    catalog.RenameSection(project.Id, glossary.Id, "Glossaire projet");

    global::Nodalis.Core.Domain.ProjectManifest child = catalog.CreateProject(
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

    foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode child in node.Children)
    {
        foreach (global::Nodalis.Core.Navigation.WorkspaceNavigationNode descendant in DescendantsAndSelf(child))
        {
            yield return descendant;
        }
    }
}


static void VerifyMarkdownOutlineParser()
{
    string markdown =
        "# Introduction\r\n" +
        "## Détails ##\r\n" +
        "### Même titre\r\n" +
        "## Même titre\r\n" +
        "\u0060\u0060\u0060markdown\r\n" +
        "# Ignoré dans le code\r\n" +
        "\u0060\u0060\u0060\r\n" +
        "###### Section profonde\r\n";

    global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Markdown.MarkdownOutlineEntry> outline =
        MarkdownOutlineParser.Parse(markdown);

    Assert(outline.Count == 5,
        "Markdown outline must ignore headings inside fenced code blocks.");

    Assert(
        outline[0].Level == 1 &&
        outline[0].Title == "Introduction" &&
        outline[0].LineNumber == 1,
        "Markdown outline must preserve heading level, title and line number.");

    Assert(
        outline[1].Title == "Détails" &&
        outline[1].LineNumber == 2,
        "Markdown outline must trim optional closing heading markers.");

    Assert(
        outline[2].Title == "Même titre" &&
        outline[3].Title == "Même titre" &&
        outline[2].Offset != outline[3].Offset,
        "Duplicate heading titles must remain independently addressable by source offset.");

    Assert(
        markdown.AsSpan(outline[4].Offset).StartsWith(
            "###### Section profonde",
            StringComparison.Ordinal),
        "Markdown outline offsets must point to the original source heading without rewriting it.");
}
