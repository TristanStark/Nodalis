using Nodalis.Core.Backups;
using Nodalis.Core.Domain;
using Nodalis.Core.Inbox;
using Nodalis.Core.Navigation;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Backups;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Search;
using Nodalis.Infrastructure.Tasks;

/// <summary>
/// Qualification 1.1 for the same Small/Medium/Large synthetic workspace
/// fixtures used by the 1.0 navigation/search/link regression tests.
/// </summary>
internal static class InboxReleaseQualificationSmokeTests
{
    /// <summary>
    /// Exercises capture, duplicate warning, native conversion, trash, archive,
    /// backup, restore and legacy data on a real generated fixture workspace.
    /// </summary>
    /// <param name="workspaceRoot">Generated fixture workspace.</param>
    /// <param name="applicationPath">Existing application folder.</param>
    /// <param name="projectPath">Existing simple project folder.</param>
    /// <param name="size">Small, Medium or Large fixture name.</param>
    /// <returns>A qualification task.</returns>
    public static async Task RunAsync(
        string workspaceRoot, string applicationPath,
        string projectPath, string size)
    {
        string backupDirectory = workspaceRoot + "-InboxReleaseBackups";
        string restoredDirectory = workspaceRoot + "-InboxReleaseRestored";
        try
        {
            string globalNotesPath = Path.Combine(
                workspaceRoot, WorkspaceLayout.GlobalQuickNotesFileName);
            string applicationNotesPath = Path.Combine(
                applicationPath, WorkspaceLayout.GlobalQuickNotesFileName);
            string projectNotesPath = Path.Combine(
                projectPath, WorkspaceLayout.GlobalQuickNotesFileName);
            string globalBefore = await File.ReadAllTextAsync(globalNotesPath);
            string appBefore = await File.ReadAllTextAsync(applicationNotesPath);
            string projectBefore = await File.ReadAllTextAsync(projectNotesPath);
            string workspaceManifestPath = Path.Combine(
                workspaceRoot, WorkspaceLayout.WorkspaceManifestFileName);
            byte[] manifestBefore = await File.ReadAllBytesAsync(workspaceManifestPath);
            Assert(!Directory.Exists(Path.Combine(workspaceRoot, "Inbox")),
                size + ": Inbox must not exist before the first explicit capture.");

            WorkspaceInboxService inbox = new WorkspaceInboxService(workspaceRoot);
            WorkspaceInboxConversionService converter =
                new WorkspaceInboxConversionService(workspaceRoot);
            string sentinel = "inbox-release-" + size.ToLowerInvariant();
            InboxCaptureRequest capture = new InboxCaptureRequest
            {
                Kind = InboxCaptureKind.Text,
                ContentMarkdown = "# Capturé\n\n" + sentinel,
                Origin = "synthetic-release-qualification"
            };

            Assert((await inbox.FindProbableDuplicatesAsync(capture)).Count == 0,
                size + ": duplicate preview must not create content.");
            InboxEntry item = await inbox.CaptureAsync(capture);
            Assert((await inbox.FindProbableDuplicatesAsync(capture))
                .Any(candidate => candidate.Id == item.Id),
                size + ": completed capture must be discoverable as a duplicate.");

            string relativeProject = Path.GetRelativePath(
                workspaceRoot, projectPath).Replace('\\', '/');
            InboxConversionRequest conversion = new InboxConversionRequest
            {
                EntryId = item.Id,
                Mode = InboxConversionMode.NewDocument,
                DestinationRelativeDirectory = relativeProject,
                NewDocumentName = "Inbox Qualification.md"
            };
            string preview = await converter.PreviewAsync(conversion);
            string targetPath = Path.Combine(projectPath, "Inbox Qualification.md");
            Assert(preview.Contains(sentinel, StringComparison.Ordinal) &&
                !File.Exists(targetPath),
                size + ": preview must never publish a document.");

            InboxConversionResult converted = await converter.ConvertAsync(conversion);
            string committed = await File.ReadAllTextAsync(targetPath);
            InboxConversionResult retried = await converter.ConvertAsync(conversion);
            Assert(!converted.AlreadyConverted && retried.AlreadyConverted &&
                committed == await File.ReadAllTextAsync(targetPath),
                size + ": re-running conversion must not create a second document.");

            WorkspaceSearchService search = new WorkspaceSearchService();
            global::Nodalis.Core.Search.SearchResultSet found =
                await search.SearchAsync(workspaceRoot, targetPath, sentinel);
            Assert(found.Project.Any(result =>
                    string.Equals(result.FilePath, targetPath,
                        StringComparison.OrdinalIgnoreCase)),
                size + ": the converted document must be searchable using 1.0 search.");

            InboxEntry task = await inbox.CaptureAsync(
                capture with
                {
                    ContentMarkdown = "Relire la livraison Inbox " + size
                });
            await converter.ConvertAsync(new InboxConversionRequest
            {
                EntryId = task.Id,
                Mode = InboxConversionMode.MarkdownTask,
                DestinationRelativeDirectory = relativeProject
            });
            WorkspaceTaskService tasks = new WorkspaceTaskService(workspaceRoot);
            Assert((await tasks.GetTasksAsync(projectPath, includeCompleted: true))
                .Any(result => result.Text.Contains(
                    "Relire la livraison Inbox " + size, StringComparison.Ordinal)),
                size + ": Inbox tasks must remain normal workspace tasks.");

            InboxEntry trash = await inbox.CaptureAsync(
                capture with { ContentMarkdown = "Corbeille " + sentinel });
            await inbox.MoveToTrashAsync(trash.Id);
            Assert((await inbox.ReadTrashAsync()).Entries
                .Any(entry => entry.Id == trash.Id),
                size + ": Inbox trash must retain the original GUID.");
            InboxEntry restoredTrash = await inbox.RestoreAsync(trash.Id);
            Assert(restoredTrash.Id == trash.Id &&
                (await inbox.ReadAllAsync()).Entries.Any(entry => entry.Id == trash.Id),
                size + ": Inbox restore must be non-destructive and stable.");

            await inbox.UpdateStateAsync(task.Id, InboxItemState.Archived);
            Assert((await inbox.ReadAllAsync()).Entries
                .Any(entry => entry.Id == task.Id &&
                    entry.State == InboxItemState.Archived),
                size + ": archive must preserve converted task identity.");

            byte[] manifestAfter = await File.ReadAllBytesAsync(
                workspaceManifestPath);
            string globalAfter = await File.ReadAllTextAsync(globalNotesPath);
            string applicationAfter = await File.ReadAllTextAsync(applicationNotesPath);
            string projectAfter = await File.ReadAllTextAsync(projectNotesPath);
            Assert(globalAfter == globalBefore &&
                applicationAfter == appBefore &&
                projectAfter == projectBefore &&
                manifestBefore.SequenceEqual(manifestAfter),
                size + ": Inbox features must not change 1.0 notes or workspace schema.");

            // The Small profile exercises the 1.0 ultra-simple structure too.
            if (size == "Small")
            {
                await QualifyUltraSimpleAsync(
                    workspaceRoot, applicationPath, sentinel);
            }

            WorkspaceBackupService backups =
                new WorkspaceBackupService(workspaceRoot);
            WorkspaceBackupInfo archive =
                await backups.CreateBackupAsync(backupDirectory, retentionCount: 2);
            Assert(archive.Status == BackupValidationStatus.Valid,
                size + ": backup containing Inbox must validate.");
            string restored = await backups.RestoreBackupAsync(
                archive.ArchivePath, restoredDirectory);
            WorkspaceInboxService restoredInbox =
                new WorkspaceInboxService(restored);
            InboxReadResult restoredEntries = await restoredInbox.ReadAllAsync();
            Assert(restoredEntries.Entries.Any(entry =>
                    entry.Id == item.Id &&
                    entry.ConvertedTargetRelativePath ==
                        converted.TargetRelativePath) &&
                restoredEntries.Entries.Any(entry =>
                    entry.Id == task.Id && entry.State == InboxItemState.Archived) &&
                (await File.ReadAllTextAsync(
                    Path.Combine(restored, converted.TargetRelativePath
                        .Replace('/', Path.DirectorySeparatorChar))))
                    .Contains(sentinel, StringComparison.Ordinal),
                size + ": workspace backup and restore must preserve Inbox and target Markdown.");

            WorkspaceNavigationBuilder builder = new WorkspaceNavigationBuilder();
            WorkspaceNavigationNode nav = await builder.BuildAsync(restored);
            Assert(ContainsMarkdownDocument(nav, "Inbox Qualification.md"),
                size + ": restored converted document must appear in normal navigation.");

            Console.WriteLine("Inbox 1.1 qualification " + size +
                ": capture / convert / task / trash / backup / restore / legacy OK.");
        }
        finally
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }

