using System.Text;
using Nodalis.Core.AI;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Persists user-approved meeting assistant output as a separate Markdown document without modifying the source meeting.
/// </summary>
public sealed class MeetingAiResultDocumentService
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes the result writer for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public MeetingAiResultDocumentService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
    }

    /// <summary>
    /// Writes one approved assistant result next to its source meeting while keeping the source unchanged.
    /// </summary>
    /// <param name="sourcePath">The source meeting document.</param>
    /// <param name="promptId">The versioned prompt identifier.</param>
    /// <param name="approved">The user-approved and possibly edited result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created result document path.</returns>
    public async Task<string> WriteAsync(
            string sourcePath,
            string promptId,
            MeetingAiDraft approved,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            promptId);
        ArgumentNullException.ThrowIfNull(
            approved);

        string fullSourcePath =
            Path.GetFullPath(
                sourcePath);

        ValidateWorkspacePath(
            fullSourcePath);

        if (!File.Exists(
                fullSourcePath))
        {
            throw new FileNotFoundException(
                "Le compte-rendu source est introuvable.",
                fullSourcePath);
        }

        if (string.IsNullOrWhiteSpace(
                approved.Summary) &&
            approved.Proposals.Count ==
                0)
        {
            throw new InvalidOperationException(
                "Aucun élément IA n'a été accepté.");
        }

        string outputPath =
            BuildAvailableOutputPath(
                fullSourcePath);

        string content =
            Render(
                fullSourcePath,
                promptId,
                approved);

        await AtomicFileWriter.WriteAllTextAsync(
            outputPath,
            content,
            cancellationToken);

        return outputPath;
    }

    /// <summary>
    /// Renders accepted data with explicit source and prompt provenance.
    /// </summary>
    /// <param name="sourcePath">The source meeting path.</param>
    /// <param name="promptId">The versioned prompt identifier.</param>
    /// <param name="approved">The approved result.</param>
    /// <returns>The Markdown document.</returns>
    private string Render(
            string sourcePath,
            string promptId,
            MeetingAiDraft approved)
    {
        string relativeSource =
            Path.GetRelativePath(
                    _workspaceRoot,
                    sourcePath)
                .Replace(
                    '\\',
                    '/');

        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "<!-- nodalis-ai");
        builder.AppendLine(
            "source: " +
            relativeSource);
        builder.AppendLine(
            "prompt: " +
            promptId);
        builder.AppendLine(
            "-->");
        builder.AppendLine();
        builder.AppendLine(
            "# Synthèse IA — " +
            Path.GetFileNameWithoutExtension(
                sourcePath));
        builder.AppendLine();
        builder.AppendLine(
            "> Source : `" +
            relativeSource +
            "` · Prompt : `" +
            promptId +
            "`");
        builder.AppendLine();
        builder.AppendLine(
            "> Contenu proposé par un moteur local puis explicitement validé/édité par l'utilisateur.");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(
                approved.Summary))
        {
            builder.AppendLine(
                "## Résumé");
            builder.AppendLine();
            builder.AppendLine(
                approved.Summary.Trim());
            builder.AppendLine();
        }

        AppendSection(
            builder,
            "Décisions candidates",
            approved.Proposals,
            MeetingAiProposalKind.Decision);
        AppendSection(
            builder,
            "Actions candidates",
            approved.Proposals,
            MeetingAiProposalKind.Action);
        AppendSection(
            builder,
            "Questions ouvertes",
            approved.Proposals,
            MeetingAiProposalKind.OpenQuestion);
        AppendSection(
            builder,
            "Risques / points d'attention",
            approved.Proposals,
            MeetingAiProposalKind.Risk);

        return builder.ToString();
    }

    /// <summary>
    /// Appends one proposal category when at least one accepted item exists.
    /// </summary>
    /// <param name="builder">The destination Markdown builder.</param>
    /// <param name="heading">The section heading.</param>
    /// <param name="proposals">The accepted proposals.</param>
    /// <param name="kind">The proposal category to append.</param>
    private static void AppendSection(
            StringBuilder builder,
            string heading,
            IReadOnlyList<MeetingAiProposal> proposals,
            MeetingAiProposalKind kind)
    {
        MeetingAiProposal[] matching =
            proposals
                .Where(proposal =>
                    proposal.Kind ==
                    kind &&
                    !string.IsNullOrWhiteSpace(
                        proposal.Text))
                .ToArray();

        if (matching.Length ==
            0)
        {
            return;
        }

        builder.AppendLine(
            "## " +
            heading);
        builder.AppendLine();

        foreach (MeetingAiProposal proposal in matching)
        {
            builder.AppendLine(
                "- " +
                proposal.Text.Trim());
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Builds a non-destructive sibling file name for one assistant result.
    /// </summary>
    /// <param name="sourcePath">The source meeting path.</param>
    /// <returns>An available output path.</returns>
    private static string BuildAvailableOutputPath(
            string sourcePath)
    {
        string directory =
            Path.GetDirectoryName(
                sourcePath) ??
            throw new InvalidDataException(
                "Le compte-rendu source n'a pas de répertoire parent.");

        string baseName =
            Path.GetFileNameWithoutExtension(
                sourcePath);
        string candidate =
            Path.Combine(
                directory,
                baseName +
                "-synthese-ia.md");
        int suffix =
            2;

        while (File.Exists(
                   candidate))
        {
            candidate =
                Path.Combine(
                    directory,
                    baseName +
                    "-synthese-ia-" +
                    suffix +
                    ".md");
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// Prevents result creation from escaping the configured workspace.
    /// </summary>
    /// <param name="path">The candidate source path.</param>
    private void ValidateWorkspacePath(
            string path)
    {
        string relative =
            Path.GetRelativePath(
                _workspaceRoot,
                path);

        if (Path.IsPathRooted(
                relative) ||
            relative.Equals(
                "..",
                StringComparison.Ordinal) ||
            relative.StartsWith(
                ".." +
                Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(
                ".." +
                Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Le compte-rendu source se trouve hors du workspace.");
        }
    }
}
