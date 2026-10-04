using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Milestones;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Milestones;

public sealed class WorkspaceMilestoneService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _linkIndex;

    public WorkspaceMilestoneService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
    }

    public async Task<IReadOnlyList<MilestoneItem>> GetMilestonesAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var context = ResolveContext(contextPath, links);
        var result = new List<MilestoneItem>();

        foreach (var project in links.Targets.Where(target =>
                     target.Kind == LinkTargetKind.Project))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (context.ProjectId is Guid projectId &&
                project.Id != projectId)
            {
                continue;
            }

            if (context.ProjectId is null &&
                context.ApplicationId is Guid applicationId)
            {
                var projectApplication = FindApplicationForProject(
                    project,
                    links);

                if (projectApplication?.Id != applicationId)
                {
                    continue;
                }
            }

            var projectDirectory = ResolveWorkspacePath(
                project.RelativePath);

            var sourcePath = await ResolveMilestoneFileAsync(
                projectDirectory,
                cancellationToken);

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var relativeSource = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    sourcePath));

            var lines = await File.ReadAllLinesAsync(
                sourcePath,
                cancellationToken);

            result.AddRange(
                ParseTable(
                    lines,
                    project.Id,
                    project.DisplayName,
                    relativeSource));
        }

        return result
            .OrderBy(item => item.TargetDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<MilestoneItem>> GetUpcomingAsync(
        DateOnly today,
        int forwardDays = 60,
        CancellationToken cancellationToken = default)
    {
        if (forwardDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(forwardDays));
        }

        var maximum = today.AddDays(forwardDays);
        var all = await GetMilestonesAsync(
            _workspaceRoot,
            cancellationToken);

        return all
            .Where(item =>
                item.TargetDate is DateOnly date &&
                date >= today &&
                date <= maximum &&
                !IsCompletedStatus(item.Status))
            .OrderBy(item => item.TargetDate)
            .ThenBy(item => item.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<MilestoneItem> AddAsync(
        string projectDirectory,
        MilestoneDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ValidateDraft(draft);

        var fullProjectDirectory = Path.GetFullPath(projectDirectory);
        var manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            Path.Combine(
                fullProjectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            cancellationToken);

        var sourcePath = await ResolveMilestoneFileAsync(
            fullProjectDirectory,
            cancellationToken);

        await EnsureMilestoneFileAsync(
            sourcePath,
            cancellationToken);

        var session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        var existing = session.Content.TrimEnd();
        var row = FormatRow(draft);
        var updated = existing + Environment.NewLine + row + Environment.NewLine;

        await session.SaveAsync(
            updated,
            cancellationToken);

        var lines = updated
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var relativeSource = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                sourcePath));

        var parsed = ParseTable(
            lines,
            manifest.Id,
            manifest.Name,
            relativeSource);

        return parsed.Last(item =>
            string.Equals(
                item.RawLine,
                row,
                StringComparison.Ordinal));
    }

    public async Task<MilestoneItem> UpdateAsync(
        MilestoneItem item,
        MilestoneDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateDraft(draft);

        var sourcePath = ResolveWorkspacePath(
            item.SourceRelativePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Le fichier de jalons est introuvable.",
                sourcePath);
        }

        var session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        var newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var normalized = session.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        var lines = normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var index = LocateSourceLine(
            lines,
            item.LineNumber,
            item.RawLine,
            sourcePath);

        var replacement = FormatRow(draft);
        lines[index] = replacement;

        var content = string.Join(
            newline,
            lines);

        if (hadTrailingNewline)
        {
            content += newline;
        }

        await session.SaveAsync(
            content,
            cancellationToken);

        return item with
        {
            Name = draft.Name.Trim(),
            TargetDate = draft.TargetDate,
            Status = draft.Status.Trim(),
            Description = draft.Description.Trim(),
            Link = NormalizeOptional(draft.Link),
            LineNumber = index + 1,
            RawLine = replacement,
            Id = CreateMilestoneId(
                item.ProjectId,
                index + 1,
                draft.Name)
        };
    }

    public async Task DeleteAsync(
        MilestoneItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        var sourcePath = ResolveWorkspacePath(
            item.SourceRelativePath);

        var session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        var newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var normalized = session.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        var lines = normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var index = LocateSourceLine(
            lines,
            item.LineNumber,
            item.RawLine,
            sourcePath);

        lines.RemoveAt(index);

        var content = string.Join(
            newline,
            lines);

        if (hadTrailingNewline)
        {
            content += newline;
        }

        await session.SaveAsync(
            content,
            cancellationToken);
    }

    public async Task<string?> GetProjectDirectoryForContextAsync(
        string? contextPath,
        CancellationToken cancellationToken = default)
    {
        var links = await _linkIndex.RefreshAsync(cancellationToken);
        var context = ResolveContext(contextPath, links);

        if (context.ProjectId is not Guid projectId)
        {
            return null;
        }

        var project = links.Targets.FirstOrDefault(target =>
            target.Kind == LinkTargetKind.Project &&
            target.Id == projectId);

        return project is null
            ? null
            : ResolveWorkspacePath(project.RelativePath);
    }

    private async Task<string> ResolveMilestoneFileAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(
            projectDirectory,
            WorkspaceLayout.ProjectManifestFileName);

        if (File.Exists(manifestPath))
        {
            var manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                manifestPath,
                cancellationToken);

            var section = manifest.Sections.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.TemplateKey,
                    "milestones",
                    StringComparison.OrdinalIgnoreCase));

            if (section is not null)
            {
                var safeName = WindowsPathRules.SanitizeSegment(
                    section.Name);

                return Path.Combine(
                    projectDirectory,
                    safeName,
                    safeName + ".md");
            }
        }

        return Path.Combine(
            projectDirectory,
            "Jalons",
            "Jalons.md");
    }

    private static async Task EnsureMilestoneFileAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(sourcePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException(
                "Impossible de déterminer le dossier du fichier de jalons.");

        Directory.CreateDirectory(directory);

        await AtomicFileWriter.WriteAllTextAsync(
            sourcePath,
            "# Jalons\n\n" +
            "| Jalon | Date cible | Statut | Description | Lien |\n" +
            "| --- | --- | --- | --- | --- |\n",
            cancellationToken);
    }

    private static IReadOnlyList<MilestoneItem> ParseTable(
        IReadOnlyList<string> lines,
        Guid projectId,
        string projectName,
        string sourceRelativePath)
    {
        var result = new List<MilestoneItem>();
        Dictionary<string, int>? columns = null;

        for (var index = 0;
             index < lines.Count;
             index++)
        {
            var line = lines[index];

            if (!line.TrimStart().StartsWith(
                    "|",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var cells = SplitTableRow(line);

            if (cells.Count == 0)
            {
                continue;
            }

            if (columns is null &&
                cells.Any(cell =>
                    NormalizeHeader(cell) == "jalon"))
            {
                columns = cells
                    .Select((cell, columnIndex) =>
                        (Key: NormalizeHeader(cell), columnIndex))
                    .Where(pair =>
                        !string.IsNullOrWhiteSpace(pair.Key))
                    .GroupBy(pair => pair.Key)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First().columnIndex,
                        StringComparer.OrdinalIgnoreCase);

                continue;
            }

            if (IsSeparatorRow(cells))
            {
                continue;
            }

            if (columns is null)
            {
                continue;
            }

            var name = GetCell(
                cells,
                columns,
                "jalon");

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var dateText = GetCell(
                cells,
                columns,
                "date cible");

            DateOnly? targetDate = null;

            if (DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                targetDate = parsedDate;
            }

            var description = GetCell(
                cells,
                columns,
                "description",
                "commentaire");

            string link;

            if (columns.ContainsKey("description") ||
                columns.ContainsKey("commentaire"))
            {
                link = GetCell(
                    cells,
                    columns,
                    "lien");
            }
            else
            {
                // Legacy 4-column table: Jalon | Date cible | Statut | Lien.
                link = GetCell(
                    cells,
                    columns,
                    "lien");
            }

            result.Add(new MilestoneItem
            {
                Id = CreateMilestoneId(
                    projectId,
                    index + 1,
                    name),
                ProjectId = projectId,
                ProjectName = projectName,
                SourceRelativePath = sourceRelativePath,
                LineNumber = index + 1,
                RawLine = line,
                Name = UnescapeCell(name),
                TargetDate = targetDate,
                Status = UnescapeCell(
                    GetCell(
                        cells,
                        columns,
                        "statut")),
                Description = UnescapeCell(description),
                Link = NormalizeOptional(
                    UnescapeCell(link))
            });
        }

        return result;
    }

    private static string FormatRow(
        MilestoneDraft draft) =>
        $"| {EscapeCell(draft.Name.Trim())} | " +
        $"{(draft.TargetDate is DateOnly date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty)} | " +
        $"{EscapeCell(draft.Status.Trim())} | " +
        $"{EscapeCell(draft.Description.Trim())} | " +
        $"{EscapeCell(NormalizeOptional(draft.Link) ?? string.Empty)} |";

    private static int LocateSourceLine(
        IReadOnlyList<string> lines,
        int expectedLineNumber,
        string rawLine,
        string sourcePath)
    {
        var expectedIndex = expectedLineNumber - 1;

        if (expectedIndex >= 0 &&
            expectedIndex < lines.Count &&
            string.Equals(
                lines[expectedIndex],
                rawLine,
                StringComparison.Ordinal))
        {
            return expectedIndex;
        }

        var candidates = lines
            .Select((line, index) =>
                (line, index))
            .Where(candidate =>
                string.Equals(
                    candidate.line,
                    rawLine,
                    StringComparison.Ordinal))
            .Select(candidate =>
                candidate.index)
            .ToArray();

        if (candidates.Length != 1)
        {
            throw new MilestoneSourceConflictException(
                sourcePath);
        }

        return candidates[0];
    }

    private (Guid? ApplicationId, Guid? ProjectId) ResolveContext(
        string? contextPath,
        LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return (null, null);
        }

        var fullPath = Path.GetFullPath(contextPath);

        var contextDirectory =
            File.Exists(fullPath)
                ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
                : fullPath;

        var relative = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                contextDirectory));

        var project = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Project &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relative))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        if (project is not null)
        {
            var application = FindApplicationForProject(
                project,
                links);

            return (
                application?.Id,
                project.Id);
        }

        var app = links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Application &&
                IsRelativeAncestorOrEqual(
                    target.RelativePath,
                    relative))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

        return (
            app?.Id,
            null);
    }

    private static LinkTargetEntry? FindApplicationForProject(
        LinkTargetEntry project,
        LinkIndexCatalog links) =>
        links.Targets
            .Where(target =>
                target.Kind == LinkTargetKind.Application &&
                IsRelativeAncestor(
                    target.RelativePath,
                    project.RelativePath))
            .OrderByDescending(target =>
                target.RelativePath.Length)
            .FirstOrDefault();

    private string ResolveWorkspacePath(
        string relativePath) =>
        Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

    private static List<string> SplitTableRow(string line)
    {
        var trimmed = line.Trim().Trim('|');

        return trimmed
            .Split('|')
            .Select(cell => cell.Trim())
            .ToList();
    }

    private static bool IsSeparatorRow(
        IReadOnlyList<string> cells) =>
        cells.Count > 0 &&
        cells.All(cell =>
            cell.Trim()
                .Trim(':')
                .All(character =>
                    character == '-' ||
                    char.IsWhiteSpace(character)));

    private static string GetCell(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columns,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (columns.TryGetValue(
                    key,
                    out var index) &&
                index >= 0 &&
                index < cells.Count)
            {
                return cells[index].Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeHeader(string value) =>
        value
            .Trim()
            .ToLowerInvariant()
            .Replace("é", "e", StringComparison.Ordinal)
            .Replace("è", "e", StringComparison.Ordinal)
            .Replace("ê", "e", StringComparison.Ordinal);

    private static string EscapeCell(string value) =>
        value.Replace(
            "|",
            "\\|",
            StringComparison.Ordinal);

    private static string UnescapeCell(string value) =>
        value.Replace(
            "\\|",
            "|",
            StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static bool IsCompletedStatus(string status)
    {
        var normalized = NormalizeHeader(status);

        return normalized is
            "termine" or
            "terminee" or
            "clos" or
            "cloture" or
            "done" or
            "complete" or
            "completed";
    }

    private static Guid CreateMilestoneId(
        Guid projectId,
        int lineNumber,
        string name)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{projectId:D}|{lineNumber}|{name.Trim()}"));

        return new Guid(
            bytes.AsSpan(
                0,
                16));
    }

    private static bool IsRelativeAncestor(
        string candidateParent,
        string child) =>
        child.StartsWith(
            candidateParent.TrimEnd('/') + "/",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsRelativeAncestorOrEqual(
        string candidateParent,
        string child) =>
        string.Equals(
            candidateParent.TrimEnd('/'),
            child.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase) ||
        IsRelativeAncestor(
            candidateParent,
            child);

    private static string NormalizeRelativePath(string path) =>
        path.Replace(
            Path.DirectorySeparatorChar,
            '/');

    private static void ValidateDraft(MilestoneDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Name);
    }
}
