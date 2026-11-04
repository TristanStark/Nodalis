using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Markdown;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>
/// Synthetic, isolated snapshots of CA edits made outside the import flow.
/// No production workspace, identifiable paths or real campaign outcomes.
/// </summary>
internal static class ProjectCriterionBaselineSmokeTests
{
    /// <summary>
    /// Exercises opt-in baselines, direct Markdown edits, append-only history,
    /// concurrent revisions, ambiguous sources and cross-project rejection.
    /// </summary>
    /// <param name="root">Disposable smoke-test directory.</param>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "CA-baseline-fixture");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Workspace fictif");
        try
        {
            string app = Path.Combine(workspace,
                WorkspaceLayout.ApplicationsDirectoryName, "Application fictive");
            Directory.CreateDirectory(app);
            Guid appId = Guid.NewGuid();
            FileSystemProjectCreator creator = new(workspace);
            ProjectCreationResult project = await creator.CreateAsync(
                Request(app, appId, "Projet source fictif"));
            ProjectCreationResult foreign = await creator.CreateAsync(
                Request(app, appId, "Projet étranger fictif"));
            ProjectRequirementRegistryService registry = new(workspace);
            ProjectRequirementEntry created = await registry.CreateAsync(
                project.ProjectDirectory, "Besoin synthétique");
            ProjectRequirementEntry foreignExi = await registry.CreateAsync(
                foreign.ProjectDirectory, "Besoin étranger");
            string file = Path.Combine(project.ProjectDirectory,
                "Documentation", "Exigences", created.Card.Reference + ".md");
            string original = await File.ReadAllTextAsync(file);
            string nl = Environment.NewLine;
            Guid firstId = Guid.NewGuid();
            string anchor = "## Critères d'acceptation" + nl + nl;
            string heading = "### CA-01 — Action observable" + nl +
                MarkdownBlockIdentityCodec.FormatMarker(firstId) + nl + nl +
                "Résultat attendu A" + nl + nl;
            string markdown = original.Replace(anchor,
                anchor + heading, StringComparison.Ordinal);
            Assert(markdown != original,
                "The fixture must have one identifiable CA.");
            await File.WriteAllTextAsync(file, markdown,
                new UTF8Encoding(false));

            ProjectRequirementCatalog catalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectCriterionBaselineService baseline = new(workspace);
            string folder = Path.Combine(project.ProjectDirectory,
                ".nodalis-ca-baselines");
            ProjectCriterionBaselineOverview empty =
                await baseline.InspectAsync(project.ProjectDirectory, catalog);
            Assert(empty.Snapshot is null && empty.UnresolvedCount == 1 &&
                empty.Rows.Single().State ==
                    ProjectCriterionBaselineState.NotTracked &&
                !Directory.Exists(folder),
                "Passive inspection cannot silently initialize the baseline.");
            await RejectAsync(() => baseline.InspectAsync(
                foreign.ProjectDirectory, catalog),
                "Baseline from another project cannot be displayed here.");
            await RejectAsync(() => baseline.RecordAsync(
                foreign.ProjectDirectory, catalog, 0, "Motif humain"),
                "A foreign project cannot receive a forged observation.");
            await RejectAsync(() => baseline.RecordAsync(
                project.ProjectDirectory, catalog, 0, ""),
                "A human reason is mandatory.");
            Assert(!Directory.Exists(folder),
                "Invalid or foreign actions must not create local journals.");

            ProjectCriterionBaselineSnapshot first =
                await baseline.RecordAsync(project.ProjectDirectory, catalog,
                    0, "Activation humaine du suivi CA");
            Assert(first.Sequence == 1 && first.Entries.Count == 1 &&
                first.Entries.Single().CriterionId == firstId &&
                first.Entries.Single().BodySha256 ==
                    ProjectCriterionBaselineProjection.Sha("Résultat attendu A") &&
                File.Exists(Path.Combine(folder, "baseline-000001.json")),
                "Initial reference must atomically persist stable CA fingerprints.");
            ProjectCriterionBaselineOverview stable =
                await baseline.InspectAsync(project.ProjectDirectory, catalog);
            Assert(stable.Snapshot?.Sequence == 1 &&
                stable.Rows.Single().State ==
                    ProjectCriterionBaselineState.Unchanged,
                "An unchanged CA is recognized after restarting the service.");

            // EXI metadata unrelated to the CA body is allowed to change.
            await File.AppendAllTextAsync(file,
                nl + "Information générale sans modification CA." + nl);
            ProjectRequirementCatalog unrelated =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectCriterionBaselineOverview notChanged =
                await new ProjectCriterionBaselineService(workspace)
                    .InspectAsync(project.ProjectDirectory, unrelated);
            Assert(notChanged.Rows.Single().State ==
                    ProjectCriterionBaselineState.Unchanged,
                "Unrelated EXI document edits do not trigger CA requalification.");

            string edited = (await File.ReadAllTextAsync(file)).Replace(
                "Résultat attendu A", "Résultat attendu B",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(file, edited,
                new UTF8Encoding(false));
            ProjectRequirementCatalog changed =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectCriterionBaselineOverview drift =
                await baseline.InspectAsync(project.ProjectDirectory, changed);
            Assert(drift.Snapshot?.Sequence == 1 &&
                drift.ChangedCount == 1 &&
                drift.Rows.Single().State ==
                    ProjectCriterionBaselineState.Changed &&
                drift.Rows.Single().CriterionId == firstId,
                "Direct Markdown CA content edits must survive reload as drift.");
            await RejectAsync(() => baseline.RecordAsync(
                project.ProjectDirectory, catalog, 1, "Revue obsolète"),
                "Outdated human preview must be rejected before appending.");
            await RejectAsync(() => baseline.RecordAsync(
                project.ProjectDirectory, changed, 0, "Ancienne séquence"),
                "Concurrent baseline observations cannot take the same index.");
            Assert((await baseline.ListHistoryAsync(
                project.ProjectDirectory)).Count == 1,
                "Failed confirmations cannot replace or append a baseline.");

            ProjectCriterionBaselineSnapshot second =
                await baseline.RecordAsync(project.ProjectDirectory, changed,
                    1, "Nouvelle observation après contrôle humain des tests");
            Assert(second.Sequence == 2 &&
                ProjectCriterionBaselineProjection.IsSha(
                    second.PreviousSha256) &&
                (await baseline.ListHistoryAsync(
                    project.ProjectDirectory)).Count == 2,
                "Human re-baselining appends a chain-linked event, preserving history.");
            ProjectCriterionBaselineOverview updated =
                await baseline.InspectAsync(project.ProjectDirectory, changed);
            Assert(updated.Rows.Single().State ==
                    ProjectCriterionBaselineState.Unchanged,
                "An explicitly updated reference compares to its own SHA.");

            Guid secondId = Guid.NewGuid();
            string secondHeading = "### CA-02 — Cas complémentaire" + nl +
                MarkdownBlockIdentityCodec.FormatMarker(secondId) + nl +
                nl + "Résultat secondaire" + nl + nl;
            string changedMarkdown = await File.ReadAllTextAsync(file);
            string replaced = changedMarkdown.Replace(
                "## Dépendances et preuves",
                secondHeading + "## Dépendances et preuves",
                StringComparison.Ordinal);
            Assert(replaced != changedMarkdown,
                "Fixture must insert a second CA in the correct section.");
            await File.WriteAllTextAsync(file, replaced,
                new UTF8Encoding(false));
            ProjectRequirementCatalog addedCatalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectCriterionBaselineOverview added =
                await baseline.InspectAsync(project.ProjectDirectory, addedCatalog);
            Assert(added.Rows.Count == 2 &&
                added.Rows.Single(item => item.CriterionId == secondId).State ==
                    ProjectCriterionBaselineState.Added &&
                added.Rows.Single(item => item.CriterionId == firstId).State ==
                    ProjectCriterionBaselineState.Unchanged,
                "A new CA must never be confused with a renamed old CA.");

            // Removing a CA does not delete its historic baseline.
            string text = await File.ReadAllTextAsync(file);
            int begin = text.IndexOf("### CA-01 — Action observable",
                StringComparison.Ordinal);
            int end = text.IndexOf("### CA-02 — Cas complémentaire",
                StringComparison.Ordinal);
            Assert(begin >= 0 && end > begin, "Synthetic CA boundaries missing.");
            await File.WriteAllTextAsync(file,
                text.Remove(begin, end - begin), new UTF8Encoding(false));
            ProjectRequirementCatalog removedCatalog =
                await registry.ListAsync(project.ProjectDirectory);
            ProjectCriterionBaselineOverview removed =
                await baseline.InspectAsync(project.ProjectDirectory,
                    removedCatalog);
            Assert(removed.Rows.Any(item => item.CriterionId == firstId &&
                    item.State == ProjectCriterionBaselineState.Missing) &&
                removed.Rows.Any(item => item.CriterionId == secondId &&
                    item.State == ProjectCriterionBaselineState.Added),
                "A deleted CA remains visible as a missing stable identity.");

            ProjectRequirementCatalog partial = removedCatalog with
            {
                Requirements = [],
                Diagnostics = ["EXI corrompue : source non analysable."]
            };
            ProjectCriterionBaselineOverview uncertain =
                ProjectCriterionBaselineProjection.Compare(partial, second);
            Assert(uncertain.Rows.Single().State ==
                    ProjectCriterionBaselineState.Unresolved,
                "Unreadable EXI is not proof that a criterion was deleted.");

            // Corrupt the first event: the second predecessor hash must block
            // silently accepting or reusing altered observation history.
            string firstFile = Path.Combine(folder, "baseline-000001.json");
            string originalEvent = await File.ReadAllTextAsync(firstFile);
            await File.WriteAllTextAsync(firstFile,
                originalEvent.Replace("Activation humaine",
                    "Mauvaise activation", StringComparison.Ordinal),
                new UTF8Encoding(false));
            await RejectAsync(() => baseline.InspectAsync(
                project.ProjectDirectory, removedCatalog),
                "Tampered earlier event must invalidate the entire baseline chain.");
            Assert((await File.ReadAllTextAsync(file)).Contains(
                "CA-02", StringComparison.Ordinal),
                "Journal corruption must not touch any EXI source.");
            await File.WriteAllTextAsync(firstFile, originalEvent,
                new UTF8Encoding(false));

            ProjectRequirementCatalog wrongCatalog =
                await registry.ListAsync(foreign.ProjectDirectory);
            Assert(wrongCatalog.Requirements.Single().Card.Id ==
                    foreignExi.Card.Id &&
                (await baseline.ListHistoryAsync(
                    project.ProjectDirectory)).Count == 2,
                "Events are isolated from synthetic sibling projects.");

            // No operation here is capable of editing a frozen REC, a test
            // definition, an EXI state or a link.
            Assert(created.Card.Validation == ProjectRequirementValidation.Draft &&
                created.Card.Verification ==
                    ProjectRequirementVerification.NotVerified,
                "Recording a reference must not claim human verification.");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    /// <summary>Project fixture using only synthetic identifiers.</summary>
    /// <param name="app">Application folder.</param>
    /// <param name="appId">Generated application GUID.</param>
    /// <param name="name">Fictional project name.</param>
    /// <returns>Valid project request.</returns>
    private static ProjectCreationRequest Request(string app, Guid appId,
        string name) => new()
    {
        Name = name,
        Complexity = ProjectComplexity.Complex,
        Target = new ProjectCreationTarget
        {
            ApplicationId = appId,
            ApplicationName = "Application fictive",
            ParentDirectory = app,
            DisplayName = "Application fictive"
        }
    };

    /// <summary>Expects a safe refusal without business writes.</summary>
    /// <param name="action">Guarded asynchronous action.</param>
    /// <param name="message">Test diagnostic.</param>
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Checks the expected invariant.</summary>
    /// <param name="condition">Assertion.</param>
    /// <param name="message">Diagnostic.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
