using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Reliability;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

internal static class WorkspaceIntegritySmokeTests
{
    public static async Task RunAsync(string root)
    {
        string integrityRoot = Path.Combine(
            root,
            "WorkspaceIntegrity");

        FileSystemWorkspaceStore workspaceStore =
            new FileSystemWorkspaceStore(integrityRoot);
        await workspaceStore.InitializeAsync("Integrity Workspace");

        Guid applicationId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid sectionId = Guid.NewGuid();

        string applicationDirectory = Path.Combine(
            integrityRoot,
            WorkspaceLayout.ApplicationsDirectoryName,
            "Application A");
        string projectDirectory = Path.Combine(
            applicationDirectory,
            WorkspaceLayout.ProjectsDirectoryName,
            "Projet A");
        string sectionDirectory = Path.Combine(
            projectDirectory,
            "Technique");

        Directory.CreateDirectory(sectionDirectory);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

        ApplicationManifest application =
            new ApplicationManifest
            {
                Id = applicationId,
                Name = "Application A"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id = projectId,
                Name = "Projet A",
                ApplicationId = applicationId,
                InitialComplexity = ProjectComplexity.Simple,
                Sections =
                [
                    new SectionManifest
                    {
                        Id = sectionId,
                        Name = "Technique",
                        Order = 0,
                        IsSingleton = false
                    }
                ]
            };

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

        string indexedDocument = Path.Combine(
            sectionDirectory,
            "Architecture.md");
        await File.WriteAllTextAsync(
            indexedDocument,
            "# Architecture\n");

        WorkspaceLinkIndexService linkIndex =
            new WorkspaceLinkIndexService(integrityRoot);
        await linkIndex.RefreshAsync();

        string lateDocument = Path.Combine(
            sectionDirectory,
            "Ajout tardif.md");
        await File.WriteAllTextAsync(
            lateDocument,
            "# Ajout tardif\n");
        File.SetLastWriteTimeUtc(
            lateDocument,
            DateTime.UtcNow.AddMinutes(1));

        WorkspaceIntegrityDiagnosticService service =
            new WorkspaceIntegrityDiagnosticService(integrityRoot);

        WorkspaceIntegrityReport staleReport =
            await service.ScanAsync();

        Assert(
            staleReport.Issues.Any(issue =>
                issue.Code == "UNINDEXED_FILE"),
            "Integrity diagnostics must report Markdown files missing from the derived index.");
        Assert(
            staleReport.Issues.Any(issue =>
                issue.Code == "LINK_INDEX_STALE"),
            "Integrity diagnostics must report a stale derived link index.");

        File.SetLastWriteTimeUtc(
            lateDocument,
            DateTime.UtcNow.AddMinutes(-1));
        await service.RebuildDerivedIndexesAsync();

        WorkspaceIntegrityReport rebuiltReport =
            await service.ScanAsync();

        Assert(
            !rebuiltReport.Issues.Any(issue =>
                issue.Code == "UNINDEXED_FILE"),
            "Explicit index rebuild must register structured Markdown files.");
        Assert(
            !rebuiltReport.Issues.Any(issue =>
                issue.Code == "LINK_INDEX_STALE"),
            "Explicit index rebuild must clear the stale-index warning.");

        string duplicateApplicationDirectory = Path.Combine(
            integrityRoot,
            WorkspaceLayout.ApplicationsDirectoryName,
            "Application B");
        Directory.CreateDirectory(duplicateApplicationDirectory);

        ApplicationManifest duplicateApplication =
            new ApplicationManifest
            {
                Id = applicationId,
                Name = "Application B"
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                duplicateApplicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                duplicateApplication,
                jsonOptions));

        Guid missingParentId = Guid.NewGuid();
        Guid missingSectionId = Guid.NewGuid();

        project =
            project with
            {
                ParentProjectId = missingParentId,
                Sections =
                [
                    project.Sections[0],
                    new SectionManifest
                    {
                        Id = missingSectionId,
                        Name = "Section absente",
                        Order = 1,
                        IsSingleton = false
                    }
                ]
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(
                project,
                jsonOptions));

        WorkspaceIntegrityReport brokenReport =
            await service.ScanAsync();

        Assert(
            brokenReport.Issues.Any(issue =>
                issue.Code == "DUPLICATE_GUID" &&
                issue.Severity == WorkspaceIntegritySeverity.Error),
            "Duplicate manifest GUIDs must be reported as errors.");
        Assert(
            brokenReport.Issues.Any(issue =>
                issue.Code == "PROJECT_PARENT_MISSING" &&
                issue.Severity == WorkspaceIntegritySeverity.Error),
            "Missing logical parents must be reported as errors.");
        Assert(
            brokenReport.Issues.Any(issue =>
                issue.Code == "SECTION_DIRECTORY_MISSING" &&
                issue.Severity == WorkspaceIntegritySeverity.Error),
            "Missing declared section directories must be reported as errors.");
    }

    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
