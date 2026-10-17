using Nodalis.Core.Domain;
using Nodalis.Core.Reliability;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Reliability;

public sealed partial class WorkspaceIntegrityDiagnosticService
{
    /// <summary>
    /// Enumerates non-hidden direct child directories and converts access failures into findings.
    /// </summary>
    /// <param name="directory">The parent directory.</param>
    /// <param name="issues">The report findings.</param>
    /// <returns>The accessible child directories.</returns>
    private static IReadOnlyList<string> EnumerateChildDirectories(
            string directory,
            ICollection<WorkspaceIntegrityIssue> issues)
    {
        try
        {
            return Directory
                .EnumerateDirectories(directory)
                .Where(path =>
                    !Path.GetFileName(path)
                        .StartsWith(
                            ".",
                            StringComparison.Ordinal))
                .OrderBy(
                    path => Path.GetFileName(path),
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "DIRECTORY_UNREADABLE",
                $"Impossible de parcourir le dossier : {exception.Message}",
                directory));

            return [];
        }
    }

    /// <summary>
    /// Enumerates all Markdown files that should participate in the workspace link index.
    /// </summary>
    /// <param name="issues">The report findings.</param>
    /// <returns>The expected indexed Markdown files.</returns>
    private IReadOnlyList<string> EnumerateIndexedMarkdownFiles(
            ICollection<WorkspaceIntegrityIssue> issues)
    {
        global::System.Collections.Generic.List<string> files =
            new List<string>();

        try
        {
            files.AddRange(
                Directory.EnumerateFiles(
                    _workspaceRoot,
                    "*.md",
                    SearchOption.TopDirectoryOnly));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "DIRECTORY_UNREADABLE",
                $"Impossible de parcourir les notes globales : {exception.Message}",
                "."));
        }

        string journalRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.JournalDirectoryName);
        CollectFilesRecursively(
            journalRoot,
            "*.md",
            files,
            issues);

        string applicationsRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ApplicationsDirectoryName);
        CollectFilesRecursively(
            applicationsRoot,
            "*.md",
            files,
            issues);

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Enumerates files whose changes make the derived link index stale.
    /// </summary>
    /// <param name="issues">The report findings.</param>
    /// <returns>The index source files.</returns>
    private IReadOnlyList<string> EnumerateIndexSourceFiles(
            ICollection<WorkspaceIntegrityIssue> issues)
    {
        global::System.Collections.Generic.List<string> files =
            new List<string>(
                EnumerateIndexedMarkdownFiles(issues));

        string workspaceManifestPath = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.WorkspaceManifestFileName);

        if (File.Exists(workspaceManifestPath))
        {
            files.Add(workspaceManifestPath);
        }

        string applicationsRoot = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ApplicationsDirectoryName);

        CollectFilesRecursively(
            applicationsRoot,
            "*.json",
            files,
            issues,
            fileName =>
                fileName.Equals(
                    WorkspaceLayout.ApplicationManifestFileName,
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(
                    WorkspaceLayout.ModuleManifestFileName,
                    StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals(
                    WorkspaceLayout.ProjectManifestFileName,
                    StringComparison.OrdinalIgnoreCase));

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Recursively collects matching files while excluding hidden Nodalis implementation directories.
    /// </summary>
    /// <param name="directory">The directory to scan.</param>
    /// <param name="pattern">The file search pattern.</param>
    /// <param name="files">The destination collection.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="fileFilter">An optional file-name filter.</param>
    private static void CollectFilesRecursively(
            string directory,
            string pattern,
            ICollection<string> files,
            ICollection<WorkspaceIntegrityIssue> issues,
            Func<string, bool>? fileFilter = null)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(
                         directory,
                         pattern,
                         SearchOption.TopDirectoryOnly))
            {
                if (fileFilter is null ||
                    fileFilter(Path.GetFileName(file)))
                {
                    files.Add(file);
                }
            }

            foreach (string childDirectory in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(childDirectory)
                    .StartsWith(
                        ".",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                CollectFilesRecursively(
                    childDirectory,
                    pattern,
                    files,
                    issues,
                    fileFilter);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "DIRECTORY_UNREADABLE",
                $"Impossible de parcourir le dossier : {exception.Message}",
                directory));
        }
    }

    /// <summary>
    /// Adds one stable manifest identity to the cross-workspace validation set.
    /// </summary>
    /// <param name="identities">The identity collection.</param>
    /// <param name="id">The stable identifier.</param>
    /// <param name="kind">The entity kind.</param>
    /// <param name="name">The entity display name.</param>
    /// <param name="relativePath">The workspace-relative location.</param>
    /// <param name="scopeKey">The logical scope key.</param>
    private static void AddIdentity(
            ICollection<IdentityEntry> identities,
            Guid id,
            string kind,
            string name,
            string relativePath,
            string scopeKey)
    {
        identities.Add(new IdentityEntry
        {
            Id = id,
            Kind = kind,
            Name = name,
            RelativePath = relativePath,
            ScopeKey = scopeKey
        });
    }

    /// <summary>
    /// Creates one normalized integrity finding.
    /// </summary>
    /// <param name="severity">The finding severity.</param>
    /// <param name="code">The stable diagnostic code.</param>
    /// <param name="message">The human-readable message.</param>
    /// <param name="relativePath">The optional related path.</param>
    /// <param name="entityId">The optional related entity identifier.</param>
    /// <returns>The normalized finding.</returns>
    private static WorkspaceIntegrityIssue CreateIssue(
            WorkspaceIntegritySeverity severity,
            string code,
            string message,
            string? relativePath = null,
            Guid? entityId = null)
    {
        return new WorkspaceIntegrityIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            RelativePath =
                string.IsNullOrWhiteSpace(relativePath)
                    ? null
                    : relativePath,
            EntityId = entityId
        };
    }

    /// <summary>
    /// Normalizes an absolute or relative path into a workspace-relative slash-separated path.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized relative path.</returns>
    private string NormalizeRelativePath(string path)
    {
        string fullPath =
            Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(
                    Path.Combine(
                        _workspaceRoot,
                        path));

        return NormalizeStoredRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath));
    }

    /// <summary>
    /// Normalizes a stored relative path to use forward slashes.
    /// </summary>
    /// <param name="path">The stored path.</param>
    /// <returns>The normalized stored path.</returns>
    private static string NormalizeStoredRelativePath(string path) =>
        path
            .Replace(
                '\\',
                '/')
            .Trim();

    /// <summary>
    /// Resolves an index path only when it remains inside the workspace root.
    /// </summary>
    /// <param name="relativePath">The index relative path.</param>
    /// <param name="fullPath">The resolved absolute path when valid.</param>
    /// <returns>True when the path remains inside the workspace.</returns>
    private bool TryResolveWorkspacePath(
            string relativePath,
            out string fullPath)
    {
        try
        {
            fullPath = Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            string relative = Path.GetRelativePath(
                _workspaceRoot,
                fullPath);

            return !relative.Equals(
                       "..",
                       StringComparison.Ordinal) &&
                   !relative.StartsWith(
                       ".." + Path.DirectorySeparatorChar,
                       StringComparison.Ordinal) &&
                   !Path.IsPathRooted(relative);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            IOException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private sealed record ApplicationEntry
    {
        public required ApplicationManifest Manifest { get; init; }

        public required string DirectoryPath { get; init; }
    }

    private sealed record ModuleEntry
    {
        public required ModuleManifest Manifest { get; init; }

        public required string DirectoryPath { get; init; }
    }

    private sealed record ProjectEntry
    {
        public required ProjectManifest Manifest { get; init; }

        public required string DirectoryPath { get; init; }
    }

    private sealed record IdentityEntry
    {
        public required Guid Id { get; init; }

        public required string Kind { get; init; }

        public required string Name { get; init; }

        public required string RelativePath { get; init; }

        public required string ScopeKey { get; init; }
    }
}
