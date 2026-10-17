using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Inbox;
using Nodalis.Core.Maintenance;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Maintenance;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Release-level safety and compatibility fixtures for maintenance 1.2:
/// 1.0 ultra-simple projects, old free Markdown, bulk histories, damage,
/// missing Inbox attachments and recoverable source files.
/// </summary>
internal static class MaintenanceReleaseQualificationSmokeTests
{
    /// <summary>Runs the 1.2 qualification fixtures without modifying real workspaces.</summary>
    /// <param name="root">Temporary smoke-test workspace root.</param>
    /// <returns>Qualification task.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "maintenance-release-v12");
        Directory.CreateDirectory(workspace);
        string appRoot = Path.Combine(workspace, "Applications", "Application Héritée");
        Directory.CreateDirectory(appRoot);
        Guid applicationId = Guid.NewGuid();
        await File.WriteAllTextAsync(Path.Combine(appRoot,
            WorkspaceLayout.ApplicationManifestFileName), JsonSerializer.Serialize(
                new ApplicationManifest
                {
                    Id = applicationId,
                    Name = "Application Héritée"
                }));

        FileSystemProjectCreator creator = new FileSystemProjectCreator(workspace);
        ProjectCreationResult created = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Maintenance rapide",
                Complexity = ProjectComplexity.UltraSimple,
                Target = new ProjectCreationTarget
                {
                    ApplicationId = applicationId,
                    ApplicationName = "Application Héritée",
                    ParentDirectory = appRoot,
                    DisplayName = "Application Héritée"
                }
            });

        string project = created.ProjectDirectory;
        string fiche = Path.Combine(project, "Fiche de Maintenance.md");
        string rapide = Path.Combine(project, "Notes rapides.md");
        string manifest = Path.Combine(project, WorkspaceLayout.ProjectManifestFileName);
        string[] originalFiles = Directory.GetFiles(project, "*",
            SearchOption.AllDirectories);
        Assert(originalFiles.Length == 3 &&
               originalFiles.Contains(fiche, StringComparer.OrdinalIgnoreCase) &&
               originalFiles.Contains(rapide, StringComparer.OrdinalIgnoreCase) &&
               originalFiles.Contains(manifest, StringComparer.OrdinalIgnoreCase) &&
               Directory.GetDirectories(project, "*", SearchOption.AllDirectories).Length == 0,
            "Ultra-simple creation must require only the project name and create exactly " +
            "two business Markdown files plus the existing technical project manifest.");

        byte[] projectManifestBefore = await File.ReadAllBytesAsync(manifest);
        string quickNotesBefore = await File.ReadAllTextAsync(rapide);
        string oldFile = await File.ReadAllTextAsync(fiche);
        string legacy = oldFile + "\r\n## Procédure 1.0\r\n\r\n" +
            "Conserver accents, caractères spéciaux : < & é £.\r\n" +
            "- [ ] tâche issue de l'ancienne fiche\r\n\r\n" +
            "~~~markdown\r\n### Intervention\r\n- **Identifiant :** exemple\r\n~~~\r\n";
        await File.WriteAllTextAsync(fiche, legacy, new UTF8Encoding(false));
        byte[] unchangedBeforePreview = await File.ReadAllBytesAsync(fiche);
        WorkspaceMaintenanceService maintenance = new WorkspaceMaintenanceService();
        MaintenanceParseResult oldRead = await maintenance.ReadAsync(fiche);
        Assert(oldRead.Records.Count == 0 && oldRead.Diagnostics.Count == 0 &&
               (await File.ReadAllBytesAsync(fiche)).SequenceEqual(unchangedBeforePreview),
            "Reading a free-form 1.0 sheet must not migrate or rewrite a single byte.");

        MaintenanceRecord incident = new MaintenanceRecord
        {
            Kind = MaintenanceRecordKind.Incident,
            Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Incident),
            Date = new DateOnly(2026, 10, 17),
            Summary = "Défaillance intermittente du module"
        };
        await maintenance.AddAsync(fiche, incident);
        const int count = 128;
        for (int i = 0; i < count; i++)
        {
            MaintenanceRecord action = new MaintenanceRecord
            {
                Kind = MaintenanceRecordKind.Intervention,
                Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention),
                Date = new DateOnly(2026, 10, 17).AddDays(i % 5),
                Summary = "Contrôle de maintenance #" + (i + 1),
                IncidentId = incident.Id,
                Details = new Dictionary<string, string>
                {
                    ["Action"] = "Inspection numéro " + (i + 1)
                }
            };
            await maintenance.AddAsync(fiche, action);
        }

        string history = await File.ReadAllTextAsync(fiche);
        MaintenanceParseResult parsed = MaintenanceMarkdownCodec.Parse(history);
        IReadOnlyList<MaintenanceHistoryItem> indexed = MaintenanceHistoryIndex.Parse(
            fiche, "Maintenance rapide", history);
        Assert(history.StartsWith(legacy, StringComparison.Ordinal) &&
               parsed.Diagnostics.Count == 0 && parsed.Records.Count == count + 1 &&
               indexed.Count == count + 1 &&
               indexed.Count(item => item.Kind == MaintenanceRecordKind.Intervention &&
                   item.IncidentId == incident.Id) == count,
            "128 interventions must preserve a 1.0 fiche and retain every original incident link.");
        Assert(MaintenanceHistoryIndex.Search(indexed, new MaintenanceHistoryFilter
            {
                Search = "Inspection numéro 128"
            }).Count == 1,
            "Full-text history search must work with 100+ saved maintenance interventions.");
        Assert(!Directory.Exists(Path.Combine(project, "Réunions")) &&
               !File.Exists(Path.Combine(project, "Jalons.md")) &&
               !File.Exists(Path.Combine(project, "Présentation.md")) &&
               Directory.GetFiles(project, "*", SearchOption.AllDirectories).Length == 3 &&
               projectManifestBefore.SequenceEqual(await File.ReadAllBytesAsync(manifest)) &&
               quickNotesBefore == await File.ReadAllTextAsync(rapide),
            "Maintenance must not create optional project scaffolding or modify 1.0 manifests.");

        // A partially damaged old free-form entry must remain visible to the tolerant
        // browser but block all automatic writes; no automatic repair or migration.
        string corrupted = history + "\r\n### Intervention\r\n" +
            "- **Résumé :** Panne ancienne sans ID ni date\r\n";
        await File.WriteAllTextAsync(fiche, corrupted);
        byte[] corruptedBytes = await File.ReadAllBytesAsync(fiche);
        IReadOnlyList<MaintenanceHistoryItem> tolerant = MaintenanceHistoryIndex.Parse(
            fiche, "Maintenance rapide", corrupted);
        Assert(tolerant.Count == count + 2 &&
               tolerant.Last().IsIncomplete &&
               MaintenanceMarkdownCodec.Parse(corrupted).Diagnostics.Count > 0,
            "Partial corruption must produce a visible warning instead of hiding old notes.");

        bool refused = false;
        try
        {
            await maintenance.AddAsync(fiche, new MaintenanceRecord
            {
                Kind = MaintenanceRecordKind.Intervention,
                Id = MaintenanceMarkdownCodec.NewId(MaintenanceRecordKind.Intervention),
                Date = new DateOnly(2026, 10, 17),
                Summary = "Ajout interdit sur fiche partiellement invalide"
            });
        }
        catch (InvalidDataException)
        {
            refused = true;
        }

        Assert(refused &&
               corruptedBytes.SequenceEqual(await File.ReadAllBytesAsync(fiche)),
            "A partially damaged fiche must be byte-identical after a rejected write.");

        // A manual restoration from the untouched snapshot must be possible.
        await File.WriteAllTextAsync(fiche, history);
        Assert(MaintenanceMarkdownCodec.Parse(await File.ReadAllTextAsync(fiche))
                   .Records.Count == count + 1,
            "Restoration of the prior Markdown must recover all 128 interventions.");

        // A real absent attachment: an Inbox image capture was valid at capture
        // time but its canonical file disappears before the user converts it.
        string photo = Path.Combine(workspace, "source-illustration.png");
        await File.WriteAllBytesAsync(photo,
            [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4]);
        WorkspaceInboxService inbox = new WorkspaceInboxService(workspace);
        InboxEntry image = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Image,
            ContentMarkdown = "Photo d'écran de diagnostic",
            AttachmentSourcePaths = [photo]
        });
        string itemDirectory = Path.Combine(workspace,
            WorkspaceLayout.InboxDirectoryName, "Items", image.Id.ToString("N"));
        string attachedFile = Path.Combine(itemDirectory,
            image.Attachments[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(attachedFile);
        string beforeFailedConvert = await File.ReadAllTextAsync(fiche);
        MaintenanceInboxRequest request = new MaintenanceInboxRequest
        {
            EntryId = image.Id,
            ProjectRelativePath = Path.GetRelativePath(workspace, project).Replace('\\', '/'),
            Mode = MaintenanceInboxMode.Intervention,
            ExistingIncidentId = incident.Id,
            Summary = "Analyse d'une capture indisponible"
        };
        WorkspaceInboxMaintenanceService converter =
            new WorkspaceInboxMaintenanceService(workspace);
        refused = false;
        try
        {
            await converter.ConvertAsync(request);
        }
        catch (IOException)
        {
            refused = true;
        }

        InboxEntry? remaining = (await inbox.ReadAllAsync()).Entries
            .FirstOrDefault(item => item.Id == image.Id);
        Assert(refused && remaining is not null &&
               remaining.ConvertedTargetRelativePath is null &&
               beforeFailedConvert == await File.ReadAllTextAsync(fiche) &&
               !Directory.EnumerateFiles(project, "maintenance-conversion.json",
                   SearchOption.AllDirectories).Any(),
            "Missing attachment must reject conversion without touching the fiche " +
            "or removing the recoverable Inbox capture.");

        // The failure should be resumable after the user manually restores the
        // same source attachment. Verify that no duplicate is created on retry.
        await File.CopyAsync(photo, attachedFile);
        MaintenanceInboxResult successful = await converter.ConvertAsync(request);
        MaintenanceInboxResult repeated = await converter.ConvertAsync(request);
        Assert(!successful.AlreadyConverted && repeated.AlreadyConverted &&
               MaintenanceMarkdownCodec.Parse(await File.ReadAllTextAsync(fiche))
                   .Records.Count == count + 2 &&
               (await inbox.ReadAllAsync()).Entries.Any(item =>
                   item.Id == image.Id && item.State == InboxItemState.Processed) &&
               Directory.GetFiles(project, "*.md", SearchOption.TopDirectoryOnly).Length == 2,
            "Restoring an attachment must permit a single idempotent conversion.");

        // A file-level copy is a valid recovery procedure: no new schema or
        // semantic migration step is needed to recover a 1.0 Markdown source.
        string restored = Path.Combine(workspace, "recovered-maintenance.md");
        await File.WriteAllTextAsync(restored, history);
        Assert(SHA256.HashData(await File.ReadAllBytesAsync(restored))
                   .SequenceEqual(SHA256.HashData(Encoding.UTF8.GetBytes(history))),
            "The original Markdown must remain independently recoverable.");
    }

    /// <summary>Throws a clear regression error when an invariant fails.</summary>
    /// <param name="success">Expected condition.</param>
    /// <param name="message">Failure detail.</param>
    private static void Assert(bool success, string message)
    {
        if (!success)
        {
            throw new InvalidOperationException(message);
        }
    }
}
