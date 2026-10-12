using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Meetings;

internal static class ProjectMeetingPromptSmokeTests
{
    /// <summary>
    /// Verifies section isolation, source-date resolution, read-only source handling and temporary prompt export.
    /// </summary>
    /// <param name="workspaceRoot">Root of the test workspace.</param>
    /// <returns>A task representing the smoke tests.</returns>
    public static async Task RunAsync(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string isolatedWorkspace = Path.Combine(
            Path.GetTempPath(),
            "Nodalis-ProjectPromptSmoke-" + Guid.NewGuid().ToString("N"));

        try
        {
            await RunIsolatedAsync(isolatedWorkspace);
        }
        finally
        {
            if (Directory.Exists(isolatedWorkspace))
            {
                Directory.Delete(isolatedWorkspace, recursive: true);
            }
        }
    }

    /// <summary>
    /// Runs prompt generation tests in a disposable workspace without contaminating other smoke suites.
    /// </summary>
    /// <param name="workspaceRoot">Isolated temporary workspace root.</param>
    /// <returns>A task representing the isolated checks.</returns>
    private static async Task RunIsolatedAsync(string workspaceRoot)
    {
        string applicationDirectory = Path.Combine(workspaceRoot, "Applications", "Prompt Demo");
        string projectDirectory = Path.Combine(
            applicationDirectory, "Projets", "Projet Prompt");
        string meetingsDirectory = Path.Combine(projectDirectory, "Réunions");
        Directory.CreateDirectory(meetingsDirectory);

        Guid applicationId = Guid.NewGuid();
        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        await File.WriteAllTextAsync(
            Path.Combine(applicationDirectory, ".application.json"),
            JsonSerializer.Serialize(
                new ApplicationManifest
                {
                    Id = applicationId,
                    Name = "Prompt Demo"
                },
                options));
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, ".project.json"),
            JsonSerializer.Serialize(
                new ProjectManifest
                {
                    Id = Guid.NewGuid(),
                    Name = "Projet Prompt",
                    ApplicationId = applicationId,
                    InitialComplexity = ProjectComplexity.Medium
                },
                options));

        string presentationPath = Path.Combine(projectDirectory, "Présentation.md");
        string presentation = "# Projet\n\n## Présentation\n\n" +
                              "Description métier du projet.\n\n" +
                              "### Architecture actuelle\n\nApplication locale.\n\n" +
                              "## Liens métier\n\nNe pas exporter ce contenu.\n";
        await File.WriteAllTextAsync(presentationPath, presentation);

        string meetingPath = Path.Combine(meetingsDirectory, "2026-09-01 - Comité.md");
        string meeting = "# Comité\n\n**Date :** 2026-10-09\n\n" +
                         "## Notes\n\nNe pas inclure les notes.\n\n" +
                         "## Résumé IA\n\n" +
                         "Décision actée : accepter la maquette.\n\n" +
                         "### Actions\n\n- [ ] Préparer les tests.\n\n" +
                         "## Contenu Outlook\n\nNe pas inclure Outlook.\n";
        await File.WriteAllTextAsync(meetingPath, meeting);

        ProjectMeetingPromptService service = new ProjectMeetingPromptService(workspaceRoot);
        (string FilePath, string Content) result = await service.GenerateAsync(meetingPath);
        try
        {
            Ensure(File.Exists(result.FilePath), "The generated prompt file must exist.");
            Ensure(result.FilePath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
                "Temporary prompts must be written outside the project workspace.");
            Ensure(string.Equals(await File.ReadAllTextAsync(result.FilePath), result.Content, StringComparison.Ordinal),
                "The temporary prompt must match the content offered for clipboard copy.");
            Ensure(result.Content.StartsWith("---" + Environment.NewLine + "Examine le contexte du projet", StringComparison.Ordinal),
                "The generated prompt must use the requested instruction.");
            Ensure(result.Content.Contains("Description métier du projet.", StringComparison.Ordinal) &&
                   result.Content.Contains("### Architecture actuelle", StringComparison.Ordinal),
                "The presentation body and its nested headings must be preserved.");
            Ensure(result.Content.Contains("Décision actée : accepter la maquette.", StringComparison.Ordinal) &&
                   result.Content.Contains("- [ ] Préparer les tests.", StringComparison.Ordinal),
                "The AI summary must preserve decisions and actions.");
            Ensure(result.Content.Contains("RESUME DE LA REUNION DU 09/10/2026:", StringComparison.Ordinal),
                "The date in the document must take precedence over the filename.");
            Ensure(!result.Content.Contains("Ne pas inclure Outlook.", StringComparison.Ordinal) &&
                   !result.Content.Contains("Ne pas exporter ce contenu.", StringComparison.Ordinal) &&
                   !result.Content.Contains("Ne pas inclure les notes.", StringComparison.Ordinal),
                "Unrelated Markdown sections must not be copied into the prompt.");
            Ensure(string.Equals(await File.ReadAllTextAsync(meetingPath), meeting, StringComparison.Ordinal) &&
                   string.Equals(await File.ReadAllTextAsync(presentationPath), presentation, StringComparison.Ordinal),
                "Generating a prompt must never modify source Markdown documents.");
        }
        finally
        {
            File.Delete(result.FilePath);
        }

        string fallbackPath = Path.Combine(meetingsDirectory, "2026-10-10 - Projet.md");
        await File.WriteAllTextAsync(fallbackPath, "## Résumé IA\n\nAction depuis le nom du fichier.\n");
        (string FilePath, string Content) fallback = await service.GenerateAsync(fallbackPath);
        try
        {
            Ensure(fallback.Content.Contains("RESUME DE LA REUNION DU 10/10/2026:", StringComparison.Ordinal),
                "Filename ISO date should be used when the meeting metadata is absent.");
        }
        finally
        {
            File.Delete(fallback.FilePath);
        }

        string missingSummary = Path.Combine(meetingsDirectory, "2026-10-11 - Vide.md");
        await File.WriteAllTextAsync(missingSummary, "## Résumé IA\n\n## Contenu Outlook\n");
        await ExpectFailureAsync(service, missingSummary, "An empty AI summary must be rejected.");

        string nonMeeting = Path.Combine(projectDirectory, "2026-10-11 - Autre.md");
        await File.WriteAllTextAsync(nonMeeting, "## Résumé IA\n\nContenu hors Réunions.\n");
        await ExpectFailureAsync(service, nonMeeting, "A document outside the Réunions directory must be rejected.");

        string externalFile = Path.Combine(Path.GetTempPath(), "Nodalis-Outside-" + Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(externalFile, "## Résumé IA\n\nNe pas accepter.\n");
        try
        {
            await ExpectFailureAsync(service, externalFile, "Files outside the workspace must be rejected.");
        }
        finally
        {
            File.Delete(externalFile);
        }
    }

    /// <summary>
    /// Asserts that invalid meeting input fails without producing an exported prompt.
    /// </summary>
    /// <param name="service">Prompt generator under test.</param>
    /// <param name="meetingPath">Invalid meeting document.</param>
    /// <param name="message">Failure description.</param>
    /// <returns>A task representing the negative test.</returns>
    private static async Task ExpectFailureAsync(
        ProjectMeetingPromptService service, string meetingPath, string message)
    {
        bool failed = false;
        try
        {
            await service.GenerateAsync(meetingPath);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            failed = true;
        }

        Ensure(failed, message);
    }

    /// <summary>
    /// Fails the regression suite when a condition does not hold.
    /// </summary>
    /// <param name="condition">Condition to verify.</param>
    /// <param name="message">Failure text.</param>
    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
