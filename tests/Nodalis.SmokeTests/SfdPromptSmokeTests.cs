using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;

/// <summary>Synthetic four-step SFD prompt and strict contract regression tests.</summary>
internal static class SfdPromptSmokeTests
{
    /// <summary>Checks 40 sources, bundled contract, no DOCX and strict role validation.</summary>
    /// <param name="root">Disposable suite root.</param>
    /// <returns>Completed SFD prompt regression suite.</returns>
    public static async Task RunAsync(string root)
    {
        SfdImportEnvelope example = SfdImportContract.Parse(
            SfdImportContract.ExampleJson);
        Require(example.Proposals.Count == 2 &&
            example.Proposals[0].Kind == "businessProcess" &&
            example.Proposals[1].ParentKey == "pm-demo",
            "The schema's step 01 example must parse without invented GUIDs.");

        const string stepTwo = """
        {
          "schema":"nodalis.sfd.import.v1", "step":"sfd-02",
          "runId":"test002",
          "proposals":[
            {
              "clientKey":"req01", "kind":"userRequirement",
              "operation":"create", "certainty":"toConfirm",
              "title":"Obtenir un résultat sans écran",
              "description":"Une extraction fictive.",
              "sourceKeys":["s001"], "activityKeys":[],
              "requirementKeys":[],
              "conditions":[{
                "clientKey":"c01",
                "statement":"Quand une demande fictive est reçue, l'utilisateur doit pouvoir obtenir un fichier.",
                "subconditions":[{
                  "clientKey":"sc01",
                  "statement":"En tant que conseiller fictif, je veux recevoir le fichier afin de vérifier le résultat sur [CANAL À CONFIRMER].",
                  "businessRuleKeys":["rm01"]
                }]
              }]
            },
            {
              "clientKey":"rm01", "kind":"businessRule",
              "operation":"create", "certainty":"derived",
              "title":"Le résultat fictif comporte une date.",
              "description":"Règle proposée, source à confirmer.",
              "sourceKeys":["s001"], "activityKeys":[],
              "requirementKeys":[], "conditions":[]
            }
          ]
        }
        """;
        SfdImportEnvelope needs = SfdImportContract.Parse(stepTwo);
        Require(needs.Proposals.Count == 2 &&
            needs.Proposals[0].Conditions[0].Subconditions[0]
                .BusinessRuleKeys.SequenceEqual(["rm01"]),
            "Step 02 must allow EXU results without UI and a single canonical RM reference.");

        const string stepThree = """
        {
          "schema":"nodalis.sfd.import.v1", "step":"sfd-03",
          "runId":"test003",
          "proposals":[{
            "clientKey":"fct01", "kind":"feature",
            "operation":"create", "certainty":"derived",
            "title":"Extraire les résultats en tâche automatique",
            "description":"Capacité de solution sans interface.",
            "uiMode":"withoutUi",
            "sourceKeys":[], "activityKeys":["act01","act02"],
            "requirementKeys":[], "conditions":[]
          }]
        }
        """;
        Require(SfdImportContract.Parse(stepThree).Proposals[0]
            .UiMode == "withoutUi",
            "Step 03 must support a single FCT covering two ACT without UI.");

        const string stepFour = """
        {
          "schema":"nodalis.sfd.import.v1", "step":"sfd-04",
          "runId":"test004",
          "proposals":[{
            "clientKey":"emo01", "kind":"implementationRequirement",
            "operation":"create", "certainty":"toConfirm",
            "title":"Assembler le fichier fictif",
            "description":"Données, sortie et erreur à détailler.",
            "parentKey":"fct01",
            "sourceKeys":[], "activityKeys":[],
            "requirementKeys":["req01"],
            "conditions":[{
              "clientKey":"c04",
              "statement":"À réception des données fictives, produire une sortie contrôlée.",
              "subconditions":[{
                "clientKey":"sc04",
                "statement":"Le champ fictif doit être contrôlé et journalisé.",
                "businessRuleKeys":["rm01"]
              }]
            }]
          }]
        }
        """;
        Require(SfdImportContract.Parse(stepFour).Proposals[0]
            .Kind == "implementationRequirement",
            "Step 04 must produce a single canonical EMO kind with RM links.");

        Reject(() => SfdImportContract.Parse(
            SfdImportContract.ExampleJson.Replace(
                "nodalis.sfd.import.v1", "nodalis.import.v1",
                StringComparison.Ordinal)),
            "Old v2 import contract must not be accepted as SFD.");
        Reject(() => SfdImportContract.Parse(
            stepThree.Replace("\"withoutUi\"", "\"unknownUi\"",
                StringComparison.Ordinal)),
            "Unknown interface mode must fail.");
        Reject(() => SfdImportContract.Parse(
            stepTwo.Replace("\"businessRule\"", "\"useCaseSystemRequirement\"",
                StringComparison.Ordinal)),
            "The former three use-case categories are not canonical EMO.");
        Reject(() => SfdImportContract.Parse(
            stepThree.Replace("\"sourceKeys\":[]", "\"sourceKeys\":[],\"status\":\"approved\"",
                StringComparison.Ordinal)),
            "Unknown approval/status properties must fail closed.");
        Reject(() => SfdImportContract.Parse(
            stepThree.Replace("\"clientKey\":\"fct01\"",
                "\"clientKey\":\"fct01\",\"clientKey\":\"fct02\"",
                StringComparison.Ordinal)),
            "Duplicate JSON properties must be rejected.");

        string workspace = Path.Combine(root, "Sfd-prompts-workspace");
        await new FileSystemWorkspaceStore(workspace)
            .InitializeAsync("Sources SFD fictives");
        string app = Path.Combine(workspace, "Applications", "Example");
        Directory.CreateDirectory(app);
        ProjectCreationTarget target = new()
        {
            ApplicationId = Guid.NewGuid(),
            ApplicationName = "Example",
            ParentDirectory = app,
            DisplayName = "Example"
        };
        FileSystemProjectCreator creator = new(workspace);
        ProjectCreationResult project = await creator.CreateAsync(
            new ProjectCreationRequest
            {
                Name = "Fictional export",
                Complexity = ProjectComplexity.Simple,
                Kind = ProjectKind.WithSpecifications,
                Target = target
            });
        SfdJourneyService journeyService = new(workspace);
        ProjectJourneySnapshot initial = await journeyService.InitializeAsync(
            project.ProjectDirectory);
        string sourcesDirectory = Path.Combine(project.ProjectDirectory,
            "DOCUMENTATION");
        ProjectJourneySourceService resolver = new(workspace);
        List<string> references = [];
        for (int i = 1; i <= 40; i++)
        {
            string file = Path.Combine(sourcesDirectory,
                "source-fictive-" + i.ToString("D2") + ".md");
            await File.WriteAllTextAsync(file,
                "# Note fictive " + i + "\n\nExemple de données fictives.");
            references.Add(resolver.GetReferenceForExistingFile(file));
        }

        ProjectJourneyStep stage = initial.Document.Steps[0];
        ProjectJourneyOperation operation = stage.Operations[0];
        ProjectJourneyDocument updated = ProjectJourneyEditor.AddSources(
            initial.Document, stage.Id, operation.Id, references);
        await journeyService.SaveAsync(project.ProjectDirectory,
            updated, initial.Sha256);
        SfdPromptPreparationService preparer = new(workspace);
        SfdPreparedPrompt prepared = await preparer.PrepareAsync(
            project.ProjectDirectory, stage.Id, operation.Id);
        Require(prepared.IncludedSources == 40 &&
            prepared.Content.Contains(SfdImportContract.JsonSchema,
                StringComparison.Ordinal) &&
            prepared.Content.Contains("source-fictive-40.md",
                StringComparison.Ordinal) &&
            prepared.Content.Contains("SHA-256",
                StringComparison.Ordinal) &&
            prepared.Content.Contains("sfd-01",
                StringComparison.Ordinal),
            "40 selected sources must be included with SHA and exact contract.");

        ProjectJourneySnapshot current = (await journeyService.ReadAsync(
            project.ProjectDirectory))!;
        ProjectJourneyDocument withUnsupported = ProjectJourneyEditor.AddSource(
            current.Document, stage.Id, operation.Id,
            "Applications/Example/fichier-fictif.docx");
        await journeyService.SaveAsync(project.ProjectDirectory,
            withUnsupported, current.Sha256);
        SfdPreparedPrompt unsupported = await preparer.PrepareAsync(
            project.ProjectDirectory, stage.Id, operation.Id);
        Require(unsupported.IncludedSources == 40 &&
            unsupported.Warnings.Any(warning =>
                warning.Contains(".docx", StringComparison.Ordinal)),
            "DOCX references must never be decoded, converted or included.");

        SfdPreparedPrompt batch = await preparer.PrepareAllAsync(
            project.ProjectDirectory);
        Require(batch.Content.Contains("sfd-02", StringComparison.Ordinal) &&
            batch.Content.Contains("sfd-03", StringComparison.Ordinal) &&
            batch.Content.Contains("sfd-04", StringComparison.Ordinal) &&
            batch.Content.Split(
                "### JSON Schema EXHAUSTIF", StringSplitOptions.None).Length == 5 &&
            batch.Warnings.Any(warning =>
                warning.Contains("Pré-requis 02", StringComparison.Ordinal)) &&
            batch.Warnings.Any(warning =>
                warning.Contains("Pré-requis 04", StringComparison.Ordinal)),
            "Batch preparation must carry FOUR independent complete JSON contracts " +
            "and warn about absent prerequisite business documents.");

        string[] generatedMarkdown =
            Directory.GetFiles(project.ProjectDirectory, "*.md",
                SearchOption.TopDirectoryOnly);
        Require(!File.Exists(Path.Combine(project.ProjectDirectory,
                ".project-journey.json")) &&
            generatedMarkdown.All(file =>
                !file.EndsWith("fichier-fictif.md", StringComparison.Ordinal)),
            "Prompt preparation must not create classic journeys or convert DOCX.");
    }

    /// <summary>Asserts strict parser rejection for a deliberately invalid fixture.</summary>
    /// <param name="action">Invalid envelope to parse.</param>
    /// <param name="message">Regression failure.</param>
    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidDataException or
            ArgumentException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Asserts one synthetic contract invariant.</summary>
    /// <param name="value">Expected truth.</param>
    /// <param name="message">Regression failure.</param>
    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }
}
