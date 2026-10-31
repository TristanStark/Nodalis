using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Protects the v2 workspace template editor against retroactive edits,
/// cross-workspace mutations and lost updates from external editors.
/// </summary>
internal static class ProjectJourneyTemplatesSmokeTests
{
    /// <summary>Executes the full catalog editing, opt-in and persistence scenario.</summary>
    /// <param name="root">Disposable smoke-test root.</param>
    /// <returns>Completed regression.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Journey-template-editor-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Parcours v2 · modèles");
        try
        {
            ProjectJourneyService service = new(workspace);
            string catalogPath = Path.Combine(workspace,
                WorkspaceLayout.TemplatesDirectoryName,
                ProjectJourneyService.TemplateCatalogFileName);

            ProjectJourneyTemplateSnapshot baseline =
                await service.ReadTemplatesSnapshotAsync();
            Assert(baseline.Sha256 is null &&
                baseline.Catalog.Templates.Count == 3 &&
                !File.Exists(catalogPath),
                "Previewing built-in templates must not create a workspace override.");
            ProjectJourneyTemplate originalComplex =
                baseline.Catalog.Templates.Single(item =>
                    item.Complexity == ProjectComplexity.Complex);

            Guid applicationId = Guid.NewGuid();
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Test");
            Directory.CreateDirectory(application);
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult complexProject = await creator.CreateAsync(
                CreateProject("Complexe existant", ProjectComplexity.Complex,
                    applicationId, application));
            ProjectJourneySnapshot frozen = await service.InitializeAsync(
                complexProject.ProjectDirectory);
            string originalJourneyJson = await File.ReadAllTextAsync(
                Path.Combine(complexProject.ProjectDirectory,
                    ProjectJourneyService.JourneyFileName));
            string originalOverview =
                await File.ReadAllTextAsync(complexProject.OverviewFilePath);

            // A custom Ultra-simple model is merely offered; it is never
            // activated by the regular no-argument initializer.
            ProjectJourneyTemplateCatalog changed =
                ProjectJourneyTemplateEditor.AddTemplate(
                    baseline.Catalog, "custom-ultra", "Suivi ultra personnalisé",
                    ProjectComplexity.UltraSimple);
            changed = ProjectJourneyTemplateEditor.AddStep(
                changed, "custom-ultra", "cadrage-custom", "Cadrage libre");
            changed = ProjectJourneyTemplateEditor.AddStep(
                changed, "custom-ultra", "validation-custom", "Validation libre");
            changed = ProjectJourneyTemplateEditor.AddOperation(
                changed, "custom-ultra", "cadrage-custom",
                "review-custom", "Revoir le périmètre");
            changed = ProjectJourneyTemplateEditor.AddOperation(
                changed, "custom-ultra", "cadrage-custom",
                "proof-custom", "Vérifier les preuves");
            changed = ProjectJourneyTemplateEditor.SetPromptKey(
                changed, "custom-ultra", "cadrage-custom",
                "proof-custom", "review-proofs");
            changed = ProjectJourneyTemplateEditor.MoveOperation(
                changed, "custom-ultra", "cadrage-custom", "proof-custom", -1);
            changed = ProjectJourneyTemplateEditor.MoveStep(
                changed, "custom-ultra", "validation-custom", -1);
            changed = ProjectJourneyTemplateEditor.RenameStep(
                changed, "custom-ultra", "validation-custom", "Vérification finale");
            changed = ProjectJourneyTemplateEditor.RenameTemplate(
                changed, "custom-ultra", "Suivi court personnalisé");

            Assert(!baseline.Catalog.Templates.Any(item =>
                    item.Key == "custom-ultra") &&
                changed.Templates.Single(item =>
                    item.Key == "custom-ultra").Steps[0].Key ==
                    "validation-custom" &&
                originalComplex.Steps.Count == 9,
                "Editor transforms must not mutate default catalogs or stage keys.");

            await RejectAsync(() => Task.FromResult(
                ProjectJourneyTemplateEditor.AddStep(
                    changed, "custom-ultra", "cadrage-custom", "Doublon")),
                "Duplicate stable stage keys must be rejected.");
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyTemplateEditor.AddTemplate(
                    changed, "clé accentuée", "Invalide", ProjectComplexity.Simple)),
                "Template keys must be portable ASCII slugs.");
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyTemplateEditor.SetPromptKey(
                    changed, "custom-ultra", "cadrage-custom",
                    "proof-custom", "../unsafe")),
                "Unsafe prompt references must be rejected.");

            Assert(!File.Exists(catalogPath),
                "In-memory editing must not create a template file.");
            string firstSha = await service.SaveTemplatesAsync(
                changed, baseline.Sha256);
            Assert(File.Exists(catalogPath) && firstSha.Length == 64,
                "An explicit initial catalog save must create one versioned file.");

            ProjectJourneyTemplateSnapshot restarted =
                await new ProjectJourneyService(workspace)
                    .ReadTemplatesSnapshotAsync();
            Assert(restarted.Sha256 == firstSha &&
                restarted.Catalog.Templates.Single(item =>
                    item.Key == "custom-ultra").Steps[1].Operations[0]
                    .PromptKey == "review-proofs",
                "Reloaded templates must retain stage order, operation order and prompt keys.");

            ProjectCreationResult ultraProject = await creator.CreateAsync(
                CreateProject("Ultra avec opt-in", ProjectComplexity.UltraSimple,
                    applicationId, application));
            Assert(await service.PreviewDefaultAsync(
                    ultraProject.ProjectDirectory) is null &&
                (await service.ListApplicableTemplatesAsync(
                    ultraProject.ProjectDirectory)).Count == 1,
                "Ultra-simple must require an explicit template selection.");
            await RejectAsync(() => service.InitializeAsync(
                ultraProject.ProjectDirectory),
                "Ultra-simple default initialization must remain forbidden.");
            ProjectJourneySnapshot opted = await service.InitializeAsync(
                ultraProject.ProjectDirectory, "custom-ultra");
            Assert(opted.Document.Steps.Count == 2 &&
                opted.Document.TemplateKey == "custom-ultra" &&
                opted.Document.Steps[1].Operations[0].PromptKey ==
                    "review-proofs" &&
                opted.Document.Steps[1].Operations[0].TemplateKey ==
                    "proof-custom",
                "Explicit Ultra-simple creation must freeze the selected template.");

            ProjectJourneyTemplateCatalog renamed =
                ProjectJourneyTemplateEditor.RenameOperation(
                    restarted.Catalog, "custom-ultra", "cadrage-custom",
                    "proof-custom", "Revoir les justificatifs");
            renamed = ProjectJourneyTemplateEditor.RemoveOperation(
                renamed, "custom-ultra", "cadrage-custom", "review-custom");
            string secondSha = await service.SaveTemplatesAsync(
                renamed, restarted.Sha256);
            Assert(secondSha != firstSha,
                "Every template change must create a new revision.");
            Assert(await File.ReadAllTextAsync(
                    Path.Combine(complexProject.ProjectDirectory,
                        ProjectJourneyService.JourneyFileName)) ==
                    originalJourneyJson &&
                await File.ReadAllTextAsync(complexProject.OverviewFilePath) ==
                    originalOverview &&
                (await service.ReadAsync(ultraProject.ProjectDirectory))!
                    .Document.Steps[1].Operations.Count == 2 &&
                (await service.ReadAsync(complexProject.ProjectDirectory))!
                    .Document.Steps.Count == frozen.Document.Steps.Count,
                "Editing templates must never retroactively rewrite materialized journeys.");

            await RejectAsync(() => service.SaveTemplatesAsync(
                changed, firstSha),
                "A stale template revision must not overwrite newer catalog bytes.");
            await RejectAsync(() => service.SaveTemplatesAsync(
                changed, null),
                "A second initial-save call cannot overwrite an existing override.");
            Assert((await service.ReadTemplatesSnapshotAsync()).Sha256 == secondSha,
                "Rejected stale writes must not mutate the current template catalog.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Creates a project in an isolated smoke-test application.</summary>
    /// <param name="name">Project name.</param>
    /// <param name="complexity">Project complexity.</param>
    /// <param name="applicationId">Application stable ID.</param>
    /// <param name="applicationDirectory">Application directory.</param>
    /// <returns>Project creation request.</returns>
    private static ProjectCreationRequest CreateProject(
        string name, ProjectComplexity complexity, Guid applicationId,
        string applicationDirectory) => new()
    {
        Name = name,
        Complexity = complexity,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Test",
            ParentDirectory = applicationDirectory,
            DisplayName = "Test"
        }
    };

    /// <summary>Runs an operation expected to refuse invalid or stale input.</summary>
    /// <param name="action">Rejected action.</param>
    /// <param name="message">Failure description.</param>
    /// <returns>Completed negative assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool denied = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidDataException or InvalidOperationException or
            KeyNotFoundException or IOException)
        {
            denied = true;
        }

        Assert(denied, message);
    }

    /// <summary>Raises a readable smoke failure.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure label.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
