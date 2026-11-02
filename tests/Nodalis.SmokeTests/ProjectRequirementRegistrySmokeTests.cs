using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic tests of the first requirement slice: one stable EXI document,
/// no automatic approval, no implicit migration and safe project ownership.
/// </summary>
internal static class ProjectRequirementRegistrySmokeTests
{
    /// <summary>Exercises round-trips, isolation, duplicates and stale previews.</summary>
    /// <param name="root">Disposable smoke test root.</param>
    /// <returns>Completed local tests.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Requirements-v2-smoke");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Requirements fixture");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                NewProject("Projet fictif A", app, appId));
            ProjectCreationResult other = await creator.CreateAsync(
                NewProject("Projet fictif B", app, appId));
            ProjectRequirementRegistryService service = new(workspace);

            string directory = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences");
            string[] beforeFiles = Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.md") : [];
            Dictionary<string, string> originals = beforeFiles.ToDictionary(
                path => path, path => File.ReadAllText(path));

            ProjectRequirementCatalog empty = await service.ListAsync(
                project.ProjectDirectory);
            Assert(empty.Requirements.Count == 0 &&
                   empty.NextReference == "EXI-0001" &&
                   originals.All(item => File.ReadAllText(item.Key) == item.Value),
                "Read-only discovery cannot migrate or rewrite legacy notes.");

            await RejectAsync(() => service.CreateAsync(
                project.ProjectDirectory, "Titre\nmalveillant"),
                "Control characters cannot inject a YAML field.");
            await RejectAsync(() => service.CreateAsync(
                project.ProjectDirectory, "  "),
                "Empty titles cannot create requirement files.");
            Assert(originals.All(item =>
                    File.ReadAllText(item.Key) == item.Value),
                "Rejected creates cannot rewrite existing documentation.");

            Directory.CreateDirectory(directory);
            string legacyPath = Path.Combine(directory, "EXI-0001.md");
            const string legacy = "# Ancienne exigence\n\nNe pas migrer.\n";
            await File.WriteAllTextAsync(legacyPath, legacy);
            ProjectRequirementCatalog legacyCatalog = await service.ListAsync(
                project.ProjectDirectory);
            Assert(legacyCatalog.NextReference == "EXI-0002" &&
                   legacyCatalog.Requirements.Count == 0 &&
                   legacyCatalog.Diagnostics.Count >= 1,
                "Legacy EXI filenames must be preserved and reserved.");

            await RejectAsync(() => service.CreateAsync(
                project.ProjectDirectory, "Titre fictif", "EXI-0001"),
                "A stale, human-reviewed reference must be rejected.");

            ProjectRequirementEntry first = await service.CreateAsync(
                project.ProjectDirectory, "Recherche « exemple »",
                "EXI-0002");
            string sourcePath = Path.Combine(directory, "EXI-0002.md");
            string source = await File.ReadAllTextAsync(sourcePath);
            ProjectRequirementCard roundtrip =
                ProjectRequirementMarkdownCodec.Parse(source);
            Assert(first.Card.Reference == "EXI-0002" &&
                   first.Card.Id != Guid.Empty &&
                   first.Card.ProjectId == project.Project.Id &&
                   roundtrip == first.Card &&
                   first.Card.Kind == ProjectRequirementKind.Unclassified &&
                   first.Card.Validation == ProjectRequirementValidation.Draft &&
                   first.Card.Implementation ==
                       ProjectRequirementImplementation.NotStarted &&
                   first.Card.Verification ==
                       ProjectRequirementVerification.NotVerified &&
                   source.Contains("## Critères d'acceptation",
                       StringComparison.Ordinal) &&
                   source.Contains("Recherche « exemple »",
                       StringComparison.Ordinal) &&
                   first.SourceSha256 == Convert.ToHexString(
                       SHA256.HashData(Encoding.UTF8.GetBytes(source))) &&
                   await File.ReadAllTextAsync(legacyPath) == legacy,
                "Draft creation keeps a stable GUID and independent, unapproved statuses.");

            ProjectRequirementEntry second = await service.CreateAsync(
                project.ProjectDirectory, "Délai de réponse");
            Assert(second.Card.Reference == "EXI-0003" &&
                   second.Card.Id != first.Card.Id,
                "Successive requirements must have distinct stable identities.");
            ProjectRequirementCatalog catalog = await service.ListAsync(
                project.ProjectDirectory);
            Assert(catalog.Requirements.Count == 2 &&
                   catalog.NextReference == "EXI-0004",
                "The catalog is derived from canonical Markdown source.");

