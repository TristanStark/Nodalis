using Nodalis.Core.Inbox;
using Nodalis.Core.Maintenance;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Maintenance;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Persistence;

/// <summary>Inbox-to-maintenance tests for origin preservation and interrupted retry safety.</summary>
internal static class MaintenanceInboxSmokeTests
{
    /// <summary>Validates text/image conversion, project isolation, retries and failed writes.</summary>
    /// <param name="root">Isolated existing smoke-test workspace.</param>
    /// <returns>Asynchronous test task.</returns>
    public static async Task RunAsync(string root)
    {
        string project = Path.Combine(root, WorkspaceLayout.ApplicationsDirectoryName,
            "Maintenance-Inbox-App", WorkspaceLayout.ProjectsDirectoryName, "Ultra simple");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName), "{}");
        string sheet = Path.Combine(project, "Fiche de Maintenance.md");
        string original = "# Fiche de Maintenance\r\n\r\n## Notes libres\r\nConserver ces notes.\r\n";
        await File.WriteAllTextAsync(sheet, original);
        string projectRelative = Path.GetRelativePath(root, project).Replace('\\', '/');
        WorkspaceInboxService inbox = new WorkspaceInboxService(root);
        WorkspaceInboxMaintenanceService converter = new WorkspaceInboxMaintenanceService(root);

        InboxEntry text = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Échec du démarrage\n\n### Ceci est un titre dans la capture\n- détail important",
            Origin = "Réunion technique / lien interne"
        });
        MaintenanceInboxRequest firstRequest = new MaintenanceInboxRequest
        {
            EntryId = text.Id,
            ProjectRelativePath = projectRelative,
            Mode = MaintenanceInboxMode.Incident,
            Summary = "Démarrage impossible"
        };
        string preview = await converter.PreviewAsync(firstRequest);
        Assert(preview.Contains(MaintenanceInboxMarkdown.Marker(text.Id),
                StringComparison.Ordinal) &&
               !File.ReadAllText(sheet).Contains(
                   MaintenanceInboxMarkdown.Marker(text.Id), StringComparison.Ordinal),
            "Preview must be pure, showing source provenance without writing.");
        MaintenanceInboxResult created = await converter.ConvertAsync(firstRequest);
        string saved = await File.ReadAllTextAsync(sheet);
        MaintenanceParseResult history = MaintenanceMarkdownCodec.Parse(saved);
        Assert(!created.AlreadyConverted && history.Diagnostics.Count == 0 &&
               history.Records.Count == 1 &&
               history.Records[0].Kind == MaintenanceRecordKind.Incident &&
               history.Records[0].Id ==
                    MaintenanceInboxMarkdown.StableId(text.Id, MaintenanceRecordKind.Incident),
            "Text Inbox capture must create one structured, navigable incident.");
        Assert(saved.Contains("Conserver ces notes.", StringComparison.Ordinal) &&
               saved.Contains("> ### Ceci est un titre dans la capture", StringComparison.Ordinal) &&
               saved.Contains("Réunion technique", StringComparison.Ordinal),
            "All original content must be retained, without introducing false history headings.");
        InboxEntry stillThere = (await inbox.ReadAllAsync()).Entries.Single(e => e.Id == text.Id);
        Assert(stillThere.State == InboxItemState.Processed &&
               stillThere.ConvertedTargetRelativePath == created.TargetRelativePath,
            "Source Inbox capture must remain recoverable and reflect its destination.");
        MaintenanceInboxResult repeat = await converter.ConvertAsync(firstRequest);
        Assert(repeat.AlreadyConverted && await File.ReadAllTextAsync(sheet) == saved,
            "Repeating a completed conversion must not append duplicate blocks.");

        bool refused = false;
        try
        {
            await converter.ConvertAsync(firstRequest with { ProjectRelativePath = "../ailleurs" });
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or DirectoryNotFoundException)
        {
            refused = true;
        }

        Assert(refused && await File.ReadAllTextAsync(sheet) == saved,
            "A retry must not silently diverge to a different project.");

        string photo = Path.Combine(root, "inbox-test-image.png");
        byte[] imageBytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 1, 2, 3];
        await File.WriteAllBytesAsync(photo, imageBytes);
        InboxEntry image = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Image,
            ContentMarkdown = "Photographie d'un écran noir",
            AttachmentSourcePaths = [photo]
        });
        MaintenanceInboxRequest interventionRequest = new MaintenanceInboxRequest
        {
            EntryId = image.Id,
            ProjectRelativePath = projectRelative,
            Mode = MaintenanceInboxMode.Intervention,
            Summary = "Inspection après démarrage bloqué",
            ExistingIncidentId = history.Records[0].Id
        };
        MaintenanceInboxResult interventionResult = await converter.ConvertAsync(interventionRequest);
        string afterImage = await File.ReadAllTextAsync(sheet);
        MaintenanceParseResult history2 = MaintenanceMarkdownCodec.Parse(afterImage);
        Assert(history2.Diagnostics.Count == 0 && history2.Records.Count == 2 &&
               history2.Records.Last().Kind == MaintenanceRecordKind.Intervention &&
               history2.Records.Last().IncidentId == history.Records[0].Id,
            "Image capture must create a linked intervention in the existing history.");
        string attachmentDir = Path.Combine(project, WorkspaceLayout.AttachmentsDirectoryName,
            "Inbox-" + image.Id.ToString("N"));
        Assert(Directory.Exists(attachmentDir) &&
               Directory.EnumerateFiles(attachmentDir).Count() == 1 &&
               (await File.ReadAllBytesAsync(
                   Directory.EnumerateFiles(attachmentDir).Single())).SequenceEqual(imageBytes) &&
               afterImage.Contains("Attachments/Inbox-", StringComparison.Ordinal),
            "Image attachment must be retained as a verified link and project-local copy.");
        MaintenanceInboxResult imageRetry = await converter.ConvertAsync(interventionRequest);
        Assert(imageRetry.AlreadyConverted && afterImage == await File.ReadAllTextAsync(sheet),
            "Images must be idempotent even when their destination attachments already exist.");

        InboxEntry doubleCapture = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Nouvelle panne et première action"
        });
        MaintenanceInboxRequest twoBlocks = new MaintenanceInboxRequest
        {
            EntryId = doubleCapture.Id,
            ProjectRelativePath = projectRelative,
            Mode = MaintenanceInboxMode.IncidentAndIntervention,
            Summary = "Défaillance intermittent réseau"
        };
        await converter.ConvertAsync(twoBlocks);
        string afterBoth = await File.ReadAllTextAsync(sheet);
        MaintenanceParseResult history3 = MaintenanceMarkdownCodec.Parse(afterBoth);
        Assert(history3.Diagnostics.Count == 0 && history3.Records.Count == 4 &&
               history3.Records.Last().IncidentId == history3.Records[^2].Id &&
               afterBoth.Split(MaintenanceInboxMarkdown.Marker(doubleCapture.Id)).Length == 2,
            "Incident plus first intervention must create exactly two linked blocks and one marker.");

        InboxEntry interrupted = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Reprise après interruption"
        });
        MaintenanceInboxRequest resume = new MaintenanceInboxRequest
        {
            EntryId = interrupted.Id,
            ProjectRelativePath = projectRelative,
            Mode = MaintenanceInboxMode.Incident,
            Summary = "Tentative interrompue"
        };
        string itemDir = Path.Combine(root, WorkspaceLayout.InboxDirectoryName,
            "Items", interrupted.Id.ToString("N"));
        await File.WriteAllTextAsync(Path.Combine(itemDir, "maintenance-conversion.json"),
            System.Text.Json.JsonSerializer.Serialize(resume,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web)));
        string simulatedWrite = MaintenanceInboxMarkdown.Append(afterBoth, interrupted, resume);
        await File.WriteAllTextAsync(sheet, simulatedWrite);
        MaintenanceInboxResult recovered = await converter.ConvertAsync(resume);
        Assert(recovered.AlreadyConverted && await File.ReadAllTextAsync(sheet) == simulatedWrite &&
               (await inbox.ReadAllAsync()).Entries.Single(e => e.Id == interrupted.Id)
                   .ConvertedTargetRelativePath == recovered.TargetRelativePath,
            "Interruption after target write but before Inbox metadata must resume without duplication.");

        InboxEntry invalid = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Ne surtout pas perdre"
        });
        MaintenanceInboxRequest wrongIncident = new MaintenanceInboxRequest
        {
            EntryId = invalid.Id,
            ProjectRelativePath = projectRelative,
            Mode = MaintenanceInboxMode.Intervention,
            ExistingIncidentId = "INC-ffffffffffffffffffffffffffffffff",
            Summary = "Essai invalide"
        };
        string prior = await File.ReadAllTextAsync(sheet);
        refused = false;
        try
        {
            await converter.ConvertAsync(wrongIncident);
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused && prior == await File.ReadAllTextAsync(sheet) &&
               (await inbox.ReadAllAsync()).Entries.Single(e => e.Id == invalid.Id)
                   .ConvertedTargetRelativePath is null,
            "Failed incident selection must leave target and original Inbox capture untouched.");

        // Only two optional files/folders were created: the existing maintenance sheet
        // and image attachments. No meeting/milestone scaffolding is introduced.
        Assert(!Directory.Exists(Path.Combine(project, "Réunions")) &&
               !File.Exists(Path.Combine(project, "Jalons.md")),
            "Ultra-simple projects must not acquire mandatory meeting/milestone scaffolding.");
    }

    /// <summary>Throws for a violated safety property.</summary>
    /// <param name="ok">Expected property.</param>
    /// <param name="message">Descriptive failure.</param>
    private static void Assert(bool ok, string message)
    {
        if (!ok)
        {
            throw new InvalidOperationException(message);
        }
    }
}
