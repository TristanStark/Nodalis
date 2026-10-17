using Nodalis.Core.AI;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Builds explicit, readable local-AI context candidates from the current document or its nearest project.
/// </summary>
public sealed class ProjectAiContextService
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes the context service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public ProjectAiContextService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
    }

    /// <summary>
    /// Loads candidate Markdown sources without sending or modifying any workspace data.
    /// </summary>
    /// <param name="contextPath">The current file or directory context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Readable context candidates ordered with the current document first.</returns>
    public async Task<IReadOnlyList<LocalAiContextItem>> LoadCandidatesAsync(
            string contextPath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            contextPath);

        string fullContextPath =
            Path.GetFullPath(
                contextPath);

        ValidateWorkspacePath(
            fullContextPath);

        string? currentDocumentPath =
            File.Exists(
                    fullContextPath) &&
                string.Equals(
                    Path.GetExtension(
                        fullContextPath),
                    ".md",
                    StringComparison.OrdinalIgnoreCase)
                ? fullContextPath
                : null;

        string? projectDirectory =
            ResolveProjectDirectory(
                fullContextPath);

        List<string> paths =
            new List<string>();

        if (currentDocumentPath is not null)
        {
            paths.Add(
                currentDocumentPath);
        }

        if (projectDirectory is not null)
        {
            IEnumerable<string> projectDocuments =
                Directory
                    .EnumerateFiles(
                        projectDirectory,
                        "*.md",
                        SearchOption.AllDirectories)
                    .Where(path =>
                        IsProjectDocument(
                            projectDirectory,
                            path))
                    .OrderBy(
                        path =>
                            NormalizeRelativePath(
                                Path.GetRelativePath(
                                    projectDirectory,
                                    path)),
                        StringComparer.CurrentCultureIgnoreCase);

            foreach (string path in projectDocuments)
            {
                string fullPath =
                    Path.GetFullPath(
                        path);

                if (!paths.Contains(
                        fullPath,
                        StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(
                        fullPath);
                }
            }
        }

        List<LocalAiContextItem> result =
            new List<LocalAiContextItem>();

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content =
                await File.ReadAllTextAsync(
                    path,
                    cancellationToken);

            result.Add(
                new LocalAiContextItem
                {
                    Label =
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                _workspaceRoot,
                                path)),
                    Content =
                        content
                });
        }

        return result;
    }

    /// <summary>
    /// Resolves the nearest project manifest ancestor for one context.
    /// </summary>
    /// <param name="contextPath">A file or directory inside the workspace.</param>
    /// <returns>The nearest project directory, or null when the context is not inside a project.</returns>
    private string? ResolveProjectDirectory(
            string contextPath)
    {
        string? current =
            File.Exists(
                contextPath)
                ? Path.GetDirectoryName(
                    contextPath)
                : contextPath;

        while (!string.IsNullOrWhiteSpace(
                   current) &&
               IsInsideOrEqual(
                   current,
                   _workspaceRoot))
        {
            if (File.Exists(
                    Path.Combine(
                        current,
                        WorkspaceLayout.ProjectManifestFileName)))
            {
                return current;
            }

            if (string.Equals(
                    current,
                    _workspaceRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current =
                Directory.GetParent(
                    current)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a Markdown file belongs directly to the project rather than a nested project or attachment store.
    /// </summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="path">The Markdown file path.</param>
    /// <returns>Whether the document can be proposed as project context.</returns>
    private static bool IsProjectDocument(
            string projectDirectory,
            string path)
    {
        string relative =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    projectDirectory,
                    path));

        string[] segments =
            relative.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length <=
            1)
        {
            return true;
        }

        string first =
            segments[0];

        return !string.Equals(
                   first,
                   WorkspaceLayout.SubProjectsDirectoryName,
                   StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(
                   first,
                   WorkspaceLayout.AttachmentsDirectoryName,
                   StringComparison.OrdinalIgnoreCase) &&
               !first.StartsWith(
                   ".",
                   StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates that a candidate context remains inside the configured workspace.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    private void ValidateWorkspacePath(
            string path)
    {
        if (!IsInsideOrEqual(
                path,
                _workspaceRoot))
        {
            throw new InvalidDataException(
                "Le contexte à analyser se trouve hors du workspace.");
        }
    }

    /// <summary>
    /// Tests whether a path is inside or equal to a root path.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="root">The root path.</param>
    /// <returns>Whether the candidate belongs to the root.</returns>
    private static bool IsInsideOrEqual(
            string path,
            string root)
    {
        string relative =
            Path.GetRelativePath(
                root,
                path);

        return relative.Equals(
                   ".",
                   StringComparison.Ordinal) ||
               (!Path.IsPathRooted(
                    relative) &&
                !relative.Equals(
                    "..",
                    StringComparison.Ordinal) &&
                !relative.StartsWith(
                    ".." +
                    Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                !relative.StartsWith(
                    ".." +
                    Path.AltDirectorySeparatorChar,
                    StringComparison.Ordinal));
    }

    /// <summary>
    /// Normalizes a relative filesystem path for display and prompt provenance.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>A slash-separated relative path.</returns>
    private static string NormalizeRelativePath(
            string path) =>
        path.Replace(
            '\\',
            '/');
}
