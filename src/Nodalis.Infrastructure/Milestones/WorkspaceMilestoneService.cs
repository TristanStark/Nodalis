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

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> enriched =
            EnrichDependencyMetadata(result);

        return enriched
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

        await EnsureStableSchemaAsync(
            sourcePath,
            manifest.Id,
            cancellationToken);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> existingMilestones =
            await GetMilestonesAsync(
                fullProjectDirectory,
                cancellationToken);

        global::System.Guid milestoneId = Guid.NewGuid();

        ValidateDependencyDraft(
            milestoneId,
            draft.DependencyIds,
            existingMilestones);

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session = await TextDocumentSession.OpenAsync(
            sourcePath,
            cancellationToken);

        string existing = session.Content.TrimEnd();
        string row = FormatRow(
            draft,
            milestoneId);
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

        global::Nodalis.Core.Links.LinkIndexCatalog links =
            await _linkIndex.RefreshAsync(cancellationToken);

        global::Nodalis.Core.Links.LinkTargetEntry project = links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Project &&
                target.Id == item.ProjectId)
            ?? throw new InvalidDataException(
                "Le projet du jalon est introuvable.");

        string projectDirectory = ResolveWorkspacePath(
            project.RelativePath);

        string sourcePath = await ResolveMilestoneFileAsync(
            projectDirectory,
            cancellationToken);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Le fichier de jalons est introuvable.",
                sourcePath);
        }

        bool schemaMigrated = await EnsureStableSchemaAsync(
            sourcePath,
            item.ProjectId,
            cancellationToken);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> milestones =
            await GetMilestonesAsync(
                projectDirectory,
                cancellationToken);

        global::Nodalis.Core.Milestones.MilestoneItem refreshedItem =
            milestones.FirstOrDefault(candidate =>
                candidate.Id == item.Id)
            ?? throw new MilestoneSourceConflictException(
                sourcePath);

        global::Nodalis.Core.Milestones.MilestoneItem currentItem =
            schemaMigrated
                ? refreshedItem
                : item;

        ValidateDependencyDraft(
            currentItem.Id,
            draft.DependencyIds,
            milestones);

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
            currentItem.LineNumber,
            currentItem.RawLine,
            sourcePath);

        string replacement = FormatRow(
            draft,
            currentItem.Id);
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

        return currentItem with
        {
            Name = draft.Name.Trim(),
            TargetDate = draft.TargetDate,
            Status = draft.Status.Trim(),
            Description = draft.Description.Trim(),
            Link = NormalizeOptional(draft.Link),
            DependencyIds = NormalizeDependencyIds(
                draft.DependencyIds),
            DependencyNames = Array.Empty<string>(),
            DependencyWarnings = Array.Empty<string>(),
            LineNumber = index + 1,
            RawLine = replacement
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

        global::Nodalis.Core.Links.LinkIndexCatalog links =
            await _linkIndex.RefreshAsync(cancellationToken);

        global::Nodalis.Core.Links.LinkTargetEntry project = links.Targets.FirstOrDefault(target =>
                target.Kind == LinkTargetKind.Project &&
                target.Id == item.ProjectId)
            ?? throw new InvalidDataException(
                "Le projet du jalon est introuvable.");

        string projectDirectory = ResolveWorkspacePath(
            project.RelativePath);

        string sourcePath = await ResolveMilestoneFileAsync(
            projectDirectory,
            cancellationToken);

        bool schemaMigrated = await EnsureStableSchemaAsync(
            sourcePath,
            item.ProjectId,
            cancellationToken);

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> projectMilestones =
            await GetMilestonesAsync(
                projectDirectory,
                cancellationToken);

        global::Nodalis.Core.Milestones.MilestoneItem refreshedItem =
            projectMilestones.FirstOrDefault(candidate =>
                candidate.Id == item.Id)
            ?? throw new MilestoneSourceConflictException(
                sourcePath);

        global::Nodalis.Core.Milestones.MilestoneItem currentItem =
            schemaMigrated
                ? refreshedItem
                : item;

        global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Milestones.MilestoneItem> workspaceMilestones =
            await GetMilestonesAsync(
                _workspaceRoot,
                cancellationToken);

        global::System.Collections.Generic.List<global::Nodalis.Core.Milestones.MilestoneItem> dependents =
            workspaceMilestones
                .Where(candidate =>
                    candidate.DependencyIds.Contains(
                        currentItem.Id))
                .ToList();

        if (dependents.Count > 0)
        {
            string dependentNames = string.Join(
                ", ",
                dependents.Select(candidate =>
                    $"{candidate.ProjectName} / {candidate.Name}"));

            throw new MilestoneDependencyConflictException(
                $"Le jalon « {currentItem.Name} » est encore requis par : {dependentNames}. Retirez d'abord ces dépendances.");
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
            currentItem.LineNumber,
            currentItem.RawLine,
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
            "| Jalon | Date cible | Statut | Description | Lien | Id | Dépend de |\n" +
            "| --- | --- | --- | --- | --- | --- | --- |\n",
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
                Id = ParseMilestoneId(
                    GetCell(
                        cells,
                        columns,
                        "id",
                        "identifiant"),
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
                    UnescapeCell(link)),
                DependencyIds = ParseDependencyIds(
                    GetCell(
                        cells,
                        columns,
                        "depend de",
                        "dependances",
                        "prerequis"))
            });
        }

        return result;
    }

    /// <summary>
    /// Performs the <c>FormatRow</c> operation.
    /// </summary>
    /// <param name="draft">The milestone values.</param>
    /// <param name="id">The stable milestone identifier.</param>
    /// <returns>The Markdown table row.</returns>
    private static string FormatRow(
            MilestoneDraft draft,
            Guid id) =>
            $"| {EscapeCell(draft.Name.Trim())} | " +
            $"{(draft.TargetDate is DateOnly date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty)} | " +
            $"{EscapeCell(draft.Status.Trim())} | " +
            $"{EscapeCell(draft.Description.Trim())} | " +
            $"{EscapeCell(NormalizeOptional(draft.Link) ?? string.Empty)} | " +
            $"{id:D} | " +
            $"{FormatDependencyIds(draft.DependencyIds)} |";

    /// <summary>
    /// Ensures that an existing milestone table persists stable identifiers and dependency columns.
    /// </summary>
    /// <param name="sourcePath">The milestone Markdown file.</param>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> when the table was migrated.</returns>
    private static async Task<bool> EnsureStableSchemaAsync(
            string sourcePath,
            Guid projectId,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        global::Nodalis.Infrastructure.Reliability.TextDocumentSession session =
            await TextDocumentSession.OpenAsync(
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

        global::System.Collections.Generic.List<string> lines =
            normalized.Split('\n').ToList();

        if (hadTrailingNewline &&
            lines.Count > 0 &&
            lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        int headerIndex = -1;
        global::System.Collections.Generic.Dictionary<string, int>? columns = null;

        for (int index = 0; index < lines.Count; index++)
        {
            if (!lines[index].TrimStart().StartsWith(
                    "|",
                    StringComparison.Ordinal))
            {
                continue;
            }

            global::System.Collections.Generic.List<string> cells =
                SplitTableRow(
                    lines[index]);

            if (!cells.Any(cell =>
                    NormalizeHeader(cell) == "jalon"))
            {
                continue;
            }

            headerIndex = index;
            columns = cells
                .Select((cell, columnIndex) =>
                    (Key: NormalizeHeader(cell), columnIndex))
                .Where(pair =>
                    !string.IsNullOrWhiteSpace(pair.Key))
                .GroupBy(pair =>
                    pair.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().columnIndex,
                    StringComparer.OrdinalIgnoreCase);
            break;
        }

        if (headerIndex < 0 ||
            columns is null)
        {
            return false;
        }

        bool alreadyStable =
            columns.ContainsKey("id") &&
            columns.ContainsKey("depend de") &&
            columns.ContainsKey("description") &&
            columns.ContainsKey("lien");

        if (alreadyStable)
        {
            return false;
        }

        lines[headerIndex] =
            "| Jalon | Date cible | Statut | Description | Lien | Id | Dépend de |";

        int separatorIndex = headerIndex + 1;

        if (separatorIndex < lines.Count &&
            lines[separatorIndex].TrimStart().StartsWith(
                "|",
                StringComparison.Ordinal))
        {
            lines[separatorIndex] =
                "| --- | --- | --- | --- | --- | --- | --- |";
        }

        for (int index = headerIndex + 2; index < lines.Count; index++)
        {
            string line = lines[index];

            if (!line.TrimStart().StartsWith(
                    "|",
                    StringComparison.Ordinal))
            {
                break;
            }

            global::System.Collections.Generic.List<string> cells =
                SplitTableRow(
                    line);

            if (IsSeparatorRow(cells))
            {
                continue;
            }

            string rawName = GetCell(
                cells,
                columns,
                "jalon");
            string name = UnescapeCell(
                rawName);

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            DateOnly? targetDate = null;
            string dateText = GetCell(
                cells,
                columns,
                "date cible");

            if (DateOnly.TryParseExact(
                    dateText,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out global::System.DateOnly parsedDate))
            {
                targetDate = parsedDate;
            }

            global::System.Guid stableId =
                ParseMilestoneId(
                    GetCell(
                        cells,
                        columns,
                        "id",
                        "identifiant"),
                    projectId,
                    index + 1,
                    rawName);

            global::Nodalis.Core.Milestones.MilestoneDraft draft =
                new MilestoneDraft
                {
                    Name = name,
                    TargetDate = targetDate,
                    Status = UnescapeCell(
                        GetCell(
                            cells,
                            columns,
                            "statut")),
                    Description = UnescapeCell(
                        GetCell(
                            cells,
                            columns,
                            "description",
                            "commentaire")),
                    Link = NormalizeOptional(
                        UnescapeCell(
                            GetCell(
                                cells,
                                columns,
                                "lien"))),
                    DependencyIds = ParseDependencyIds(
                        GetCell(
                            cells,
                            columns,
                            "depend de",
                            "dependances",
                            "prerequis"))
                };

            lines[index] = FormatRow(
                draft,
                stableId);
        }

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

        return true;
    }

    /// <summary>
    /// Parses a persisted milestone identifier, falling back to the deterministic legacy identifier.
    /// </summary>
    /// <param name="value">The persisted identifier.</param>
    /// <param name="projectId">The project identifier.</param>
    /// <param name="lineNumber">The source line number.</param>
    /// <param name="name">The milestone name.</param>
    /// <returns>The stable identifier.</returns>
    private static Guid ParseMilestoneId(
            string value,
            Guid projectId,
            int lineNumber,
            string name) =>
            Guid.TryParse(
                value,
                out global::System.Guid parsed)
                ? parsed
                : CreateMilestoneId(
                    projectId,
                    lineNumber,
                    name);

    /// <summary>
    /// Parses a comma- or semicolon-separated list of milestone identifiers.
    /// </summary>
    /// <param name="value">The persisted dependency list.</param>
    /// <returns>The normalized dependency identifiers.</returns>
    private static IReadOnlyList<Guid> ParseDependencyIds(
            string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<Guid>();
        }

        global::System.Collections.Generic.List<global::System.Guid> result =
            new List<Guid>();

        foreach (string token in value.Split(
                     new[] { ',', ';' },
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(
                    token,
                    out global::System.Guid dependencyId) &&
                dependencyId != Guid.Empty &&
                !result.Contains(
                    dependencyId))
            {
                result.Add(
                    dependencyId);
            }
        }

        return result;
    }

    /// <summary>
    /// Normalizes dependency identifiers before persistence.
    /// </summary>
    /// <param name="dependencyIds">The dependency identifiers.</param>
    /// <returns>The normalized identifiers.</returns>
    private static IReadOnlyList<Guid> NormalizeDependencyIds(
            IEnumerable<Guid> dependencyIds) =>
            dependencyIds
                .Where(dependencyId =>
                    dependencyId != Guid.Empty)
                .Distinct()
                .ToArray();

    /// <summary>
    /// Formats dependency identifiers for the Markdown table.
    /// </summary>
    /// <param name="dependencyIds">The dependency identifiers.</param>
    /// <returns>The persisted list.</returns>
    private static string FormatDependencyIds(
            IEnumerable<Guid> dependencyIds) =>
            string.Join(
                ", ",
                NormalizeDependencyIds(
                    dependencyIds)
                    .Select(dependencyId =>
                        dependencyId.ToString("D")));

    /// <summary>
    /// Validates that a proposed dependency set references existing milestones and remains acyclic.
    /// </summary>
    /// <param name="milestoneId">The milestone being created or updated.</param>
    /// <param name="dependencyIds">The proposed dependencies.</param>
    /// <param name="milestones">The current project milestones.</param>
    private static void ValidateDependencyDraft(
            Guid milestoneId,
            IReadOnlyList<Guid> dependencyIds,
            IReadOnlyList<MilestoneItem> milestones)
    {
        global::System.Collections.Generic.IReadOnlyList<global::System.Guid> normalized =
            NormalizeDependencyIds(
                dependencyIds);

        if (normalized.Contains(
                milestoneId))
        {
            throw new InvalidDataException(
                "Un jalon ne peut pas dépendre de lui-même.");
        }

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Milestones.MilestoneItem> byId =
            milestones
                .GroupBy(item =>
                    item.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());

        foreach (Guid dependencyId in normalized)
        {
            if (!byId.ContainsKey(
                    dependencyId))
            {
                throw new InvalidDataException(
                    $"Le jalon prérequis {dependencyId:D} est introuvable.");
            }
        }

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Collections.Generic.IReadOnlyList<global::System.Guid>> graph =
            milestones
                .GroupBy(item =>
                    item.Id)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        (IReadOnlyList<Guid>)NormalizeDependencyIds(
                            group.First().DependencyIds));

        graph[milestoneId] = normalized;

        if (HasDependencyCycle(
                milestoneId,
                graph))
        {
            throw new InvalidDataException(
                "Cette modification créerait un cycle de dépendances entre jalons.");
        }
    }

    /// <summary>
    /// Resolves dependency names and warnings for display.
    /// </summary>
    /// <param name="milestones">The parsed milestones.</param>
    /// <returns>The enriched milestone list.</returns>
    private static IReadOnlyList<MilestoneItem> EnrichDependencyMetadata(
            IReadOnlyList<MilestoneItem> milestones)
    {
        global::System.Collections.Generic.Dictionary<global::System.Guid, global::Nodalis.Core.Milestones.MilestoneItem> byId =
            milestones
                .GroupBy(item =>
                    item.Id)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());

        global::System.Collections.Generic.Dictionary<global::System.Guid, global::System.Collections.Generic.IReadOnlyList<global::System.Guid>> graph =
            byId.ToDictionary(
                pair => pair.Key,
                pair =>
                    (IReadOnlyList<Guid>)NormalizeDependencyIds(
                        pair.Value.DependencyIds));

        global::System.Collections.Generic.List<global::Nodalis.Core.Milestones.MilestoneItem> result =
            new List<MilestoneItem>();

        foreach (MilestoneItem item in milestones)
        {
            global::System.Collections.Generic.List<string> names =
                new List<string>();
            global::System.Collections.Generic.List<string> warnings =
                new List<string>();

            foreach (Guid dependencyId in NormalizeDependencyIds(
                         item.DependencyIds))
            {
                if (dependencyId == item.Id)
                {
                    warnings.Add(
                        "Le jalon dépend de lui-même.");
                    continue;
                }

                if (!byId.TryGetValue(
                        dependencyId,
                        out global::Nodalis.Core.Milestones.MilestoneItem? dependency))
                {
                    warnings.Add(
                        $"Prérequis introuvable : {dependencyId:D}.");
                    continue;
                }

                names.Add(
                    dependency.Name);

                if (item.TargetDate is DateOnly itemDate &&
                    dependency.TargetDate is DateOnly dependencyDate &&
                    itemDate < dependencyDate)
                {
                    warnings.Add(
                        $"Prévu le {itemDate:dd/MM/yyyy} avant « {dependency.Name} » ({dependencyDate:dd/MM/yyyy}).");
                }
            }

            if (HasDependencyCycle(
                    item.Id,
                    graph))
            {
                warnings.Add(
                    "Cycle de dépendances détecté.");
            }

            result.Add(
                item with
                {
                    DependencyIds = NormalizeDependencyIds(
                        item.DependencyIds),
                    DependencyNames = names,
                    DependencyWarnings = warnings
                });
        }

        return result;
    }

    /// <summary>
    /// Detects a dependency cycle reachable from a milestone.
    /// </summary>
    /// <param name="startId">The starting milestone.</param>
    /// <param name="graph">The dependency graph.</param>
    /// <returns><see langword="true"/> when a cycle is detected.</returns>
    private static bool HasDependencyCycle(
            Guid startId,
            IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> graph)
    {
        global::System.Collections.Generic.HashSet<global::System.Guid> visiting =
            new HashSet<Guid>();
        global::System.Collections.Generic.HashSet<global::System.Guid> visited =
            new HashSet<Guid>();

        return VisitDependency(
            startId,
            graph,
            visiting,
            visited);
    }

    /// <summary>
    /// Traverses the dependency graph for cycle detection.
    /// </summary>
    /// <param name="milestoneId">The current milestone.</param>
    /// <param name="graph">The dependency graph.</param>
    /// <param name="visiting">The active recursion path.</param>
    /// <param name="visited">The completed nodes.</param>
    /// <returns><see langword="true"/> when a cycle is detected.</returns>
    private static bool VisitDependency(
            Guid milestoneId,
            IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> graph,
            ISet<Guid> visiting,
            ISet<Guid> visited)
    {
        if (visited.Contains(
                milestoneId))
        {
            return false;
        }

        if (!visiting.Add(
                milestoneId))
        {
            return true;
        }

        if (graph.TryGetValue(
                milestoneId,
                out global::System.Collections.Generic.IReadOnlyList<global::System.Guid>? dependencies))
        {
            foreach (Guid dependencyId in dependencies)
            {
                if (VisitDependency(
                        dependencyId,
                        graph,
                        visiting,
                        visited))
                {
                    return true;
                }
            }
        }

        visiting.Remove(
            milestoneId);
        visited.Add(
            milestoneId);
        return false;
    }

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
    private static void ValidateDraft(MilestoneDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Name);
    }
}
