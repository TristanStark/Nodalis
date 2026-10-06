using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Quality;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Quality;

internal static class ProjectCoverageSmokeTests
{
    /// <summary>
    /// Verifies explainable cross-source project coverage findings and source immutability.
    /// </summary>
    /// <param name="root">The smoke-test root directory.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string workspaceRoot =
            Path.Combine(
                root,
                "ProjectCoverageWorkspace");

        FileSystemWorkspaceStore store =
            new FileSystemWorkspaceStore(
                workspaceRoot);

        await store.InitializeAsync(
            "Project Coverage Workspace");

        Guid applicationId =
            Guid.NewGuid();
        Guid projectId =
            Guid.NewGuid();

        string applicationDirectory =
            Path.Combine(
                workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName,
                "Application Couverture");
        string projectDirectory =
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ProjectsDirectoryName,
                "Projet Couverture");

        string functionalDirectory =
            Path.Combine(
                projectDirectory,
                "Fonctionnel");
        string testsDirectory =
            Path.Combine(
                projectDirectory,
                "Tests");
        string milestonesDirectory =
            Path.Combine(
                projectDirectory,
                "Jalons");
        string glossaryDirectory =
            Path.Combine(
                projectDirectory,
                "Glossaire");
        string decisionsDirectory =
            Path.Combine(
                projectDirectory,
                "Décisions");

        Directory.CreateDirectory(
            functionalDirectory);
        Directory.CreateDirectory(
            testsDirectory);
        Directory.CreateDirectory(
            milestonesDirectory);
        Directory.CreateDirectory(
            glossaryDirectory);
        Directory.CreateDirectory(
            decisionsDirectory);

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        ApplicationManifest application =
            new ApplicationManifest
            {
                Id =
                    applicationId,
                Name =
                    "Application Couverture"
            };

        ProjectManifest project =
            new ProjectManifest
            {
                Id =
                    projectId,
                Name =
                    "Projet Couverture",
                ApplicationId =
                    applicationId,
                InitialComplexity =
                    ProjectComplexity.Medium,
                Sections =
                [
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Fonctionnel",
                        Order =
                            10,
                        TemplateKey =
                            "functional"
                    },
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Tests",
                        Order =
                            20,
                        TemplateKey =
                            "tests"
                    },
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Jalons",
                        Order =
                            30,
                        TemplateKey =
                            "milestones"
                    },
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Glossaire",
                        Order =
                            40,
                        TemplateKey =
                            "glossary"
                    }
                ]
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                applicationDirectory,
                WorkspaceLayout.ApplicationManifestFileName),
            JsonSerializer.Serialize(
                application,
                jsonOptions));

        string manifestPath =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                project,
                jsonOptions));

        string functionalPath =
            Path.Combine(
                functionalDirectory,
                "Fonctions.md");
        string testsPath =
            Path.Combine(
                testsDirectory,
                "Validation.md");
        string milestonesPath =
            Path.Combine(
                milestonesDirectory,
                "Jalons.md");
        string glossaryPath =
            Path.Combine(
                glossaryDirectory,
                "Glossaire.md");
        string tasksPath =
            Path.Combine(
                projectDirectory,
                "Tâches.md");
        string decisionPath =
            Path.Combine(
                decisionsDirectory,
                "2026-10-06 - Architecture.md");

        await File.WriteAllTextAsync(
            functionalPath,
            "# Fonctionnel\n\n## Authentification forte\n\nLe flux SSO utilise SSO pour ouvrir une session. SSO reste obligatoire.\n");
        await File.WriteAllTextAsync(
            testsPath,
            "# Tests\n\n## Validation existante\n\nUn scénario générique suffisamment détaillé pour constituer du contenu de test réel.\n");
        await File.WriteAllTextAsync(
            milestonesPath,
            "# Jalons\n\n| Jalon | Date cible | Statut |\n| --- | --- | --- |\n| Mise en production | 2026-10-10 | En cours |\n");
        await File.WriteAllTextAsync(
            glossaryPath,
            "# Glossaire\n\n- **API** : interface technique.\n");
        await File.WriteAllTextAsync(
            tasksPath,
            "# Tâches\n\n- [ ] Stabiliser migration | Priorité: Bloquante | Échéance: 2026-10-12 | Tags: #blocking\n");
        await File.WriteAllTextAsync(
            decisionPath,
            "# Architecture\n\n## Décision\n\nConserver le mode local.\n\n## Impacts\n\n_À compléter._\n\n## Sources et liens\n\n_À compléter._\n");

        Dictionary<string, string> before =
            Directory.EnumerateFiles(
                    projectDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .ToDictionary(
                    path =>
                        path,
                    path =>
                        File.ReadAllText(
                            path),
                    StringComparer.OrdinalIgnoreCase);

        ProjectCoverageAnalysisService service =
            new ProjectCoverageAnalysisService(
                workspaceRoot);

        ProjectCoverageReport report =
            await service.AnalyzeAsync(
                projectDirectory);

        string[] codes =
            report.Findings
                .Select(finding =>
                    finding.Code)
                .ToArray();

        Assert(
            codes.Contains(
                "FUNCTION_WITHOUT_TEST_REFERENCE",
                StringComparer.Ordinal) &&
            codes.Contains(
                "MILESTONE_WITHOUT_TASK",
                StringComparer.Ordinal) &&
            codes.Contains(
                "DECISION_WITHOUT_IMPACT",
                StringComparer.Ordinal) &&
            codes.Contains(
                "BLOCKING_TASK_AFTER_MILESTONE",
                StringComparer.Ordinal) &&
            codes.Contains(
                "FREQUENT_TERM_MISSING_GLOSSARY",
                StringComparer.Ordinal),
            "Coverage analysis must cross documentation, tests, milestones, tasks, decisions, and glossary data.");

        Assert(
            report.Findings.All(finding =>
                !string.IsNullOrWhiteSpace(
                    finding.RuleExplanation) &&
                finding.Sources.Count >
                    0),
            "Every coverage finding must explain its deterministic rule and expose the sources used.");

        foreach (KeyValuePair<string, string> entry in before)
        {
            Assert(
                File.Exists(
                    entry.Key) &&
                File.ReadAllText(
                    entry.Key) ==
                    entry.Value,
                "Coverage analysis must not modify project source files.");
        }
    }

    /// <summary>
    /// Throws when a coverage smoke-test condition is not satisfied.
    /// </summary>
    /// <param name="condition">Condition to verify.</param>
    /// <param name="message">Failure message.</param>
    private static void Assert(
            bool condition,
            string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }
}
