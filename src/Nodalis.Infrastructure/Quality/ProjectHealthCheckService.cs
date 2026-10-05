using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nodalis.Core.Domain;
using Nodalis.Core.Links;
using Nodalis.Core.Projects;
using Nodalis.Core.Quality;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Quality;

/// <summary>
/// Performs deterministic, read-only quality checks for one project.
/// </summary>
public sealed class ProjectHealthCheckService
{
    private const string CheckboxPattern =
        @"^\s*[-*+]\s+\[(?<checked>[ xX])\]\s+(?<body>.*)$";

    private readonly string _workspaceRoot;
    private readonly string _linkIndexPath;

    /// <summary>
    /// Initializes a project health service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public ProjectHealthCheckService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _linkIndexPath =
            Path.Combine(
                _workspaceRoot,
                WorkspaceLayout.LinkIndexFileName);
    }

    /// <summary>
    /// Analyzes one project without writing to the workspace or using network services.
    /// </summary>
    /// <param name="projectDirectory">The project directory to analyze.</param>
    /// <param name="options">The deterministic rule thresholds, or defaults when omitted.</param>
    /// <param name="today">The effective local date, primarily injectable for deterministic tests.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered project health report.</returns>
    public async Task<ProjectHealthReport> ScanAsync(
            string projectDirectory,
            ProjectHealthOptions? options = null,
            DateOnly? today = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);

        string fullProjectDirectory =
            Path.GetFullPath(
                projectDirectory);

        if (!IsWithinWorkspace(
                fullProjectDirectory))
        {
            throw new InvalidDataException(
                "Le projet à analyser se trouve hors du workspace.");
        }

        string manifestPath =
            Path.Combine(
                fullProjectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

        if (!File.Exists(
                manifestPath))
        {
            throw new FileNotFoundException(
                "Le manifest du projet est introuvable.",
                manifestPath);
        }

        ProjectHealthOptions effectiveOptions =
            options ??
            new ProjectHealthOptions();

        ValidateOptions(
            effectiveOptions);

        ProjectManifest project =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                manifestPath,
                cancellationToken);

        DateOnly effectiveToday =
            today ??
            DateOnly.FromDateTime(
                DateTime.Today);

        List<DocumentSnapshot> documents =
            await LoadProjectDocumentsAsync(
                fullProjectDirectory,
                cancellationToken);

        List<ProjectHealthIssue> issues =
            new List<ProjectHealthIssue>();

        CheckRequiredSections(
            project,
            fullProjectDirectory,
            issues);

        CheckOverdueTasks(
            documents,
            effectiveToday,
            issues);

        CheckMilestones(
            project,
            documents,
            issues);

        CheckTestsSection(
            project,
            fullProjectDirectory,
            documents,
            effectiveOptions,
            issues);

        CheckMeetingRecency(
            fullProjectDirectory,
            documents,
            effectiveToday,
            effectiveOptions,
            issues);

        LinkIndexCatalog? linkIndex =
            await TryLoadLinkIndexAsync(
                issues,
                cancellationToken);

        if (linkIndex is not null)
        {
            CheckLinksDecisionsAndOrphans(
                project,
                fullProjectDirectory,
                documents,
                linkIndex,
                issues);
        }

        List<ProjectHealthIssue> ordered =
            issues
                .OrderBy(issue =>
                    issue.Severity == ProjectHealthSeverity.Error
                        ? 0
                        : 1)
                .ThenBy(
                    issue => issue.Code,
                    StringComparer.Ordinal)
                .ThenBy(
                    issue => issue.RelativePath ?? string.Empty,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    issue => issue.LineNumber ?? 0)
                .ThenBy(
                    issue => issue.Message,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        return new ProjectHealthReport
        {
            ProjectId =
                project.Id,
            ProjectName =
                project.Name,
            GeneratedUtc =
                DateTimeOffset.UtcNow,
            Issues =
                ordered
        };
    }

    /// <summary>
    /// Validates user-configurable deterministic thresholds.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    private static void ValidateOptions(
            ProjectHealthOptions options)
    {
        if (options.MeetingRecencyDays < 1 ||
            options.MeetingRecencyDays > 3650)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Le seuil de réunion récente doit être compris entre 1 et 3650 jours.");
        }

        if (options.MinimumTestContentCharacters < 1 ||
            options.MinimumTestContentCharacters > 100000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Le seuil de contenu de tests doit être compris entre 1 et 100000 caractères.");
        }
    }

    /// <summary>
    /// Loads Markdown documents that belong directly to the project while excluding nested sub-projects and non-note storage.
    /// </summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The project Markdown snapshots.</returns>
    private async Task<List<DocumentSnapshot>> LoadProjectDocumentsAsync(
            string projectDirectory,
            CancellationToken cancellationToken)
    {
        List<DocumentSnapshot> documents =
            new List<DocumentSnapshot>();

        foreach (string filePath in Directory.EnumerateFiles(
                     projectDirectory,
                     "*.md",
                     SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string projectRelativePath =
                NormalizeRelativePath(
                    Path.GetRelativePath(
                        projectDirectory,
                        filePath));

            if (ShouldIgnoreProjectPath(
                    projectRelativePath))
            {
                continue;
            }

            string content =
                await File.ReadAllTextAsync(
                    filePath,
                    cancellationToken);

            documents.Add(
                new DocumentSnapshot(
                    filePath,
                    NormalizeRelativePath(
                        Path.GetRelativePath(
                            _workspaceRoot,
                            filePath)),
                    projectRelativePath,
                    content,
                    NormalizeNewlines(
                            content)
                        .Split(
                            '\n')));
        }

        return documents;
    }

    /// <summary>
    /// Determines whether a project-relative path belongs to nested or non-note storage.
    /// </summary>
    /// <param name="projectRelativePath">The normalized project-relative path.</param>
    /// <returns><see langword="true"/> when the path must be ignored.</returns>
    private static bool ShouldIgnoreProjectPath(
            string projectRelativePath)
    {
        string[] segments =
            projectRelativePath.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length <= 1)
        {
            return false;
        }

        string firstSegment =
            segments[0];

        return string.Equals(
                   firstSegment,
                   WorkspaceLayout.SubProjectsDirectoryName,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   firstSegment,
                   WorkspaceLayout.AttachmentsDirectoryName,
                   StringComparison.OrdinalIgnoreCase) ||
               firstSegment.StartsWith(
                   ".",
                   StringComparison.Ordinal);
    }

    /// <summary>
    /// Reports required logical project sections that are absent from the manifest.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="issues">The findings collection.</param>
    private void CheckRequiredSections(
            ProjectManifest project,
            string projectDirectory,
            ICollection<ProjectHealthIssue> issues)
    {
        foreach (string requiredRole in ProjectRequiredSectionPolicy.RequiredRoles)
        {
            bool present =
                project.Sections.Any(section =>
                    SectionMatchesRole(
                        section,
                        requiredRole));

            if (present)
            {
                continue;
            }

            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Error,
                    "SECTION_REQUIRED_MISSING",
                    "La section obligatoire « " +
                    requiredRole +
                    " » est absente du projet.",
                    NormalizeRelativePath(
                        Path.GetRelativePath(
                            _workspaceRoot,
                            Path.Combine(
                                projectDirectory,
                                WorkspaceLayout.ProjectManifestFileName)))));
        }
    }

    /// <summary>
    /// Reports open Markdown tasks whose valid due date is before the effective date.
    /// </summary>
    /// <param name="documents">The project documents.</param>
    /// <param name="today">The effective date.</param>
    /// <param name="issues">The findings collection.</param>
    private static void CheckOverdueTasks(
            IReadOnlyList<DocumentSnapshot> documents,
            DateOnly today,
            ICollection<ProjectHealthIssue> issues)
    {
        foreach (DocumentSnapshot document in documents)
        {
            for (int index = 0;
                 index < document.Lines.Length;
                 index++)
            {
                Match checkbox =
                    Regex.Match(
                        document.Lines[index],
                        CheckboxPattern,
                        RegexOptions.CultureInvariant);

                if (!checkbox.Success ||
                    !string.IsNullOrWhiteSpace(
                        checkbox.Groups["checked"].Value))
                {
                    continue;
                }

                string body =
                    checkbox.Groups["body"].Value.Trim();

                DateOnly? dueDate =
                    TryParseTaskDueDate(
                        body);

                if (dueDate is not DateOnly parsedDueDate ||
                    parsedDueDate >= today)
                {
                    continue;
                }

                issues.Add(
                    CreateIssue(
                        ProjectHealthSeverity.Warning,
                        "TASK_OVERDUE",
                        "La tâche « " +
                        ExtractTaskText(
                            body) +
                        " » est en retard depuis le " +
                        parsedDueDate.ToString(
                            "dd/MM/yyyy",
                            CultureInfo.InvariantCulture) +
                        ".",
                        document.RelativePath,
                        index + 1));
            }
        }
    }

    /// <summary>
    /// Parses the due date stored in readable task metadata.
    /// </summary>
    /// <param name="body">The Markdown checkbox body.</param>
    /// <returns>The due date when a valid one is present.</returns>
    private static DateOnly? TryParseTaskDueDate(
            string body)
    {
        string[] segments =
            body.Split(
                '|',
                StringSplitOptions.TrimEntries);

        foreach (string segment in segments)
        {
            int separatorIndex =
                segment.IndexOf(
                    ':');

            if (separatorIndex <= 0)
            {
                continue;
            }

            string key =
                segment[..separatorIndex]
                    .Trim();

            if (!string.Equals(
                    key,
                    "Échéance",
                    StringComparison.CurrentCultureIgnoreCase) &&
                !string.Equals(
                    key,
                    "Echeance",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    key,
                    "Due",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value =
                segment[(separatorIndex + 1)..]
                    .Trim();

            if (DateOnly.TryParseExact(
                    value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly dueDate))
            {
                return dueDate;
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Extracts the readable task text from a metadata-bearing checkbox body.
    /// </summary>
    /// <param name="body">The Markdown checkbox body.</param>
    /// <returns>The task text.</returns>
    private static string ExtractTaskText(
            string body)
    {
        string[] segments =
            body.Split(
                '|',
                StringSplitOptions.TrimEntries);

        return segments.Length == 0 ||
               string.IsNullOrWhiteSpace(
                   segments[0])
            ? "tâche sans libellé"
            : segments[0].Trim();
    }

    /// <summary>
    /// Reports milestones whose rows do not contain a valid target date.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="documents">The project documents.</param>
    /// <param name="issues">The findings collection.</param>
    private static void CheckMilestones(
            ProjectManifest project,
            IReadOnlyList<DocumentSnapshot> documents,
            ICollection<ProjectHealthIssue> issues)
    {
        SectionManifest[] milestoneSections =
            project.Sections
                .Where(section =>
                    SectionMatchesRole(
                        section,
                        "Jalons"))
                .ToArray();

        foreach (DocumentSnapshot document in documents.Where(document =>
                     milestoneSections.Any(section =>
                         IsDocumentInSection(
                             document,
                             section))))
        {
            int milestoneColumn =
                -1;
            int dateColumn =
                -1;

            for (int lineIndex = 0;
                 lineIndex < document.Lines.Length;
                 lineIndex++)
            {
                string line =
                    document.Lines[lineIndex];

                if (!line.TrimStart().StartsWith(
                        "|",
                        StringComparison.Ordinal))
                {
                    milestoneColumn =
                        -1;
                    dateColumn =
                        -1;
                    continue;
                }

                string[] cells =
                    SplitTableCells(
                        line);

                if (milestoneColumn < 0)
                {
                    milestoneColumn =
                        FindTableColumn(
                            cells,
                            "jalon");
                    dateColumn =
                        FindTableColumn(
                            cells,
                            "date cible");

                    if (milestoneColumn >= 0 &&
                        dateColumn >= 0)
                    {
                        continue;
                    }

                    milestoneColumn =
                        -1;
                    dateColumn =
                        -1;
                    continue;
                }

                if (IsTableSeparatorRow(
                        cells) ||
                    milestoneColumn >= cells.Length ||
                    dateColumn >= cells.Length)
                {
                    continue;
                }

                string milestoneName =
                    cells[milestoneColumn]
                        .Trim();

                if (string.IsNullOrWhiteSpace(
                        milestoneName))
                {
                    continue;
                }

                string dateText =
                    cells[dateColumn]
                        .Trim();

                if (DateOnly.TryParseExact(
                        dateText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly _))
                {
                    continue;
                }

                issues.Add(
                    CreateIssue(
                        ProjectHealthSeverity.Warning,
                        "MILESTONE_DATE_MISSING",
                        "Le jalon « " +
                        milestoneName +
                        " » n'a pas de date cible valide.",
                        document.RelativePath,
                        lineIndex + 1));
            }
        }
    }

    /// <summary>
    /// Splits a simple Markdown table row while preserving empty cells.
    /// </summary>
    /// <param name="line">The Markdown row.</param>
    /// <returns>The table cells without outer pipes.</returns>
    private static string[] SplitTableCells(
            string line)
    {
        string trimmed =
            line.Trim();

        if (trimmed.StartsWith(
                "|",
                StringComparison.Ordinal))
        {
            trimmed =
                trimmed[1..];
        }

        if (trimmed.EndsWith(
                "|",
                StringComparison.Ordinal))
        {
            trimmed =
                trimmed[..^1];
        }

        return trimmed.Split(
            '|');
    }

    /// <summary>
    /// Finds a normalized table column.
    /// </summary>
    /// <param name="cells">The header cells.</param>
    /// <param name="name">The normalized column name.</param>
    /// <returns>The zero-based column index, or -1 when absent.</returns>
    private static int FindTableColumn(
            IReadOnlyList<string> cells,
            string name)
    {
        for (int index = 0;
             index < cells.Count;
             index++)
        {
            if (string.Equals(
                    NormalizeHeader(
                        cells[index]),
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Normalizes a Markdown table header for deterministic matching.
    /// </summary>
    /// <param name="value">The header value.</param>
    /// <returns>The normalized header.</returns>
    private static string NormalizeHeader(
            string value) =>
            value
                .Trim()
                .ToLowerInvariant();

    /// <summary>
    /// Determines whether a Markdown table row is only a header separator.
    /// </summary>
    /// <param name="cells">The row cells.</param>
    /// <returns><see langword="true"/> for a separator row.</returns>
    private static bool IsTableSeparatorRow(
            IReadOnlyList<string> cells) =>
            cells.Count > 0 &&
            cells.All(cell =>
            {
                string trimmed =
                    cell.Trim();

                return trimmed.Length >= 3 &&
                       trimmed.All(character =>
                           character is '-' or ':');
            });

    /// <summary>
    /// Reports a Tests section whose meaningful content remains below the configured threshold.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="documents">The project documents.</param>
    /// <param name="options">The health options.</param>
    /// <param name="issues">The findings collection.</param>
    private void CheckTestsSection(
            ProjectManifest project,
            string projectDirectory,
            IReadOnlyList<DocumentSnapshot> documents,
            ProjectHealthOptions options,
            ICollection<ProjectHealthIssue> issues)
    {
        SectionManifest[] testSections =
            project.Sections
                .Where(section =>
                    SectionMatchesRole(
                        section,
                        "Tests"))
                .ToArray();

        if (testSections.Length == 0)
        {
            return;
        }

        DocumentSnapshot[] testDocuments =
            documents
                .Where(document =>
                    testSections.Any(section =>
                        IsDocumentInSection(
                            document,
                            section)))
                .ToArray();

        int meaningfulCharacters =
            testDocuments.Sum(document =>
                CountMeaningfulContentCharacters(
                    document.Lines));

        if (meaningfulCharacters >=
            options.MinimumTestContentCharacters)
        {
            return;
        }

        string relativePath =
            testDocuments.FirstOrDefault()?.RelativePath ??
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    Path.Combine(
                        projectDirectory,
                        WindowsPathRules.SanitizeSegment(
                            testSections[0].Name))));

        issues.Add(
            CreateIssue(
                ProjectHealthSeverity.Warning,
                "TESTS_TOO_LIGHT",
                "La section Tests contient seulement " +
                meaningfulCharacters +
                " caractère(s) de contenu significatif ; le seuil est de " +
                options.MinimumTestContentCharacters +
                ".",
                relativePath));
    }

    /// <summary>
    /// Counts letters and digits outside structural Markdown-only lines.
    /// </summary>
    /// <param name="lines">The document lines.</param>
    /// <returns>The meaningful character count.</returns>
    private static int CountMeaningfulContentCharacters(
            IReadOnlyList<string> lines)
    {
        int count =
            0;
        bool frontMatter =
            false;

        for (int index = 0;
             index < lines.Count;
             index++)
        {
            string trimmed =
                lines[index].Trim();

            if (index == 0 &&
                string.Equals(
                    trimmed,
                    "---",
                    StringComparison.Ordinal))
            {
                frontMatter =
                    true;
                continue;
            }

            if (frontMatter)
            {
                if (string.Equals(
                        trimmed,
                        "---",
                        StringComparison.Ordinal))
                {
                    frontMatter =
                        false;
                }

                continue;
            }

            if (trimmed.Length == 0 ||
                trimmed.StartsWith(
                    "#",
                    StringComparison.Ordinal) ||
                trimmed.StartsWith(
                    "<!--",
                    StringComparison.Ordinal) ||
                IsMarkdownSeparator(
                    trimmed))
            {
                continue;
            }

            count +=
                trimmed.Count(
                    char.IsLetterOrDigit);
        }

        return count;
    }

    /// <summary>
    /// Determines whether a line contains only Markdown separator characters.
    /// </summary>
    /// <param name="line">The trimmed line.</param>
    /// <returns><see langword="true"/> for structural separators.</returns>
    private static bool IsMarkdownSeparator(
            string line)
    {
        string compact =
            line.Replace(
                    "|",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    ":",
                    string.Empty,
                    StringComparison.Ordinal)
                .Replace(
                    "-",
                    string.Empty,
                    StringComparison.Ordinal)
                .Trim();

        return compact.Length == 0;
    }

    /// <summary>
    /// Reports projects without a dated meeting inside the configured recency window.
    /// </summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="documents">The project documents.</param>
    /// <param name="today">The effective date.</param>
    /// <param name="options">The health options.</param>
    /// <param name="issues">The findings collection.</param>
    private void CheckMeetingRecency(
            string projectDirectory,
            IReadOnlyList<DocumentSnapshot> documents,
            DateOnly today,
            ProjectHealthOptions options,
            ICollection<ProjectHealthIssue> issues)
    {
        string meetingPrefix =
            WorkspaceMeetingDirectoryName +
            "/";

        DocumentSnapshot[] meetingDocuments =
            documents
                .Where(document =>
                    document.ProjectRelativePath.StartsWith(
                        meetingPrefix,
                        StringComparison.CurrentCultureIgnoreCase) &&
                    document.ProjectRelativePath.Count(character =>
                        character == '/') == 1)
                .ToArray();

        DateOnly? latestDate =
            null;
        DocumentSnapshot? latestDocument =
            null;

        foreach (DocumentSnapshot document in meetingDocuments)
        {
            DateOnly? meetingDate =
                TryReadMeetingDate(
                    document);

            if (meetingDate is not DateOnly parsedDate ||
                latestDate is DateOnly currentLatest &&
                parsedDate <= currentLatest)
            {
                continue;
            }

            latestDate =
                parsedDate;
            latestDocument =
                document;
        }

        DateOnly minimumDate =
            today.AddDays(
                -options.MeetingRecencyDays);

        if (latestDate is DateOnly lastMeeting &&
            lastMeeting >= minimumDate)
        {
            return;
        }

        string message =
            latestDate is DateOnly knownDate
                ? "Aucune réunion depuis le " +
                  knownDate.ToString(
                      "dd/MM/yyyy",
                      CultureInfo.InvariantCulture) +
                  " ; le seuil configuré est de " +
                  options.MeetingRecencyDays +
                  " jour(s)."
                : "Aucune réunion datée n'a été trouvée sur les " +
                  options.MeetingRecencyDays +
                  " dernier(s) jour(s).";

        string relativePath =
            latestDocument?.RelativePath ??
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    Path.Combine(
                        projectDirectory,
                        WorkspaceMeetingDirectoryName)));

        issues.Add(
            CreateIssue(
                ProjectHealthSeverity.Warning,
                "MEETING_NOT_RECENT",
                message,
                relativePath));
    }

    /// <summary>
    /// Reads a meeting date from the canonical filename or the readable Date metadata.
    /// </summary>
    /// <param name="document">The meeting document.</param>
    /// <returns>The meeting date when available.</returns>
    private static DateOnly? TryReadMeetingDate(
            DocumentSnapshot document)
    {
        string fileName =
            Path.GetFileName(
                document.FullPath);

        if (fileName.Length >= 10 &&
            DateOnly.TryParseExact(
                fileName[..10],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly fileDate))
        {
            return fileDate;
        }

        foreach (string line in document.Lines)
        {
            Match match =
                Regex.Match(
                    line,
                    @"^\s*\*\*Date\s*:\*\*\s*(?<date>\d{4}-\d{2}-\d{2})\s*$",
                    RegexOptions.CultureInvariant |
                    RegexOptions.IgnoreCase);

            if (match.Success &&
                DateOnly.TryParseExact(
                    match.Groups["date"].Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateOnly metadataDate))
            {
                return metadataDate;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads the existing derived link index without rebuilding it.
    /// </summary>
    /// <param name="issues">The findings collection.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The link index, or <see langword="null"/> when it is unavailable.</returns>
    private async Task<LinkIndexCatalog?> TryLoadLinkIndexAsync(
            ICollection<ProjectHealthIssue> issues,
            CancellationToken cancellationToken)
    {
        if (!File.Exists(
                _linkIndexPath))
        {
            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Warning,
                    "LINK_INDEX_UNAVAILABLE",
                    "L'index de liens est absent. Le Health Check ne le reconstruit pas automatiquement ; utilisez le diagnostic du workspace.",
                    WorkspaceLayout.LinkIndexFileName));
            return null;
        }

        try
        {
            LinkIndexCatalog catalog =
                await AtomicJsonFile.ReadAsync<LinkIndexCatalog>(
                    _linkIndexPath,
                    cancellationToken);

            if (catalog.SchemaVersion !=
                LinkIndexCatalog.CurrentSchemaVersion)
            {
                issues.Add(
                    CreateIssue(
                        ProjectHealthSeverity.Warning,
                        "LINK_INDEX_UNAVAILABLE",
                        "La version de l'index de liens n'est pas compatible. Utilisez le diagnostic du workspace pour le reconstruire.",
                        WorkspaceLayout.LinkIndexFileName));
                return null;
            }

            return catalog;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Warning,
                    "LINK_INDEX_UNAVAILABLE",
                    "L'index de liens est illisible. Utilisez le diagnostic du workspace pour le reconstruire.",
                    WorkspaceLayout.LinkIndexFileName));
            return null;
        }
    }

    /// <summary>
    /// Reports unresolved links, missing decision sources and orphan documents from the existing link index.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="documents">The project document snapshots.</param>
    /// <param name="linkIndex">The existing read-only link index.</param>
    /// <param name="issues">The findings collection.</param>
    private static void CheckLinksDecisionsAndOrphans(
            ProjectManifest project,
            string projectDirectory,
            IReadOnlyList<DocumentSnapshot> documents,
            LinkIndexCatalog linkIndex,
            ICollection<ProjectHealthIssue> issues)
    {
        string scopeIdentity =
            "project:" +
            project.Id.ToString(
                "D",
                CultureInfo.InvariantCulture);

        LinkTargetEntry[] projectTargets =
            linkIndex.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Document &&
                    string.Equals(
                        target.ScopeIdentity,
                        scopeIdentity,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        HashSet<Guid> projectDocumentIds =
            projectTargets
                .Select(target =>
                    target.Id)
                .ToHashSet();

        HashSet<Guid> existingTargetIds =
            linkIndex.Targets
                .Select(target =>
                    target.Id)
                .ToHashSet();

        Dictionary<string, DocumentSnapshot> documentsByPath =
            documents.ToDictionary(
                document =>
                    document.RelativePath,
                StringComparer.OrdinalIgnoreCase);

        Dictionary<Guid, LinkTargetEntry> projectTargetsById =
            projectTargets
                .GroupBy(target =>
                    target.Id)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.First());

        CheckDecisionSources(
            projectTargets,
            documentsByPath,
            linkIndex,
            existingTargetIds,
            issues);

        foreach (LinkReferenceEntry reference in linkIndex.References.Where(reference =>
                     projectDocumentIds.Contains(
                         reference.SourceId)))
        {
            bool broken =
                reference.TargetId is not Guid targetId ||
                !existingTargetIds.Contains(
                    targetId);

            if (!broken ||
                IsDecisionSourceLine(
                    reference,
                    projectTargetsById,
                    documentsByPath))
            {
                continue;
            }

            if (!projectTargetsById.TryGetValue(
                    reference.SourceId,
                    out LinkTargetEntry? sourceTarget))
            {
                continue;
            }

            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Error,
                    "LINK_UNRESOLVED",
                    "Le lien interne « " +
                    reference.RawTarget +
                    " » ne peut pas être résolu.",
                    sourceTarget.RelativePath,
                    reference.LineNumber));
        }

        foreach (global::Nodalis.Core.Relations.TypedRelationEntry relation in linkIndex.Relations.Where(relation =>
                     projectDocumentIds.Contains(
                         relation.SourceId)))
        {
            bool missing =
                relation.TargetId is not Guid targetId ||
                !existingTargetIds.Contains(
                    targetId);

            if (!missing ||
                !projectTargetsById.TryGetValue(
                    relation.SourceId,
                    out LinkTargetEntry? sourceTarget))
            {
                continue;
            }

            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Error,
                    "RELATION_TARGET_MISSING",
                    "La relation « " +
                    relation.RelationType +
                    " » cible un élément introuvable (« " +
                    relation.RawTargetId +
                    " »).",
                    sourceTarget.RelativePath,
                    relation.LineNumber));
        }

        foreach (LinkTargetEntry target in projectTargets)
        {
            if (!documentsByPath.TryGetValue(
                    target.RelativePath,
                    out DocumentSnapshot? document) ||
                IsStructuralDocument(
                    document,
                    project))
            {
                continue;
            }

            bool hasInboundReference =
                linkIndex.References.Any(reference =>
                    reference.TargetId == target.Id) ||
                linkIndex.Relations.Any(relation =>
                    relation.TargetId == target.Id);

            if (hasInboundReference)
            {
                continue;
            }

            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Warning,
                    "DOCUMENT_ORPHAN",
                    "Le document n'est référencé par aucun lien ou relation du workspace.",
                    target.RelativePath));
        }
    }

    /// <summary>
    /// Reports Decision Records whose explicit source metadata is no longer resolvable.
    /// </summary>
    /// <param name="projectTargets">The project document targets.</param>
    /// <param name="documentsByPath">The project documents by workspace-relative path.</param>
    /// <param name="linkIndex">The existing link index.</param>
    /// <param name="existingTargetIds">The existing indexed target identifiers.</param>
    /// <param name="issues">The findings collection.</param>
    private static void CheckDecisionSources(
            IReadOnlyList<LinkTargetEntry> projectTargets,
            IReadOnlyDictionary<string, DocumentSnapshot> documentsByPath,
            LinkIndexCatalog linkIndex,
            IReadOnlySet<Guid> existingTargetIds,
            ICollection<ProjectHealthIssue> issues)
    {
        foreach (LinkTargetEntry target in projectTargets)
        {
            if (!documentsByPath.TryGetValue(
                    target.RelativePath,
                    out DocumentSnapshot? document) ||
                !IsDecisionDocument(
                    document))
            {
                continue;
            }

            int sourceLineIndex =
                FindDecisionSourceLineIndex(
                    document);

            if (sourceLineIndex < 0)
            {
                continue;
            }

            bool resolved =
                linkIndex.References.Any(reference =>
                    reference.SourceId == target.Id &&
                    reference.LineNumber == sourceLineIndex + 1 &&
                    reference.TargetId is Guid targetId &&
                    existingTargetIds.Contains(
                        targetId));

            if (resolved)
            {
                continue;
            }

            issues.Add(
                CreateIssue(
                    ProjectHealthSeverity.Error,
                    "DECISION_SOURCE_MISSING",
                    "La source déclarée de cette décision est introuvable ou ne peut plus être résolue.",
                    document.RelativePath,
                    sourceLineIndex + 1));
        }
    }

    /// <summary>
    /// Determines whether a broken link reference is the source metadata of a Decision Record.
    /// </summary>
    /// <param name="reference">The indexed reference.</param>
    /// <param name="projectTargetsById">The project targets by identifier.</param>
    /// <param name="documentsByPath">The project documents by path.</param>
    /// <returns><see langword="true"/> for Decision Record source metadata.</returns>
    private static bool IsDecisionSourceLine(
            LinkReferenceEntry reference,
            IReadOnlyDictionary<Guid, LinkTargetEntry> projectTargetsById,
            IReadOnlyDictionary<string, DocumentSnapshot> documentsByPath)
    {
        if (!projectTargetsById.TryGetValue(
                reference.SourceId,
                out LinkTargetEntry? sourceTarget) ||
            !documentsByPath.TryGetValue(
                sourceTarget.RelativePath,
                out DocumentSnapshot? document) ||
            !IsDecisionDocument(
                document))
        {
            return false;
        }

        return FindDecisionSourceLineIndex(
                   document) ==
               reference.LineNumber - 1;
    }

    /// <summary>
    /// Finds readable Decision Record source metadata.
    /// </summary>
    /// <param name="document">The Decision Record document.</param>
    /// <returns>The zero-based line index, or -1 when no source metadata exists.</returns>
    private static int FindDecisionSourceLineIndex(
            DocumentSnapshot document)
    {
        for (int index = 0;
             index < document.Lines.Length;
             index++)
        {
            if (document.Lines[index]
                .TrimStart()
                .StartsWith(
                    "**Source :**",
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Determines whether a document belongs to the project's Decision Records folder.
    /// </summary>
    /// <param name="document">The project document.</param>
    /// <returns><see langword="true"/> for a Decision Record.</returns>
    private static bool IsDecisionDocument(
            DocumentSnapshot document)
    {
        string prefix =
            WorkspaceDecisionDirectoryName +
            "/";

        return document.ProjectRelativePath.StartsWith(
            prefix,
            StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Determines whether a document is generated structural project content that should not be treated as orphaned.
    /// </summary>
    /// <param name="document">The project document.</param>
    /// <param name="project">The project manifest.</param>
    /// <returns><see langword="true"/> for structural content.</returns>
    private static bool IsStructuralDocument(
            DocumentSnapshot document,
            ProjectManifest project)
    {
        if (string.Equals(
                document.ProjectRelativePath,
                "Présentation.md",
                StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(
                document.ProjectRelativePath,
                WorkspaceLayout.GlobalQuickNotesFileName,
                StringComparison.CurrentCultureIgnoreCase) ||
            document.ProjectRelativePath.StartsWith(
                WorkspaceDecisionDirectoryName + "/",
                StringComparison.CurrentCultureIgnoreCase) ||
            document.ProjectRelativePath.StartsWith(
                WorkspaceMeetingDirectoryName + "/",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        foreach (SectionManifest section in project.Sections)
        {
            string sectionSegment =
                WindowsPathRules.SanitizeSegment(
                    section.Name);
            string canonicalPath =
                sectionSegment +
                "/" +
                sectionSegment +
                ".md";

            if (string.Equals(
                    document.ProjectRelativePath,
                    canonicalPath,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether one project document belongs to a manifest section.
    /// </summary>
    /// <param name="document">The project document.</param>
    /// <param name="section">The manifest section.</param>
    /// <returns><see langword="true"/> when the first path segment matches the section directory.</returns>
    private static bool IsDocumentInSection(
            DocumentSnapshot document,
            SectionManifest section)
    {
        string prefix =
            WindowsPathRules.SanitizeSegment(
                section.Name) +
            "/";

        return document.ProjectRelativePath.StartsWith(
            prefix,
            StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Determines whether a manifest section fulfills a required logical role.
    /// </summary>
    /// <param name="section">The manifest section.</param>
    /// <param name="role">The required logical role.</param>
    /// <returns><see langword="true"/> when the section fulfills the role.</returns>
    private static bool SectionMatchesRole(
            SectionManifest section,
            string role)
    {
        string? requiredRole =
            ProjectRequiredSectionPolicy.GetRequiredRole(
                section.TemplateKey);

        return string.Equals(
                   requiredRole,
                   role,
                   StringComparison.CurrentCultureIgnoreCase) ||
               string.Equals(
                   section.Name,
                   role,
                   StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Checks whether a path is contained by the configured workspace.
    /// </summary>
    /// <param name="path">The full path to validate.</param>
    /// <returns><see langword="true"/> when the path is within the workspace.</returns>
    private bool IsWithinWorkspace(
            string path)
    {
        string normalizedRoot =
            _workspaceRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        string normalizedPath =
            Path.GetFullPath(
                path);

        return string.Equals(
                   normalizedPath,
                   _workspaceRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(
                   normalizedRoot,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates one project health issue.
    /// </summary>
    /// <param name="severity">The issue severity.</param>
    /// <param name="code">The stable rule code.</param>
    /// <param name="message">The readable explanation.</param>
    /// <param name="relativePath">The workspace-relative source path.</param>
    /// <param name="lineNumber">The optional one-based line.</param>
    /// <returns>The issue.</returns>
    private static ProjectHealthIssue CreateIssue(
            ProjectHealthSeverity severity,
            string code,
            string message,
            string? relativePath = null,
            int? lineNumber = null) =>
            new ProjectHealthIssue
            {
                Severity =
                    severity,
                Code =
                    code,
                Message =
                    message,
                RelativePath =
                    relativePath,
                LineNumber =
                    lineNumber
            };

    /// <summary>
    /// Normalizes path separators for persisted and displayed workspace-relative paths.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The slash-separated path.</returns>
    private static string NormalizeRelativePath(
            string path) =>
            path.Replace(
                Path.DirectorySeparatorChar,
                '/');

    /// <summary>
    /// Normalizes line endings without otherwise changing Markdown content.
    /// </summary>
    /// <param name="content">The Markdown content.</param>
    /// <returns>The LF-normalized content.</returns>
    private static string NormalizeNewlines(
            string content) =>
            content
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

    private const string WorkspaceMeetingDirectoryName =
        "Réunions";

    private const string WorkspaceDecisionDirectoryName =
        "Décisions";

    /// <summary>
    /// Stores one immutable in-memory project document used by the read-only checks.
    /// </summary>
    /// <param name="FullPath">The full filesystem path.</param>
    /// <param name="RelativePath">The workspace-relative path.</param>
    /// <param name="ProjectRelativePath">The project-relative path.</param>
    /// <param name="Content">The original Markdown content.</param>
    /// <param name="Lines">The normalized Markdown lines.</param>
    private sealed record DocumentSnapshot(
        string FullPath,
        string RelativePath,
        string ProjectRelativePath,
        string Content,
        string[] Lines);
}
