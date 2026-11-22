using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Fixture-only regression for explicit migration of historical ACT directories.</summary>
internal static class SfdFlatActivityMigrationSmokeTests
{
    /// <summary>Preserves user text, stable GUIDs and non-leaf historical ACT folders.</summary>
    /// <param name="root">Disposable smoke test root.</param>
    /// <returns>Completed conversion regression.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "sfd-flat-legacy-workspace");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync(
            "SFD migration fictive");
        string app = Path.Combine(workspace, "Applications", "Démo");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Démo",
            ParentDirectory = app,
            DisplayName = "Démo"
        };
        ProjectCreationResult project =
            await new FileSystemProjectCreator(workspace).CreateAsync(
                new ProjectCreationRequest
                {
                    Name = "Conversion fictive",
                    Kind = ProjectKind.WithSpecifications,
                    Complexity = ProjectComplexity.Simple,
                    Target = target
                });
        SfdDocumentRegistryService documents = new(workspace);
        SfdDocumentEntry pm = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process, "PM historique");
        string activities = Path.Combine(
            Path.GetDirectoryName(pm.AbsolutePath)!, "ACTIVITES");

        Guid firstGuid = Guid.NewGuid();
        SfdDocumentCard oldCard = new()
        {
            Id = firstGuid,
            ProjectId = project.Project.Id,
            Kind = SfdDocumentKind.Activity,
            Reference = "PM-0001-ACT-001",
            Title = "PM-0001-ACT-001",
            ParentId = pm.Card.Id,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        string originalDirectory = Path.Combine(activities, oldCard.Reference);
        Directory.CreateDirectory(originalDirectory);
        string oldFile = Path.Combine(originalDirectory, "presentation activité.md");
        await File.WriteAllTextAsync(oldFile,
            SfdDocumentMarkdownCodec.Format(oldCard) +
            "\n## Notes personnelles\n\nNe pas perdre ce texte de test.\n");

        SfdDocumentCard complexCard = oldCard with
        {
            Id = Guid.NewGuid(),
            Reference = "PM-0001-ACT-002",
            Title = "Conserver le dossier complexe"
        };
        string complexDirectory = Path.Combine(activities, complexCard.Reference);
        Directory.CreateDirectory(complexDirectory);
        await File.WriteAllTextAsync(Path.Combine(complexDirectory,
            "presentation activité.md"), SfdDocumentMarkdownCodec.Format(complexCard));
        Directory.CreateDirectory(Path.Combine(complexDirectory, "Exigences"));
        await File.WriteAllTextAsync(Path.Combine(complexDirectory,
            "Exigences", "preuve.md"), "# Contenu à conserver\n");

        string overviewDuplicate = Path.Combine(project.ProjectDirectory,
            "SPECS FONCTIONNELLES", "presentation projet.md");
        await File.WriteAllTextAsync(overviewDuplicate,
            "# Présentation du projet\n\n" +
            "_Décrire ici le contexte, la cible, le périmètre et les décisions validées._\n");

        SfdFlatActivityMigrationService service = new(workspace);
        SfdFlatActivityPreview preview = await service.PreviewAsync(
            project.ProjectDirectory);
        Assert(preview.Candidates.Count == 1 &&
            preview.Candidates[0].Id == firstGuid &&
            preview.Candidates[0].NewReference == "PM001-ACT001" &&
            preview.ArchiveDuplicateOverview &&
            preview.Warnings.Count > 0,
            "Only a leaf ACT folder and an untouched duplicate overview can migrate.");

        SfdFlatActivityResult result = await service.ConvertAsync(
            project.ProjectDirectory, preview);
        string flat = Path.Combine(activities, "PM001-ACT001.md");
        Assert(result.Converted == 1 && result.OverviewArchived &&
            File.Exists(flat) && !Directory.Exists(originalDirectory) &&
            File.Exists(Path.Combine(complexDirectory, "Exigences", "preuve.md")) &&
            File.Exists(Path.Combine(project.ProjectDirectory, "Présentation.md")) &&
            !File.Exists(overviewDuplicate),
            "Migration must flatten only safe folders, keep complex ones and reuse root overview.");

        SfdDocumentCard migrated = SfdDocumentMarkdownCodec.Parse(
            await File.ReadAllTextAsync(flat));
        string backing = Path.Combine(project.ProjectDirectory,
            SfdFlatActivityMigrationService.BackupDirectoryName);
        Assert(migrated.Id == firstGuid &&
            migrated.Reference == "PM001-ACT001" &&
            migrated.Title == "PM001-ACT001" &&
            (await File.ReadAllTextAsync(flat)).Contains(
                "Ne pas perdre ce texte", StringComparison.Ordinal) &&
            File.Exists(Path.Combine(backing, firstGuid.ToString("N"),
                "presentation activité.md")) &&
            File.Exists(Path.Combine(backing, "presentation-projet.md")),
            "Stable identity, handwritten body and the original files must survive.");

        SfdDocumentCatalog after = await documents.ListAsync(project.ProjectDirectory);
        Assert(after.Diagnostics.Count == 0 &&
            after.Entries.Count == 3 &&
            (await service.PreviewAsync(project.ProjectDirectory)).Candidates.Count == 0,
            "Flattening must keep the active catalog unambiguous and be repeat-safe.");

        SfdDocumentEntry next = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            string.Empty, pm.Card.Id);
        Assert(next.Card.Reference == "PM001-ACT003" &&
            Path.GetFileName(next.AbsolutePath) == "PM001-ACT003.md",
            "ACT numbering must include compact and historical ACT references.");
    }

    /// <summary>Reports one synthetic test failure.</summary>
    /// <param name="condition">Expected true predicate.</param>
    /// <param name="message">Diagnostic message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
