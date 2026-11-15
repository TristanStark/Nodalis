using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Fictional durable single-PM publication regression tests.</summary>
internal static class SfdApprovedProcessImportSmokeTests
{
    /// <summary>Checks explicit creation, replay, recovery and conflict protection.</summary>
    /// <param name="root">Disposable workspace parent.</param>
    /// <returns>Completed smoke checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-pm-publication");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("Workspace fictif");
        string app = Path.Combine(workspace, "Applications", "Exemple");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Exemple",
            ParentDirectory = app,
            DisplayName = "Exemple"
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet SFD fictif",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        SfdDocumentRegistryService documents = new(workspace);
        SfdImportReviewService reviewer = new(workspace);
        SfdApprovedProcessImportService publisher = new(workspace);
        string response = SfdImportContract.ExampleJson;
        SfdImportReviewSnapshot proposal = await reviewer.ReviewAsync(
            project.ProjectDirectory, response);
        Require(proposal.Items.Count == 2 &&
            proposal.Items[0].Status == SfdImportReviewStatus.NewCandidate &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 0,
            "Review must not silently create a process or activity.");

        SfdApprovedProcessResult applied = await publisher.PublishAsync(
            project.ProjectDirectory, response, proposal, "pm-demo");
        SfdDocumentCatalog catalog = await documents.ListAsync(project.ProjectDirectory);
        Require(!applied.AlreadyApplied && applied.Reference == "PM-0001" &&
            applied.Id != Guid.Empty && catalog.Diagnostics.Count == 0 &&
            catalog.Entries.Count == 1 &&
            catalog.Entries[0].Card.Id == applied.Id &&
            catalog.Entries[0].Card.Certainty == SfdAssertionCertainty.ToConfirm &&
            catalog.Entries[0].Card.Kind == SfdDocumentKind.Process &&
            File.ReadAllText(applied.AbsolutePath).Contains(
                "Description proposée par IA", StringComparison.Ordinal),
            "Only the reviewed PM may be published, with Nodalis GUID and unconfirmed status.");

        SfdApprovedProcessResult repeat = await publisher.PublishAsync(
            project.ProjectDirectory, response, proposal, "pm-demo");
        Require(repeat.AlreadyApplied && repeat.Id == applied.Id &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 1,
            "Repeating exactly the same approval must not clone a PM.");

        string[] journals = Directory.GetDirectories(Path.Combine(
            project.ProjectDirectory, ".sfd-ai-import"));
        Require(journals.Length == 1 &&
            File.Exists(Path.Combine(journals[0], "intent.json")),
            "The durable intent must be written before business publication.");

        // Simulate a crash just before the staged PM directory was moved
        // to its canonical destination, then explicitly retry the same approval.
        string staging = Path.Combine(journals[0], "staged-process");
        Directory.Move(Path.GetDirectoryName(applied.AbsolutePath)!, staging);
        Require((await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 0,
            "Interrupted publication must be isolated outside the canonical SFD index.");

        SfdApprovedProcessResult resumed = await publisher.PublishAsync(
            project.ProjectDirectory, response, proposal, "pm-demo");
        Require(!resumed.AlreadyApplied && resumed.Id == applied.Id &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 1,
            "Recovery must keep the original Nodalis business identity.");

        await File.AppendAllTextAsync(applied.AbsolutePath,
            "\n## Note manuelle après import\n");
        bool changedRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                response, proposal, "pm-demo");
        }
        catch (InvalidDataException)
        {
            changedRefused = true;
        }

        Require(changedRefused && File.ReadAllText(applied.AbsolutePath)
            .Contains("Note manuelle après import", StringComparison.Ordinal),
            "Recovery must refuse to overwrite user changes after the approved snapshot.");

        bool actRefused = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                response, proposal, "act-demo");
        }
        catch (InvalidDataException)
        {
            actRefused = true;
        }

        Require(actRefused &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 1,
            "Pilot publisher must refuse ACT or any role except a new PM.");

        ProjectCreationResult standard = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet standard fictif",
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        bool wrongKindRefused = false;
        try
        {
            await publisher.PublishAsync(standard.ProjectDirectory,
                response, proposal, "pm-demo");
        }
        catch (InvalidOperationException)
        {
            wrongKindRefused = true;
        }

        Require(wrongKindRefused,
            "A standard project must never receive a SFD AI publication.");
    }

    /// <summary>Checks one invariant without production project data.</summary>
    /// <param name="condition">Expected state.</param>
    /// <param name="message">Regression diagnostic.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
