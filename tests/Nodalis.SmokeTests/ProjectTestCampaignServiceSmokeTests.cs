using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Core.Maintenance;
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
            ProjectTestResultEvent originalResult =
                final.Campaign.Passages[0].Results[0];
            ProjectTestCampaignEntry rectified =
                await service.RectifyResultAsync(
                    own.ProjectDirectory, final, passage1.Id,
                    originalResult.Id, ProjectTestOutcome.Blocked,
                    "Responsable fictif",
                    "Erreur de saisie initiale, sans nouvelle exécution.");
            Assert(rectified.Campaign.ResultCorrections?.Count == 1 &&
                   rectified.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   rectified.Campaign.ResultCorrections[0].CorrectedOutcome ==
                       ProjectTestOutcome.Blocked &&
                   ProjectTestResultCorrectionPolicy.ForPassage(
                       rectified.Campaign, passage1.Id).Single()
                       .OriginalOutcome == ProjectTestOutcome.Failed &&
                   (await service.ListAsync(own.ProjectDirectory))
                       .Campaigns.Single().Campaign.ResultCorrections?.Count == 1,
                "Administrative correction persists without rewriting a " +
                "historical verdict or the test definition.");
            await RejectAsync(() => service.RectifyResultAsync(
                own.ProjectDirectory, final, passage1.Id,
                originalResult.Id, ProjectTestOutcome.Passed,
                "Responsable fictif", "Révision périmée."),
                "Stale REC SHA cannot overwrite concurrently appended corrections.");
            await RejectAsync(() => service.RectifyResultAsync(
                own.ProjectDirectory, rectified, passage1.Id,
                originalResult.Id, ProjectTestOutcome.Passed,
                "Responsable fictif", "Seconde correction non autorisée."),
                "Duplicate corrections for the same result must fail closed.");
            await RejectAsync(() => service.RectifyResultAsync(
                foreign.ProjectDirectory, rectified, passage1.Id,
                originalResult.Id, ProjectTestOutcome.Passed,
                "Responsable fictif", "Projet étranger."),
                "A correction cannot escape its owning project.");
            final = rectified;
            string maintenancePath = Path.Combine(
                own.ProjectDirectory, "Fiche de Maintenance.md");
            string maintenance = File.Exists(maintenancePath)
                ? await File.ReadAllTextAsync(maintenancePath)
                : "# Fiche de maintenance fictive\n";
            string firstIncident = MaintenanceMarkdownCodec.NewId(
                MaintenanceRecordKind.Incident);
            string secondIncidentId = MaintenanceMarkdownCodec.NewId(
                MaintenanceRecordKind.Incident);
            foreach (string incident in new[] { firstIncident, secondIncidentId })
            {
                maintenance = MaintenanceMarkdownCodec.Append(
                    maintenance, new MaintenanceRecord
                    {
                        Kind = MaintenanceRecordKind.Incident,
                        Id = incident,
                        Date = DateOnly.FromDateTime(DateTime.UtcNow),
                        Summary = "Incident de recette entièrement fictif."
                    });
            }

            maintenance = MaintenanceMarkdownCodec.Append(maintenance,
                new MaintenanceRecord
                {
                    Kind = MaintenanceRecordKind.Intervention,
                    Id = MaintenanceMarkdownCodec.NewId(
                        MaintenanceRecordKind.Intervention),
                    IncidentId = secondIncidentId,
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    Summary = "Intervention technique fictive sans revalidation."
                });
            await File.WriteAllTextAsync(maintenancePath, maintenance);
            ProjectTestCampaignEntry linked =
                await service.RecordIncidentLinkAsync(
                    own.ProjectDirectory, final, passage1.Id,
                    originalResult.Id, firstIncident,
                    ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                    "Anomalie déclarée pendant un résultat non conforme.");
            Assert(linked.Campaign.IncidentLinks?.Count == 1 &&
                   linked.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   ProjectTestIncidentLinkPolicy.Active(
                       linked.Campaign, originalResult.Id).Count == 1 &&
                   (await service.ListAsync(own.ProjectDirectory))
                       .Campaigns.Single().Campaign.IncidentLinks?.Count == 1,
                "Human REC association is persisted without a second " +
                "maintenance incident or changed historical test verdict.");
            await RejectAsync(() => service.RecordIncidentLinkAsync(
                own.ProjectDirectory, final, passage1.Id,
                originalResult.Id, secondIncidentId,
                ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                "Révision périmée entre deux opérations."),
                "Stale REC revisions cannot overwrite newly linked incidents.");
            await RejectAsync(() => service.RecordIncidentLinkAsync(
                own.ProjectDirectory, linked, passage1.Id,
                originalResult.Id, firstIncident,
                ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                "Association en double interdite."),
                "Duplicate incident associations are never silently accepted.");
            await RejectAsync(() => service.RecordIncidentLinkAsync(
                foreign.ProjectDirectory, linked, passage1.Id,
                originalResult.Id, secondIncidentId,
                ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                "Référence incident depuis un autre projet."),
                "Cross-project REC association must fail closed.");
            await RejectAsync(() => service.RecordIncidentLinkAsync(
                own.ProjectDirectory, linked, passage2.Id,
                final.Campaign.Passages[1].Results[0].Id, firstIncident,
                ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                "Le résultat conforme ne représente pas un échec."),
                "A Passed result cannot manufacture a recipe anomaly.");
            ProjectTestCampaignEntry twoLinks =
                await service.RecordIncidentLinkAsync(
                    own.ProjectDirectory, linked, passage1.Id,
                    originalResult.Id, secondIncidentId,
                    ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                    "Seconde cause indépendante sur le même test.");
            Assert(ProjectTestIncidentLinkPolicy.Active(
                       twoLinks.Campaign, originalResult.Id).Count == 2,
                "Multiple distinct incidents may be linked to one test result.");
            ProjectIncidentRetestProposal suggestedIncident =
                ProjectIncidentRetestPlanner.Propose(
                    own.Project.Id, secondIncidentId,
                    [twoLinks.Campaign], new HashSet<Guid>
                    {
                        firstId, secondId
                    });
            Assert(suggestedIncident.Candidates.Count == 1 &&
                   suggestedIncident.Candidates[0].TestId == firstId &&
                   suggestedIncident.Candidates[0].HasLaterPassed &&
                   suggestedIncident.Candidates[0]
                       .HasAdministrativeCorrection &&
                   suggestedIncident.Candidates[0].Reasons.Count == 1 &&
                   suggestedIncident.UnavailableCount == 0 &&
                   twoLinks.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   twoLinks.Campaign.Passages[1].Results[0].Outcome ==
                       ProjectTestOutcome.Passed,
                "Human-linked incident proposes the deduplicated TEST ID, " +
                "warning that later Passed and clerical corrections do " +
                "not establish functional revalidation.");
            ProjectIncidentRetestProposal missingTest =
                ProjectIncidentRetestPlanner.Propose(
                    own.Project.Id, secondIncidentId,
                    [twoLinks.Campaign], new HashSet<Guid>());
            Assert(missingTest.Candidates.Count == 0 &&
                   missingTest.UnavailableCount == 1 &&
                   missingTest.Warnings.Any(item => item.Contains(
                       "non sélectionnable", StringComparison.Ordinal)),
                "Unavailable or ambiguous current test IDs are never " +
                "implicitly recovered from historical REC passages.");
            await RejectAsync(() => Task.FromResult(
                ProjectIncidentRetestPlanner.Propose(
                    foreign.Project.Id, secondIncidentId,
                    [twoLinks.Campaign], new HashSet<Guid> { firstId })),
                "Incident-driven retesting cannot use foreign REC campaigns.");
            await RejectAsync(() => Task.FromResult(
                ProjectIncidentRetestPlanner.Propose(
                    own.Project.Id, "INC-not-a-valid-id",
                    [twoLinks.Campaign], new HashSet<Guid> { firstId })),
                "Incident retest suggestion requires canonical stable INC IDs.");

            DateTimeOffset repeatedStart = DateTimeOffset.UtcNow.AddHours(1);
            ProjectTestResultEvent laterFailure = new()
            {
                Id = Guid.NewGuid(),
                TestId = firstId,
                Outcome = ProjectTestOutcome.Blocked,
                Actor = "Testeur fictif",
                Rationale = "Défaut non confirmé comme corrigé.",
                RecordedUtc = repeatedStart.AddMinutes(1)
            };
            ProjectTestPassage repeated = passage2 with
            {
                Id = Guid.NewGuid(),
                Number = 3,
                StartedUtc = repeatedStart,
                Results = [laterFailure]
            };
            ProjectTestIncidentLinkEvent secondFailureLink = new()
            {
                Id = Guid.NewGuid(),
                PassageId = repeated.Id,
                ResultId = laterFailure.Id,
                TestId = firstId,
                IncidentId = secondIncidentId,
                Kind = ProjectTestIncidentLinkKind.Linked,
                Actor = "Testeur fictif",
                Rationale = "Anomalie réapparue dans un nouveau passage.",
                RecordedUtc = laterFailure.RecordedUtc.AddMinutes(1)
            };
            ProjectTestCampaign twoFailures = twoLinks.Campaign with
            {
                Passages = [.. twoLinks.Campaign.Passages, repeated],
                IncidentLinks =
                    [.. twoLinks.Campaign.IncidentLinks!, secondFailureLink]
            };
            ProjectIncidentRetestProposal deduplicated =
                ProjectIncidentRetestPlanner.Propose(
                    own.Project.Id, secondIncidentId, [twoFailures],
                    new HashSet<Guid> { firstId, secondId });
            Assert(deduplicated.Candidates.Count == 1 &&
                   deduplicated.Candidates[0].TestId == firstId &&
                   deduplicated.Candidates[0].Reasons.Count == 2 &&
                   !deduplicated.Candidates[0].HasLaterPassed &&
                   deduplicated.Candidates[0]
                       .HasAdministrativeCorrection &&
                   twoFailures.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   twoFailures.Passages[1].Results[0].Outcome ==
                       ProjectTestOutcome.Passed,
                "Two human incident-linked failures suggest only one GUID; " +
                "an older Passed does not close a later blocked failure.");

            ProjectRecAnomalyService anomalyService = new(workspace);
            ProjectRecAnomalyOverview firstOverview =
                await anomalyService.ReadAsync(own.ProjectDirectory);
            Assert(firstOverview.Rows.Count == 1 &&
                   firstOverview.Rows[0].ResultId == originalResult.Id &&
                   firstOverview.Rows[0].OriginalOutcome ==
                       ProjectTestOutcome.Failed &&
                   firstOverview.Rows[0].RectifiedOutcome ==
                       ProjectTestOutcome.Blocked &&
                   firstOverview.Rows[0].LinkedIncidentIds.Count == 2 &&
                   firstOverview.Rows[0].MissingIncidentIds.Count == 0 &&
                   firstOverview.Rows[0].InterventionCount == 1 &&
                   firstOverview.UnlinkedCount == 0 &&
                   firstOverview.MissingSourceCount == 0 &&
                   firstOverview.Diagnostics.Count > 0 &&
                   firstOverview.Rows[0].ApplicationVersion == "v1.0" &&
                   firstOverview.Rows.All(item =>
                       item.PassageId != passage2.Id) &&
                   (await service.ListAsync(own.ProjectDirectory))
                       .Campaigns.Single().Campaign.Passages[1]
                       .Results.Single().Outcome == ProjectTestOutcome.Passed,
                "Calculated historical failure view never mistakes a later " +
                "Passed or maintenance intervention for verified resolution.");
            ProjectRecAnomalyOverview foreignOverview =
                await anomalyService.ReadAsync(foreign.ProjectDirectory);
            Assert(foreignOverview.Rows.Count == 0,
                "Other projects do not inherit the current REC incidents.");
            await RejectAsync(() => Task.FromResult(
                ProjectRecAnomalyProjection.Build(Guid.NewGuid(),
                    [twoLinks.Campaign], [], [])),
                "Foreign project ownership cannot reuse REC failure results.");

            ProjectTestCampaignEntry unlinkFirst =
                await service.RecordIncidentLinkAsync(
                    own.ProjectDirectory, twoLinks, passage1.Id,
                    originalResult.Id, firstIncident,
                    ProjectTestIncidentLinkKind.Unlinked, "Testeur fictif",
                    "Erreur d'association constatée et corrigée.");
            Assert(unlinkFirst.Campaign.IncidentLinks?.Count == 3 &&
                   ProjectTestIncidentLinkPolicy.Active(
                       unlinkFirst.Campaign, originalResult.Id).Single()
                       .IncidentId == secondIncidentId &&
                   unlinkFirst.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed,
                "Unlink is an audit event rather than erasure of earlier links.");
            File.Delete(maintenancePath);
            await RejectAsync(() => service.RecordIncidentLinkAsync(
                own.ProjectDirectory, unlinkFirst, passage1.Id,
                originalResult.Id, firstIncident,
                ProjectTestIncidentLinkKind.Linked, "Testeur fictif",
                "Incident indisponible dans la maintenance courante."),
                "Missing maintenance incidents cannot be newly linked.");
            ProjectRecAnomalyOverview afterMissing =
                await anomalyService.ReadAsync(own.ProjectDirectory);
            Assert(afterMissing.Rows.Count == 1 &&
                   afterMissing.Rows[0].LinkedIncidentIds.Count == 1 &&
                   afterMissing.Rows[0].LinkedIncidentIds.Single() ==
                       secondIncidentId &&
                   afterMissing.Rows[0].MissingIncidentIds.Single() ==
                       secondIncidentId &&
                   afterMissing.Rows[0].InterventionCount == 0 &&
                   afterMissing.MissingSourceCount == 1 &&
                   afterMissing.Diagnostics.Any(item => item.Contains(
                       "source", StringComparison.OrdinalIgnoreCase)) &&
                   unlinkFirst.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed,
                "A disappeared maintenance sheet leaves old REC links " +
                "visible but uncertain, never treated as resolved.");
            final = unlinkFirst;
            ProjectTestSourceValidityOverview live =
                await service.AssessSourceValidityAsync(
                    own.ProjectDirectory, final, updatedIndex);
            Assert(live.Rows.Count == 3 &&
                   live.Rows.Single(row => row.PassageId == passage1.Id &&
                       row.TestId == firstId).Validity ==
                           ProjectTestSourceValidity.Modified &&
                   live.Rows.Single(row => row.PassageId == passage1.Id &&
                       row.TestId == secondId).Validity ==
                           ProjectTestSourceValidity.Current &&
                   live.Rows.Single(row => row.PassageId == passage2.Id &&
                       row.TestId == firstId).Validity ==
                           ProjectTestSourceValidity.Current &&
                   live.NeedsReview == 1 &&
                   final.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed,
                "A modified test needs requalification; unaffected tests and " +
                "historic verdicts remain unchanged.");

            LinkIndexCatalog duplicatedBlock = updatedIndex with
            {
                Blocks = updatedIndex.Blocks.Append(
                    updatedIndex.Blocks.First(block =>
                        block.Id == firstId)).ToList()
            };
            ProjectTestSourceValidityOverview ambiguous =
                await service.AssessSourceValidityAsync(
                    own.ProjectDirectory, final, duplicatedBlock);
            Assert(ambiguous.Rows.Where(row => row.TestId == firstId)
                       .All(row => row.Validity ==
                           ProjectTestSourceValidity.Ambiguous),
                "A globally duplicated block GUID must never be current.");

            string updatedCahier = await File.ReadAllTextAsync(cahierPath);
            await File.WriteAllTextAsync(cahierPath, updatedCahier.Replace(
                "Étape 2", "Étape 2 modifiée",
                StringComparison.Ordinal));
            ProjectTestSourceValidityOverview staleIndex =
                await service.AssessSourceValidityAsync(
                    own.ProjectDirectory, final, updatedIndex);
            Assert(staleIndex.Rows.All(row =>
                       row.Validity == ProjectTestSourceValidity.IndexOutOfDate),
                "Index source hashes must match current bytes before declaring " +
                "any frozen block current.");

            File.Delete(cahierPath);
            ProjectTestSourceValidityOverview missingSource =
                await service.AssessSourceValidityAsync(
                    own.ProjectDirectory, final, updatedIndex);
            Assert(missingSource.Rows.All(row =>
                       row.Validity == ProjectTestSourceValidity.Missing) &&
                   final.Campaign.Passages[1].Results[0].Outcome ==
                       ProjectTestOutcome.Passed,
                "A deleted source must be flagged without rewriting old results.");

            WorkspaceLinkIndexService.NotifyWorkspaceChanged(workspace);
            LinkIndexCatalog refreshedMissingIndex =
                await indexService.RefreshAsync();
            ProjectTestSourceValidityOverview stillMissing =
                await service.AssessSourceValidityAsync(
                    own.ProjectDirectory, final, refreshedMissingIndex);
            Assert(stillMissing.Rows.All(row =>
                       row.Validity == ProjectTestSourceValidity.Missing),
                "Deleting a cahier must still report Missing after the " +
                "refreshed link index has removed its block identities.");

            await RejectAsync(() => service.AssessSourceValidityAsync(
                foreign.ProjectDirectory, final, updatedIndex),
                "A campaign cannot be assessed in another project.");

            ProjectTestQualificationSummary guidance =
                ProjectTestCampaignQualification.Review(
                    final.Campaign, live.NeedsReview);
            Assert(guidance.SourceNeedsReview == 1 &&
                   guidance.Warnings.Any(item => item.Contains(
                       "définition(s)", StringComparison.Ordinal)),
                "Closure warnings must expose source changes without blocking.");

            ProjectTestCampaignEntry qualifying =
                await service.RecordDecisionAsync(
                    own.ProjectDirectory, final,
                    ProjectTestCampaignDecisionKind.EnterQualification,
                    "Responsable fictif", "Dossier à vérifier.", null, null);
            Assert(qualifying.Campaign.State ==
                       ProjectTestCampaignState.InQualification &&
                   qualifying.Campaign.DecisionHistory?.Count == 1,
                "Qualification starts only after explicit human approval.");

            await RejectAsync(() => service.RecordDecisionAsync(
                own.ProjectDirectory, final,
                ProjectTestCampaignDecisionKind.Close,
                "Responsable fictif", "Révision périmée.",
                ProjectTestCampaignVerdict.ValidatedWithReservations,
                ["Réserve fictive"]),
                "An obsolete source revision cannot rewrite lifecycle history.");
            await RejectAsync(() => service.RecordDecisionAsync(
                own.ProjectDirectory, qualifying,
                ProjectTestCampaignDecisionKind.Close,
                "Responsable fictif", "Sans réserve explicite.",
                ProjectTestCampaignVerdict.ValidatedWithReservations,
                []),
                "Qualified-with-reservations closure requires actual reservations.");

            ProjectTestCampaignEntry closed =
                await service.RecordDecisionAsync(
                    own.ProjectDirectory, qualifying,
                    ProjectTestCampaignDecisionKind.Close,
                    "Responsable fictif",
                    "Clôture humaine avec requalification restante.",
                    ProjectTestCampaignVerdict.ValidatedWithReservations,
                    ["Réévaluer la définition modifiée du test."]);
            Assert(closed.Campaign.State == ProjectTestCampaignState.Closed &&
                   closed.Campaign.DecisionHistory?.Count == 2 &&
                   closed.Campaign.DecisionHistory[^1].Verdict ==
                       ProjectTestCampaignVerdict.ValidatedWithReservations &&
                   closed.Campaign.Passages[0].Results[0].Outcome ==
                       ProjectTestOutcome.Failed &&
                   closed.Campaign.Passages[1].Results[0].Outcome ==
                       ProjectTestOutcome.Passed,
                "Nonblocking closure stores human reservations, not an " +
                "automatic EXI validation or a rewritten passage.");

            await RejectAsync(() => service.RectifyResultAsync(
                own.ProjectDirectory, closed, passage1.Id,
                originalResult.Id, ProjectTestOutcome.Passed,
                "Responsable fictif", "Rectification après clôture."),
                "Closed campaigns require explicit human reopening.");

            ProjectTestCampaignEntry reopened =
                await service.RecordDecisionAsync(
                    own.ProjectDirectory, closed,
                    ProjectTestCampaignDecisionKind.Reopen,
                    "Responsable fictif",
                    "Réouverture pour poursuivre la recette.", null, null);
            ProjectTestCampaignEntry canceled =
                await service.RecordDecisionAsync(
                    own.ProjectDirectory, reopened,
                    ProjectTestCampaignDecisionKind.Cancel,
                    "Responsable fictif",
                    "Campagne interrompue explicitement.", null, null);
            Assert(canceled.Campaign.State ==
                       ProjectTestCampaignState.Canceled &&
                   canceled.Campaign.DecisionHistory?.Count == 4 &&
                   canceled.Campaign.DecisionHistory[1].Kind ==
                       ProjectTestCampaignDecisionKind.Close &&
                   canceled.Campaign.DecisionHistory[2].Kind ==
                       ProjectTestCampaignDecisionKind.Reopen &&
                   canceled.Campaign.Passages.Count == 2 &&
                   (await service.ListAsync(own.ProjectDirectory))
                       .Campaigns.Single().Campaign.DecisionHistory?.Count == 4,
                "Reopening and canceling append events without deleting the " +
                "original human verdict or historic test results.");

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
