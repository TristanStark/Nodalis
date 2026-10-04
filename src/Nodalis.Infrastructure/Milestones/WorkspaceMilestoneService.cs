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

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceMilestoneService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public WorkspaceMilestoneService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _linkIndex = new WorkspaceLinkIndexService(_workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>GetMilestonesAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<MilestoneItem>> GetMilestonesAsync(
            string? contextPath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::System.Guid? ApplicationId, global::System.Guid? ProjectId) context = ResolveContext(contextPath, links);
        global::System.Collections.Generic.List<global::Nodalis.Core.Milestones.MilestoneItem> result = new List<MilestoneItem>();

        foreach (global::Nodalis.Core.Links.LinkTargetEntry project in links.Targets.Where(target =>
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
                global::Nodalis.Core.Links.LinkTargetEntry? projectApplication = FindApplicationForProject(
                    project,
                    links);

                if (projectApplication?.Id != applicationId)
                {
                    continue;
                }
            }

            string projectDirectory = ResolveWorkspacePath(
                project.RelativePath);

            string sourcePath = await ResolveMilestoneFileAsync(
                projectDirectory,
                cancellationToken);

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            string relativeSource = NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    sourcePath));

            string[] lines = await File.ReadAllLinesAsync(
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

    /// <summary>
    /// Performs the <c>GetUpcomingAsync</c> operation.
    /// </summary>
    /// <param name="today">The <c>today</c> value.</param>
    /// <param name="forwardDays">The <c>forwardDays</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<IReadOnlyList<MilestoneItem>> GetUpcomingAsync(
            DateOnly today,
            int forwardDays = 60,
            CancellationToken cancellationToken = default)
    {
        if (forwardDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(forwardDays));
        }

        global::System.DateOnly maximum = today.AddDays(forwardDays);
        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> all = await GetMilestonesAsync(
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

    /// <summary>
    /// Performs the <c>AddAsync</c> operation.
    /// </summary>
    /// <param name="projectDirectory">The <c>projectDirectory</c> value.</param>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<MilestoneItem> AddAsync(
            string projectDirectory,
            MilestoneDraft draft,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ValidateDraft(draft);

        string fullProjectDirectory = Path.GetFullPath(projectDirectory);
        global::Nodalis.Core.Domain.ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            Path.Combine(
                fullProjectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            cancellationToken);

        string sourcePath = await ResolveMilestoneFileAsync(
            fullProjectDirectory,
            cancellationToken);

        await EnsureMilestoneFileAsync(
            sourcePath,
            cancellationToken);

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        string existing = session.Content.TrimEnd();
        string row = FormatRow(draft);
        string updated = existing + Environment.NewLine + row + Environment.NewLine;

        await session.SaveAsync(
            updated,
            cancellationToken);

        string[] lines = updated
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        string relativeSource = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                sourcePath));

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> parsed = ParseTable(
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

    /// <summary>
    /// Performs the <c>UpdateAsync</c> operation.
    /// </summary>
    /// <param name="item">The <c>item</c> value.</param>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<MilestoneItem> UpdateAsync(
            MilestoneItem item,
            MilestoneDraft draft,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateDraft(draft);

        string sourcePath = ResolveWorkspacePath(
            item.SourceRelativePath);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Le fichier de jalons est introuvable.",
                sourcePath);
        }

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        string newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        string normalized = session.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        bool hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        global::System.Collections.Generic.List<string> lines = normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        int index = LocateSourceLine(
            lines,
            item.LineNumber,
            item.RawLine,
            sourcePath);

        string replacement = FormatRow(draft);
        lines[index] = replacement;

        string content = string.Join(
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

    /// <summary>
    /// Performs the <c>DeleteAsync</c> operation.
    /// </summary>
    /// <param name="item">The <c>item</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task DeleteAsync(
            MilestoneItem item,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        string sourcePath = ResolveWorkspacePath(
            item.SourceRelativePath);

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        string newline = session.Content.Contains(
            "\r\n",
            StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        string normalized = session.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        bool hadTrailingNewline = normalized.EndsWith(
            "\n",
            StringComparison.Ordinal);

        global::System.Collections.Generic.List<string> lines = normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        int index = LocateSourceLine(
            lines,
            item.LineNumber,
            item.RawLine,
            sourcePath);

        lines.RemoveAt(index);

        string content = string.Join(
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

    /// <summary>
    /// Performs the <c>GetProjectDirectoryForContextAsync</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<string?> GetProjectDirectoryForContextAsync(
            string? contextPath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Links.LinkIndexCatalog links = await _linkIndex.RefreshAsync(cancellationToken);
        (global::System.Guid? ApplicationId, global::System.Guid? ProjectId) context = ResolveContext(contextPath, links);

        if (context.ProjectId is not Guid projectId)
        {
            return null;
        }

        global::Nodalis.Core.Links.LinkTargetEntry? project = links.Targets.FirstOrDefault(target =>
            target.Kind == LinkTargetKind.Project &&
            target.Id == projectId);

        return project is null
            ? null
            : ResolveWorkspacePath(project.RelativePath);
    }

    /// <summary>
    /// Performs the <c>ResolveMilestoneFileAsync</c> operation.
    /// </summary>
    /// <param name="projectDirectory">The <c>projectDirectory</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private async Task<string> ResolveMilestoneFileAsync(
            string projectDirectory,
            CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(
            projectDirectory,
            WorkspaceLayout.ProjectManifestFileName);

        if (File.Exists(manifestPath))
        {
            global::Nodalis.Core.Domain.ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                manifestPath,
                cancellationToken);

            global::Nodalis.Core.Domain.SectionManifest? section = manifest.Sections.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.TemplateKey,
                    "milestones",
                    StringComparison.OrdinalIgnoreCase));

            if (section is not null)
            {
                string safeName = WindowsPathRules.SanitizeSegment(
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

    /// <summary>
    /// Performs the <c>EnsureMilestoneFileAsync</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task EnsureMilestoneFileAsync(
            string sourcePath,
            CancellationToken cancellationToken)
    {
        if (File.Exists(sourcePath))
        {
            return;
        }

        string directory = Path.GetDirectoryName(sourcePath)
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

    /// <summary>
    /// Performs the <c>ParseTable</c> operation.
    /// </summary>
    /// <param name="lines">The <c>lines</c> value.</param>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="projectName">The <c>projectName</c> value.</param>
    /// <param name="sourceRelativePath">The <c>sourceRelativePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static IReadOnlyList<MilestoneItem> ParseTable(
            IReadOnlyList<string> lines,
            Guid projectId,
            string projectName,
            string sourceRelativePath)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Milestones.MilestoneItem> result = new List<MilestoneItem>();
        Dictionary<string, int>? columns = null;

        for (int index = 0;
             index < lines.Count;
             index++)
        {
            string line = lines[index];

            if (!line.TrimStart().StartsWith(
                    "|",
                    StringComparison.Ordinal))
            {
                continue;
            }

            global::System.Collections.Generic.List<string> cells = SplitTableRow(line);

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

            string name = GetCell(
                cells,
                columns,
                "jalon");

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string dateText = GetCell(
                cells,
                columns,
                "date cible");

            DateOnly? targetDate = null;

            if (DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out global::System.DateOnly parsedDate))
            {
                targetDate = parsedDate;
            }

            string description = GetCell(
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

    /// <summary>
    /// Performs the <c>FormatRow</c> operation.
    /// </summary>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string FormatRow(
            MilestoneDraft draft) =>
            $"| {EscapeCell(draft.Name.Trim())} | " +
            $"{(draft.TargetDate is DateOnly date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty)} | " +
            $"{EscapeCell(draft.Status.Trim())} | " +
            $"{EscapeCell(draft.Description.Trim())} | " +
            $"{EscapeCell(NormalizeOptional(draft.Link) ?? string.Empty)} |";

    /// <summary>
    /// Performs the <c>LocateSourceLine</c> operation.
    /// </summary>
    /// <param name="lines">The <c>lines</c> value.</param>
    /// <param name="expectedLineNumber">The <c>expectedLineNumber</c> value.</param>
    /// <param name="rawLine">The <c>rawLine</c> value.</param>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static int LocateSourceLine(
            IReadOnlyList<string> lines,
            int expectedLineNumber,
            string rawLine,
            string sourcePath)
    {
        int expectedIndex = expectedLineNumber - 1;

        if (expectedIndex >= 0 &&
            expectedIndex < lines.Count &&
            string.Equals(
                lines[expectedIndex],
                rawLine,
                StringComparison.Ordinal))
        {
            return expectedIndex;
        }

        int[] candidates = lines
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

    /// <summary>
    /// Performs the <c>ResolveContext</c> operation.
    /// </summary>
    /// <param name="contextPath">The <c>contextPath</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private (Guid? ApplicationId, Guid? ProjectId) ResolveContext(
            string? contextPath,
            LinkIndexCatalog links)
    {
        if (string.IsNullOrWhiteSpace(contextPath))
        {
            return (null, null);
        }

        string fullPath = Path.GetFullPath(contextPath);

        string contextDirectory =
            File.Exists(fullPath)
                ? Path.GetDirectoryName(fullPath) ?? _workspaceRoot
                : fullPath;

        string relative = NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                contextDirectory));

        global::Nodalis.Core.Links.LinkTargetEntry? project = links.Targets
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
            global::Nodalis.Core.Links.LinkTargetEntry? application = FindApplicationForProject(
                project,
                links);

            return (
                application?.Id,
                project.Id);
        }

        global::Nodalis.Core.Links.LinkTargetEntry? app = links.Targets
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

    /// <summary>
    /// Performs the <c>FindApplicationForProject</c> operation.
    /// </summary>
    /// <param name="project">The <c>project</c> value.</param>
    /// <param name="links">The <c>links</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>ResolveWorkspacePath</c> operation.
    /// </summary>
    /// <param name="relativePath">The <c>relativePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private string ResolveWorkspacePath(
            string relativePath) =>
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

    /// <summary>
    /// Performs the <c>SplitTableRow</c> operation.
    /// </summary>
    /// <param name="line">The <c>line</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> SplitTableRow(string line)
    {
        string trimmed = line.Trim();

        if (trimmed.StartsWith(
                "|",
                StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith(
                "|",
                StringComparison.Ordinal))
        {
            trimmed = trimmed[..^1];
        }

        global::System.Collections.Generic.List<string> cells = new List<string>();
        global::System.Text.StringBuilder current = new StringBuilder();
        bool escaped = false;

        foreach (char character in trimmed)
        {
            if (escaped)
            {
                if (character == '|')
                {
                    current.Append('|');
                }
                else
                {
                    current.Append('\\');
                    current.Append(character);
                }

                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '|')
            {
                cells.Add(
                    current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (escaped)
        {
            current.Append('\\');
        }

        cells.Add(
            current.ToString().Trim());

        return cells;
    }

    /// <summary>
    /// Performs the <c>IsSeparatorRow</c> operation.
    /// </summary>
    /// <param name="cells">The <c>cells</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsSeparatorRow(
            IReadOnlyList<string> cells) =>
            cells.Count > 0 &&
            cells.All(cell =>
                cell.Trim()
                    .Trim(':')
                    .All(character =>
                        character == '-' ||
                        char.IsWhiteSpace(character)));

    /// <summary>
    /// Performs the <c>GetCell</c> operation.
    /// </summary>
    /// <param name="cells">The <c>cells</c> value.</param>
    /// <param name="columns">The <c>columns</c> value.</param>
    /// <param name="keys">The <c>keys</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string GetCell(
            IReadOnlyList<string> cells,
            IReadOnlyDictionary<string, int> columns,
            params string[] keys)
    {
        foreach (string key in keys)
        {
            if (columns.TryGetValue(
                    key,
                    out int index) &&
                index >= 0 &&
                index < cells.Count)
            {
                return cells[index].Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Performs the <c>NormalizeHeader</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeHeader(string value) =>
            value
                .Trim()
                .ToLowerInvariant()
                .Replace("é", "e", StringComparison.Ordinal)
                .Replace("è", "e", StringComparison.Ordinal)
                .Replace("ê", "e", StringComparison.Ordinal);

    /// <summary>
    /// Performs the <c>EscapeCell</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string EscapeCell(string value) =>
            value.Replace(
                "|",
                "\\|",
                StringComparison.Ordinal);

    /// <summary>
    /// Performs the <c>UnescapeCell</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string UnescapeCell(string value) =>
            value.Replace(
                "\\|",
                "|",
                StringComparison.Ordinal);

    /// <summary>
    /// Performs the <c>NormalizeOptional</c> operation.
    /// </summary>
    /// <param name="value">The <c>value</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();

    /// <summary>
    /// Performs the <c>IsCompletedStatus</c> operation.
    /// </summary>
    /// <param name="status">The <c>status</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsCompletedStatus(string status)
    {
        string normalized = NormalizeHeader(status);

        return normalized is
            "termine" or
            "terminee" or
            "clos" or
            "cloture" or
            "done" or
            "complete" or
            "completed";
    }

    /// <summary>
    /// Performs the <c>CreateMilestoneId</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="lineNumber">The <c>lineNumber</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static Guid CreateMilestoneId(
            Guid projectId,
            int lineNumber,
            string name)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(
                $"{projectId:D}|{lineNumber}|{name.Trim()}"));

        return new Guid(
            bytes.AsSpan(
                0,
                16));
    }

    /// <summary>
    /// Performs the <c>IsRelativeAncestor</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static bool IsRelativeAncestor(
            string candidateParent,
            string child) =>
            child.StartsWith(
                candidateParent.TrimEnd('/') + "/",
                StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Performs the <c>IsRelativeAncestorOrEqual</c> operation.
    /// </summary>
    /// <param name="candidateParent">The <c>candidateParent</c> value.</param>
    /// <param name="child">The <c>child</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>NormalizeRelativePath</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeRelativePath(string path) =>
            path.Replace(
                Path.DirectorySeparatorChar,
                '/');

    /// <summary>
    /// Performs the <c>ValidateDraft</c> operation.
    /// </summary>
    /// <param name="draft">The <c>draft</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static void ValidateDraft(MilestoneDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Name);
    }
}
