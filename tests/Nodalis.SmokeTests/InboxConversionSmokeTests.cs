using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Nodalis.Core.Domain;
using Nodalis.Core.Inbox;
using Nodalis.Core.Navigation;
using Nodalis.Infrastructure.Applications;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Navigation;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Tasks;

internal static class InboxConversionSmokeTests
{
    /// <summary>
    /// Exercises four native conversion paths, task indexing, fail-safe
    /// collisions, attachment publication and interruption/retry idempotence.
    /// </summary>
    /// <param name="testRoot">Temporary smoke-test parent.</param>
    /// <returns>Conversion test task.</returns>
    public static async Task RunAsync(string testRoot)
    {
        string root = Path.Combine(testRoot, "inbox-conversion");
        FileSystemWorkspaceStore workspace = new FileSystemWorkspaceStore(root);
        await workspace.InitializeAsync("Inbox conversion demo");
        ApplicationStructureService apps = new ApplicationStructureService(root);
        string app = await apps.CreateApplicationAsync("Application A");

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        ApplicationManifest appManifest = JsonSerializer.Deserialize<ApplicationManifest>(
            await File.ReadAllTextAsync(
                Path.Combine(app, WorkspaceLayout.ApplicationManifestFileName)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        string project = Path.Combine(app, "Projets", "PROJ-001");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(
            Path.Combine(project, WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(new ProjectManifest
            {
                Id = Guid.NewGuid(),
                Name = "PROJ-001",
                ApplicationId = appManifest.Id,
                InitialComplexity = ProjectComplexity.UltraSimple
            }, options));
        string maintenance = Path.Combine(project, "Fiche de Maintenance.md");
        string initialMaintenance = "# Fiche de Maintenance\n\nContenu existant.\n";
        await File.WriteAllTextAsync(maintenance, initialMaintenance);
        await File.WriteAllTextAsync(Path.Combine(project, "Notes rapides.md"),
            "# Notes rapides\n\n");

        WorkspaceInboxService inbox = new WorkspaceInboxService(root);
        WorkspaceInboxConversionService conversion = new WorkspaceInboxConversionService(root);
        string relativeApp = Relative(root, app);
        string relativeProject = Relative(root, project);

        InboxEntry note = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Première idée pour Global.",
            Origin = "capture manuelle"
        });
        InboxConversionRequest quick = new InboxConversionRequest
        {
            EntryId = note.Id,
            Mode = InboxConversionMode.QuickNote,
            DestinationRelativeDirectory = ""
        };
        string notesPath = Path.Combine(root, "Notes rapides.md");
        string beforePreview = await File.ReadAllTextAsync(notesPath);
        string preview = await conversion.PreviewAsync(quick);
        Assert(preview.Contains("Première idée", StringComparison.Ordinal) &&
            beforePreview == await File.ReadAllTextAsync(notesPath),
            "A conversion preview must not mutate any Markdown file.");

        InboxConversionResult quickResult = await conversion.ConvertAsync(quick);
        string afterQuick = await File.ReadAllTextAsync(notesPath);
        InboxConversionResult quickAgain = await conversion.ConvertAsync(quick);
        Assert(quickAgain.AlreadyConverted &&
            afterQuick == await File.ReadAllTextAsync(notesPath) &&
            quickResult.TargetRelativePath == "Notes rapides.md" &&
            afterQuick.Contains("Première idée pour Global.", StringComparison.Ordinal),
            "Quick-note retries may not append a second copy.");

        // Simulate a crash after Markdown publication and before item.json update.
        string itemJson = Path.Combine(root, "Inbox", "Items", note.Id.ToString("N"), "item.json");
        JsonNode node = JsonNode.Parse(await File.ReadAllTextAsync(itemJson))!;
        node["state"] = "pending";
        node["convertedTargetRelativePath"] = null;
        await File.WriteAllTextAsync(itemJson, node.ToJsonString(options));
        InboxConversionResult resumed = await new WorkspaceInboxConversionService(root)
            .ConvertAsync(quick);
        Assert(resumed.AlreadyConverted &&
            (await inbox.ReadAllAsync()).Entries.Single(e => e.Id == note.Id)
                .State == InboxItemState.Processed &&
            afterQuick == await File.ReadAllTextAsync(notesPath),
            "The durable intent and marker must recover interrupted metadata commits.");

        InboxEntry existing = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Compte rendu complémentaire."
        });
        InboxConversionRequest append = new InboxConversionRequest
        {
            EntryId = existing.Id,
            Mode = InboxConversionMode.AppendDocument,
            DestinationRelativeDirectory = relativeProject,
            ExistingDocumentRelativePath = Relative(root, maintenance)
        };
        await conversion.ConvertAsync(append);
        string maintained = await File.ReadAllTextAsync(maintenance);
        await conversion.ConvertAsync(append);
        Assert(maintained.Contains(initialMaintenance, StringComparison.Ordinal) &&
            maintained.Contains("Compte rendu complémentaire.", StringComparison.Ordinal) &&
            maintained == await File.ReadAllTextAsync(maintenance),
            "Existing maintenance sheet is appended to once, never overwritten.");

