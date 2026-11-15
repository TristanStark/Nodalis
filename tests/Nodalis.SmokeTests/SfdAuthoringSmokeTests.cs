using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>End-to-end authoring regressions using exclusively fictitious SFD data.</summary>
internal static class SfdAuthoringSmokeTests
{
    /// <summary>
    /// Verifies versioned source identity, parent GUIDs, distinct EXU/EXS/EXR
    /// formalism, FCT modes, incomplete drafts and safe error behavior.
    /// </summary>
    /// <param name="root">Temporary suite root.</param>
    /// <returns>Completed filesystem smoke checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-authoring");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("SFD fictif");
        string app = Path.Combine(workspace, "Applications", "Application fictive");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Application fictive",
            ParentDirectory = app,
            DisplayName = "Application fictive"
        };

        FileSystemProjectCreator projectCreator = new(workspace);
        ProjectCreationResult project = await projectCreator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Processus fictif",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        SfdDocumentRegistryService registry = new(workspace);
        SfdDocumentCatalog empty = await registry.ListAsync(project.ProjectDirectory);
        Require(empty.Entries.Count == 0 && empty.Diagnostics.Count == 0,
            "New SFD project must have no invented business objects.");

        SfdDocumentEntry process = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Process, "Gérer des demandes fictives");
        SfdDocumentEntry activity = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Activity, "Contrôler la demande", process.Card.Id);
        SfdDocumentEntry user = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.UserRequirement, "Consulter le résultat", activity.Card.Id);
        SfdDocumentEntry system = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.SystemRequirement, "Produire un accusé fictif", activity.Card.Id);
        SfdDocumentEntry regulatory = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.RegulatoryRequirement, "Conserver une preuve à confirmer",
            activity.Card.Id);
        SfdDocumentEntry ui = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Functionality, "Page de consultation", functionalityMode:
                SfdFunctionalityMode.WithInterface);
        SfdDocumentEntry noUi = await registry.CreateAsync(project.ProjectDirectory,
            SfdDocumentKind.Functionality, "Générer un fichier fictif", functionalityMode:
                SfdFunctionalityMode.WithoutInterface);

        Require(process.Card.Reference == "PM-0001" &&
            activity.Card.Reference == "ACT-0001" &&
            user.Card.Reference == "EXU-0001" &&
            system.Card.Reference == "EXS-0001" &&
            regulatory.Card.Reference == "EXR-0001" &&
            ui.Card.Reference == "FCT-0001" &&
            noUi.Card.Reference == "FCT-0002",
            "Distinct SFD roles must get stable, project-wide references.");
        Require(activity.Card.ParentId == process.Card.Id &&
            user.Card.ParentId == activity.Card.Id,
            "SFD parent relationship must be stored as stable GUIDs.");
        Require(ui.Card.FunctionalityMode == SfdFunctionalityMode.WithInterface &&
            noUi.Card.FunctionalityMode == SfdFunctionalityMode.WithoutInterface,
            "FCT must retain exclusive with/sans IHM mode.");

        string exuMarkdown = await File.ReadAllTextAsync(user.AbsolutePath);
        string exsMarkdown = await File.ReadAllTextAsync(system.AbsolutePath);
        string exrMarkdown = await File.ReadAllTextAsync(regulatory.AbsolutePath);
        Require(exuMarkdown.Contains(
                "Quand [condition], l'utilisateur doit pouvoir [action / résultat].",
                StringComparison.Ordinal) &&
            exuMarkdown.Contains(
                "En tant que [RÔLE], je veux [ACTION] afin de [BUT / BÉNÉFICE] sur [CANAL À CONFIRMER].",
                StringComparison.Ordinal) &&
            exsMarkdown.Contains(
                "Quand [condition], le système doit permettre de [action / résultat].",
                StringComparison.Ordinal) &&
            exrMarkdown.Contains(
                "Quand [condition], le système doit respecter [obligation applicable].",
                StringComparison.Ordinal) &&
            exrMarkdown.Contains("Source réglementaire applicable",
                StringComparison.Ordinal) &&
            !exuMarkdown.Contains("<!-- nodalis-sfd-condition:", StringComparison.Ordinal),
            "EXU/EXS/EXR drafts need distinct confirmed patterns but no fake C/SC/RM.");
        SfdDocumentCard parsed = SfdDocumentMarkdownCodec.Parse(exuMarkdown);
        Require(parsed == user.Card && parsed.Certainty == SfdAssertionCertainty.ToConfirm,
            "The source Markdown must round-trip the original uncertain draft identity.");

        bool badParentRefused = false;
        try
        {
            await registry.CreateAsync(project.ProjectDirectory,
                SfdDocumentKind.RegulatoryRequirement, "Rattachement invalide",
                process.Card.Id);
        }
        catch (InvalidDataException)
        {
            badParentRefused = true;
        }

        Require(badParentRefused,
            "An EXR cannot be attached directly to a process.");

        bool missingModeRefused = false;
        try
        {
            await registry.CreateAsync(project.ProjectDirectory,
                SfdDocumentKind.Functionality, "Mode absent");
        }
        catch (InvalidDataException)
        {
            missingModeRefused = true;
        }

        Require(missingModeRefused,
            "FCT requires an explicit with/sans IHM category.");

        string processFolder = Path.GetDirectoryName(process.AbsolutePath)!;
        Directory.Move(processFolder, processFolder + " renommé");
        SfdDocumentCatalog renamed = await registry.ListAsync(project.ProjectDirectory);
        Require(renamed.Diagnostics.Count == 0 && renamed.Entries.Count == 7 &&
            renamed.Entries.Single(item => item.Card.Id == user.Card.Id)
                .Card.ParentId == activity.Card.Id,
            "Renaming a PM directory must preserve the full identity/parent chain.");
        SfdDocumentEntry anotherActivity = await registry.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Préparer la suite", process.Card.Id);
        Require(anotherActivity.Card.Reference == "ACT-0002" &&
            anotherActivity.AbsolutePath.Contains("renommé", StringComparison.Ordinal),
            "New ACT must resolve its PM using GUID, after a directory rename.");

        string updatedUserPath = renamed.Entries.Single(item =>
            item.Card.Id == user.Card.Id).AbsolutePath;
        SfdActivityFunctionalityService linkService = new(workspace);
        SfdActivityFunctionalitySnapshot noLinks = await linkService.ReadAsync(
            project.ProjectDirectory);
        Require(noLinks.Registry.Links.Count == 0 && noLinks.Sha256 is null,
            "Reading a new project must not materialize any invented ACT/FCT link.");
        await linkService.LinkAsync(project.ProjectDirectory, activity.Card.Id,
            ui.Card.Id);
        await linkService.LinkAsync(project.ProjectDirectory, activity.Card.Id,
            noUi.Card.Id);
        await linkService.LinkAsync(project.ProjectDirectory, anotherActivity.Card.Id,
            noUi.Card.Id);
        SfdActivityFunctionalitySnapshot links = await linkService.ReadAsync(
            project.ProjectDirectory);
        Require(links.Registry.Links.Count == 3 &&
            links.Registry.Links.Count(item => item.ActivityId == activity.Card.Id) == 2 &&
            links.Registry.Links.Count(item => item.FunctionalityId == noUi.Card.Id) == 2,
            "ACT↔FCT must support N:N GUID links after PM directory rename.");

        SfdActivityFunctionalitySnapshot repeated = await linkService.LinkAsync(
            project.ProjectDirectory, activity.Card.Id, ui.Card.Id);
        Require(repeated.Registry.Links.Count == 3,
            "Linking an existing pair must be idempotent.");

        bool invalidRelationRefused = false;
        try
        {
            await linkService.LinkAsync(project.ProjectDirectory, process.Card.Id,
                noUi.Card.Id);
        }
        catch (InvalidDataException)
        {
            invalidRelationRefused = true;
        }

        Require(invalidRelationRefused,
            "A process cannot stand in for an ACT in a functionality link.");

        SfdActivityFunctionalitySnapshot stale = await linkService.ReadAsync(
            project.ProjectDirectory);
        await linkService.UnlinkAsync(project.ProjectDirectory, activity.Card.Id,
            ui.Card.Id);
        bool staleRefused = false;
        try
        {
            await linkService.SaveAsync(project.ProjectDirectory, stale.Registry,
                stale.Sha256);
        }
        catch (InvalidDataException)
        {
            staleRefused = true;
        }

        Require(staleRefused &&
            (await linkService.ReadAsync(project.ProjectDirectory)).Registry.Links.Count == 2,
            "A stale relation revision must never overwrite newer human choices.");

        // All examples below are invented; none is copied from a workplace SFD.
        SfdStructuredAuthoringService structured = new(workspace);
        SfdCompletionReviewService completeness = new(workspace);
        SfdCompletionSnapshot initialCompleteness = await completeness.ReviewAsync(
            project.ProjectDirectory);
        Require(initialCompleteness.Findings.Any(item =>
                item.Reference == user.Card.Reference &&
                item.Code == "SFD_C_MISSING") &&
            initialCompleteness.Findings.Any(item =>
                item.Reference == regulatory.Card.Reference &&
                item.Code == "SFD_EXR_SOURCE_MISSING"),
            "An EXU without C and an EXR without legal source stay visible but incomplete.");

        bool fabricatedConfirmedRuleRefused = false;
        try
        {
            await structured.AddRuleAsync(project.ProjectDirectory,
                "Une demande fictive dispose d'un numéro unique.",
                certainty: SfdAssertionCertainty.Confirmed);
        }
        catch (InvalidDataException)
        {
            fabricatedConfirmedRuleRefused = true;
        }

        Require(fabricatedConfirmedRuleRefused,
            "An RM cannot be marked confirmed without a declared source.");

        SfdBusinessRule sharedRule = await structured.AddRuleAsync(
            project.ProjectDirectory,
            "Toute demande fictive doit porter un numéro fictif non vide.",
            "Compte rendu fictif pour tests de validation",
            SfdAssertionCertainty.Confirmed);
        SfdBusinessRule otherRule = await structured.AddRuleAsync(
            project.ProjectDirectory,
            "La date fictive doit respecter le calendrier de simulation.");
        Require(sharedRule.Reference == "RM-0001" &&
            otherRule.Reference == "RM-0002" &&
            (await structured.ReadRulesAsync(project.ProjectDirectory)).Rules.Count == 2,
            "Business rules must have unique stable references in one canonical Markdown file.");

        SfdConditionBlock firstCondition = await structured.AddConditionAsync(
            project.ProjectDirectory, user.Card.Id,
            "Quand une demande fictive arrive, l'utilisateur doit pouvoir consulter son résultat.");
        SfdCompletionSnapshot cOnly = await completeness.ReviewAsync(
            project.ProjectDirectory);
        Require(cOnly.Findings.Any(item =>
                item.Reference == user.Card.Reference &&
                item.Code == "SFD_SC_MISSING") &&
            !cOnly.Findings.Any(item =>
                item.Reference == user.Card.Reference &&
                item.Code == "SFD_C_MISSING"),
            "Adding a C removes the missing-C gap but leaves missing-SC incomplete.");

        SfdSubconditionBlock userSubcondition = await structured.AddSubconditionAsync(
            project.ProjectDirectory, user.Card.Id, firstCondition.Id,
            "En tant que conseiller fictif, je veux consulter une réponse afin de répondre à la demande sur [CANAL À CONFIRMER].");
        SfdCompletionSnapshot scOnly = await completeness.ReviewAsync(
            project.ProjectDirectory);
        Require(scOnly.Findings.Any(item =>
                item.Reference == user.Card.Reference &&
                item.Code == "SFD_RM_MISSING") &&
            scOnly.Findings.Any(item =>
                item.Reference == user.Card.Reference &&
                item.Code == "SFD_SC_CHANNEL_TO_CONFIRM"),
            "An SC with no rule or confirmed channel remains an incomplete draft.");

        await structured.LinkRuleAsync(project.ProjectDirectory,
            user.Card.Id, userSubcondition.Id, sharedRule.Id);
        SfdRequirementBlockSnapshot linkedUser = await structured.ReadRequirementAsync(
            project.ProjectDirectory, user.Card.Id);
        Require(linkedUser.Blocks.Count == 1 &&
            linkedUser.Blocks[0].Id == firstCondition.Id &&
            linkedUser.Blocks[0].Subconditions[0].Id == userSubcondition.Id &&
            linkedUser.Blocks[0].Subconditions[0].BusinessRuleIds
                .SequenceEqual([sharedRule.Id]),
            "SFD C/SC keep GUID identities and only references to shared RM.");

        SfdConditionBlock systemCondition = await structured.AddConditionAsync(
            project.ProjectDirectory, system.Card.Id,
            "Quand une demande fictive est reçue, le système doit permettre de produire un accusé.");
        SfdSubconditionBlock systemSubcondition = await structured.AddSubconditionAsync(
            project.ProjectDirectory, system.Card.Id, systemCondition.Id,
            "Le système génère exactement un identifiant fictif contrôlable.");
        await structured.LinkRuleAsync(project.ProjectDirectory,
            system.Card.Id, systemSubcondition.Id, sharedRule.Id);
        SfdRequirementBlockSnapshot linkedSystem = await structured.ReadRequirementAsync(
            project.ProjectDirectory, system.Card.Id);
        Require(linkedSystem.Blocks[0].Subconditions[0].BusinessRuleIds.Contains(
                sharedRule.Id) &&
            !File.ReadAllText(updatedUserPath).Contains(sharedRule.Statement,
                StringComparison.Ordinal) &&
            !File.ReadAllText(
                (await registry.ListAsync(project.ProjectDirectory)).Entries
                    .Single(item => item.Card.Id == system.Card.Id).AbsolutePath)
                .Contains(sharedRule.Statement, StringComparison.Ordinal),
            "Two SC in separate requirement files must share the same RM without copying its text.");

        SfdRequirementBlockSnapshot idempotent = await structured.LinkRuleAsync(
            project.ProjectDirectory, user.Card.Id, userSubcondition.Id, sharedRule.Id);
        Require(idempotent.Blocks[0].Subconditions[0].BusinessRuleIds.Count == 1,
            "Re-adding the same RM reference must never produce a duplicate.");

        bool badFormalismRefused = false;
        try
        {
            await structured.AddConditionAsync(project.ProjectDirectory,
                user.Card.Id, "Le système fait une action automatique.");
        }
        catch (InvalidDataException)
        {
            badFormalismRefused = true;
        }

        Require(badFormalismRefused,
            "Only confirmed EXU C syntax is accepted for actual authored conditions.");

        bool missingRuleRefused = false;
        try
        {
            await structured.LinkRuleAsync(project.ProjectDirectory, user.Card.Id,
                userSubcondition.Id, Guid.NewGuid());
        }
        catch (InvalidDataException)
        {
            missingRuleRefused = true;
        }

        Require(missingRuleRefused,
            "A user cannot associate an invented RM GUID.");

        SfdCompletionSnapshot withSharedRules = await completeness.ReviewAsync(
            project.ProjectDirectory);
        Require(!withSharedRules.Findings.Any(item =>
                item.Code == "SFD_RM_UNRESOLVED") &&
            !withSharedRules.Findings.Any(item =>
                item.Reference == system.Card.Reference &&
                item.Code == "SFD_RM_MISSING") &&
            withSharedRules.Findings.Any(item =>
                item.Reference == otherRule.Reference &&
                item.Code == "SFD_RM_UNUSED"),
            "Completeness must resolve shared rules and report unreferenced ones.");

        // A second SC may refer to the same RM without copying or owning it.
        await structured.UnlinkRuleAsync(project.ProjectDirectory, user.Card.Id,
            userSubcondition.Id, sharedRule.Id);
        SfdRequirementBlockSnapshot afterUnlink = await structured.ReadRequirementAsync(
            project.ProjectDirectory, user.Card.Id);
        Require(afterUnlink.Blocks[0].Subconditions[0].BusinessRuleIds.Count == 0 &&
            (await structured.ReadRequirementAsync(project.ProjectDirectory,
                system.Card.Id)).Blocks[0].Subconditions[0].BusinessRuleIds
                    .Contains(sharedRule.Id) &&
            (await structured.ReadRulesAsync(project.ProjectDirectory)).Rules.Any(
                rule => rule.Id == sharedRule.Id),
            "Unlinking a shared RM must not remove the RM definition or other backlinks.");
        await structured.LinkRuleAsync(project.ProjectDirectory, user.Card.Id,
            userSubcondition.Id, sharedRule.Id);

        string initialExuCrLf = exuMarkdown.Replace("\n", "\r\n",
            StringComparison.Ordinal);
        string preview = SfdConditionBlockCodec.Apply(initialExuCrLf,
            [firstCondition with
            {
                Subconditions =
                [
                    userSubcondition with { BusinessRuleIds = [sharedRule.Id] }
                ]
            }]);
        Require(preview.Contains("\r\n", StringComparison.Ordinal) &&
            !preview.Replace("\r\n", string.Empty, StringComparison.Ordinal)
                .Contains('\n') &&
            !preview.Contains("_Aucune condition renseignée",
                StringComparison.Ordinal) &&
            !preview.Contains("_Aucune sous-condition renseignée",
                StringComparison.Ordinal) &&
            SfdConditionBlockCodec.Parse(preview).Count == 1,
            "C/SC roundtrip must preserve CRLF and clear obsolete draft hints.");

        string rulesCrLf = "# Liste des règles métiers\r\n\r\n_Aucune règle métier définie._\r\n";
        string rulesPreview = SfdBusinessRuleMarkdownCodec.Apply(rulesCrLf,
            [sharedRule]);
        Require(rulesPreview.Contains("\r\n", StringComparison.Ordinal) &&
            SfdBusinessRuleMarkdownCodec.Parse(rulesPreview).Count == 1 &&
            !rulesPreview.Contains("_Aucune règle métier définie._",
                StringComparison.Ordinal),
            "RM canonical catalog must preserve Windows line endings and remove empty hint.");

        bool manualRuleRefused = false;
        try
        {
            SfdBusinessRuleMarkdownCodec.Parse(
                "# Liste des règles métiers\n## RM-9999 — règle sans identité\n");
        }
        catch (InvalidDataException)
        {
            manualRuleRefused = true;
        }

        Require(manualRuleRefused,
            "Undecorated pre-existing RM text must not be silently adopted or overwritten.");

        string copied = Path.Combine(Path.GetDirectoryName(updatedUserPath)!,
            "EXU-9999 - clone.md");
        await File.WriteAllTextAsync(copied, exuMarkdown);
        SfdDocumentCatalog conflicted = await registry.ListAsync(project.ProjectDirectory);
        Require(conflicted.Diagnostics.Any(item =>
                item.Contains("dupliqué", StringComparison.OrdinalIgnoreCase)),
            "Duplicated document IDs and references must be reported.");
        bool conflictRefused = false;
        try
        {
            await registry.CreateAsync(project.ProjectDirectory,
                SfdDocumentKind.Process, "Projet en conflit");
        }
        catch (InvalidDataException)
        {
            conflictRefused = true;
        }

        Require(conflictRefused,
            "Ambiguous identity must prevent creating a new SFD document.");
        File.Delete(copied);

        ProjectCreationResult standard = await projectCreator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet standard fictif",
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        bool standardRefused = false;
        try
        {
            await registry.ListAsync(standard.ProjectDirectory);
        }
        catch (InvalidDataException)
        {
            standardRefused = true;
        }

        Require(standardRefused,
            "The SFD registry must never operate on a Standard 2.0 project.");
    }

    /// <summary>Checks one expected filesystem or business invariant.</summary>
    /// <param name="condition">Expected state.</param>
    /// <param name="message">Failure diagnostic.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
