using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Verifies safe source references and strictly manual prompt drafts without
/// touching existing project documents, evidence or external files.
/// </summary>
internal static class ProjectJourneyEvidenceSmokeTests
{
    /// <summary>Exercises the evidence/prompt journey contract end to end.</summary>
    /// <param name="root">Disposable test root.</param>
    /// <returns>Completed regression checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Journey-evidence-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Parcours v2 · preuves");
        try
        {
            string application = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Test");
            Directory.CreateDirectory(application);
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Parcours preuves",
                    Complexity = ProjectComplexity.Complex,
                    Target = new ProjectCreationTarget
                    {
                        ApplicationId = Guid.NewGuid(),
                        ApplicationName = "Test",
                        ParentDirectory = application,
                        DisplayName = "Test"
                    }
                });

            ProjectJourneyService service = new(workspace);
            ProjectJourneySnapshot original = await service.InitializeAsync(
                project.ProjectDirectory);
            ProjectJourneyStep stage = original.Document.Steps.Single(item =>
                item.TemplateKey == "tests");
            ProjectJourneyOperation operation = stage.Operations[0];
            Guid originalOperationId = operation.Id;
            string? originalPromptKey = operation.PromptKey;
            string overview = await File.ReadAllTextAsync(project.OverviewFilePath);

            string evidenceDirectory = Path.Combine(project.ProjectDirectory,
                "Tests");
            Directory.CreateDirectory(evidenceDirectory);
            string markdown = Path.Combine(evidenceDirectory, "Preuves.md");
            string evidenceContent = "# Recette\n\n" +
                "Preuve de validation : résultat attendu et vérifié.";
            await File.WriteAllTextAsync(markdown, evidenceContent);
            string binary = Path.Combine(evidenceDirectory, "Image.png");
            await File.WriteAllBytesAsync(binary,
                [0x89, 0x50, 0x4e, 0x47, 0x00, 0x01, 0x02, 0x03]);

            ProjectJourneySourceService sources = new(workspace);
            string source = sources.GetReferenceForExistingFile(markdown);
            string image = sources.GetReferenceForExistingFile(binary);
            Assert(source.Contains("/Tests/Preuves.md", StringComparison.Ordinal) &&
                sources.ResolveExistingFile(source) == Path.GetFullPath(markdown) &&
                sources.IsAvailable(image),
                "Source paths must be portable and resolve inside the workspace.");

            await RejectAsync(() => Task.FromResult(
                sources.GetReferenceForExistingFile(
                    Path.Combine(root, "outside.md"))),
                "Outside files cannot be linked into a workspace journey.");
            foreach (string invalid in new[]
            {
                "../secret.md",
                "/absolute.md",
                "C:/other.md",
                "Applications//Test/file.md",
                "Applications/../secret.md",
                "Applications\\Test\\file.md",
                "Applications/Test/file.md "
            })
            {
                Assert(!ProjectJourneySourcePath.IsSafe(invalid),
                    "Traversal and non-portable paths must be denied: " + invalid);
                await RejectAsync(() => Task.FromResult(
                    ProjectJourneyEditor.AddSource(original.Document,
                        stage.Id, operation.Id, invalid)),
                    "Unsafe source must not enter a journey document.");
                await RejectAsync(() => Task.FromResult(
                    sources.ResolveExistingFile(invalid)),
                    "Unsafe source must never resolve to a file.");
            }

            ProjectJourneyDocument changed = ProjectJourneyEditor.AddSource(
                original.Document, stage.Id, operation.Id, source);
            changed = ProjectJourneyEditor.AddSource(
                changed, stage.Id, operation.Id, image);
            changed = ProjectJourneyEditor.SetPromptKey(
                changed, stage.Id, operation.Id, "evidence-review");
            Assert(operation.SourcePaths.Count == 0 &&
                operation.PromptKey == originalPromptKey &&
                changed.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == originalOperationId)
                    .PromptKey == "evidence-review" &&
                changed.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == originalOperationId)
                    .SourcePaths.Count == 2,
                "Source and prompt commands must not mutate the previous revision.");

            string prompts = Path.Combine(workspace, "_packaged-test-prompts");
            Directory.CreateDirectory(prompts);
            await File.WriteAllTextAsync(
                Path.Combine(prompts, "evidence-review.md"),
                "Examine uniquement les preuves citées et marque les incertitudes.");
            ProjectJourneyPromptPreparationService preparer = new(
                workspace, prompts);
            ProjectJourneyStep selected = changed.Steps.Single(item =>
                item.Id == stage.Id);
            ProjectJourneyOperation withReferences = selected.Operations.Single(
                item => item.Id == originalOperationId);
            string firstJourneyJson = await File.ReadAllTextAsync(
                Path.Combine(project.ProjectDirectory,
                    ProjectJourneyService.JourneyFileName));

            ProjectJourneyPromptDraft draft = await preparer.PrepareAsync(
                selected, withReferences);
            Assert(draft.IncludedSources == 1 &&
                draft.Warnings.Count == 0 &&
                draft.Content.Contains("Examine uniquement les preuves citées",
                    StringComparison.Ordinal) &&
                draft.Content.Contains(evidenceContent, StringComparison.Ordinal) &&
                draft.Content.Contains(image, StringComparison.Ordinal) &&
                !draft.Content.Contains("0x89", StringComparison.Ordinal) &&
                await File.ReadAllTextAsync(Path.Combine(
                    project.ProjectDirectory,
                    ProjectJourneyService.JourneyFileName)) == firstJourneyJson,
                "A draft must reference selected evidence without executing a model " +
                "or writing the journey JSON.");

            string largePath = Path.Combine(evidenceDirectory, "Long.md");
            await File.WriteAllTextAsync(largePath, new string('Z', 16000));
            string longReference = sources.GetReferenceForExistingFile(largePath);
            changed = ProjectJourneyEditor.AddSource(
                changed, stage.Id, operation.Id, longReference);
            selected = changed.Steps.Single(item => item.Id == stage.Id);
            withReferences = selected.Operations.Single(item =>
                item.Id == originalOperationId);
            ProjectJourneyPromptDraft bounded = await preparer.PrepareAsync(
                selected, withReferences);
            Assert(bounded.IncludedSources == 2 &&
                bounded.Warnings.Any(warning => warning.Contains(
                    "tronqué", StringComparison.Ordinal)) &&
                bounded.Content.Length < 50000,
                "Large sources must be read using a bounded prefix and reported.");

            ProjectJourneyDocument unlinked = ProjectJourneyEditor.RemoveSource(
                changed, stage.Id, operation.Id, source);
            await RejectAsync(() => Task.FromResult(
                ProjectJourneyEditor.RemoveSource(
                    unlinked, stage.Id, operation.Id, source)),
                "Missing source references must not be silently unlinked.");
            Assert(File.Exists(markdown) &&
                await File.ReadAllTextAsync(markdown) == evidenceContent &&
                unlinked.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == originalOperationId)
                    .Id == originalOperationId,
                "Unlink must preserve underlying files, identities and contents.");

            ProjectJourneySnapshot saved = await service.SaveAsync(
                project.ProjectDirectory, unlinked, original.Sha256);
            ProjectJourneySnapshot reloaded = (await new ProjectJourneyService(
                workspace).ReadAsync(project.ProjectDirectory))!;
            Assert(saved.Sha256 == reloaded.Sha256 &&
                reloaded.Document.Steps.Single(item => item.Id == stage.Id)
                    .Operations.Single(item => item.Id == originalOperationId)
                    .SourcePaths.SequenceEqual([image, longReference]) &&
                await File.ReadAllTextAsync(project.OverviewFilePath) == overview,
                "Source references must survive restart without rewriting Markdown.");

            await RejectAsync(() => service.SaveAsync(
                project.ProjectDirectory, changed, original.Sha256),
                "Stale evidence editors must never overwrite current references.");

            ProjectJourneyOperation missingPrompt = withReferences with
            {
                PromptKey = "local-prompt-missing",
                SourcePaths = [source, "Applications/No-longer-present.md"]
            };
            ProjectJourneyStep missingStage = selected with
            {
                Operations = selected.Operations.Select(item =>
                    item.Id == missingPrompt.Id ? missingPrompt : item).ToList()
            };
            ProjectJourneyPromptDraft missing = await preparer.PrepareAsync(
                missingStage, missingPrompt);
            Assert(missing.Warnings.Count == 2 &&
                missing.Content.Contains("Prompt local non disponible",
                    StringComparison.Ordinal) &&
                missing.Content.Contains("Source inaccessible",
                    StringComparison.Ordinal),
                "Missing prompts and missing evidence need explicit warnings.");

            await RejectAsync(() => Task.FromResult(
                ProjectJourneyEditor.SetPromptKey(
                    reloaded.Document, stage.Id, operation.Id, "../escape")),
                "Prompt keys must be traversal safe.");
            Assert(await File.ReadAllTextAsync(markdown) == evidenceContent,
                "Prompt generation must never rewrite evidence content.");
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    /// <summary>Asserts rejection of unsafe edits and stale writes.</summary>
    /// <param name="action">Operation expected to fail.</param>
    /// <param name="message">Failure label.</param>
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
            KeyNotFoundException or IOException or UnauthorizedAccessException)
        {
            denied = true;
        }

        Assert(denied, message);
    }

    /// <summary>Reports an invariant violation.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">User-readable failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
