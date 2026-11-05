using System.Security.Cryptography;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Portable, opt-in project journey regression tests on isolated workspaces.</summary>
internal static class ProjectJourneySmokeTests
{
    /// <summary>Qualifies defaults, identity, updates, concurrency and archiving.</summary>
    /// <param name="root">Disposable smoke-test parent directory.</param>
    /// <returns>Completed journey contract qualification.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Project-journey-v2-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Parcours 2.0");
        try
        {
            ProjectJourneyService service = new(workspace);
            ProjectJourneyTemplateCatalog defaults =
                await service.LoadTemplatesAsync();
            string templateFile = Path.Combine(workspace, "Templates",
                ProjectJourneyService.TemplateCatalogFileName);
            Assert(!File.Exists(templateFile) &&
                defaults.Templates.Count == 3 &&
                defaults.Templates.All(item => item.Complexity !=
                    ProjectComplexity.UltraSimple),
                "Reading journey templates must never write an override or impose Ultra-simple.");

            string application = Path.Combine(workspace, "Applications", "Test");
            Directory.CreateDirectory(application);
            Guid applicationId = Guid.NewGuid();
            ProjectCreationTarget target = new()
            {
                ApplicationId = applicationId,
                ApplicationName = "Test",
                ParentDirectory = application,
                DisplayName = "Test"
            };

            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult complex = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Complexe parcours",
                    Complexity = ProjectComplexity.Complex,
                    Target = target
                });
            ProjectCreationResult simple = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Simple parcours",
                    Complexity = ProjectComplexity.Simple,
                    Target = target
                });
            ProjectCreationResult medium = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Moyen parcours",
                    Complexity = ProjectComplexity.Medium,
                    Target = target
                });
            ProjectCreationResult ultra = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Ultra-simple parcours",
                    Complexity = ProjectComplexity.UltraSimple,
                    Target = target
                });

            Assert((await service.PreviewDefaultAsync(complex.ProjectDirectory))
                    ?.Steps.Count == 9 &&
                (await service.PreviewDefaultAsync(simple.ProjectDirectory))
                    ?.Steps.Count == 3 &&
                (await service.PreviewDefaultAsync(medium.ProjectDirectory))
                    ?.Steps.Count == 5 &&
                await service.PreviewDefaultAsync(ultra.ProjectDirectory) is null &&
                await service.ReadAsync(complex.ProjectDirectory) is null &&
                await service.ReadAsync(ultra.ProjectDirectory) is null,
                "Templates are read-only previews and Ultra-simple must remain opt-in.");

            string complexJourney = Path.Combine(complex.ProjectDirectory,
                ProjectJourneyService.JourneyFileName);
            string ultraJourney = Path.Combine(ultra.ProjectDirectory,
                ProjectJourneyService.JourneyFileName);
            Assert(!File.Exists(complexJourney) && !File.Exists(ultraJourney),
                "Project creation and preview must not materialize a journey.");

            await RejectAsync(() => service.InitializeAsync(ultra.ProjectDirectory),
                "Ultra-simple must refuse a default journey.");
            Assert(!File.Exists(ultraJourney),
                "Refused Ultra-simple initialization must not create a file.");

            ProjectJourneySnapshot initialized = await service.InitializeAsync(
                complex.ProjectDirectory);
            Assert(File.Exists(complexJourney) &&
                initialized.Document.ProjectId == complex.Project.Id &&
                initialized.Document.Steps.Count == 9 &&
                initialized.Document.Steps.Select(item => item.Name).SequenceEqual(
                [
                    "Cadrage", "Exigences", "Risques", "Conception",
                    "Préparation développement", "Réalisation et changements",
                    "Tests", "Mise en production", "Clôture"
                ]) &&
                initialized.Document.Steps.All(step =>
                    step.Status == ProjectJourneyStepStatus.Todo),
                "Explicit Complex initialization must create the nine prescribed steps.");

            IReadOnlyList<ProjectJourneyPilotageStage> initialMenu =
                ProjectJourneyPilotageProjection.Build(initialized.Document);
            Assert(initialMenu.Count == 9 &&
                initialMenu.Select(item => item.Name).SequenceEqual(
                [
                    "Cadrage", "Exigences", "Risques", "Conception",
                    "Préparation développement", "Réalisation et changements",
                    "Tests", "Mise en production", "Clôture"
                ]) &&
                initialMenu.SelectMany(stage => stage.Operations).Count() ==
                    initialized.Document.Steps.Sum(
                        stage => stage.Operations.Count) &&
                initialMenu.Select(item => item.Id).SequenceEqual(
                    initialized.Document.Steps.Select(item => item.Id)),
                "Pilotage must display ALL nine 2.0 stages and every " +
                "real operation using stable IDs, without hidden shortcuts.");

            Guid[] identities = initialized.Document.Steps.Select(item => item.Id)
                .Concat(initialized.Document.Steps.SelectMany(
                    item => item.Operations.Select(operation => operation.Id))).ToArray();
            Assert(identities.All(id => id != Guid.Empty) &&
                identities.Distinct().Count() == identities.Length,
                "Stages and operations must have globally distinct stable GUIDs.");

            await RejectAsync(() => service.InitializeAsync(complex.ProjectDirectory),
                "A second initializer must never overwrite a committed journey.");
            ProjectJourneySnapshot reloaded =
                (await new ProjectJourneyService(workspace).ReadAsync(
                    complex.ProjectDirectory))!;
            Assert(reloaded.Sha256 == initialized.Sha256 &&
                reloaded.Document.Steps.Select(item => item.Id).SequenceEqual(
                    initialized.Document.Steps.Select(item => item.Id)),
                "Reloading after restart must keep the same byte revision and stage GUIDs.");

            List<ProjectJourneyStep> changedSteps = reloaded.Document.Steps.Select(step =>
                step.TemplateKey == "risques"
                    ? step with
                    {
                        Status = ProjectJourneyStepStatus.Completed,
                        Order = 15
                    }
                    : step.TemplateKey == "exigences"
                        ? step with { Order = 30 }
                        : step.TemplateKey == "conception"
                            ? step with { Order = 40 }
                            : step).ToList();
            // The proposed order 15 replaces 30 for the Risks stage.
            // Exigences receives 30; the order stays unambiguous.
            ProjectJourneyDocument changed = reloaded.Document with
            {
                Steps = changedSteps
            };
            ProjectJourneySnapshot saved = await service.SaveAsync(
                complex.ProjectDirectory, changed, reloaded.Sha256);
            Assert(saved.Sha256 != reloaded.Sha256 &&
                saved.Document.Steps.Single(step => step.TemplateKey == "risques")
                    .Status == ProjectJourneyStepStatus.Completed &&
                saved.Document.Steps.Select(item => item.Id).SequenceEqual(
                    reloaded.Document.Steps.Select(item => item.Id)),
                "Status and order may change without reallocating any GUID.");

            IReadOnlyList<ProjectJourneyPilotageStage> updatedMenu =
                ProjectJourneyPilotageProjection.Build(saved.Document);
            Assert(updatedMenu.Select(item => item.Order)
                       .SequenceEqual(updatedMenu.Select(item => item.Order)
                           .OrderBy(order => order)) &&
                   updatedMenu.Single(item => item.Name == "Risques")
                       .Status == ProjectJourneyStepStatus.Completed &&
                   updatedMenu.Select(item => item.Id).SequenceEqual(
                       saved.Document.Steps.OrderBy(step => step.Order)
                           .Select(step => step.Id)),
                "Pilotage must reflect customized ordering and statuses, " +
                "not original hard-coded template positions.");

            ProjectJourneyDocument customizedMenu =
                ProjectJourneyEditor.AddStep(
                    saved.Document, "Préparation complémentaire");
            ProjectJourneyStep custom = customizedMenu.Steps.Single(item =>
                item.Name == "Préparation complémentaire");
            customizedMenu = ProjectJourneyEditor.AddOperation(
                customizedMenu, custom.Id, "Copier le prompt métier");
            IReadOnlyList<ProjectJourneyPilotageStage> customMenu =
                ProjectJourneyPilotageProjection.Build(customizedMenu);
            Assert(customMenu.Count == saved.Document.Steps.Count + 1 &&
                   customMenu[0].Order == 0 &&
                   customMenu.Single(item => item.Id == custom.Id)
                       .Operations.Single().Name == "Copier le prompt métier" &&
                   customMenu.SelectMany(item => item.Operations).Count() ==
                       customizedMenu.Steps.Sum(item => item.Operations.Count) &&
                   saved.Document.Steps.All(item => item.Id != custom.Id),
                "Pilotage lists newly customized stages and prompt " +
                "operations without mutating the persisted original.");

            ProjectJourneyDocument negativeOrder = customizedMenu with
            {
                Steps = customizedMenu.Steps.Select((step, index) =>
                    index == 0 ? step with { Order = -1 } : step).ToList()
            };
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyPilotageProjection.Build(negativeOrder)),
                "Pilotage rejects negative technical stage order while " +
                "accepting zero-based numbering from the journey editor.");

            // The WPF Pilotage menu materializes only stage headers on
            // opening. The detached projection must nevertheless contain
            // every real operation of a large, synthetic project.
            ProjectJourneyDocument largeJourney = saved.Document with
            {
                Steps = Enumerable.Range(0, 80).Select(stageIndex =>
                    new ProjectJourneyStep
                    {
                        Id = Guid.NewGuid(),
                        Name = "Phase fictive " + stageIndex,
                        Order = stageIndex,
                        Operations = Enumerable.Range(0, 60)
                            .Select(operationIndex =>
                                new ProjectJourneyOperation
                                {
                                    Id = Guid.NewGuid(),
                                    Name = "Opération fictive " +
                                        operationIndex
                                }).ToList()
                    }).ToList()
            };
            IReadOnlyList<ProjectJourneyPilotageStage> largeMenu =
                ProjectJourneyPilotageProjection.Build(largeJourney);
            Assert(largeMenu.Count == 80 &&
                   largeMenu.Sum(stage => stage.Operations.Count) == 4800 &&
                   largeMenu[0].Order == 0 &&
                   largeMenu[^1].Order == 79 &&
                   largeMenu.SelectMany(stage => stage.Operations)
                       .Select(operation => operation.Id)
                       .Distinct().Count() == 4800,
                "Pilotage must keep all 4,800 real operations accessible, " +
                "without converting them into eager WPF controls.");

            IReadOnlyList<ProjectJourneyPilotageSearchRow> searchable =
                ProjectJourneyPilotageSearch.Index(largeMenu);
            ProjectJourneyPilotageSearchResult firstPage =
                ProjectJourneyPilotageSearch.Find(searchable, "", 250);
            Assert(searchable.Count == 4880 &&
                   firstPage.Rows.Count == 250 &&
                   firstPage.HasMore &&
                   firstPage.TotalIndexed == 4880,
                "Quick search indexes every stage and operation while " +
                "limiting the displayed WPF result set to 250.");
            ProjectJourneyPilotageSearchResult accent =
                ProjectJourneyPilotageSearch.Find(searchable,
                    "operation fictive 59");
            Assert(accent.Rows.Count == 80 && !accent.HasMore &&
                   accent.Rows.All(item => item.OperationId is not null &&
                       item.Title.Contains("Opération fictive 59",
                           StringComparison.Ordinal)) &&
                   accent.TotalIndexed == 4880,
                "French accent-insensitive search must find all exact " +
                "custom operation titles across hidden result pages.");
            ProjectJourneyPilotageSearchResult oneStage =
                ProjectJourneyPilotageSearch.Find(searchable,
                    "Phase fictive 0");
            Assert(oneStage.Rows.Count == 61 &&
                   oneStage.Rows.Single(item =>
                       item.OperationId is null).StepId ==
                       largeMenu[0].Id,
                "Search results must keep stable parent stage identities.");
            ProjectJourneyPilotageSearchResult missing =
                ProjectJourneyPilotageSearch.Find(searchable,
                    "projet-externe-inexistant");
            Assert(missing.Rows.Count == 0 && !missing.HasMore,
                "An unmatched query must not use another project's data.");
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyPilotageSearch.Find(searchable, "", 0)),
                "Invalid search limits must be rejected.");
            ProjectJourneyPilotageSearchResult frtSearch =
                ProjectJourneyPilotageSearch.Find(
                    ProjectJourneyPilotageSearch.Index(initialMenu),
                    "frt100-01");
            Assert(frtSearch.Rows.Any(item => item.OperationId is not null),
                "Pilotage quick search must also find FRT-100 prompt keys.");

            ProjectJourneyDocument ambiguousMenu = saved.Document with
            {
                Steps = [.. saved.Document.Steps, saved.Document.Steps[0]]
            };
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyPilotageProjection.Build(ambiguousMenu)),
                "Pilotage must refuse duplicate step IDs instead of " +
                "displaying shortcuts to an ambiguous project object.");

            await RejectAsync(() => service.SaveAsync(
                    complex.ProjectDirectory, reloaded.Document, reloaded.Sha256),
                "A stale journey revision must not overwrite a newer one.");

            // Even valid JSON modified by an external editor invalidates
            // the optimistic byte revision without risking silent overwrite.
            byte[] savedBytes = await File.ReadAllBytesAsync(complexJourney);
            await File.AppendAllTextAsync(complexJourney, " \n");
            await RejectAsync(() => service.SaveAsync(
                    complex.ProjectDirectory, changed, saved.Sha256),
                "Edits from another process must be protected.");
            Assert((await File.ReadAllTextAsync(complexJourney)).EndsWith(
                    " \n", StringComparison.Ordinal),
                "A denied write must retain external changes.");
            await File.WriteAllBytesAsync(complexJourney, savedBytes);

            ProjectJourneyStep firstStep = saved.Document.Steps[0];
            ProjectJourneyDocument duplicate = changed with
            {
                Steps = changed.Steps.Select(step => step.Id == firstStep.Id
                    ? step with
                    {
                        Operations =
                        [
                            new ProjectJourneyOperation
                            {
                                Id = firstStep.Id,
                                Name = "Collision"
                            }
                        ]
                    }
                    : step).ToList()
            };
            await RejectAsync(() => service.SaveAsync(
                    complex.ProjectDirectory, duplicate, saved.Sha256),
                "Colliding stage and operation GUIDs must fail closed.");
            Assert((await File.ReadAllBytesAsync(complexJourney))
                .SequenceEqual(savedBytes),
                "Invalid revision must not mutate canonical JSON.");

            // Workspace-local template customization is opt-in and versioned.
            await service.SaveTemplatesAsync(defaults, expectedSha256: null);
            byte[] templateBytes = await File.ReadAllBytesAsync(templateFile);
            string templateHash = Convert.ToHexString(SHA256.HashData(templateBytes));
            ProjectJourneyTemplateCatalog customized = defaults with
            {
                Templates =
                [
                    .. defaults.Templates,
                    new ProjectJourneyTemplate
                    {
                        Key = "ultra-optional",
                        Name = "Parcours Ultra-simple optionnel",
                        Complexity = ProjectComplexity.UltraSimple,
                        Steps =
                        [
                            new ProjectJourneyStepTemplate
                            {
                                Key = "bilan", Name = "Bilan", Order = 10
                            }
                        ]
                    }
                ]
            };
            await service.SaveTemplatesAsync(customized, templateHash);
            Assert(await service.PreviewDefaultAsync(ultra.ProjectDirectory) is null,
                "Custom Ultra-simple templates must not become automatic defaults.");
            ProjectJourneySnapshot optedIn = await service.InitializeAsync(
                ultra.ProjectDirectory, "ultra-optional");
            Assert(optedIn.Document.Steps.Count == 1 &&
                File.Exists(ultraJourney),
                "An Ultra-simple journey must be possible only by explicit opt-in.");
            await RejectAsync(() => service.SaveTemplatesAsync(defaults, templateHash),
                "Stale workspace template catalog writes must be refused.");

            // Archive moves all canonical files without changing journey IDs.
            ProjectArchiveService archive = new(workspace);
            string archived = await archive.ArchiveAsync(complex.ProjectDirectory);
            ProjectJourneySnapshot archivedSnapshot =
                (await service.ReadAsync(archived))!;
            Assert(archivedSnapshot.Sha256 == saved.Sha256 &&
                archivedSnapshot.Document.ProjectId == complex.Project.Id,
                "Archived projects must retain a readable, unchanged journey.");
            await RejectAsync(() => service.SaveAsync(
                    archived, changed, archivedSnapshot.Sha256),
                "Archived journeys must be read-only.");
            string restored = await archive.RestoreAsync(archived);
            ProjectJourneySnapshot restoredSnapshot =
                (await service.ReadAsync(restored))!;
            Assert(restoredSnapshot.Sha256 == saved.Sha256 &&
                restoredSnapshot.Document.Steps.Select(step => step.Id).SequenceEqual(
                    saved.Document.Steps.Select(step => step.Id)),
                "Reopening must preserve every stage ID and byte revision.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Expects a safe refusal rather than a successful mutation.</summary>
    /// <param name="operation">Operation which must fail.</param>
    /// <param name="message">Diagnostic if the operation unexpectedly succeeds.</param>
    /// <returns>Completed negative assertion.</returns>
    private static async Task RejectAsync(Func<Task> operation, string message)
    {
        bool rejected = false;
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Signals any violated test invariant.</summary>
    /// <param name="condition">Invariant.</param>
    /// <param name="message">Failure detail.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
