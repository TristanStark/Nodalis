using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Tests;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// End-to-end synthetic tests of one REC campaign with versioned passages,
/// no source rewrites and optimistic conflict detection.
/// </summary>
internal static class ProjectTestCampaignServiceSmokeTests
{
    /// <summary>Executes the isolated project and test source regression.</summary>
    /// <param name="root">Disposable smoke-test parent directory.</param>
    /// <returns>Completed regression.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Test-campaign-v2-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Campaign fixture");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(app);
            Guid applicationId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult own = await creator.CreateAsync(
                CreateProject("Projet A", applicationId, app));
            ProjectCreationResult foreign = await creator.CreateAsync(
                CreateProject("Projet B", applicationId, app));
            ProjectTestCampaignService service = new(workspace);
            ProjectTestCampaignCatalog initial = await service.ListAsync(
                own.ProjectDirectory);
            Assert(initial.Campaigns.Count == 0 &&
                   initial.NextReference == "REC-0001",
                "Reading cannot invent a campaign or overwrite old sources.");

            string campaignsFolder = Path.Combine(
                own.ProjectDirectory, "Tests", "Campagnes");
            Directory.CreateDirectory(campaignsFolder);
            string legacyPath = Path.Combine(campaignsFolder, "REC-0001.md");
            const string legacy = "# Ancienne campagne fictive\n";
            await File.WriteAllTextAsync(legacyPath, legacy);
            ProjectTestCampaignCatalog before = await service.ListAsync(
                own.ProjectDirectory);
            Assert(before.Campaigns.Count == 0 &&
                   before.NextReference == "REC-0002" &&
                   before.Diagnostics.Count >= 1,
                "Legacy REC names must reserve references and remain untouched.");
            await RejectAsync(() => service.CreateAsync(
                own.ProjectDirectory, "Essai", "REC-0001"),
                "A stale reviewed reference must never be silently changed.");

            ProjectTestCampaignEntry created = await service.CreateAsync(
                own.ProjectDirectory, "Recette fonctionnelle fictive",
                "REC-0002");
            Assert(created.Campaign.Reference == "REC-0002" &&
                   created.Campaign.State == ProjectTestCampaignState.Draft &&
                   created.Campaign.Passages.Count == 0 &&
                   created.Campaign.ProjectId == own.Project.Id &&
                   await File.ReadAllTextAsync(legacyPath) == legacy,
                "Explicit creation persists only a new human-owned draft.");

            string originalCampaign = await File.ReadAllTextAsync(
                Path.Combine(campaignsFolder, "REC-0002.md"));
            await RejectAsync(() => service.StartPassageAsync(
                own.ProjectDirectory, created, new LinkIndexCatalog(),
                [Guid.NewGuid()], "REC", "v1", "Exécutant"),
                "Missing test IDs must never create a passage.");
            Assert(await File.ReadAllTextAsync(
                   Path.Combine(campaignsFolder, "REC-0002.md")) ==
                   originalCampaign,
                "Negative start cannot change a campaign.");

            string testsDir = Path.Combine(own.ProjectDirectory, "Tests");
            string otherTestsDir = Path.Combine(foreign.ProjectDirectory, "Tests");
            Directory.CreateDirectory(otherTestsDir);
            Guid firstId = Guid.NewGuid();
            Guid secondId = Guid.NewGuid();
            Guid foreignId = Guid.NewGuid();
            StringBuilder testMarkdown = new("# Cahier fictif\n");
            for (int number = 1; number <= 155; number++)
            {
                Guid id = number == 1 ? firstId :
                    number == 2 ? secondId : Guid.NewGuid();
                testMarkdown.Append("## TEST-")
                    .Append(number.ToString("D4"))
                    .Append(" — Cas fictif ").Append(number).Append('\n')
                    .Append(MarkdownBlockIdentityCodec.FormatMarker(id))
                    .Append("\n### Étapes\n- Étape ").Append(number)
                    .Append("\n### Attendu\n- Contrôle ").Append(number)
                    .Append('\n');
            }

            string cahierPath = Path.Combine(testsDir, "Cahier.md");
            await File.WriteAllTextAsync(cahierPath, testMarkdown.ToString());
            string foreignTestPath = Path.Combine(
                otherTestsDir, "AutreCahier.md");
            await File.WriteAllTextAsync(foreignTestPath,
                "# Cahier B\n## TEST-0003 — Autre projet\n" +
                MarkdownBlockIdentityCodec.FormatMarker(foreignId) +
                "\n### Attendu\n- Cas indépendant\n");
            WorkspaceLinkIndexService indexService = new(workspace);
            LinkIndexCatalog index = await indexService.RefreshAsync();
            Assert(index.Blocks.Any(item => item.Id == firstId) &&
                   index.Blocks.Any(item => item.Id == secondId) &&
                   index.Blocks.Any(item => item.Id == foreignId),
                "Shared index must discover the canonical test markers.");

            await RejectAsync(() => service.StartPassageAsync(
                own.ProjectDirectory, created, index,
                [foreignId], "REC", "v1", "Exécutant"),
                "Foreign project test GUID must be rejected.");
            await RejectAsync(() => service.StartPassageAsync(
                own.ProjectDirectory, created, index,
                [firstId, firstId], "REC", "v1", "Exécutant"),
                "Duplicate requested tests must be rejected.");

