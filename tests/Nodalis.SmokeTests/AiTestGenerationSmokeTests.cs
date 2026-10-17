using System.Text.Json;
using Nodalis.Core.AI;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Persistence;

internal static class AiTestGenerationSmokeTests
{
    /// <summary>
    /// Verifies approved-only test persistence, editable Markdown preservation, and source provenance links.
    /// </summary>
    /// <param name="root">The smoke-test workspace root.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string projectDirectory =
            Path.Combine(
                root,
                WorkspaceLayout.ApplicationsDirectoryName,
                "AI Tests App",
                WorkspaceLayout.ProjectsDirectoryName,
                "AI Tests Project");
        string technicalDirectory =
            Path.Combine(
                projectDirectory,
                "Technique");
        string testsDirectory =
            Path.Combine(
                projectDirectory,
                "Tests");

        Directory.CreateDirectory(
            technicalDirectory);
        Directory.CreateDirectory(
            testsDirectory);

        string sourcePath =
            Path.Combine(
                technicalDirectory,
                "spec.md");
        const string sourceContent =
            "# Spécification\n\nLe mode hors-ligne est obligatoire.\n";
        await File.WriteAllTextAsync(
            sourcePath,
            sourceContent);

        ProjectManifest manifest =
            new ProjectManifest
            {
                Id =
                    Guid.NewGuid(),
                Name =
                    "AI Tests Project",
                ApplicationId =
                    Guid.NewGuid(),
                InitialComplexity =
                    ProjectComplexity.Simple,
                Sections =
                [
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Technique",
                        Order =
                            0,
                        TemplateKey =
                            "technical-basic"
                    },
                    new SectionManifest
                    {
                        Id =
                            Guid.NewGuid(),
                        Name =
                            "Tests",
                        Order =
                            1,
                        TemplateKey =
                            "tests-basic"
                    }
                ]
            };

        JsonSerializerOptions jsonOptions =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                WriteIndented =
                    true
            };

        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            JsonSerializer.Serialize(
                manifest,
                jsonOptions));

        string sourceLabel =
            Path.GetRelativePath(
                    root,
                    sourcePath)
                .Replace(
                    '\\',
                    '/');

        LocalAiContextItem source =
            new LocalAiContextItem
            {
                Label =
                    sourceLabel,
                Content =
                    sourceContent
            };

        AiTestGenerationOptions options =
            new AiTestGenerationOptions
            {
                Type =
                    AiTestType.Functional,
                Level =
                    AiTestLevel.Requirement
            };

        const string approvedMarkdown =
            "## Cas nominaux\n\n" +
            "### TEST-001 — Démarrage hors-ligne\n\n" +
            "**Source(s) :** spec.md\n\n" +
            "**Résultat attendu :** Le service démarre sans réseau.\n";

        GeneratedTestDocumentService service =
            new GeneratedTestDocumentService(
                root);

        string outputPath =
            await service.WriteAsync(
                sourcePath,
                "test-scenarios-v1",
                options,
                new[]
                {
                    source
                },
                approvedMarkdown);

        string outputContent =
            await File.ReadAllTextAsync(
                outputPath);

        Assert(
            Path.GetDirectoryName(
                outputPath) ==
            testsDirectory,
            "Approved generated tests must be inserted into the project Tests section.");
        Assert(
            outputContent.Contains(
                approvedMarkdown.Trim(),
                StringComparison.Ordinal),
            "User-edited Markdown must be preserved in the generated test document.");
        Assert(
            outputContent.Contains(
                "prompt: test-scenarios-v1",
                StringComparison.Ordinal) &&
            outputContent.Contains(
                "source: " +
                sourceLabel,
                StringComparison.Ordinal) &&
            outputContent.Contains(
                "../Technique/spec.md",
                StringComparison.Ordinal),
            "Generated tests must retain prompt provenance and a navigable link to each selected source.");
        Assert(
            await File.ReadAllTextAsync(
                sourcePath) ==
                sourceContent,
            "Generating tests must never modify the source documentation.");
    }

    /// <summary>
    /// Throws when a generated-test smoke-test condition fails.
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
