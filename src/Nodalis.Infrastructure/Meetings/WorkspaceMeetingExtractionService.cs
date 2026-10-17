using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Decisions;
using Nodalis.Core.Meetings;
using Nodalis.Core.Tasks;
using Nodalis.Infrastructure.Decisions;
using Nodalis.Infrastructure.Tasks;

namespace Nodalis.Infrastructure.Meetings;

/// <summary>
/// Extracts actions and decisions from known meeting sections without network access or source mutation during preview.
/// </summary>
public sealed partial class WorkspaceMeetingExtractionService
{
    private readonly string _workspaceRoot;
    private readonly WorkspaceTaskService _taskService;
    private readonly WorkspaceDecisionService _decisionService;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceMeetingExtractionService"/>.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root directory.</param>
    public WorkspaceMeetingExtractionService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _taskService =
            new WorkspaceTaskService(
                _workspaceRoot);
        _decisionService =
            new WorkspaceDecisionService(
                _workspaceRoot);
    }

    /// <summary>
    /// Creates a deterministic read-only preview for one meeting report.
    /// </summary>
    /// <param name="meetingPath">Absolute meeting Markdown path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The extraction preview.</returns>
    public async Task<MeetingExtractionPreview> PreparePreviewAsync(
            string meetingPath,
            CancellationToken cancellationToken = default)
    {
        string fullPath =
            ValidateMeetingPath(
                meetingPath);

        string content =
            await File.ReadAllTextAsync(
                fullPath,
                cancellationToken);

        string relativePath =
            NormalizeRelativePath(
                Path.GetRelativePath(
                    _workspaceRoot,
                    fullPath));

        TaskCollection taskCatalog =
            await _taskService.RefreshAsync(
                cancellationToken);

        IReadOnlyList<MeetingActionExtractionCandidate> actions =
            taskCatalog.Tasks
                .Where(task =>
                    string.Equals(
                        NormalizeRelativePath(
                            task.SourceRelativePath),
                        relativePath,
                        StringComparison.OrdinalIgnoreCase))
                .Select(task =>
                    new MeetingActionExtractionCandidate
                    {
                        Task =
                            task,
                        IsDuplicate =
                            false
                    })
                .OrderBy(candidate =>
                    candidate.Task.LineNumber)
                .ToArray();

        IReadOnlyList<DecisionRecord> existingDecisions =
            await _decisionService.GetDecisionsAsync(
                fullPath,
                cancellationToken);

        HashSet<string> normalizedExisting =
            existingDecisions
                .Select(record =>
                    NormalizeDecisionText(
                        record.Decision))
                .Where(value =>
                    value.Length > 0)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        List<MeetingDecisionExtractionCandidate> decisions =
            new List<MeetingDecisionExtractionCandidate>();

        foreach ((string Text, int LineNumber) item in ExtractDecisionBullets(
                     content))
        {
            string normalized =
                NormalizeDecisionText(
                    item.Text);

            if (normalized.Length == 0)
            {
                continue;
            }

            decisions.Add(
                new MeetingDecisionExtractionCandidate
                {
                    Id =
                        CreateCandidateId(
                            relativePath,
                            item.LineNumber,
                            normalized),
                    Text =
                        item.Text,
                    LineNumber =
                        item.LineNumber,
                    IsDuplicate =
                        normalizedExisting.Contains(
                            normalized)
                });
        }

        return new MeetingExtractionPreview
        {
            MeetingPath =
                fullPath,
            MeetingDate =
                ResolveMeetingDate(
                    content,
                    fullPath),
            Actions =
                actions,
            Decisions =
                decisions
        };
    }

    /// <summary>
    /// Applies only the candidates explicitly selected by the user.
    /// </summary>
    /// <param name="preview">Previously prepared preview.</param>
    /// <param name="request">Explicit candidate selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The materialization result.</returns>
    public async Task<MeetingExtractionResult> ApplyAsync(
            MeetingExtractionPreview preview,
            MeetingExtractionRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preview);
        ArgumentNullException.ThrowIfNull(
            request);

        MeetingExtractionPreview current =
            await PreparePreviewAsync(
                preview.MeetingPath,
                cancellationToken);

        int promotedActions =
            0;
        int createdDecisions =
            0;
        int skippedDuplicates =
            0;

        Dictionary<Guid, MeetingActionExtractionCandidate> currentActions =
            current.Actions.ToDictionary(
                candidate =>
                    candidate.Task.Id);

        foreach (Guid actionId in request.ActionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!currentActions.TryGetValue(
                    actionId,
                    out MeetingActionExtractionCandidate? candidate) ||
                candidate.IsDuplicate)
            {
                skippedDuplicates++;
                continue;
            }

            TaskItem task =
                candidate.Task;

            MeetingActionPromotionResult result =
                await _taskService.PromoteMeetingActionAsync(
                    task,
                    new TaskMetadataUpdate
                    {
                        Owner =
                            task.Owner,
                        DueDate =
                            task.DueDate,
                        Priority =
                            task.Priority,
                        Status =
                            task.Status,
                        Tags =
                            task.Tags
                    },
                    cancellationToken);

            if (result.Created)
            {
                promotedActions++;
            }
            else
            {
                skippedDuplicates++;
            }
        }

        Dictionary<Guid, MeetingDecisionExtractionCandidate> currentDecisions =
            current.Decisions.ToDictionary(
                candidate =>
                    candidate.Id);

        foreach (Guid decisionId in request.DecisionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!currentDecisions.TryGetValue(
                    decisionId,
                    out MeetingDecisionExtractionCandidate? candidate) ||
                candidate.IsDuplicate)
            {
                skippedDuplicates++;
                continue;
            }

            string title =
                BuildDecisionTitle(
                    candidate.Text);

            await _decisionService.CreateAsync(
                current.MeetingPath,
                new DecisionDraft
                {
                    Title =
                        title,
                    Date =
                        current.MeetingDate,
                    Decision =
                        candidate.Text,
                    Context =
                        "Extraite du compte-rendu de réunion.",
                    Status =
                        WorkspaceDecisionService.ActiveStatus
                },
                current.MeetingPath,
                cancellationToken);

            createdDecisions++;
        }

        return new MeetingExtractionResult
        {
            PromotedActionCount =
                promotedActions,
            CreatedDecisionCount =
                createdDecisions,
            SkippedDuplicateCount =
                skippedDuplicates
        };
    }

    /// <summary>
    /// Extracts list entries from the known Decisions section.
    /// </summary>
    /// <param name="content">Meeting Markdown.</param>
    /// <returns>Detected decision text and source line.</returns>
    private static IReadOnlyList<(string Text, int LineNumber)> ExtractDecisionBullets(
            string content)
    {
        string normalized =
            content
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        string[] lines =
            normalized.Split(
                '\n');

        List<(string Text, int LineNumber)> result =
            new List<(string Text, int LineNumber)>();

        bool inSection =
            false;

        for (int index = 0;
             index < lines.Length;
             index++)
        {
            string line =
                lines[index];

            Match heading =
                HeadingPattern().Match(
                    line);

            if (heading.Success)
            {
                string headingText =
                    heading.Groups["title"].Value.Trim();

                if (inSection)
                {
                    break;
                }

                inSection =
                    string.Equals(
                        headingText,
                        "Décisions",
                        StringComparison.CurrentCultureIgnoreCase) ||
                    string.Equals(
                        headingText,
                        "Decisions",
                        StringComparison.CurrentCultureIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            Match bullet =
                BulletPattern().Match(
                    line);

            if (!bullet.Success)
            {
                continue;
            }

            string text =
                bullet.Groups["body"].Value.Trim();

            if (text.Length == 0 ||
                string.Equals(
                    text,
                    "Aucune",
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            result.Add(
                (
                    text,
                    index + 1
                ));
        }

        return result;
    }

    /// <summary>
    /// Validates that the requested file belongs to the workspace and to a meeting directory.
    /// </summary>
    /// <param name="meetingPath">Candidate meeting path.</param>
    /// <returns>The validated absolute path.</returns>
    private string ValidateMeetingPath(
            string meetingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            meetingPath);

        string fullPath =
            Path.GetFullPath(
                meetingPath);

        string relative =
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath);

        if (relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            string.Equals(
                relative,
                "..",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Le compte-rendu se trouve hors du workspace.");
        }

        if (!File.Exists(
                fullPath) ||
            !string.Equals(
                Path.GetExtension(
                    fullPath),
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                "Le compte-rendu Markdown est introuvable.",
                fullPath);
        }

        bool isMeeting =
            relative
                .Split(
                    new[]
                    {
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    },
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(segment =>
                    string.Equals(
                        segment,
                        WorkspaceMeetingService.MeetingsDirectoryName,
                        StringComparison.CurrentCultureIgnoreCase));

        if (!isMeeting)
        {
            throw new InvalidOperationException(
                "Le document sélectionné n'est pas situé dans une section Réunions.");
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves the meeting date from metadata or filename without using current time.
    /// </summary>
    /// <param name="content">Meeting Markdown content.</param>
    /// <param name="fullPath">Absolute meeting file path.</param>
    /// <returns>The deterministic meeting date.</returns>
    private static DateOnly ResolveMeetingDate(
            string content,
            string fullPath)
    {
        Match metadata =
            DateMetadataPattern().Match(
                content);

        if (metadata.Success &&
            DateOnly.TryParseExact(
                metadata.Groups["date"].Value,
                "yyyy-MM-dd",
                null,
                System.Globalization.DateTimeStyles.None,
                out DateOnly date))
        {
            return date;
        }

        Match file =
            FileDatePattern().Match(
                Path.GetFileNameWithoutExtension(
                    fullPath));

        if (file.Success &&
            DateOnly.TryParseExact(
                file.Groups["date"].Value,
                "yyyy-MM-dd",
                null,
                System.Globalization.DateTimeStyles.None,
                out date))
        {
            return date;
        }

        return DateOnly.FromDateTime(
            File.GetCreationTime(
                fullPath));
    }

    /// <summary>
    /// Builds a concise deterministic title from decision text.
    /// </summary>
    /// <param name="text">Decision text.</param>
    /// <returns>A concise Decision Record title.</returns>
    private static string BuildDecisionTitle(
            string text)
    {
        string compact =
            Regex.Replace(
                text.Trim(),
                @"\s+",
                " ");

        const int maxLength =
            72;

        return compact.Length <=
                maxLength
            ? compact
            : compact[..maxLength].TrimEnd() +
              "…";
    }

    /// <summary>
    /// Normalizes decision text for deterministic duplicate detection.
    /// </summary>
    /// <param name="value">Decision text.</param>
    /// <returns>The normalized comparison value.</returns>
    private static string NormalizeDecisionText(
            string value)
    {
        return Regex.Replace(
                value.Trim(),
                @"\s+",
                " ")
            .TrimEnd(
                '.',
                ';',
                ':')
            .ToUpperInvariant();
    }

    /// <summary>
    /// Creates a stable candidate identifier from source coordinates and normalized content.
    /// </summary>
    /// <param name="relativePath">Workspace-relative source path.</param>
    /// <param name="lineNumber">One-based source line.</param>
    /// <param name="normalizedText">Normalized decision text.</param>
    /// <returns>A stable candidate identifier.</returns>
    private static Guid CreateCandidateId(
            string relativePath,
            int lineNumber,
            string normalizedText)
    {
        byte[] bytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    relativePath +
                    "|" +
                    lineNumber.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "|" +
                    normalizedText));

        Span<byte> guidBytes =
            stackalloc byte[16];

        bytes.AsSpan(
                0,
                16)
            .CopyTo(
                guidBytes);

        return new Guid(
            guidBytes);
    }

    /// <summary>
    /// Normalizes a workspace-relative path.
    /// </summary>
    /// <param name="value">Relative path.</param>
    /// <returns>A forward-slash path.</returns>
    private static string NormalizeRelativePath(
            string value)
    {
        return value.Replace(
            '\\',
            '/');
    }

    /// <summary>
    /// Gets the Markdown heading parser.
    /// </summary>
    /// <returns>The compiled heading expression.</returns>
    [GeneratedRegex(
        @"^\s{0,3}#{1,6}\s+(?<title>.+?)\s*#*\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    /// <summary>
    /// Gets the Markdown list-entry parser.
    /// </summary>
    /// <returns>The compiled list expression.</returns>
    [GeneratedRegex(
        @"^\s*(?:[-*+]\s+|\d+[.)]\s+)(?<body>.+?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex BulletPattern();

    /// <summary>
    /// Gets the meeting date metadata parser.
    /// </summary>
    /// <returns>The compiled date metadata expression.</returns>
    [GeneratedRegex(
        @"^\s*\*\*Date\s*:\*\*\s*(?<date>\d{4}-\d{2}-\d{2})\s*$",
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase |
        RegexOptions.Multiline)]
    private static partial Regex DateMetadataPattern();

    /// <summary>
    /// Gets the meeting filename date parser.
    /// </summary>
    /// <returns>The compiled filename date expression.</returns>
    [GeneratedRegex(
        @"^(?<date>\d{4}-\d{2}-\d{2})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex FileDatePattern();
}