            string unchangedCahier = await File.ReadAllTextAsync(cahierPath);
            ProjectTestCampaignEntry first = await service.StartPassageAsync(
                own.ProjectDirectory, created, index,
                [firstId, secondId], "REC-A", "v1.0", "Testeur fictif");
            ProjectTestPassage passage1 = first.Campaign.Passages.Single();
            Assert(first.Campaign.State == ProjectTestCampaignState.InProgress &&
                   passage1.Number == 1 &&
                   passage1.Tests.Count == 2 &&
                   passage1.Tests[0].TestId == firstId &&
                   passage1.Tests[1].TestId == secondId &&
                   passage1.Tests[0].Markdown.Contains("Étape 1",
                       StringComparison.Ordinal) &&
                   !passage1.Tests[0].Markdown.Contains("TEST-0002",
                       StringComparison.Ordinal) &&
                   passage1.Results.Count == 0 &&
                   await File.ReadAllTextAsync(cahierPath) == unchangedCahier,
                "Starting a passage freezes exact sections without rewriting the cahier.");

            ProjectTestCampaignEntry resultOne = await service.RecordResultAsync(
                own.ProjectDirectory, first, passage1.Id, firstId,
                ProjectTestOutcome.Failed, "Testeur fictif",
                "Anomalie reproductible.", "Preuve déclarée à contrôler");
            Assert(resultOne.Campaign.Passages[0].Results.Single().Outcome ==
                   ProjectTestOutcome.Failed &&
                   resultOne.Campaign.Passages[0].Tests[0].Sha256 ==
                   passage1.Tests[0].Sha256,
                "Only one explicit result may be appended.");
            await RejectAsync(() => service.RecordResultAsync(
                own.ProjectDirectory, first, passage1.Id, secondId,
                ProjectTestOutcome.Passed, "Testeur fictif", "Trop ancien.", null),
                "A stale approved REC revision must not clobber a concurrent result.");
            await RejectAsync(() => service.RecordResultAsync(
                own.ProjectDirectory, resultOne, passage1.Id, firstId,
                ProjectTestOutcome.Passed, "Testeur fictif", "Remplacement.", null),
                "Existing result cannot be overwritten within one passage.");

            ProjectTestCampaignEntry resultTwo = await service.RecordResultAsync(
                own.ProjectDirectory, resultOne, passage1.Id, secondId,
                ProjectTestOutcome.Passed, "Testeur fictif",
                "Résultat conforme déclaré.", null);

            await File.WriteAllTextAsync(cahierPath, unchangedCahier.Replace(
                "Cas fictif 1\n", "Cas fictif 1 modifié\n",
                StringComparison.Ordinal));
            File.SetLastWriteTimeUtc(
                cahierPath, DateTime.UtcNow.AddSeconds(2));
            await RejectAsync(() => service.StartPassageAsync(
                own.ProjectDirectory, resultTwo, index,
                [firstId], "REC-B", "v1.1", "Testeur fictif"),
                "An outdated index must not freeze an externally modified cahier.");

            LinkIndexCatalog updatedIndex = (await indexService
                .RefreshDocumentAsync(cahierPath)).Catalog;
            ProjectTestCampaignEntry second = await service.StartPassageAsync(
                own.ProjectDirectory, resultTwo, updatedIndex,
                [firstId], "REC-B", "v1.1", "Testeur fictif");
            ProjectTestPassage passage2 = second.Campaign.Passages[1];
            Assert(second.Campaign.Passages.Count == 2 &&
                   passage2.Number == 2 &&
                   passage2.Tests[0].Sha256 != passage1.Tests[0].Sha256 &&
                   passage1.Results.Count == 0 &&
                   second.Campaign.Passages[0].Results.Count == 2 &&
                   second.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   second.Campaign.Passages[0].ApplicationVersion == "v1.0",
                "A second passage preserves prior verdicts and source version.");
            await RejectAsync(() => service.RecordResultAsync(
                own.ProjectDirectory, second, passage1.Id, secondId,
                ProjectTestOutcome.Passed, "Testeur fictif",
                "Tentative sur l'ancien passage.", null),
                "Finished passages must not accept historical result edits.");

            ProjectTestCampaignEntry final = await service.RecordResultAsync(
                own.ProjectDirectory, second, passage2.Id, firstId,
                ProjectTestOutcome.Passed, "Testeur fictif",
                "Nouveau passage après correction.", null);
            ProjectTestCampaignCatalog after = await service.ListAsync(
                own.ProjectDirectory);
            Assert(after.Campaigns.Single().Campaign.Passages.Count == 2 &&
                   final.Campaign.Passages[1].Results.Single().Outcome ==
                       ProjectTestOutcome.Passed &&
                   final.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   await File.ReadAllTextAsync(legacyPath) == legacy,
                "Reload retains both outcomes; unrelated legacy files stay untouched.");
            await RejectAsync(() => service.RecordResultAsync(
                foreign.ProjectDirectory, final, passage2.Id, firstId,
                ProjectTestOutcome.Passed, "Testeur fictif",
                "Mauvais projet.", null),
                "The campaign owner cannot be switched to another project.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Creates only fictional projects.</summary>
    /// <param name="name">Fictional title.</param>
    /// <param name="applicationId">Owning application identity.</param>
    /// <param name="directory">Application path.</param>
    /// <returns>Project fixture request.</returns>
    private static ProjectCreationRequest CreateProject(
        string name, Guid applicationId, string directory) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = applicationId,
            ApplicationName = "Application fictive",
            ParentDirectory = directory,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Asserts invalid actions are rejected without writing.</summary>
    /// <param name="operation">Invalid action.</param>
    /// <param name="message">Synthetic diagnostic.</param>
    /// <returns>Completed test.</returns>
    private static async Task RejectAsync(Func<Task> operation, string message)
    {
        bool refused = false;
        try
        {
            await operation();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
            InvalidOperationException or ArgumentException or
            OperationCanceledException)
        {
            refused = true;
        }

        Assert(refused, message);
    }

    /// <summary>Fails using only synthetic source descriptions.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Failure details.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
