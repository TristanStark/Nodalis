using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Fictitious reviewed EXU/EXS/EXR and shared RM publication regressions.</summary>
internal static class SfdApprovedRequirementImportSmokeTests
{
    /// <summary>Verifies C/SC identity, RM reuse, incomplete drafts, journal replay and conflicts.</summary>
    /// <param name="root">Disposable smoke-test workspace parent.</param>
    /// <returns>Completed requirement import qualification.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-requirement-publication");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync(
            "Exigences de démonstration");
        string app = Path.Combine(workspace, "Applications", "Exemple fictif");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Exemple fictif",
            DisplayName = "Exemple fictif",
            ParentDirectory = app
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Besoin de test",
                Complexity = ProjectComplexity.Simple,
                Kind = ProjectKind.WithSpecifications,
                Target = target
            });
        ProjectCreationResult foreign = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Autre besoin de test",
                Complexity = ProjectComplexity.Simple,
                Kind = ProjectKind.WithSpecifications,
                Target = target
            });
        SfdDocumentRegistryService documents = new(workspace);
        SfdDocumentEntry pm = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process,
            "Recevoir une demande fictive");
        SfdDocumentEntry act = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Vérifier la demande fictive", pm.Card.Id);
        SfdDocumentEntry otherAct = await documents.CreateAsync(
            foreign.ProjectDirectory, SfdDocumentKind.Activity,
            "Activité hors périmètre",
            (await documents.CreateAsync(foreign.ProjectDirectory,
                SfdDocumentKind.Process, "PM hors périmètre")).Card.Id);
        SfdStructuredAuthoringService author = new(workspace);
        SfdBusinessRule shared = await author.AddRuleAsync(
            project.ProjectDirectory,
            "Toute demande fictive porte un numéro métier non vide.",
            "Document fictif d'acceptation",
            SfdAssertionCertainty.Confirmed);
        SfdImportReviewService reviewer = new(workspace);
        SfdApprovedRequirementImportService publisher = new(workspace);
        string response = BuildResponse(act.Card.Id, shared.Id);
        SfdImportReviewSnapshot plan = await reviewer.ReviewAsync(
            project.ProjectDirectory, response);
        Require(plan.Items.Count == 3 && plan.Blocked == 0 &&
            plan.Items[0].Status == SfdImportReviewStatus.NewCandidate &&
            plan.Items[1].Status == SfdImportReviewStatus.NewCandidate &&
            plan.Items[2].Status == SfdImportReviewStatus.IncompleteDraft &&
            plan.Items.All(item => item.ResolvedParentId == act.Card.Id &&
                item.ResolvedParentSha256?.Length == 64) &&
            plan.RulesCatalogSha256.Length == 64 &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 2,
            "Step 02 review must stay read-only with ACT and RM revisions and EXR draft.");

        // Changing the RM catalog after review invalidates the ENTIRE plan.
        await author.AddRuleAsync(project.ProjectDirectory,
            "La date fictive est déclarée dans l'interface.");
        bool oldRulesRejected = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                response, plan, "exu-test");
        }
        catch (InvalidDataException)
        {
            oldRulesRejected = true;
        }

        Require(oldRulesRejected &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 2,
            "An outdated catalog SHA cannot be used to publish an EXU.");

        plan = await reviewer.ReviewAsync(project.ProjectDirectory, response);
        SfdApprovedRequirementResult exu = await publisher.PublishAsync(
            project.ProjectDirectory, response, plan, "exu-test");
        SfdDocumentCard userCard = SfdDocumentMarkdownCodec.Parse(
            await File.ReadAllTextAsync(exu.AbsolutePath));
        IReadOnlyList<SfdConditionBlock> userBlocks =
            SfdConditionBlockCodec.Parse(
                await File.ReadAllTextAsync(exu.AbsolutePath));
        Require(!exu.AlreadyApplied &&
            exu.Kind == SfdDocumentKind.UserRequirement &&
            exu.Reference == "EXU-0001" &&
            exu.ParentActivityId == act.Card.Id &&
            userCard.Certainty == SfdAssertionCertainty.ToConfirm &&
            userBlocks.Count == 1 &&
            userBlocks[0].Id != Guid.Empty &&
            userBlocks[0].Subconditions.Count == 2 &&
            userBlocks[0].Subconditions.All(item =>
                item.Id != Guid.Empty &&
                item.BusinessRuleIds.SequenceEqual([shared.Id])),
            "An approved EXU must retain actual C/SC text, GUIDs and only RM GUID links.");

        SfdApprovedRequirementResult exactReplay = await publisher.PublishAsync(
            project.ProjectDirectory, response, plan, "exu-test");
        Require(exactReplay.AlreadyApplied && exactReplay.Id == exu.Id,
            "An identical reviewed requirement cannot be cloned on replay.");

        string intentPath = Directory.GetFiles(
            Path.Combine(project.ProjectDirectory, ".sfd-ai-import"),
            "intent-requirement.json", SearchOption.AllDirectories).Single();
        string stage = Path.Combine(Path.GetDirectoryName(intentPath)!,
            "staged-requirement.md");
        File.Move(exu.AbsolutePath, stage);
        Require((await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 2,
            "A staged EXU outside the indexed folder must not be considered published.");
        SfdApprovedRequirementResult resumed = await publisher.PublishAsync(
            project.ProjectDirectory, response, plan, "exu-test");
        Require(!resumed.AlreadyApplied && resumed.Id == exu.Id &&
            (await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 3,
            "Recovery replays the same EXU and C/SC GUIDs, without renumbering.");

        plan = await reviewer.ReviewAsync(project.ProjectDirectory, response);
        SfdApprovedRequirementResult exs = await publisher.PublishAsync(
            project.ProjectDirectory, response, plan, "exs-test");
        IReadOnlyList<SfdConditionBlock> systemBlocks =
            SfdConditionBlockCodec.Parse(
                await File.ReadAllTextAsync(exs.AbsolutePath));
        Require(exs.Kind == SfdDocumentKind.SystemRequirement &&
            exs.Reference == "EXS-0001" &&
            systemBlocks.Count == 1 &&
            systemBlocks[0].Subconditions[0].BusinessRuleIds
                .SequenceEqual([shared.Id]) &&
            !File.ReadAllText(exu.AbsolutePath).Contains(shared.Statement,
                StringComparison.Ordinal) &&
            !File.ReadAllText(exs.AbsolutePath).Contains(shared.Statement,
                StringComparison.Ordinal),
            "One canonical RM must be reused across EXU and EXS without duplicate text.");

        plan = await reviewer.ReviewAsync(project.ProjectDirectory, response);
        SfdApprovedRequirementResult exr = await publisher.PublishAsync(
            project.ProjectDirectory, response, plan, "exr-draft");
        Require(exr.Kind == SfdDocumentKind.RegulatoryRequirement &&
            exr.Reference == "EXR-0001" &&
            SfdConditionBlockCodec.Parse(
                await File.ReadAllTextAsync(exr.AbsolutePath)).Count == 0 &&
            SfdDocumentMarkdownCodec.Parse(
                await File.ReadAllTextAsync(exr.AbsolutePath)).Certainty ==
                SfdAssertionCertainty.ToConfirm,
            "EXR without a cited source and C must remain incomplete, not fabricated.");

        SfdCompletionSnapshot completeness =
            await new SfdCompletionReviewService(workspace).ReviewAsync(
                project.ProjectDirectory);
        Require(completeness.Findings.Any(item =>
                item.Reference == "EXR-0001" &&
                item.Code == "SFD_EXR_SOURCE_MISSING") &&
            completeness.Findings.Any(item =>
                item.Reference == "EXR-0001" &&
                item.Code == "SFD_C_MISSING") &&
            completeness.Findings.All(item => item.Code != "SFD_RM_UNRESOLVED"),
            "The final draft review must report missing EXR evidence and no orphaned RM.");

        await File.AppendAllTextAsync(exu.AbsolutePath,
            "\n## Correction métier après publication\n");
        bool manualConflict = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                response, await reviewer.ReviewAsync(
                    project.ProjectDirectory, response), "exu-test");
        }
        catch (InvalidDataException)
        {
            manualConflict = true;
        }

        Require(manualConflict &&
            File.ReadAllText(exu.AbsolutePath).Contains(
                "Correction métier après publication", StringComparison.Ordinal),
            "Never overwrite a human-modified requirement during replay.");

        // Missing or cross-project ACT references must not become publication paths.
        string foreignResponse = BuildResponse(otherAct.Card.Id, shared.Id);
        SfdImportReviewSnapshot foreignPlan = await reviewer.ReviewAsync(
            project.ProjectDirectory, foreignResponse);
        Require(foreignPlan.Items.All(item =>
            item.Status == SfdImportReviewStatus.NeedsResolution &&
            item.ResolvedParentId is null),
            "Foreign ACT GUIDs are not valid owners for EXU/EXS/EXR.");
        bool foreignRejected = false;
        try
        {
            await publisher.PublishAsync(project.ProjectDirectory,
                foreignResponse, foreignPlan, "exu-test");
        }
        catch (InvalidDataException)
        {
            foreignRejected = true;
        }

        Require(foreignRejected,
            "Cross-project requirement publication must be blocked.");

        // A proposed but unpublished RM is not an acceptable canonical reference.
        string unresolvedRm = BuildResponse(act.Card.Id, Guid.NewGuid());
        SfdImportReviewSnapshot unresolved = await reviewer.ReviewAsync(
            project.ProjectDirectory, unresolvedRm);
        Require(unresolved.Items[0].Status ==
                SfdImportReviewStatus.NeedsResolution,
            "Unknown RM GUIDs must require resolution before publication.");
    }

    /// <summary>Builds a strictly versioned step 02 JSON batch from fictitious input.</summary>
    /// <param name="activityId">Actual canonical ACT parent.</param>
    /// <param name="ruleId">Canonical RM to reference in both requirements.</param>
    /// <returns>Three independently reviewable requirement proposals.</returns>
    private static string BuildResponse(Guid activityId, Guid ruleId)
    {
        string parentKey = activityId.ToString("D");
        string rmKey = ruleId.ToString("D");
        return JsonSerializer.Serialize(new
        {
            schema = SfdImportContract.Schema,
            step = "sfd-02",
            runId = "requirements-fictifs",
            proposals = new object[]
            {
                new
                {
                    clientKey = "exu-test", kind = "userRequirement",
                    operation = "create", certainty = "derived",
                    title = "Consulter la réponse fictive",
                    description = "Besoin métier simulé.",
                    parentKey, sourceKeys = Array.Empty<string>(),
                    activityKeys = Array.Empty<string>(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = new[]
                    {
                        new
                        {
                            clientKey = "c-exu",
                            statement = "Quand une demande fictive arrive, l'utilisateur doit pouvoir consulter sa réponse.",
                            subconditions = new[]
                            {
                                new
                                {
                                    clientKey = "sc-exu-1",
                                    statement = "En tant que conseiller fictif, je veux consulter une réponse afin de vérifier la demande sur [CANAL À CONFIRMER].",
                                    businessRuleKeys = new[] { rmKey }
                                },
                                new
                                {
                                    clientKey = "sc-exu-2",
                                    statement = "En tant que conseiller fictif, je veux obtenir une référence afin de vérifier une demande sur [CANAL À CONFIRMER].",
                                    businessRuleKeys = new[] { rmKey }
                                }
                            }
                        }
                    }
                },
                new
                {
                    clientKey = "exs-test", kind = "systemRequirement",
                    operation = "create", certainty = "toConfirm",
                    title = "Générer une réponse fictive",
                    description = "Fonction simulée et contrôlable.",
                    parentKey, sourceKeys = Array.Empty<string>(),
                    activityKeys = Array.Empty<string>(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = new[]
                    {
                        new
                        {
                            clientKey = "c-exs",
                            statement = "Quand une demande fictive est reçue, le système doit permettre de produire un accusé.",
                            subconditions = new[]
                            {
                                new
                                {
                                    clientKey = "sc-exs",
                                    statement = "Le numéro fictif est présent dans le fichier généré.",
                                    businessRuleKeys = new[] { rmKey }
                                }
                            }
                        }
                    }
                },
                new
                {
                    clientKey = "exr-draft", kind = "regulatoryRequirement",
                    operation = "create", certainty = "toConfirm",
                    title = "Obligation réglementaire à vérifier",
                    description = "Aucune obligation applicable n'est établie.",
                    parentKey, sourceKeys = Array.Empty<string>(),
                    activityKeys = Array.Empty<string>(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = Array.Empty<object>()
                }
            }
        });
    }

    /// <summary>Checks a single fake SFD invariant.</summary>
    /// <param name="condition">Expected truth.</param>
    /// <param name="message">Failure message.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
