using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Fictional, read-only SFD import comparison regressions.</summary>
internal static class SfdImportReviewSmokeTests
{
    /// <summary>Checks GUID scope, canonical RM, completeness and no automatic writes.</summary>
    /// <param name="root">Temporary suite root.</param>
    /// <returns>Completed regression checks.</returns>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-import-review");
        await new FileSystemWorkspaceStore(workspace).InitializeAsync("Projet fictif");
        string app = Path.Combine(workspace, "Applications", "Fictive");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Fictive", ParentDirectory = app,
            DisplayName = "Fictive"
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(new ProjectCreationRequest
        {
            Name = "Projet SFD fictif", Complexity = ProjectComplexity.Simple,
            Kind = ProjectKind.WithSpecifications, Target = target
        });
        ProjectCreationResult other = await creator.CreateAsync(new ProjectCreationRequest
        {
            Name = "Projet SFD indépendant", Complexity = ProjectComplexity.Simple,
            Kind = ProjectKind.WithSpecifications, Target = target
        });
        SfdImportReviewService review = new(workspace);
        SfdImportReviewSnapshot initial = await review.ReviewAsync(
            project.ProjectDirectory, SfdImportContract.ExampleJson);
        SfdDocumentRegistryService author = new(workspace);
        Require(initial.Items.Count == 2 &&
            initial.Items.All(item => item.Status == SfdImportReviewStatus.NewCandidate) &&
            (await author.ListAsync(project.ProjectDirectory)).Entries.Count == 0,
            "Previewing step 01 must NOT create PM/ACT.");

