using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Isolated synthetic journey tests with no real professional content.</summary>
internal static class SfdJourneySmokeTests
{
    /// <summary>
    /// Checks SFD initialization, exact four generations, v2 isolation,
    /// guarded stage edits, bulk source refs and write-revision conflicts.
    /// </summary>
    /// <param name="root">Disposable smoke workspace parent.</param>
    /// <returns>Completed SFD journey regression suite.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-journey-workspace");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync(
            "Fictional SFD journey");
        string app = Path.Combine(workspace, "Applications", "Application test");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Application test",
            ParentDirectory = app,
            DisplayName = "Application test"
        };

        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult sfd = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Example SFD",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        ProjectCreationResult regular = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Classic standard",
                Complexity = ProjectComplexity.Simple,
                Target = target
            });

        SfdJourneyService service = new(workspace);
        ProjectJourneyService standard = new(workspace);
        string sfdPath = Path.Combine(sfd.ProjectDirectory,
            SfdJourneyService.JourneyFileName);
        string classicPath = Path.Combine(sfd.ProjectDirectory,
            ProjectJourneyService.JourneyFileName);
        string classicProjectPath = Path.Combine(regular.ProjectDirectory,
            ProjectJourneyService.JourneyFileName);

        ProjectJourneyDocument preview = await service.PreviewAsync(
            sfd.ProjectDirectory);
        Assert(preview.TemplateKey == SfdJourneyDefaults.TemplateKey &&
            preview.ProjectId == sfd.Project.Id &&
            preview.Steps.Count == 5 &&
            preview.Steps.Select(item => item.Name).SequenceEqual(
            [
                "Processus métiers et activités",
                "Exigences métier",
                "Fonctionnalités",
                "Mise en œuvre fonctionnelle",
                "Pilotage et qualification"
            ]),
            "The SFD journey must propose only the five reference stages.");
        string[] promptKeys = preview.Steps.SelectMany(item => item.Operations)
            .Where(item => item.PromptKey is not null)
            .Select(item => item.PromptKey!).ToArray();
        Assert(promptKeys.SequenceEqual(["sfd-01", "sfd-02", "sfd-03", "sfd-04"]) &&
            preview.Steps.All(item =>
                item.Status == ProjectJourneyStepStatus.Todo) &&
            preview.Steps.Last().Operations.All(item => item.PromptKey is null),
            "The four SFD generation prompts must be distinct from generic pilotage.");
        Assert(!File.Exists(sfdPath) && !File.Exists(classicPath) &&
            !File.Exists(classicProjectPath) &&
            await service.ReadAsync(sfd.ProjectDirectory) is null &&
            await standard.ReadAsync(sfd.ProjectDirectory) is null &&
            await standard.PreviewDefaultAsync(sfd.ProjectDirectory) is null,
            "Preview and read must never initialize either SFD or v2 workflows.");

        ProjectJourneySnapshot initialized = await service.InitializeAsync(
            sfd.ProjectDirectory);
        Assert(File.Exists(sfdPath) && !File.Exists(classicPath) &&
            initialized.Document.ProjectId == sfd.Project.Id &&
            initialized.Document.Steps.Count == 5,
            "Explicit initialization must create SFD only, not the classic journey.");

        bool repeatRefused = false;
        try
        {
            await service.InitializeAsync(sfd.ProjectDirectory);
        }
        catch (IOException)
        {
            repeatRefused = true;
        }

        Assert(repeatRefused,
            "SFD journey initialization must never overwrite an existing file.");

        Guid firstStageId = initialized.Document.Steps[0].Id;
        Guid firstOperationId = initialized.Document.Steps[0].Operations[0].Id;
        string sourceOne = Path.Combine(sfd.ProjectDirectory,
            "Présentation.md");
        if (!File.Exists(sourceOne))
        {
            sourceOne = Path.Combine(sfd.ProjectDirectory,
                "DOCUMENTATION", "liste des règles métiers.md");
        }

        string sourceTwo = Path.Combine(sfd.ProjectDirectory,
            "DOCUMENTATION", "note fictive.txt");
        await File.WriteAllTextAsync(sourceTwo, "Un exemple entièrement fictif.");
        ProjectJourneySourceService sourceResolver = new(workspace);
        string refOne = sourceResolver.GetReferenceForExistingFile(sourceOne);
        string refTwo = sourceResolver.GetReferenceForExistingFile(sourceTwo);

        ProjectJourneyDocument edited = ProjectJourneyEditor.SetStatus(
            initialized.Document, firstStageId, ProjectJourneyStepStatus.InProgress);
        edited = ProjectJourneyEditor.AddSources(edited,
            firstStageId, firstOperationId,
            [refOne, refTwo, refTwo]);
        edited = ProjectJourneyEditor.RenameStep(
            edited, firstStageId, "Étape de cadrage fictive");
        edited = ProjectJourneyEditor.AddOperation(
            edited, firstStageId, "Collecter des preuves fictives");
        ProjectJourneySnapshot committed = await service.SaveAsync(
            sfd.ProjectDirectory, edited, initialized.Sha256);

        ProjectJourneySnapshot loaded = (await service.ReadAsync(
            sfd.ProjectDirectory))!;
        Assert(loaded.Sha256 == committed.Sha256 &&
            loaded.Document.Steps[0].Id == firstStageId &&
            loaded.Document.Steps[0].Operations[0].Id == firstOperationId &&
            loaded.Document.Steps[0].Operations[0].SourcePaths.Count == 2 &&
            loaded.Document.Steps[0].Status == ProjectJourneyStepStatus.InProgress &&
            loaded.Document.Steps[1].Status == ProjectJourneyStepStatus.Todo &&
            loaded.Document.Steps[0].Name == "Étape de cadrage fictive",
            "SFD journey edits must preserve IDs, bulk source links and separate statuses.");

        ProjectJourneyDocument next = ProjectJourneyEditor.SetStatus(
            loaded.Document, firstStageId, ProjectJourneyStepStatus.Completed);
        await service.SaveAsync(sfd.ProjectDirectory, next, loaded.Sha256);
        bool conflictRefused = false;
        try
        {
            await service.SaveAsync(sfd.ProjectDirectory,
                ProjectJourneyEditor.RenameStep(
                    loaded.Document, firstStageId, "Modification périmée"),
                loaded.Sha256);
        }
        catch (InvalidDataException)
        {
            conflictRefused = true;
        }

        Assert(conflictRefused &&
            (await service.ReadAsync(sfd.ProjectDirectory))!
                .Document.Steps[0].Name != "Modification périmée",
            "A stale journey edit must fail rather than overwrite reviewed changes.");

        bool invalidSourceRefused = false;
        try
        {
            ProjectJourneyEditor.AddSources(loaded.Document,
                firstStageId, firstOperationId, ["../outside.txt"]);
        }
        catch (ArgumentException)
        {
            invalidSourceRefused = true;
        }

        Assert(invalidSourceRefused,
            "SFD source picker must reject traversal and outside-workspace references.");

        bool classicRefused = false;
        try
        {
            await service.PreviewAsync(regular.ProjectDirectory);
        }
        catch (InvalidDataException)
        {
            classicRefused = true;
        }

        Assert(classicRefused && !File.Exists(classicProjectPath) &&
            !File.Exists(classicPath),
            "SFD service must reject standard projects and preserve v2 files.");

        ProjectJourneyTemplateCatalog defaults =
            await standard.LoadTemplatesAsync();
        Assert(defaults.Templates.Count == 3 &&
            defaults.Templates.All(template =>
                template.Key != SfdJourneyDefaults.TemplateKey),
            "Classic Simple/Medium/Complex template catalog must remain unchanged.");
    }

    /// <summary>Throws a clear synthetic SFD regression failure.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Description of failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
