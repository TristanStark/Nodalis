using System.Text;
using Nodalis.Core.AI;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Persists explicitly approved local-AI test scenarios into the required project Tests section.
/// </summary>
public sealed class GeneratedTestDocumentService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceProjectStructureService _structureService;

    /// <summary>
    /// Initializes the generated-test writer for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public GeneratedTestDocumentService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _structureService =
            new WorkspaceProjectStructureService(
                _workspaceRoot);
    }

    /// <summary>
    /// Writes user-approved editable Markdown into the nearest project's Tests section with source provenance.
    /// </summary>
    /// <param name="contextPath">The document or project context used to resolve the destination project.</param>
    /// <param name="promptId">The versioned local prompt identifier.</param>
    /// <param name="options">The explicit test-generation options.</param>
    /// <param name="sources">The exact selected source documents.</param>
    /// <param name="approvedMarkdown">The user-reviewed Markdown to insert.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created Markdown test document path.</returns>
    public async Task<string> WriteAsync(
            string contextPath,
            string promptId,
            AiTestGenerationOptions options,
            IReadOnlyList<LocalAiContextItem> sources,
            string approvedMarkdown,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            contextPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            promptId);
        ArgumentNullException.ThrowIfNull(
            options);
        ArgumentNullException.ThrowIfNull(
            sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            approvedMarkdown);

        string? projectDirectory =
            _structureService.GetProjectDirectoryForContext(
                contextPath);

        if (projectDirectory is null)
        {
            throw new InvalidOperationException(
                "La génération de tests nécessite un contexte rattaché à un projet.");
        }

        ProjectStructureState state =
            await _structureService.GetStructureAsync(
                projectDirectory,
                cancellationToken);

        ProjectSectionState? testsSection =
            state.Sections.FirstOrDefault(section =>
                string.Equals(
                    ProjectRequiredSectionPolicy.GetRequiredRole(
                        section.TemplateKey),
                    "Tests",
                    StringComparison.OrdinalIgnoreCase)) ??
            state.Sections.FirstOrDefault(section =>
                string.Equals(
                    section.Name,
                    "Tests",
                    StringComparison.CurrentCultureIgnoreCase));

        if (testsSection is null)
        {
            throw new InvalidDataException(
                "La section Tests requise est introuvable dans ce projet.");
        }

        Directory.CreateDirectory(
            testsSection.DirectoryPath);

        string outputPath =
            BuildAvailableOutputPath(
                testsSection.DirectoryPath);

        string content =
            Render(
                outputPath,
                promptId,
                options,
                sources,
                approvedMarkdown);

        await AtomicFileWriter.WriteAllTextAsync(
            outputPath,
            content,
            cancellationToken);

        return outputPath;
    }

    /// <summary>
    /// Renders provenance, clickable source links, generation options, and the approved Markdown body.
    /// </summary>
    /// <param name="outputPath">The destination document path.</param>
    /// <param name="promptId">The prompt identifier.</param>
    /// <param name="options">The selected generation options.</param>
    /// <param name="sources">The selected context sources.</param>
    /// <param name="approvedMarkdown">The approved editable body.</param>
    /// <returns>The final Markdown document.</returns>
    private string Render(
            string outputPath,
            string promptId,
            AiTestGenerationOptions options,
            IReadOnlyList<LocalAiContextItem> sources,
            string approvedMarkdown)
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "<!-- nodalis-ai");
        builder.AppendLine(
            "prompt: " +
            promptId);
        builder.AppendLine(
            "test-type: " +
            options.Type);
        builder.AppendLine(
            "test-level: " +
            options.Level);

        foreach (LocalAiContextItem source in sources)
        {
            builder.AppendLine(
                "source: " +
                source.Label);
        }

        builder.AppendLine(
            "-->");
        builder.AppendLine();
        builder.AppendLine(
            "# Scénarios de tests générés");
        builder.AppendLine();
        builder.AppendLine(
            "> Proposition générée localement puis explicitement relue et validée avant insertion.");
        builder.AppendLine();
        builder.AppendLine(
            "**Type :** " +
            options.TypeLabel +
            "  ");
        builder.AppendLine(
            "**Niveau :** " +
            options.LevelLabel +
            "  ");
        builder.AppendLine(
            "**Prompt :** " +
            promptId);
        builder.AppendLine();
        builder.AppendLine(
            "## Sources");
        builder.AppendLine();

        string outputDirectory =
            Path.GetDirectoryName(
                outputPath) ??
            throw new InvalidDataException(
                "Le document de tests n'a pas de répertoire parent.");

        foreach (LocalAiContextItem source in sources)
        {
            string sourcePath =
                Path.GetFullPath(
                    Path.Combine(
                        _workspaceRoot,
                        source.Label.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            string relativeLink =
                Path.GetRelativePath(
                        outputDirectory,
                        sourcePath)
                    .Replace(
                        '\\',
                        '/');

            builder.AppendLine(
                "- [" +
                source.Label +
                "](" +
                relativeLink +
                ")");
        }

        builder.AppendLine();
        builder.AppendLine(
            "## Scénarios proposés");
        builder.AppendLine();
        builder.AppendLine(
            approvedMarkdown.Trim());
        builder.AppendLine();

        return builder.ToString();
    }

    /// <summary>
    /// Builds a collision-safe filename without overwriting previously approved generations.
    /// </summary>
    /// <param name="testsDirectory">The Tests section directory.</param>
    /// <returns>An available Markdown path.</returns>
    private static string BuildAvailableOutputPath(
            string testsDirectory)
    {
        string stamp =
            DateTime.Now.ToString(
                "yyyyMMdd-HHmmss");
        string candidate =
            Path.Combine(
                testsDirectory,
                "scenarios-ia-" +
                stamp +
                ".md");
        int suffix =
            2;

        while (File.Exists(
                   candidate))
        {
            candidate =
                Path.Combine(
                    testsDirectory,
                    "scenarios-ia-" +
                    stamp +
                    "-" +
                    suffix +
                    ".md");
            suffix++;
        }

        return candidate;
    }
}