            if (Directory.Exists(restoredDirectory))
            {
                Directory.Delete(restoredDirectory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Ensures 1.0 ultra-simple projects retain their maintenance file and
    /// only their minimal generated structure while using Inbox 1.1.
    /// </summary>
    /// <param name="root">Fixture workspace root.</param>
    /// <param name="applicationPath">Application directory.</param>
    /// <param name="token">Unique fixture sentinel.</param>
    /// <returns>Qualification task.</returns>
    private static async Task QualifyUltraSimpleAsync(
        string root, string applicationPath, string token)
    {
        string applicationManifest = await File.ReadAllTextAsync(Path.Combine(
            applicationPath, WorkspaceLayout.ApplicationManifestFileName));
        global::Nodalis.Core.Domain.ApplicationManifest application =
            System.Text.Json.JsonSerializer.Deserialize<
                global::Nodalis.Core.Domain.ApplicationManifest>(
                    applicationManifest,
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? throw new InvalidDataException(
                        "Fixture application manifest is unreadable.");

        FileSystemProjectCreator creator = new FileSystemProjectCreator(root);
        ProjectCreationResult ultra = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Ultra Simple Inbox",
                Complexity = ProjectComplexity.UltraSimple,
                Target = new ProjectCreationTarget
                {
                    ApplicationId = application.Id,
                    ApplicationName = application.Name,
                    ParentDirectory = applicationPath,
                    DisplayName = application.Name
                }
            });
        string maintenance = Path.Combine(
            ultra.ProjectDirectory, "Fiche de Maintenance.md");
        string notes = Path.Combine(
            ultra.ProjectDirectory, "Notes rapides.md");
        Assert(File.Exists(maintenance) && File.Exists(notes),
            "Ultra-simple 1.0 projects must still have maintenance and notes.");

        string before = await File.ReadAllTextAsync(maintenance);
        WorkspaceInboxService inbox = new WorkspaceInboxService(root);
        InboxEntry entry = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Maintenance " + token
        });
        WorkspaceInboxConversionService conversion =
            new WorkspaceInboxConversionService(root);
        string scope = Path.GetRelativePath(root, ultra.ProjectDirectory)
            .Replace('\\', '/');
        await conversion.ConvertAsync(new InboxConversionRequest
        {
            EntryId = entry.Id,
            Mode = InboxConversionMode.AppendDocument,
            DestinationRelativeDirectory = scope,
            ExistingDocumentRelativePath = scope + "/Fiche de Maintenance.md"
        });
        string after = await File.ReadAllTextAsync(maintenance);
        Assert(after.Contains(before, StringComparison.Ordinal) &&
            after.Contains("Maintenance " + token, StringComparison.Ordinal),
            "Inbox addition to existing maintenance sheet must retain all 1.0 content.");
    }

    /// <summary>Traverses existing workspace navigation without synthesizing nodes.</summary>
    /// <param name="node">Navigation root.</param>
    /// <param name="name">Expected Markdown filename.</param>
    /// <returns>Whether a native document with that filename exists.</returns>
    private static bool ContainsMarkdownDocument(
        WorkspaceNavigationNode node, string name)
    {
        if (node.Kind == WorkspaceNodeKind.Document &&
            string.Equals(Path.GetFileName(node.FullPath), name,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node.Children.Any(child => ContainsMarkdownDocument(child, name));
    }

    /// <summary>Raises a CI failure on a qualification invariant violation.</summary>
    /// <param name="condition">Expected result.</param>
    /// <param name="message">Diagnostic explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
