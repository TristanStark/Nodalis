using System.Text.Json;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Persistence;

internal static class InboxPersistenceSmokeTests
{
    /// <summary>Exercises Inbox v1 persistence, interruption recovery and path safety.</summary>
    /// <param name="testRoot">Parent directory for an isolated test workspace.</param>
    /// <returns>A task representing the smoke test.</returns>
    public static async Task RunAsync(string testRoot)
    {
        Assert(InboxCaptureClassifier.ClassifyText(
                "https://example.org/resource?q=1") == InboxCaptureKind.Link &&
            InboxCaptureClassifier.ClassifyText("Texte ordinaire") ==
                InboxCaptureKind.Text &&
            InboxCaptureClassifier.ClassifyText(
                "https://example.org/one\nhttps://example.org/two") ==
                InboxCaptureKind.Text &&
            InboxCaptureClassifier.ClassifyText(
                "file:///C:/Windows/System32/cmd.exe") ==
                InboxCaptureKind.Text,
            "Only a single HTTP(S) URL may be classified as a link.");

        string root = Path.Combine(testRoot, "inbox-persistence");
        FileSystemWorkspaceStore workspace = new FileSystemWorkspaceStore(root);
        await workspace.InitializeAsync("Inbox smoke");

        WorkspaceInboxService inbox = new WorkspaceInboxService(root);
        string inboxRoot = Path.Combine(root, WorkspaceLayout.InboxDirectoryName);
        Assert(!Directory.Exists(inboxRoot),
            "Opening a 1.0 workspace must not force an Inbox migration.");
        InboxReadResult initiallyEmpty = await inbox.ReadAllAsync();
        Assert(initiallyEmpty.Entries.Count == 0 && !Directory.Exists(inboxRoot),
            "Reading an Inbox that never existed must not create files.");

        string quickNotes = Path.Combine(root, WorkspaceLayout.GlobalQuickNotesFileName);
        string oldNotes = await File.ReadAllTextAsync(quickNotes);
        InboxEntry created = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "# Essai\n\nContenu conservé.\n",
            Origin = "manual"
        });
        Assert(created.Id != Guid.Empty &&
            created.State == InboxItemState.Pending &&
            created.SchemaVersion == InboxEntry.CurrentSchemaVersion,
            "New captures must have a stable identifier and versioned metadata.");

        string itemDirectory = Path.Combine(inboxRoot, "Items", created.Id.ToString("N"));
        Assert(File.Exists(Path.Combine(itemDirectory, "item.json")) &&
            File.Exists(Path.Combine(itemDirectory, "capture.md")) &&
            !Directory.Exists(Path.Combine(inboxRoot, ".staging", created.Id.ToString("N"))),
            "A capture must publish complete Markdown/JSON in one directory.");
        Assert(await File.ReadAllTextAsync(quickNotes) == oldNotes,
            "Inbox must not modify existing quick notes.");

        WorkspaceInboxService reopened = new WorkspaceInboxService(root);
        InboxReadResult afterRestart = await reopened.ReadAllAsync();
        Assert(afterRestart.Entries.Single().ContentMarkdown == created.ContentMarkdown,
            "Capture text must survive a restart.");

        await reopened.UpdateStateAsync(
            created.Id, InboxItemState.Processed, "Applications/Example.md");
        InboxReadResult afterUpdate = await inbox.ReadAllAsync();
        Assert(afterUpdate.Entries.Single().State == InboxItemState.Processed &&
            afterUpdate.Entries.Single().ConvertedTargetRelativePath == "Applications/Example.md",
            "State and conversion provenance must be durable.");

        bool escaped = false;
        try
        {
            await inbox.UpdateStateAsync(
                created.Id, InboxItemState.Archived, "../outside.md");
        }
        catch (InvalidDataException)
        {
            escaped = true;
        }

        Assert(escaped, "Workspace-relative targets must reject traversal.");

        string sourceAttachment = Path.Combine(root, "source-image.png");
        await File.WriteAllBytesAsync(sourceAttachment, [1, 2, 3, 4]);
        IReadOnlyList<string> selection = InboxCaptureClassifier.ValidateFiles(
            [sourceAttachment, sourceAttachment]);
        Assert(selection.Count == 1 &&
            InboxCaptureClassifier.FormatFileSummary(selection).Contains(
                "source-image.png", StringComparison.Ordinal),
            "Explicit file selection must remain bounded, unique and readable.");
        bool missingFileRejected = false;
        try
        {
            InboxCaptureClassifier.ValidateFiles([
                Path.Combine(root, "missing-image.png")]);
        }
        catch (IOException)
        {
            missingFileRejected = true;
        }

        Assert(missingFileRejected,
            "Missing file selection must fail before publishing a capture.");
        InboxEntry image = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Image,
            ContentMarkdown = "Image attachée.",
            AttachmentSourcePaths = [sourceAttachment]
        });
        Assert(image.Attachments.Count == 1 &&
            image.Attachments[0].RelativePath.StartsWith(
                "attachments/", StringComparison.Ordinal),
            "Attachments must use generated relative paths, not their original path.");

        string copiedAttachment = Path.Combine(inboxRoot, "Items",
            image.Id.ToString("N"), image.Attachments[0].RelativePath
                .Replace('/', Path.DirectorySeparatorChar));
        Assert(File.Exists(copiedAttachment) &&
            image.Attachments[0].SizeBytes == 4,
            "Explicitly captured attachments must be copied into the workspace.");

        File.Delete(copiedAttachment);
        InboxReadResult missingAttachment = await inbox.ReadAllAsync();
        Assert(missingAttachment.Entries.Count == 2 &&
            missingAttachment.Warnings.Any(warning =>
                warning.Message.Contains("manquante", StringComparison.Ordinal)),
            "A missing attachment must be reported without deleting its Markdown.");

        Guid recoveredId = Guid.NewGuid();
        string stage = Path.Combine(inboxRoot, ".staging", recoveredId.ToString("N"));
        Directory.CreateDirectory(stage);
        await File.WriteAllTextAsync(Path.Combine(stage, "capture.md"), "Recovered capture.");
        InboxEntry recoveredMetadata = created with
        {
            Id = recoveredId,
            State = InboxItemState.Pending,
            ConvertedTargetRelativePath = null
        };
        await File.WriteAllTextAsync(Path.Combine(stage, "item.json"),
            JsonSerializer.Serialize(recoveredMetadata, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        InboxReadResult recovered = await reopened.ReadAllAsync();
        Assert(recovered.Entries.Any(entry =>
                entry.Id == recoveredId && entry.ContentMarkdown == "Recovered capture.") &&
            recovered.Warnings.Any(warning => warning.Message.Contains(
                "récupérée", StringComparison.Ordinal)),
            "Complete staged captures must be published on the next read.");

        Guid corruptId = Guid.NewGuid();
        string corrupt = Path.Combine(inboxRoot, "Items", corruptId.ToString("N"));
        Directory.CreateDirectory(corrupt);
        await File.WriteAllTextAsync(Path.Combine(corrupt, "capture.md"), "Do not lose me.");
        await File.WriteAllTextAsync(Path.Combine(corrupt, "item.json"), "{corrupt");
        Guid partialId = Guid.NewGuid();
        string partial = Path.Combine(inboxRoot, ".staging", partialId.ToString("N"));
        Directory.CreateDirectory(partial);
        await File.WriteAllTextAsync(Path.Combine(partial, "capture.md"), "Partial capture.");
        InboxReadResult quarantined = await reopened.ReadAllAsync();
        string quarantine = Path.Combine(inboxRoot, "Quarantaine");
        Assert(!Directory.Exists(corrupt) && !Directory.Exists(partial) &&
            Directory.EnumerateDirectories(quarantine).Count() >= 2 &&
            quarantined.Warnings.Count(warning => warning.Message.Contains(
                "Quarantaine", StringComparison.Ordinal)) >= 2,
            "Corrupt and incomplete captures must be isolated with diagnostics.");
        Assert(Directory.EnumerateFiles(quarantine, "capture.md",
                SearchOption.AllDirectories).Count() >= 2,
            "Quarantine must preserve readable data for manual recovery.");

        await inbox.MoveToTrashAsync(created.Id);
        InboxReadResult inTrash = await reopened.ReadTrashAsync();
        Assert(inTrash.Entries.Any(entry => entry.Id == created.Id),
            "Recoverable trash items must be discoverable.");
        Assert(!Directory.Exists(itemDirectory) &&
            Directory.Exists(Path.Combine(inboxRoot, "Corbeille", created.Id.ToString("N"))),
            "Deleting a capture must move its entire directory into recoverable trash.");
        InboxEntry restored = await reopened.RestoreAsync(created.Id);
        Assert(restored.Id == created.Id && Directory.Exists(itemDirectory),
            "Restoration must preserve the ID and source content.");

        bool collision = false;
        await inbox.MoveToTrashAsync(created.Id);
        Directory.CreateDirectory(itemDirectory);
        try
        {
            await inbox.RestoreAsync(created.Id);
        }
        catch (IOException)
        {
            collision = true;
        }

        Assert(collision, "Restoration must not overwrite an existing directory.");
    }

    /// <summary>Fails the smoke test when an Inbox invariant does not hold.</summary>
    /// <param name="condition">Assertion result.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
