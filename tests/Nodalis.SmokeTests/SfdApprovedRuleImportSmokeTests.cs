using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Entirely fictional RM publication and same-response link regressions.</summary>
internal static class SfdApprovedRuleImportSmokeTests
{
    /// <summary>Verifies one RM, durable replay, no duplicates, and reused EXU/EXS SC links.</summary>
    public static async Task RunAsync(string root)
    {
        string workspace = Path.Combine(root, "Sfd-approved-rule");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Démonstration de règles fictives");
        string app = Path.Combine(workspace, "Applications", "Fictive");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Fictive",
            DisplayName = "Fictive",
            ParentDirectory = app
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Démonstration contrôlée",
                Kind = ProjectKind.WithSpecifications,
                Complexity = ProjectComplexity.Simple,
                Target = target
            });
        SfdDocumentRegistryService documents = new(workspace);
        SfdDocumentEntry pm = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Process,
            "Processus fictif de contrôle");
        SfdDocumentEntry act = await documents.CreateAsync(
            project.ProjectDirectory, SfdDocumentKind.Activity,
            "Contrôle d'une demande fictive", pm.Card.Id);
        SfdImportReviewService reviewer = new(workspace);
        SfdApprovedRuleImportService publisher = new(workspace);
        SfdApprovedRequirementImportService requirementPublisher = new(workspace);
        SfdStructuredAuthoringService author = new(workspace);

        string json = Response(act.Card.Id);
        SfdImportReviewSnapshot initial = await reviewer.ReviewAsync(
            project.ProjectDirectory, json);
        Require(initial.Items.Single(item => item.ClientKey == "rm-fictive")
                .Status == SfdImportReviewStatus.NewCandidate &&
            initial.Items.Where(item => item.ClientKey != "rm-fictive")
                .All(item => item.Status == SfdImportReviewStatus.NeedsResolution),
            "The proposed RM must be published before dependent SC may reference it.");
        SfdApprovedRuleResult rm = await publisher.PublishAsync(
            project.ProjectDirectory, json, initial, "rm-fictive");
        SfdBusinessRuleSnapshot catalog = await author.ReadRulesAsync(
            project.ProjectDirectory);
        Require(!rm.AlreadyApplied && rm.Reference == "RM-0001" &&
            catalog.Rules.Count == 1 &&
            catalog.Rules[0].Id == rm.Id &&
            catalog.Rules[0].Certainty == SfdAssertionCertainty.ToConfirm &&
            string.IsNullOrEmpty(catalog.Rules[0].Source),
            "An AI rule can only enter the canonical catalog as unconfirmed, without invented source.");

        SfdApprovedRuleResult replay = await publisher.PublishAsync(
            project.ProjectDirectory, json, initial, "rm-fictive");
        Require(replay.AlreadyApplied && replay.Id == rm.Id &&
            (await author.ReadRulesAsync(project.ProjectDirectory)).Rules.Count == 1,
            "Same approved rule must be idempotent and retain its original GUID.");

        SfdImportReviewSnapshot published = await reviewer.ReviewAsync(
            project.ProjectDirectory, json);
        Require(published.Items.Single(item => item.ClientKey == "rm-fictive")
                .Status == SfdImportReviewStatus.LinkCandidate &&
            published.Items.Single(item => item.ClientKey == "rm-fictive")
                .ExistingId == rm.Id &&
            published.Items.Where(item => item.ClientKey != "rm-fictive")
                .All(item => item.Status == SfdImportReviewStatus.NewCandidate),
            "After publication the same-run client key must resolve to a real RM GUID.");
        SfdApprovedRequirementResult exu = await requirementPublisher.PublishAsync(
            project.ProjectDirectory, json, published, "exu-fictive");
        SfdApprovedRequirementResult exs = await requirementPublisher.PublishAsync(
            project.ProjectDirectory, json,
            await reviewer.ReviewAsync(project.ProjectDirectory, json),
            "exs-fictive");
        IReadOnlyList<SfdConditionBlock> exuBlocks = SfdConditionBlockCodec.Parse(
            await File.ReadAllTextAsync(exu.AbsolutePath));
        IReadOnlyList<SfdConditionBlock> exsBlocks = SfdConditionBlockCodec.Parse(
            await File.ReadAllTextAsync(exs.AbsolutePath));
        Require(exuBlocks.Single().Subconditions.Single().BusinessRuleIds
                .SequenceEqual([rm.Id]) &&
            exsBlocks.Single().Subconditions.Single().BusinessRuleIds
                .SequenceEqual([rm.Id]) &&
            (await author.ReadRulesAsync(project.ProjectDirectory)).Rules.Count == 1,
            "Two separate requirement SC must reference one canonical RM without duplicating its definition.");

        string catalogPath = Path.Combine(project.ProjectDirectory,
            "DOCUMENTATION", "liste des règles métiers.md");
        await File.AppendAllTextAsync(catalogPath,
            "\n## Une annotation métier a été ajoutée après publication\n");
        bool changedRefused = false;
        try
        {
            await publisher.PublishAsync(
                project.ProjectDirectory, json, initial, "rm-fictive");
        }
        catch (InvalidDataException)
        {
            changedRefused = true;
        }

        Require(changedRefused &&
            File.ReadAllText(catalogPath).Contains("annotation métier",
                StringComparison.Ordinal),
            "A modified RM catalog must never be overwritten during replay.");
        Require((await documents.ListAsync(project.ProjectDirectory)).Entries.Count == 4,
            "One PM, one ACT, one EXU and one EXS must exist, no extra objects.");

        string similar = json.Replace("run-RM-fictive", "run-RM-seconde",
            StringComparison.Ordinal);
        SfdImportReviewSnapshot similarReview = await reviewer.ReviewAsync(
            project.ProjectDirectory, similar);
        Require(similarReview.Items.Single(x => x.ClientKey == "rm-fictive")
                .Status == SfdImportReviewStatus.NeedsResolution,
            "Identical RM text in a different response must trigger reconciliation.");
    }

    private static string Response(Guid activityId) =>
        JsonSerializer.Serialize(new
        {
            schema = SfdImportContract.Schema,
            step = "sfd-02", runId = "run-RM-fictive",
            proposals = new object[]
            {
                new
                {
                    clientKey = "rm-fictive", kind = "businessRule",
                    operation = "create", certainty = "derived",
                    title = "Toute demande fictive contient un code non vide.",
                    description = "Règle proposée sans source vérifiée.",
                    sourceKeys = Array.Empty<string>(),
                    activityKeys = Array.Empty<string>(),
                    requirementKeys = Array.Empty<string>(),
                    conditions = Array.Empty<object>()
                },
                Requirement("exu-fictive", "userRequirement",
                    "Consulter le résultat fictif", activityId,
                    "Quand une demande fictive est reçue, l'utilisateur doit pouvoir consulter son résultat.",
                    "En tant que conseiller fictif, je veux consulter le résultat afin de vérifier le dossier sur [CANAL À CONFIRMER]."),
                Requirement("exs-fictive", "systemRequirement",
                    "Produire une validation fictive", activityId,
                    "Quand une demande fictive arrive, le système doit permettre de produire une validation.",
                    "La validation fictive contient le code non vide.")
            }
        });

    private static object Requirement(string key, string kind, string title,
        Guid activityId, string condition, string subcondition) => new
    {
        clientKey = key, kind, operation = "create", certainty = "derived",
        title, description = "Exigence fictive proposée.",
        parentKey = activityId.ToString("D"),
        sourceKeys = Array.Empty<string>(),
        activityKeys = Array.Empty<string>(),
        requirementKeys = Array.Empty<string>(),
        conditions = new[]
        {
            new
            {
                clientKey = "c-" + key,
                statement = condition,
                subconditions = new[]
                {
                    new
                    {
                        clientKey = "sc-" + key,
                        statement = subcondition,
                        businessRuleKeys = new[] { "rm-fictive" }
                    }
                }
            }
        }
    };

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
