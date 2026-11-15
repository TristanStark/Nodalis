using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Regression checks for the separate SFD project family.</summary>
internal static class ProjectSfdSmokeTests
{
    /// <summary>Checks SFD creation, round-trip metadata and preservation of the classic layout.</summary>
    /// <param name="root">Shared temporary test root.</param>
    /// <returns>Completed isolated filesystem checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "project-sfd");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("SFD smoke");
        string appDirectory = Path.Combine(workspace, "Applications", "Demo");
        Directory.CreateDirectory(appDirectory);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Demo",
            ParentDirectory = appDirectory,
            DisplayName = "Demo"
        };

        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult created = await creator.CreateAsync(new ProjectCreationRequest
        {
            Name = "Dossier SFD exemple",
            Kind = ProjectKind.WithSpecifications,
            Complexity = ProjectComplexity.Simple,
            Target = target
        });

        Assert(created.Project.Kind == ProjectKind.WithSpecifications &&
            created.Project.KindSchemaVersion == 1, "SFD project family must be explicit and versioned.");

        string[] required =
        [
            "SPECS FONCTIONNELLES/PROCESSUS METIERS",
            "SPECS FONCTIONNELLES/FONCTIONNALITES",
            "SPECS TECHNIQUES/dmep",
            "SPECS TECHNIQUES/APPLICATIONS HOSTS",
            "SPECS TECHNIQUES/APPLICATIONS OPEN",
            "SPECS TECHNIQUES/APPLICATIONS LINUX",
            "DOCUMENTATION/liste des règles métiers.md",
            "QUALIFICATION/TESTS/Tests.md",
            "MANAGEMENT/Réunions",
            "MANAGEMENT/Décisions",
            "MANAGEMENT/Jalons.md",
            "Glossaire.md",
            "Notes rapides.md",
            "Présentation.md"
        ];
        foreach (string path in required)
        {
            string fullPath = Path.Combine(created.ProjectDirectory,
                path.Replace('/', Path.DirectorySeparatorChar));
            Assert(File.Exists(fullPath) || Directory.Exists(fullPath),
                "Missing SFD skeleton path: " + path);
        }

        Assert(!Directory.EnumerateFileSystemEntries(
                Path.Combine(created.ProjectDirectory,
                    "SPECS FONCTIONNELLES", "PROCESSUS METIERS")).Any() &&
            !Directory.EnumerateFileSystemEntries(
                Path.Combine(created.ProjectDirectory,
                    "SPECS FONCTIONNELLES", "FONCTIONNALITES")).Any(),
            "SFD creation must not fabricate processes, activities, requirements or features.");

        string json = await File.ReadAllTextAsync(Path.Combine(
            created.ProjectDirectory, ".project.json"));
        using (JsonDocument document = JsonDocument.Parse(json))
        {
            Assert(document.RootElement.GetProperty("kind").GetString() ==
                "withSpecifications", "SFD family must survive persistence.");
        }

        ProjectJourneyService journey = new(workspace);
        Assert((await journey.ListApplicableTemplatesAsync(created.ProjectDirectory)).Count == 0 &&
            await journey.PreviewDefaultAsync(created.ProjectDirectory) is null,
            "SFD must not advertise a Simple 2.0 journey template.");
        bool rejectedClassicJourney = false;
        try
        {
            await journey.InitializeAsync(created.ProjectDirectory);
        }
        catch (InvalidOperationException)
        {
            rejectedClassicJourney = true;
        }

        Assert(rejectedClassicJourney &&
            !File.Exists(Path.Combine(created.ProjectDirectory,
                ProjectJourneyService.JourneyFileName)),
            "SFD cannot create a classic journey even through a direct service call.");

        ProjectCreationResult standard = await creator.CreateAsync(new ProjectCreationRequest
        {
            Name = "Projet classique",
            Complexity = ProjectComplexity.Simple,
            Target = target
        });
        Assert(standard.Project.Kind == ProjectKind.Standard &&
            !Directory.Exists(Path.Combine(standard.ProjectDirectory, "SPECS FONCTIONNELLES")),
            "Standard project creation must remain unchanged.");
    }

    /// <summary>Fails a smoke check with an explicit diagnostic.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Failure explanation.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