        InboxEntry newDoc = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Link,
            ContentMarkdown = "https://example.org/requirements"
        });
        InboxConversionRequest newRequest = new InboxConversionRequest
        {
            EntryId = newDoc.Id,
            Mode = InboxConversionMode.NewDocument,
            DestinationRelativeDirectory = relativeApp,
            NewDocumentName = "Spécifications"
        };
        await conversion.ConvertAsync(newRequest);
        string createdPath = Path.Combine(app, "Spécifications.md");
        Assert(File.Exists(createdPath) &&
            (await File.ReadAllTextAsync(createdPath)).Contains(
                "https://example.org/requirements", StringComparison.Ordinal),
            "Created Markdown is visible as a normal document.");
        await conversion.ConvertAsync(newRequest);
        Assert(Directory.GetFiles(app, "Spécifications*.md").Length == 1,
            "New-document retries must not create numbered duplicates.");

        InboxEntry collision = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text, ContentMarkdown = "Ne pas écraser."
        });
        bool collisionRejected = false;
        try
        {
            await conversion.ConvertAsync(newRequest with { EntryId = collision.Id });
        }
        catch (IOException)
        {
            collisionRejected = true;
        }

        Assert(collisionRejected &&
            (await inbox.ReadAllAsync()).Entries.Single(e => e.Id == collision.Id)
                .State == InboxItemState.Pending &&
            !(await File.ReadAllTextAsync(createdPath)).Contains(
                "Ne pas écraser.", StringComparison.Ordinal),
            "Name collision must not overwrite or consume the captured source.");

        InboxEntry task = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Vérifier les sauvegardes\nDétails à étudier."
        });
        InboxConversionRequest taskRequest = new InboxConversionRequest
        {
            EntryId = task.Id,
            Mode = InboxConversionMode.MarkdownTask,
            DestinationRelativeDirectory = relativeProject
        };
        await conversion.ConvertAsync(taskRequest);
        WorkspaceTaskService tasks = new WorkspaceTaskService(root);
        Assert((await tasks.GetTasksAsync(project, includeCompleted: true))
                .Any(item => item.Text.Contains(
                    "Vérifier les sauvegardes", StringComparison.Ordinal)),
            "Converted tasks must appear in the standard Nodalis task index.");
        string taskPath = Path.Combine(project, "Tâches.md");
        string taskSnapshot = await File.ReadAllTextAsync(taskPath);
        await conversion.ConvertAsync(taskRequest);
        Assert(taskSnapshot == await File.ReadAllTextAsync(taskPath),
            "Task conversion must be exactly-once.");

        string asset = Path.Combine(root, "attachment-source.png");
        await File.WriteAllBytesAsync(asset, [1, 7, 9, 12]);
        InboxEntry attachment = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Image,
            ContentMarkdown = "Image pour la fiche.",
            AttachmentSourcePaths = [asset]
        });
        InboxConversionRequest attachmentRequest = append with { EntryId = attachment.Id };
        string capturedFile = Path.Combine(root, "Inbox", "Items",
            attachment.Id.ToString("N"), attachment.Attachments[0].RelativePath
                .Replace('/', Path.DirectorySeparatorChar));
        byte[] capturedBytes = await File.ReadAllBytesAsync(capturedFile);
        File.Delete(capturedFile);
        bool interrupted = false;
        try
        {
            await conversion.ConvertAsync(attachmentRequest);
        }
        catch (IOException)
        {
            interrupted = true;
        }

        Assert(interrupted && File.Exists(Path.Combine(root, "Inbox", "Items",
                attachment.Id.ToString("N"), "conversion.json")) &&
            (await inbox.ReadAllAsync()).Entries.Single(e => e.Id == attachment.Id)
                .State == InboxItemState.Pending,
            "A failed partial conversion must retain a durable recoverable intent.");
        await File.WriteAllBytesAsync(capturedFile, capturedBytes);
        await new WorkspaceInboxConversionService(root).ConvertAsync(attachmentRequest);
        string transferred = Path.Combine(project, "Attachments",
            "Inbox-" + attachment.Id.ToString("N"),
            Path.GetFileName(attachment.Attachments[0].RelativePath));
        Assert(File.Exists(transferred) &&
            (await File.ReadAllBytesAsync(transferred)).SequenceEqual(capturedBytes) &&
            (await File.ReadAllTextAsync(maintenance)).Contains(
                "Image pour la fiche.", StringComparison.Ordinal),
            "Retry after restoring the captured attachment completes with a stable local link.");

        bool escapeRejected = false;
        try
        {
            await conversion.ConvertAsync(taskRequest with
            {
                DestinationRelativeDirectory = "../outside"
            });
        }
        catch (InvalidDataException)
        {
            escapeRejected = true;
        }
        Assert(escapeRejected, "Traversal outside workspace must be denied.");

        WorkspaceNavigationBuilder builder = new WorkspaceNavigationBuilder();
        WorkspaceNavigationNode navigation = await builder.BuildAsync(root);
        Assert(navigation.Children.Any() &&
            File.Exists(createdPath) && File.Exists(taskPath),
            "Conversion outputs remain in normal workspace navigation storage.");
    }

    /// <summary>Converts a full path into its workspace-relative representation.</summary>
    /// <param name="root">Workspace root.</param>
    /// <param name="path">File or directory.</param>
    /// <returns>Forward-slash relative path.</returns>
    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Fails on invariant violation.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
