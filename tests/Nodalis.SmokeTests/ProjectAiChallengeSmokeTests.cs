using Nodalis.Core.AI;
using Nodalis.Infrastructure.AI;
using Nodalis.Infrastructure.Persistence;

internal static class ProjectAiChallengeSmokeTests
{
    /// <summary>
    /// Verifies that project challenge context is explicit, bounded to the current project, and read-only.
    /// </summary>
    /// <param name="root">The smoke-test workspace root.</param>
    /// <returns>A task representing the verification.</returns>
    public static async Task RunAsync(
            string root)
    {
        string projectDirectory =
            Path.Combine(
                root,
                "Applications",
                "App",
                "Projets",
                "Challenge");

        Directory.CreateDirectory(
            projectDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            "{}");

        string specificationDirectory =
            Path.Combine(
                projectDirectory,
                "Technique");
        Directory.CreateDirectory(
            specificationDirectory);

        string currentPath =
            Path.Combine(
                specificationDirectory,
                "spec.md");
        string relatedPath =
            Path.Combine(
                projectDirectory,
                "tests.md");

        const string currentContent =
            "# Spécification\n\nLe service doit rester local.\n";
        const string relatedContent =
            "# Tests\n\n- Vérifier le fonctionnement hors-ligne.\n";

        await File.WriteAllTextAsync(
            currentPath,
            currentContent);
        await File.WriteAllTextAsync(
            relatedPath,
            relatedContent);

        string nestedDirectory =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.SubProjectsDirectoryName,
                "Enfant");
        Directory.CreateDirectory(
            nestedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                nestedDirectory,
                "secret.md"),
            "# Sous-projet\nNe doit pas être envoyé implicitement.\n");

        string attachmentsDirectory =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.AttachmentsDirectoryName);
        Directory.CreateDirectory(
            attachmentsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(
                attachmentsDirectory,
                "attachment.md"),
            "# Pièce jointe\n");

        ProjectAiContextService service =
            new ProjectAiContextService(
                root);

        IReadOnlyList<LocalAiContextItem> candidates =
            await service.LoadCandidatesAsync(
                currentPath);

        Assert(
            candidates.Count ==
                2,
            "Project challenge context must exclude nested projects and attachment storage.");
        Assert(
            candidates[0].Label.EndsWith(
                "Technique/spec.md",
                StringComparison.Ordinal) &&
            candidates[0].Content ==
                currentContent,
            "The current specification must be the first explicit context candidate.");
        Assert(
            candidates.Any(candidate =>
                candidate.Label.EndsWith(
                    "tests.md",
                    StringComparison.Ordinal) &&
                candidate.Content ==
                    relatedContent),
            "Sibling project documentation must be offered as selectable context.");
        Assert(
            !candidates.Any(candidate =>
                candidate.Label.Contains(
                    "secret.md",
                    StringComparison.Ordinal) ||
                candidate.Label.Contains(
                    "attachment.md",
                    StringComparison.Ordinal)),
            "Nested projects and attachments must never enter the selectable context implicitly.");

        Assert(
            await File.ReadAllTextAsync(
                currentPath) ==
                currentContent &&
            await File.ReadAllTextAsync(
                relatedPath) ==
                relatedContent,
            "Preparing project AI context must not modify source documents.");
    }

    /// <summary>
    /// Throws when a project AI challenge smoke-test condition fails.
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