            string copyPath = Path.Combine(directory, "copie.md");
            await File.WriteAllTextAsync(copyPath, source);
            ProjectRequirementCatalog collision = await service.ListAsync(
                project.ProjectDirectory);
            Assert(collision.Requirements.Count == 1 &&
                   collision.Requirements[0].Card.Id == second.Card.Id &&
                   collision.Diagnostics.Count >= 3,
                "Ambiguous IDs and references cannot become selectable.");
            File.Delete(copyPath);

            string alienPath = Path.Combine(directory, "EXI-0090.md");
            ProjectRequirementCard alien = first.Card with
            {
                Id = Guid.NewGuid(),
                ProjectId = other.Project.Id,
                Reference = "EXI-0090"
            };
            await File.WriteAllTextAsync(alienPath,
                ProjectRequirementMarkdownCodec.Format(alien));
            ProjectRequirementCatalog foreign = await service.ListAsync(
                project.ProjectDirectory);
            Assert(foreign.Requirements.Count == 2 &&
                   foreign.NextReference == "EXI-0091" &&
                   foreign.Diagnostics.Any(message =>
                       message.Contains("autre projet", StringComparison.Ordinal)),
                "A card owned by another project cannot enter this registry.");
            File.Delete(alienPath);

            await RejectAsync(() => Task.FromResult(
                ProjectRequirementMarkdownCodec.Parse(source.Replace(
                    "validation: Draft", "validation: Approved\nvalidation: Draft",
                    StringComparison.Ordinal))),
                "Duplicated metadata keys must be refused.");
            await RejectAsync(() => Task.FromResult(
                ProjectRequirementMarkdownCodec.Parse(source.Replace(
                    "verification: NotVerified", "verification: VerifiedWithoutProof",
                    StringComparison.Ordinal))),
                "Unknown verification states must never be treated as verified.");
            await RejectAsync(() => Task.FromResult(
                ProjectRequirementMarkdownCodec.Parse(source.Replace(
                    "nodalis-requirement-schema: 1",
                    "nodalis-requirement-schema: 2",
                    StringComparison.Ordinal))),
                "A future schema may not be silently interpreted.");

            Assert(ProjectRequirementMarkdownCodec.IsReference("EXI-0001") &&
                   !ProjectRequirementMarkdownCodec.IsReference("EXI-0000") &&
                   !ProjectRequirementMarkdownCodec.IsReference("EXI-10000") &&
                   !ProjectRequirementMarkdownCodec.IsReference("exi-0001"),
                "Reference numbers must be canonical and bounded.");

            using CancellationTokenSource canceled = new();
            canceled.Cancel();
            await RejectAsync(() => service.ListAsync(
                project.ProjectDirectory, canceled.Token),
                "Canceled scans must not continue.");
            await RejectAsync(() => service.ListAsync(
                Path.Combine(root, "Not-in-workspace")),
                "Projects outside the configured workspace are never browsed.");
            Assert(await File.ReadAllTextAsync(sourcePath) == source &&
                   await File.ReadAllTextAsync(legacyPath) == legacy &&
                   originals.All(item =>
                       File.ReadAllText(item.Key) == item.Value),
                "Negative tests and discovery must not mutate canonical sources.");
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>Builds an isolated project fixture request.</summary>
    /// <param name="title">Synthetic project name.</param>
    /// <param name="app">Synthetic application path.</param>
    /// <param name="appId">Owner application ID.</param>
    /// <returns>Fixture request.</returns>
    private static ProjectCreationRequest NewProject(
        string title, string app, Guid appId) => new()
    {
        Name = title,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Application fictive",
            ParentDirectory = app,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Expects refusal without silently converting the input.</summary>
    /// <param name="action">Negative operation.</param>
    /// <param name="message">Failure condition.</param>
    /// <returns>Completed assertion.</returns>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        bool rejected = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or ArgumentException or
            OperationCanceledException)
        {
            rejected = true;
        }

        Assert(rejected, message);
    }

    /// <summary>Fails with data-free synthetic diagnostics.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Synthetic failure.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
