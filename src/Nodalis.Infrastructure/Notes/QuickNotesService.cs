using Nodalis.Core.Domain;
using Nodalis.Core.Notes;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Notes;

public sealed class QuickNotesService
{
    public async Task<IReadOnlyList<QuickNoteScope>> ResolveScopesAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var root = Path.GetFullPath(workspaceRoot);
        var contextDirectory = ResolveContextDirectory(
            root,
            contextPath);

        string? projectDirectory = null;
        ProjectManifest? project = null;
        string? applicationDirectory = null;
        ApplicationManifest? application = null;

        for (var current = contextDirectory;
             current is not null && IsInsideOrEqual(current, root);
             current = Directory.GetParent(current)?.FullName)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (project is null)
            {
                var projectManifestPath = Path.Combine(
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
                var applicationManifestPath = Path.Combine(
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

        var scopes = new List<QuickNoteScope>();

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

        foreach (var scope in scopes)
        {
            await EnsureQuickNotesFileAsync(
                scope,
                cancellationToken);
        }

        return scopes;
    }

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

        var session = await TextDocumentSession.OpenAsync(
            scope.FilePath,
            cancellationToken);

        var existing = session.Content.TrimEnd();
        var entry =
            $"## {timestamp:yyyy-MM-dd HH:mm}\n\n" +
            text.Trim() +
            "\n";

        var updated = string.IsNullOrEmpty(existing)
            ? $"# Notes rapides\n\n{entry}"
            : $"{existing}\n\n{entry}";

        await session.SaveAsync(
            updated,
            cancellationToken);
    }

    public async Task<IReadOnlyList<QuickNotesSnapshot>> ReadAggregateAsync(
        string workspaceRoot,
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var scopes = await ResolveScopesAsync(
            workspaceRoot,
            contextPath,
            cancellationToken);

        var snapshots = new List<QuickNotesSnapshot>();

        foreach (var scope in scopes)
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

    public static string FormatAggregateMarkdown(
        IReadOnlyList<QuickNotesSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var sections = snapshots.Select(snapshot =>
        {
            var content = RemoveTopHeading(
                snapshot.Content);

            return $"# {snapshot.Scope.DisplayName}\n\n{content.Trim()}";
        });

        return string.Join(
            "\n\n---\n\n",
            sections) + "\n";
    }

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

    private static string ResolveContextDirectory(
        string workspaceRoot,
        string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return workspaceRoot;
        }

        var fullPath = Path.GetFullPath(contextPath);

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

    private static bool IsInsideOrEqual(
        string candidate,
        string root)
    {
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullRoot = Path.GetFullPath(root)
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

    private static string RemoveTopHeading(
        string markdown)
    {
        using var reader = new StringReader(markdown);
        var firstLine = reader.ReadLine();

        if (firstLine is not null &&
            firstLine.StartsWith("# ", StringComparison.Ordinal))
        {
            return reader.ReadToEnd();
        }

        return markdown;
    }
}