        SfdDocumentEntry pm = await author.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process, "Processus fictif");
        SfdDocumentEntry act = await author.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity, "Activité fictive",
            pm.Card.Id);
        SfdDocumentEntry fct = await author.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Functionality,
            "Extraction fictive", functionalityMode: SfdFunctionalityMode.WithoutInterface);
        SfdDocumentEntry foreign = await author.CreateAsync(
            other.ProjectDirectory, SfdDocumentKind.Process, "Processus étranger");

        string update = Envelope("sfd-01", "update-test",
            Candidate("pm-update", "businessProcess", "update",
                "Proposition de correction", targetId: pm.Card.Id));
        SfdImportReviewSnapshot reviewed = await review.ReviewAsync(
            project.ProjectDirectory, update);
        SfdImportReviewSnapshot repeated = await review.ReviewAsync(
            project.ProjectDirectory, update);
        Require(reviewed.Items.Single().Status ==
                SfdImportReviewStatus.UpdateCandidate &&
            reviewed.Items.Single().ExistingReference == pm.Card.Reference &&
            reviewed.Items.Single().ExistingSha256?.Length == 64 &&
            reviewed.PlanSha256 == repeated.PlanSha256,
            "A matched existing PM must use its GUID, original SHA, and stable fingerprint.");

        string foreignUpdate = Envelope("sfd-01", "wrong-project",
            Candidate("pm-foreign", "businessProcess", "update",
                "Faux rapprochement", targetId: foreign.Card.Id));
        Require((await review.ReviewAsync(project.ProjectDirectory,
            foreignUpdate)).Items.Single().Status == SfdImportReviewStatus.Rejected,
            "A GUID from another project must be rejected.");

        string duplicateTitle = Envelope("sfd-01", "similar-title",
            Candidate("pm-duplicate", "businessProcess", "create", pm.Card.Title));
        Require((await review.ReviewAsync(project.ProjectDirectory,
            duplicateTitle)).Items.Single().Status ==
                SfdImportReviewStatus.NeedsResolution,
            "Identical title must require review, not automatic identity matching.");

        string requirements = Envelope("sfd-02", "draft-requirements",
            Candidate("exu-draft", "userRequirement", "create",
                "Extraire des données fictives sans IHM",
                parentKey: act.Card.Id.ToString("D")),
            Candidate("exr-draft", "regulatoryRequirement", "create",
                "Obligation à confirmer", parentKey: act.Card.Id.ToString("D")));
        SfdImportReviewSnapshot incomplete = await review.ReviewAsync(
            project.ProjectDirectory, requirements);
        Require(incomplete.Incomplete == 2 && incomplete.Blocked == 0 &&
            incomplete.Items.All(item =>
                item.Status == SfdImportReviewStatus.IncompleteDraft),
            "Missing C/SC/RM and unconfirmed EXR sources are incomplete, not fabricated.");

        string orphan = Envelope("sfd-02", "missing-parent",
            Candidate("exu-orphan", "userRequirement", "create",
                "Besoin sans parent", parentKey: "act-unknown"));
        Require((await review.ReviewAsync(project.ProjectDirectory,
            orphan)).Items.Single().Status == SfdImportReviewStatus.NeedsResolution,
            "Unknown parent client keys must not be inferred from paths.");

        SfdStructuredAuthoringService authoring = new(workspace);
        SfdBusinessRule rule = await authoring.AddRuleAsync(
            project.ProjectDirectory, "Une donnée fictive doit être non vide.",
            "Compte rendu fictif", SfdAssertionCertainty.Confirmed);
        string ruleUpdate = Envelope("sfd-02", "rule-update",
            Candidate("rm-update", "businessRule", "update",
                "Une donnée fictive doit être documentée.", targetId: rule.Id));
        SfdImportReviewItem knownRule = (await review.ReviewAsync(
            project.ProjectDirectory, ruleUpdate)).Items.Single();
        Require(knownRule.Status == SfdImportReviewStatus.UpdateCandidate &&
            knownRule.ExistingId == rule.Id &&
            knownRule.ExistingSha256?.Length == 64,
            "Existing RM must resolve by shared-catalog GUID and SHA.");

        string feature = Envelope("sfd-03", "without-ui",
            Candidate("fct-new", "feature", "create",
                "Produire une extraction fictive", uiMode: "withoutUi",
                activityKeys: [act.Card.Id.ToString("D")]));
        Require((await review.ReviewAsync(project.ProjectDirectory,
            feature)).Items.Single().Status == SfdImportReviewStatus.NewCandidate,
            "A FCT without IHM may link an existing ACT by GUID.");

        string emo = Envelope("sfd-04", "emo-deferred",
            Candidate("emo-new", "implementationRequirement", "create",
                "Erreur fonctionnelle fictive", parentKey: fct.Card.Id.ToString("D"),
                activityKeys: [act.Card.Id.ToString("D")]));
        Require((await review.ReviewAsync(project.ProjectDirectory,
            emo)).Items.Single().Status == SfdImportReviewStatus.Deferred,
            "EMO import must be deferred pending physical placement arbitration.");

        await File.AppendAllTextAsync(pm.AbsolutePath,
            "\n## Information fictive ajoutée après comparaison\n");
        SfdImportReviewSnapshot changed = await review.ReviewAsync(
            project.ProjectDirectory, update);
        Require(changed.PlanSha256 != reviewed.PlanSha256 &&
            changed.Items.Single().ExistingSha256 !=
                reviewed.Items.Single().ExistingSha256,
            "Changing an underlying source must invalidate the earlier plan SHA.");

        Require((await author.ListAsync(project.ProjectDirectory)).Entries.Count == 3 &&
            !File.Exists(Path.Combine(project.ProjectDirectory, ".sfd-import.json")),
            "Analysis and comparison must leave project business objects unchanged.");

        ProjectCreationResult regular = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Projet classique", Complexity = ProjectComplexity.Simple,
                Target = target
            });
        bool rejected = false;
        try
        {
            await review.ReviewAsync(regular.ProjectDirectory, update);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Require(rejected,
            "The SFD import review cannot run against classic Nodalis projects.");
    }

    /// <summary>Builds a strict synthetic proposal, including all required properties.</summary>
    /// <param name="key">Temporary key.</param>
    /// <param name="kind">External role.</param>
    /// <param name="operation">Create/update/link.</param>
    /// <param name="title">Fictional title.</param>
    /// <param name="parentKey">Existing parent GUID or same-lot key.</param>
    /// <param name="targetId">Verified existing document GUID.</param>
    /// <param name="uiMode">Optional FCT discriminator.</param>
    /// <param name="activityKeys">Explicit ACT links.</param>
    /// <returns>Serializable proposal fixture.</returns>
    private static object Candidate(string key, string kind, string operation,
        string title, string? parentKey = null, Guid? targetId = null,
        string? uiMode = null, string[]? activityKeys = null) => new
        {
            clientKey = key, kind, operation, certainty = "toConfirm",
            title, description = "Description fictive.",
            parentKey, targetId, uiMode,
            sourceKeys = Array.Empty<string>(),
            activityKeys = activityKeys ?? [],
            requirementKeys = Array.Empty<string>(),
            conditions = Array.Empty<object>()
        };

    /// <summary>Encodes a valid step envelope from synthetic candidate rows.</summary>
    /// <param name="step">Generation stage.</param>
    /// <param name="runId">Temporary run key.</param>
    /// <param name="proposals">Up to 100 candidates.</param>
    /// <returns>External-looking JSON fixture.</returns>
    private static string Envelope(string step, string runId,
        params object[] proposals) => JsonSerializer.Serialize(new
        {
            schema = SfdImportContract.Schema, step, runId, proposals
        });

    /// <summary>Checks an explicit review invariant.</summary>
    /// <param name="condition">Expected true.</param>
    /// <param name="message">Synthetic regression failure.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
