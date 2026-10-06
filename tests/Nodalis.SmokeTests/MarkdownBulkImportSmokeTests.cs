using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Persistence;

internal static class MarkdownBulkImportSmokeTests
{
    /// <summary>
    /// Verifies bulk Markdown preview, relative attachment discovery, explicit planning, commit safety, and duplicate detection.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string workspaceRoot =
            Path.Combine(
                root,
                "MarkdownBulkWorkspace");
        string sourceRoot =
            Path.Combine(
                root,
                "MarkdownBulkSource");

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        await store.InitializeAsync(
            "Markdown Import Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application Markdown");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet Markdown");

        Directory.CreateDirectory(
            projectDirectory);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        ApplicationManifest application =
            new ApplicationManifest
            {
                Id =
                    applicationId,
                Name =
                    "Application Markdown"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id =
                    projectId,
                Name =
                    "Projet Markdown",
                ApplicationId =
                    applicationId,
                InitialComplexity =
                    ProjectComplexity.Simple,
                Sections =
                [
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Technique",
                        Order =
                            20,
                        IsSingleton =
                            false,
                        TemplateKey =
                            "technical-simple"
                    }
                ]
            };

        Directory.CreateDirectory(
            applicationDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                application,
                jsonOptions));

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(
                project,
                jsonOptions));

        string docsDirectory =
            Path.Combine(
                sourceRoot,
                "docs");
        string assetsDirectory =
            Path.Combine(
                sourceRoot,
                "assets");

        Directory.CreateDirectory(
            docsDirectory);
        Directory.CreateDirectory(
            assetsDirectory);

        string indexPath =
            Path.Combine(
                sourceRoot,
                "index.md");
        string detailPath =
            Path.Combine(
                docsDirectory,
                "detail.md");
        string attachmentPath =
            Path.Combine(
                assetsDirectory,
                "schema.bin");

        string indexContent =
            "# Corpus\n\n" +
            "Voir [le détail](docs/detail.md).\n\n" +
            "![Schéma](assets/schema.bin)\n\n" +
            "Voir aussi [la page absente](docs/missing.md).\n";
        string detailContent =
            "# Détail\n\nDocumentation technique.\n";
        byte[] attachmentContent =
            Encoding.UTF8.GetBytes(
                "binary-smoke-attachment");

        await File.WriteAllTextAsync(
            indexPath,
            indexContent);
        await File.WriteAllTextAsync(
            detailPath,
            detailContent);
        await File.WriteAllBytesAsync(
            attachmentPath,
            attachmentContent);

        DateTime indexWriteBefore =
            File.GetLastWriteTimeUtc(
                indexPath);
        DateTime detailWriteBefore =
            File.GetLastWriteTimeUtc(
                detailPath);
        byte[] attachmentBefore =
            await File.ReadAllBytesAsync(
                attachmentPath);

        WorkspaceMarkdownBulkImportService service =
            new WorkspaceMarkdownBulkImportService(
                workspaceRoot);

        MarkdownBulkImportPreview preview =
            await service.PrepareFolderPreviewAsync(
                sourceRoot);

        Assert(
            preview.MarkdownCount ==
                2 &&
            preview.AttachmentCount ==
                1,
            "Bulk Markdown preview must include all Markdown files and referenced local attachments.");

        Assert(
            preview.RelativeLinkCount >=
                3 &&
            preview.Warnings.Any(warning =>
                warning.Contains(
                    "cassé",
                    StringComparison.CurrentCultureIgnoreCase)),
            "Bulk Markdown preview must report broken relative links without modifying the source.");

        Assert(
            preview.Projects.Any(target =>
                target.Id ==
                projectId &&
                target.ApplicationId ==
                applicationId),
            "Bulk Markdown preview must discover valid project targets from workspace manifests.");

        MarkdownBulkImportRequest request =
            new MarkdownBulkImportRequest
            {
                ApplicationId =
                    applicationId,
                ProjectId =
                    projectId,
                TargetSection =
                    "Technique"
            };

        MarkdownBulkImportPlan plan =
            await service.BuildPlanAsync(
                preview,
                request);

        Assert(
            plan.Changes.Count ==
                3 &&
            plan.Changes.All(change =>
                string.Equals(
                    change.Operation,
                    "Copier",
                    StringComparison.Ordinal)),
            "The first bulk Markdown write plan must contain only the three explicit file copies for an existing section.");

        string plannedImportDirectory =
            Path.Combine(
                workspaceRoot,
                plan.ImportDirectoryRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        Assert(
            !Directory.Exists(
                plannedImportDirectory),
            "Building a bulk Markdown plan must not create destination files or directories.");

        Assert(
            await File.ReadAllTextAsync(
                indexPath) ==
                indexContent &&
            await File.ReadAllTextAsync(
                detailPath) ==
                detailContent &&
            attachmentBefore.SequenceEqual(
                await File.ReadAllBytesAsync(
                    attachmentPath)) &&
            File.GetLastWriteTimeUtc(
                indexPath) ==
                indexWriteBefore &&
            File.GetLastWriteTimeUtc(
                detailPath) ==
                detailWriteBefore,
            "Preview and planning must leave every source file byte-for-byte unchanged.");

        MarkdownBulkImportCommitResult result =
            await service.CommitAsync(
                preview,
                request);

        string importedIndex =
            Path.Combine(
                result.ImportDirectory,
                "index.md");
        string importedDetail =
            Path.Combine(
                result.ImportDirectory,
                "docs",
                "detail.md");
        string importedAttachment =
            Path.Combine(
                result.ImportDirectory,
                "assets",
                "schema.bin");

        Assert(
            File.Exists(
                importedIndex) &&
            File.Exists(
                importedDetail) &&
            File.Exists(
                importedAttachment),
            "Committed bulk Markdown import must preserve the source tree needed by relative links.");

        Assert(
            await File.ReadAllTextAsync(
                importedIndex) ==
                indexContent &&
            await File.ReadAllTextAsync(
                importedDetail) ==
                detailContent &&
            attachmentBefore.SequenceEqual(
                await File.ReadAllBytesAsync(
                    importedAttachment)),
            "Committed bulk Markdown import must copy source bytes without rewriting Markdown or attachments.");

        MarkdownBulkImportPlan secondPlan =
            await service.BuildPlanAsync(
                preview,
                request);

        Assert(
            !string.Equals(
                secondPlan.ImportDirectoryRelativePath,
                plan.ImportDirectoryRelativePath,
                StringComparison.OrdinalIgnoreCase) &&
            secondPlan.Warnings.Any(warning =>
                warning.Contains(
                    "Doublon",
                    StringComparison.CurrentCultureIgnoreCase)),
            "A subsequent plan must choose a collision-free directory and explicitly report duplicate Markdown content.");

        Assert(
            await File.ReadAllTextAsync(
                indexPath) ==
                indexContent &&
            await File.ReadAllTextAsync(
                detailPath) ==
                detailContent &&
            attachmentBefore.SequenceEqual(
                await File.ReadAllBytesAsync(
                    attachmentPath)),
            "Committing an import must never modify its source corpus.");
    }

    /// <summary>
    /// Throws when a bulk Markdown smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">The condition to verify.</param>
    /// <param name="message">The failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
