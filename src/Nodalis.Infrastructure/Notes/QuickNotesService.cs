using Nodalis.Core.Domain;
using Nodalis.Core.Notes;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Notes;

public sealed class QuickNotesService
{
    /// <summary>
    /// Performs the <c>ResolveScopesAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<QuickNoteScope>> ResolveScopesAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        string root = Path.GetFullPath(workspaceRoot);
        string contextDirectory = ResolveContextDirectory(
            root,
            contextPath);

        string? projectDirectory = null;
        ProjectManifest? project = null;
        string? applicationDirectory = null;
        ApplicationManifest? application = null;

        for (string current = contextDirectory;
             current is not null && IsInsideOrEqual(current, root);
             current = Directory.GetParent(current)?.FullName)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (project is null)
            {
                string projectManifestPath = Path.Combine(
                    current,
                    WorkspaceLayout.ProjectManifestFileName);

                if (File.Exists(projectManifestPath))
                {
                    project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                        projectManifestPath,
                        cancellationToken);
                    projectDirectory = current;
                }
            }

            if (application is null)
            {
                string applicationManifestPath = Path.Combine(
                    current,
                    WorkspaceLayout.ApplicationManifestFileName);

                if (File.Exists(applicationManifestPath))
                {
                    application = await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                        applicationManifestPath,
                        cancellationToken);
                    applicationDirectory = current;
                }
            }

            if (project is not null &&
                application is not null)
            {
                break;
            }

            if (string.Equals(
                    current,
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        global::System.Collections.Generic.List<global::Nodalis.Core.Notes.QuickNoteScope> scopes = new List<QuickNoteScope>();

        if (project is not null &&
            projectDirectory is not null)
        {
            scopes.Add(new QuickNoteScope
            {
                Kind = QuickNoteScopeKind.Project,
                DisplayName = $"Projet · {project.Name}",
                DirectoryPath = projectDirectory,
                FilePath = Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.GlobalQuickNotesFileName)
            });
        }

        if (application is not null &&
            applicationDirectory is not null)
        {
            scopes.Add(new QuickNoteScope
            {
                Kind = QuickNoteScopeKind.Application,
                DisplayName = $"Application · {application.Name}",
                DirectoryPath = applicationDirectory,
                FilePath = Path.Combine(
                    applicationDirectory,
                    WorkspaceLayout.GlobalQuickNotesFileName)
            });
        }

        scopes.Add(new QuickNoteScope
        {
            Kind = QuickNoteScopeKind.Global,
            DisplayName = "Global",
            DirectoryPath = root,
            FilePath = Path.Combine(
                root,
                WorkspaceLayout.GlobalQuickNotesFileName)
        });

        foreach (global::Nodalis.Core.Notes.QuickNoteScope scope in scopes)
        {
            await EnsureQuickNotesFileAsync(
                scope,
                cancellationToken);
        }

        return scopes;
    }

    /// <summary>
    /// Performs the <c>AppendAsync</c> operation.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="text">The <c>text</c> value.</param>
    /// <param name="timestamp">The <c>timestamp</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task AppendAsync(
        QuickNoteScope scope,
        string text,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        await EnsureQuickNotesFileAsync(
            scope,
            cancellationToken);

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            scope.FilePath,
            cancellationToken);

        string existing = session.Content.TrimEnd();
        string entry =
            $"## {timestamp:yyyy-MM-dd HH:mm}\n\n" +
            text.Trim() +
            "\n";

        string updated = string.IsNullOrEmpty(existing)
            ? $"# Notes rapides\n\n{entry}"
            : $"{existing}\n\n{entry}";

        await session.SaveAsync(
            updated,
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>ReadAggregateAsync</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public async Task<IReadOnlyList<QuickNotesSnapshot>> ReadAggregateAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Notes.QuickNoteScope> scopes = await ResolveScopesAsync(
            workspaceRoot,
            contextPath,
            cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Notes.QuickNotesSnapshot> snapshots = new List<QuickNotesSnapshot>();

        foreach (global::Nodalis.Core.Notes.QuickNoteScope scope in scopes)
        {
            snapshots.Add(new QuickNotesSnapshot
            {
                Scope = scope,
                Content = await File.ReadAllTextAsync(
                    scope.FilePath,
                    cancellationToken)
            });
        }

        return snapshots;
    }

    /// <summary>
    /// Performs the <c>FormatAggregateMarkdown</c> operation.
    /// </summary>
    /// <param name="snapshots">The <c>snapshots</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static string FormatAggregateMarkdown(
        IReadOnlyList<QuickNotesSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        global::System.Collections.Generic.IEnumerable<string> sections = snapshots.Select(snapshot =>
        {
            string content = RemoveTopHeading(
                snapshot.Content);

            return $"# {snapshot.Scope.DisplayName}\n\n{content.Trim()}";
        });

        return string.Join(
            "\n\n---\n\n",
            sections) + "\n";
    }

    /// <summary>
    /// Performs the <c>EnsureQuickNotesFileAsync</c> operation.
    /// </summary>
    /// <param name="scope">The <c>scope</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static async Task EnsureQuickNotesFileAsync(
        QuickNoteScope scope,
        CancellationToken cancellationToken)
    {
        if (File.Exists(scope.FilePath))
        {
            return;
        }

        Directory.CreateDirectory(
            scope.DirectoryPath);

        await AtomicFileWriter.WriteAllTextAsync(
            scope.FilePath,
            "# Notes rapides\n\n",
            cancellationToken);
    }

    /// <summary>
    /// Performs the <c>ResolveContextDirectory</c> operation.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string ResolveContextDirectory(
        string workspaceRoot,
        string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return workspaceRoot;
        }

        string fullPath = Path.GetFullPath(contextPath);

        if (File.Exists(fullPath) ||
            Path.HasExtension(fullPath))
        {
            return Path.GetDirectoryName(fullPath)
                ?? workspaceRoot;
        }

        return Directory.Exists(fullPath)
            ? fullPath
            : workspaceRoot;
    }

    /// <summary>
    /// Performs the <c>IsInsideOrEqual</c> operation.
    /// </summary>
    /// <param name="candidate">The <c>candidate</c> value.</param>
    /// <param name="root">The <c>root</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        string fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullCandidate,
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Performs the <c>RemoveTopHeading</c> operation.
    /// </summary>
    /// <param name="markdown">The <c>markdown</c> value.</param>
    /// <returns>The result of the operation.</returns>
private static string RemoveTopHeading(
        string markdown)
    {
        using global::System.IO.StringReader reader = new StringReader(markdown);
        string? firstLine = reader.ReadLine();

        if (firstLine is not null &&
            firstLine.StartsWith("# ", StringComparison.Ordinal))
        {
            return reader.ReadToEnd();
        }

        return markdown;
    }
}
