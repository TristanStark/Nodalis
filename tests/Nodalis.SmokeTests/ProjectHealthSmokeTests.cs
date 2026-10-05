using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Quality;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Quality;

internal static class ProjectHealthSmokeTests
{
    public static async Task RunAsync(
            string root)
    {
        string healthRoot =
            Path.Combine(
                root,
                "ProjectHealth");

        FileSystemWorkspaceStore workspaceStore =
            new FileSystemWorkspaceStore(
                healthRoot);

        await workspaceStore.InitializeAsync(
            "Health Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                healthRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application A");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet A");

        string milestonesDirectory =
            Path.Combine(
                projectDirectory,
                "Jalons");
        string technicalDirectory =
            Path.Combine(
                projectDirectory,
                "Technique");
        string testsDirectory =
            Path.Combine(
                projectDirectory,
                "Tests");
        string decisionsDirectory =
            Path.Combine(
                projectDirectory,
                "Décisions");

        Directory.CreateDirectory(
            milestonesDirectory);
        Directory.CreateDirectory(
            technicalDirectory);
        Directory.CreateDirectory(
            testsDirectory);
        Directory.CreateDirectory(
            decisionsDirectory);

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
                    "Application A"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id =
                    projectId,
                Name =
                    "Projet A",
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
                            "Jalons",
                        Order =
                            10,
                        IsSingleton =
                            false,
                        TemplateKey =
                            "milestones"
                    },
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
                    },
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Tests",
                        Order =
                            40,
                        IsSingleton =
                            false,
                        TemplateKey =
                            "tests-simple"
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

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                "Présentation.md"),
            "# Projet A\n\nPrésentation de test.\n");

        await File.WriteAllTextAsync(
            Path.Combine(
                milestonesDirectory,
                "Jalons.md"),
            "# Jalons\n\n" +
            "| Jalon | Date cible | Statut | Description | Lien | Id | Dépend de |\n" +
            "| --- | --- | --- | --- | --- | --- | --- |\n" +
            "| Mise en service |  | À faire | Date à définir |  | " +
            Guid.NewGuid().ToString("D") +
            " |  |\n");

        await File.WriteAllTextAsync(
            Path.Combine(
                technicalDirectory,
                "Technique.md"),
            "# Technique\n\n## Architecture\n\n");

        await File.WriteAllTextAsync(
            Path.Combine(
                technicalDirectory,
                "Architecture.md"),
            "# Architecture\n\n" +
            "- [ ] Finaliser le dossier | Échéance: 2026-10-01\n\n" +
            "Voir [[Document absent]].\n");

        await File.WriteAllTextAsync(
            Path.Combine(
                technicalDirectory,
                "Orphelin.md"),
            "# Note isolée\n\n" +
            "Cette note n'est référencée nulle part.\n");

        await File.WriteAllTextAsync(
            Path.Combine(
                testsDirectory,
                "Tests.md"),
            "# Tests\n\n" +
            "## Périmètre\n\n" +
            "## Cas de test\n\n" +
            "## Résultats\n");

        await File.WriteAllTextAsync(
            Path.Combine(
                decisionsDirectory,
                "2026-10-01 - Choix.md"),
            "# Decision Record : Choix\n\n" +
            "**Source :** [[Source absente]]\n\n" +
            "## Décision\n\n" +
            "Choix de test.\n");

        WorkspaceLinkIndexService linkIndexService =
            new WorkspaceLinkIndexService(
                healthRoot);

        await linkIndexService.RefreshAsync();

        string linkIndexPath =
            Path.Combine(
                healthRoot,
                WorkspaceLayout.LinkIndexFileName);

        string indexBefore =
            await File.ReadAllTextAsync(
                linkIndexPath);
        DateTime indexWriteTimeBefore =
            File.GetLastWriteTimeUtc(
                linkIndexPath);

        ProjectHealthCheckService service =
            new ProjectHealthCheckService(
                healthRoot);

        ProjectHealthReport report =
            await service.ScanAsync(
                projectDirectory,
                new ProjectHealthOptions
                {
                    MeetingRecencyDays =
                        30,
                    MinimumTestContentCharacters =
                        20
                },
                new DateOnly(
                    2026,
                    10,
                    5));

        AssertHasCode(
            report,
            "SECTION_REQUIRED_MISSING");
        AssertHasCode(
            report,
            "TASK_OVERDUE");
        AssertHasCode(
            report,
            "MILESTONE_DATE_MISSING");
        AssertHasCode(
            report,
            "LINK_UNRESOLVED");
        AssertHasCode(
            report,
            "DOCUMENT_ORPHAN");
        AssertHasCode(
            report,
            "DECISION_SOURCE_MISSING");
        AssertHasCode(
            report,
            "TESTS_TOO_LIGHT");
        AssertHasCode(
            report,
            "MEETING_NOT_RECENT");

        string indexAfter =
            await File.ReadAllTextAsync(
                linkIndexPath);
        DateTime indexWriteTimeAfter =
            File.GetLastWriteTimeUtc(
                linkIndexPath);

        Assert(
            string.Equals(
                indexBefore,
                indexAfter,
                StringComparison.Ordinal) &&
            indexWriteTimeBefore ==
            indexWriteTimeAfter,
            "Project Health Check must not modify the existing derived link index.");

        File.Delete(
            linkIndexPath);

        ProjectHealthReport noIndexReport =
            await service.ScanAsync(
                projectDirectory,
                new ProjectHealthOptions(),
                new DateOnly(
                    2026,
                    10,
                    5));

        AssertHasCode(
            noIndexReport,
            "LINK_INDEX_UNAVAILABLE");

        Assert(
            !File.Exists(
                linkIndexPath),
            "Project Health Check must never rebuild a missing derived link index.");
    }

    private static void AssertHasCode(
            ProjectHealthReport report,
            string code)
    {
        Assert(
            report.Issues.Any(issue =>
                string.Equals(
                    issue.Code,
                    code,
                    StringComparison.Ordinal)),
            "Project Health Check must report " +
            code +
            ".");
    }

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
