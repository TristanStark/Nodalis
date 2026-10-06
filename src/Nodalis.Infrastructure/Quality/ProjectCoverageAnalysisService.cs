using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nodalis.Core.Domain;
using Nodalis.Core.Quality;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Quality;

/// <summary>
/// Cross-checks project documentation sources to expose deterministic coverage and coherence gaps.
/// </summary>
public sealed partial class ProjectCoverageAnalysisService
{
    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a new project coverage analysis service.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public ProjectCoverageAnalysisService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
    }

    /// <summary>
    /// Analyzes one project without modifying project content.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The deterministic coverage report.</returns>
    public async Task<ProjectCoverageReport> AnalyzeAsync(
            string projectDirectory,
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

        ProjectManifest project =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                manifestPath,
                cancellationToken);

        List<DocumentSnapshot> documents =
            await LoadDocumentsAsync(
                fullProjectDirectory,
                cancellationToken);

        List<ProjectCoverageFinding> findings =
            new List<ProjectCoverageFinding>();

        CheckExpectedSections(
            project,
            fullProjectDirectory,
            documents,
            findings);

        CheckFunctionalCoverage(
            project,
            documents,
            findings);

        CheckDecisions(
            documents,
            findings);

        List<MilestoneSnapshot> milestones =
            ExtractMilestones(
                project,
                documents);

        List<TaskSnapshot> tasks =
            ExtractTasks(
                documents);

        CheckMilestoneTaskCoverage(
            milestones,
            tasks,
            findings);

        CheckBlockingTaskDates(
            milestones,
            tasks,
            findings);

        CheckGlossaryCoverage(
            project,
            documents,
            findings);

        IReadOnlyList<ProjectCoverageFinding> ordered =
            findings
                .OrderBy(finding =>
                    finding.Severity switch
                    {
                        ProjectCoverageSeverity.Error => 0,
                        ProjectCoverageSeverity.Warning => 1,
                        _ => 2
                    })
                .ThenBy(
                    finding => finding.Code,
                    StringComparer.Ordinal)
                .ThenBy(
                    finding => finding.Sources.FirstOrDefault()?.RelativePath ?? string.Empty,
                    StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(
                    finding => finding.Sources.FirstOrDefault()?.LineNumber ?? 0)
                .ToArray();

        return new ProjectCoverageReport
        {
            ProjectId =
                project.Id,
            ProjectName =
                project.Name,
            Findings =
                ordered
        };
    }

    /// <summary>
    /// Loads project Markdown documents while excluding nested projects and attachment storage.
    /// </summary>
    /// <param name="projectDirectory">The project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable document snapshots.</returns>
    private async Task<List<DocumentSnapshot>> LoadDocumentsAsync(
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

            string[] segments =
                projectRelativePath.Split(
                    '/',
                    StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length > 1 &&
                (string.Equals(
                     segments[0],
                     WorkspaceLayout.SubProjectsDirectoryName,
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     segments[0],
                     WorkspaceLayout.AttachmentsDirectoryName,
                     StringComparison.OrdinalIgnoreCase) ||
                 segments[0].StartsWith(
                     ".",
                     StringComparison.Ordinal)))
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
    /// Reports expected project sections that contain no meaningful documentation.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="documents">The project documents.</param>
    /// <param name="findings">The destination findings.</param>
    private void CheckExpectedSections(
            ProjectManifest project,
            string projectDirectory,
            IReadOnlyList<DocumentSnapshot> documents,
            ICollection<ProjectCoverageFinding> findings)
    {
        foreach (SectionManifest section in project.Sections)
        {
            string safeName =
                WindowsPathRules.SanitizeSegment(
                    section.Name);

            List<DocumentSnapshot> sectionDocuments =
                documents
                    .Where(document =>
                        IsInTopLevelDirectory(
                            document.ProjectRelativePath,
                            safeName))
                    .ToList();

            int meaningfulCharacters =
                sectionDocuments.Sum(document =>
                    CountMeaningfulCharacters(
                        document.Content));

            if (sectionDocuments.Count > 0 &&
                meaningfulCharacters >=
                    40)
            {
                continue;
            }

            string source =
                NormalizeRelativePath(
                    Path.GetRelativePath(
                        _workspaceRoot,
                        Path.Combine(
                            projectDirectory,
                            WorkspaceLayout.ProjectManifestFileName)));

            findings.Add(
                new ProjectCoverageFinding
                {
                    Code =
                        "SECTION_EXPECTED_EMPTY",
                    Severity =
                        ProjectCoverageSeverity.Warning,
                    Message =
                        "La section attendue « " +
                        section.Name +
                        " » est vide ou ne contient presque aucun contenu exploitable.",
                    RuleExplanation =
                        "Une section déclarée dans project.json doit contenir au moins 40 caractères de contenu hors titres et métadonnées.",
                    Sources =
                    [
                        new ProjectCoverageSource
                        {
                            RelativePath =
                                source
                        }
                    ]
                });
        }
    }

    /// <summary>
    /// Reports functional headings that have no textual counterpart in the Tests section.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="documents">The project documents.</param>
    /// <param name="findings">The destination findings.</param>
    private static void CheckFunctionalCoverage(
            ProjectManifest project,
            IReadOnlyList<DocumentSnapshot> documents,
            ICollection<ProjectCoverageFinding> findings)
    {
        SectionManifest[] functionalSections =
            project.Sections
                .Where(section =>
                    (section.TemplateKey?.StartsWith(
                        "functional",
                        StringComparison.OrdinalIgnoreCase) ?? false) ||
                    section.Name.Contains(
                        "fonction",
                        StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        SectionManifest[] testSections =
            project.Sections
                .Where(section =>
                    (section.TemplateKey?.StartsWith(
                        "tests",
                        StringComparison.OrdinalIgnoreCase) ?? false) ||
                    section.Name.Contains(
                        "test",
                        StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        if (functionalSections.Length == 0 ||
            testSections.Length == 0)
        {
            return;
        }

        List<DocumentSnapshot> testDocuments =
            documents
                .Where(document =>
                    testSections.Any(section =>
                        IsInTopLevelDirectory(
                            document.ProjectRelativePath,
                            WindowsPathRules.SanitizeSegment(
                                section.Name))))
                .ToList();

        string testCorpus =
            NormalizeComparable(
                string.Join(
                    "\n",
                    testDocuments.Select(document =>
                        document.Content)));

        foreach (DocumentSnapshot document in documents.Where(document =>
                     functionalSections.Any(section =>
                         IsInTopLevelDirectory(
                             document.ProjectRelativePath,
                             WindowsPathRules.SanitizeSegment(
                                 section.Name)))))
        {
            for (int index = 0;
                 index < document.Lines.Length;
                 index++)
            {
                Match heading =
                    HeadingPattern().Match(
                        document.Lines[index]);

                if (!heading.Success)
                {
                    continue;
                }

                string title =
                    heading.Groups["title"].Value.Trim();

                if (title.Length <
                        4 ||
                    IsGenericHeading(
                        title))
                {
                    continue;
                }

                string normalized =
                    NormalizeComparable(
                        title);

                if (normalized.Length <
                        4 ||
                    testCorpus.Contains(
                        normalized,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                findings.Add(
                    new ProjectCoverageFinding
                    {
                        Code =
                            "FUNCTION_WITHOUT_TEST_REFERENCE",
                        Severity =
                            ProjectCoverageSeverity.Warning,
                        Message =
                            "La fonctionnalité « " +
                            title +
                            " » n'est référencée dans aucun document de tests.",
                        RuleExplanation =
                            "Chaque titre H2 à H4 d'une section fonctionnelle doit être mentionné textuellement dans la section Tests.",
                        Sources =
                            BuildSources(
                                document.RelativePath,
                                index + 1,
                                testDocuments.FirstOrDefault()?.RelativePath,
                                null)
                    });
            }
        }
    }

    /// <summary>
    /// Reports Decision Records that do not describe impacts or affected documents.
    /// </summary>
    /// <param name="documents">The project documents.</param>
    /// <param name="findings">The destination findings.</param>
    private static void CheckDecisions(
            IReadOnlyList<DocumentSnapshot> documents,
            ICollection<ProjectCoverageFinding> findings)
    {
        foreach (DocumentSnapshot document in documents.Where(document =>
                     document.ProjectRelativePath
                         .Split(
                             '/',
                             StringSplitOptions.RemoveEmptyEntries)
                         .Any(segment =>
                             string.Equals(
                                 segment,
                                 "Décisions",
                                 StringComparison.CurrentCultureIgnoreCase) ||
                             string.Equals(
                                 segment,
                                 "Decisions",
                                 StringComparison.CurrentCultureIgnoreCase))))
        {
            string impacts =
                ExtractSectionBody(
                    document.Lines,
                    "Impacts");

            string links =
                ExtractSectionBody(
                    document.Lines,
                    "Sources et liens");

            if (!string.IsNullOrWhiteSpace(
                    StripPlaceholder(
                        impacts)) ||
                !string.IsNullOrWhiteSpace(
                    StripPlaceholder(
                        links)))
            {
                continue;
            }

            findings.Add(
                new ProjectCoverageFinding
                {
                    Code =
                        "DECISION_WITHOUT_IMPACT",
                    Severity =
                        ProjectCoverageSeverity.Warning,
                    Message =
                        "Cette décision ne documente ni impact ni document affecté.",
                    RuleExplanation =
                        "Un Decision Record doit renseigner la section Impacts ou Sources et liens afin de rendre ses conséquences traçables.",
                    Sources =
                    [
                        new ProjectCoverageSource
                        {
                            RelativePath =
                                document.RelativePath
                        }
                    ]
                });
        }
    }

    /// <summary>
    /// Reports incomplete milestones without any apparent related open task.
    /// </summary>
    /// <param name="milestones">Parsed milestones.</param>
    /// <param name="tasks">Parsed project tasks.</param>
    /// <param name="findings">The destination findings.</param>
    private static void CheckMilestoneTaskCoverage(
            IReadOnlyList<MilestoneSnapshot> milestones,
            IReadOnlyList<TaskSnapshot> tasks,
            ICollection<ProjectCoverageFinding> findings)
    {
        foreach (MilestoneSnapshot milestone in milestones.Where(milestone =>
                     !IsCompletedStatus(
                         milestone.Status)))
        {
            string normalizedName =
                NormalizeComparable(
                    milestone.Name);

            bool hasRelatedTask =
                tasks.Any(task =>
                    !task.Completed &&
                    (NormalizeComparable(
                         task.Text)
                     .Contains(
                         normalizedName,
                         StringComparison.Ordinal) ||
                     normalizedName.Contains(
                         NormalizeComparable(
                             task.Text),
                         StringComparison.Ordinal)));

            if (hasRelatedTask)
            {
                continue;
            }

            findings.Add(
                new ProjectCoverageFinding
                {
                    Code =
                        "MILESTONE_WITHOUT_TASK",
                    Severity =
                        ProjectCoverageSeverity.Warning,
                    Message =
                        "Le jalon « " +
                        milestone.Name +
                        " » n'a aucune tâche ouverte explicitement associée.",
                    RuleExplanation =
                        "Un jalon non terminé doit partager son libellé, ou un libellé englobant, avec au moins une tâche Markdown ouverte.",
                    Sources =
                    [
                        new ProjectCoverageSource
                        {
                            RelativePath =
                                milestone.RelativePath,
                            LineNumber =
                                milestone.LineNumber
                        }
                    ]
                });
        }
    }

    /// <summary>
    /// Reports blocking tasks whose due date is later than the earliest unfinished milestone.
    /// </summary>
    /// <param name="milestones">Parsed milestones.</param>
    /// <param name="tasks">Parsed project tasks.</param>
    /// <param name="findings">The destination findings.</param>
    private static void CheckBlockingTaskDates(
            IReadOnlyList<MilestoneSnapshot> milestones,
            IReadOnlyList<TaskSnapshot> tasks,
            ICollection<ProjectCoverageFinding> findings)
    {
        MilestoneSnapshot? nextMilestone =
            milestones
                .Where(milestone =>
                    !IsCompletedStatus(
                        milestone.Status) &&
                    milestone.TargetDate is not null)
                .OrderBy(milestone =>
                    milestone.TargetDate)
                .FirstOrDefault();

        if (nextMilestone?.TargetDate is not DateOnly milestoneDate)
        {
            return;
        }

        foreach (TaskSnapshot task in tasks.Where(task =>
                     !task.Completed &&
                     task.DueDate is not null &&
                     IsBlockingTask(
                         task)))
        {
            if (task.DueDate is not DateOnly dueDate ||
                dueDate <=
                    milestoneDate)
            {
                continue;
            }

            findings.Add(
                new ProjectCoverageFinding
                {
                    Code =
                        "BLOCKING_TASK_AFTER_MILESTONE",
                    Severity =
                        ProjectCoverageSeverity.Error,
                    Message =
                        "La tâche bloquante « " +
                        task.Text +
                        " » est prévue le " +
                        dueDate.ToString(
                            "dd/MM/yyyy",
                            CultureInfo.InvariantCulture) +
                        ", après le jalon « " +
                        nextMilestone.Name +
                        " » du " +
                        milestoneDate.ToString(
                            "dd/MM/yyyy",
                            CultureInfo.InvariantCulture) +
                        ".",
                    RuleExplanation =
                        "Une tâche marquée Priorité: Bloquante/Blocking/Blocker ou #blocking doit être échue au plus tard à la date du prochain jalon non terminé.",
                    Sources =
                    [
                        new ProjectCoverageSource
                        {
                            RelativePath =
                                task.RelativePath,
                            LineNumber =
                                task.LineNumber
                        },
                        new ProjectCoverageSource
                        {
                            RelativePath =
                                nextMilestone.RelativePath,
                            LineNumber =
                                nextMilestone.LineNumber
                        }
                    ]
                });
        }
    }

    /// <summary>
    /// Reports frequently used uppercase business acronyms absent from project glossary documents.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="documents">The project documents.</param>
    /// <param name="findings">The destination findings.</param>
    private static void CheckGlossaryCoverage(
            ProjectManifest project,
            IReadOnlyList<DocumentSnapshot> documents,
            ICollection<ProjectCoverageFinding> findings)
    {
        SectionManifest[] glossarySections =
            project.Sections
                .Where(section =>
                    (section.TemplateKey?.StartsWith(
                        "glossary",
                        StringComparison.OrdinalIgnoreCase) ?? false) ||
                    section.Name.Contains(
                        "gloss",
                        StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        List<DocumentSnapshot> glossaryDocuments =
            documents
                .Where(document =>
                    glossarySections.Any(section =>
                        IsInTopLevelDirectory(
                            document.ProjectRelativePath,
                            WindowsPathRules.SanitizeSegment(
                                section.Name))) ||
                    document.ProjectRelativePath.Contains(
                        "Glossaire",
                        StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        HashSet<string> glossaryTokens =
            UppercaseTermPattern()
                .Matches(
                    string.Join(
                        "\n",
                        glossaryDocuments.Select(document =>
                            document.Content)))
                .Select(match =>
                    match.Value.ToUpperInvariant())
                .ToHashSet(
                    StringComparer.Ordinal);

        Dictionary<string, List<ProjectCoverageSource>> occurrences =
            new Dictionary<string, List<ProjectCoverageSource>>(
                StringComparer.Ordinal);

        foreach (DocumentSnapshot document in documents.Where(document =>
                     !glossaryDocuments.Contains(
                         document)))
        {
            for (int index = 0;
                 index < document.Lines.Length;
                 index++)
            {
                foreach (Match match in UppercaseTermPattern().Matches(
                             document.Lines[index]))
                {
                    string term =
                        match.Value.ToUpperInvariant();

                    if (IgnoredUppercaseTerms.Contains(
                            term) ||
                        glossaryTokens.Contains(
                            term))
                    {
                        continue;
                    }

                    if (!occurrences.TryGetValue(
                            term,
                            out List<ProjectCoverageSource>? sources))
                    {
                        sources =
                            new List<ProjectCoverageSource>();
                        occurrences[term] =
                            sources;
                    }

                    sources.Add(
                        new ProjectCoverageSource
                        {
                            RelativePath =
                                document.RelativePath,
                            LineNumber =
                                index + 1
                        });
                }
            }
        }

        foreach (KeyValuePair<string, List<ProjectCoverageSource>> pair in occurrences
                     .Where(pair =>
                         pair.Value.Count >=
                         3)
                     .OrderByDescending(pair =>
                         pair.Value.Count)
                     .ThenBy(pair =>
                         pair.Key,
                         StringComparer.Ordinal))
        {
            findings.Add(
                new ProjectCoverageFinding
                {
                    Code =
                        "FREQUENT_TERM_MISSING_GLOSSARY",
                    Severity =
                        ProjectCoverageSeverity.Information,
                    Message =
                        "Le terme « " +
                        pair.Key +
                        " » apparaît " +
                        pair.Value.Count +
                        " fois mais n'est pas présent dans le glossaire du projet.",
                    RuleExplanation =
                        "Un acronyme en majuscules de 3 à 12 caractères utilisé au moins trois fois doit être défini dans le glossaire projet.",
                    Sources =
                        pair.Value
                            .Take(
                                3)
                            .ToArray()
                });
        }
    }

    /// <summary>
    /// Extracts Markdown checkbox tasks and their readable metadata.
    /// </summary>
    /// <param name="documents">The project documents.</param>
    /// <returns>The parsed tasks.</returns>
    private static List<TaskSnapshot> ExtractTasks(
            IReadOnlyList<DocumentSnapshot> documents)
    {
        List<TaskSnapshot> tasks =
            new List<TaskSnapshot>();

        foreach (DocumentSnapshot document in documents)
        {
            for (int index = 0;
                 index < document.Lines.Length;
                 index++)
            {
                Match match =
                    CheckboxPattern().Match(
                        document.Lines[index]);

                if (!match.Success)
                {
                    continue;
                }

                string body =
                    match.Groups["body"].Value.Trim();
                string[] segments =
                    body.Split(
                        '|',
                        StringSplitOptions.TrimEntries);

                string text =
                    segments.Length > 0
                        ? segments[0].Trim()
                        : body;

                string priority =
                    ReadMetadata(
                        segments,
                        "Priorité",
                        "Priorite",
                        "Priority");
                string tags =
                    ReadMetadata(
                        segments,
                        "Tags",
                        "Tag");
                string dueText =
                    ReadMetadata(
                        segments,
                        "Échéance",
                        "Echeance",
                        "Due");

                DateOnly? dueDate =
                    DateOnly.TryParseExact(
                        dueText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out DateOnly parsedDate)
                        ? parsedDate
                        : null;

                tasks.Add(
                    new TaskSnapshot(
                        document.RelativePath,
                        index + 1,
                        text,
                        !string.IsNullOrWhiteSpace(
                            match.Groups["checked"].Value),
                        priority,
                        tags,
                        dueDate));
            }
        }

        return tasks;
    }

    /// <summary>
    /// Extracts milestone table rows from configured milestone sections.
    /// </summary>
    /// <param name="project">The project manifest.</param>
    /// <param name="documents">The project documents.</param>
    /// <returns>The parsed milestone rows.</returns>
    private static List<MilestoneSnapshot> ExtractMilestones(
            ProjectManifest project,
            IReadOnlyList<DocumentSnapshot> documents)
    {
        SectionManifest[] milestoneSections =
            project.Sections
                .Where(section =>
                    (section.TemplateKey?.StartsWith(
                        "milestone",
                        StringComparison.OrdinalIgnoreCase) ?? false) ||
                    section.Name.Contains(
                        "jalon",
                        StringComparison.CurrentCultureIgnoreCase))
                .ToArray();

        List<MilestoneSnapshot> milestones =
            new List<MilestoneSnapshot>();

        foreach (DocumentSnapshot document in documents.Where(document =>
                     milestoneSections.Any(section =>
                         IsInTopLevelDirectory(
                             document.ProjectRelativePath,
                             WindowsPathRules.SanitizeSegment(
                                 section.Name)))))
        {
            int nameColumn =
                -1;
            int dateColumn =
                -1;
            int statusColumn =
                -1;

            for (int index = 0;
                 index < document.Lines.Length;
                 index++)
            {
                string line =
                    document.Lines[index];

                if (!line.TrimStart().StartsWith(
                        "|",
                        StringComparison.Ordinal))
                {
                    nameColumn =
                        -1;
                    dateColumn =
                        -1;
                    statusColumn =
                        -1;
                    continue;
                }

                string[] cells =
                    SplitTableCells(
                        line);

                if (nameColumn <
                    0)
                {
                    nameColumn =
                        FindColumn(
                            cells,
                            "jalon",
                            "nom");
                    dateColumn =
                        FindColumn(
                            cells,
                            "date cible",
                            "date");
                    statusColumn =
                        FindColumn(
                            cells,
                            "statut",
                            "status");
                    continue;
                }

                if (IsSeparatorRow(
                        cells) ||
                    nameColumn >=
                        cells.Length)
                {
                    continue;
                }

                string name =
                    cells[nameColumn].Trim();

                if (string.IsNullOrWhiteSpace(
                        name))
                {
                    continue;
                }

                DateOnly? targetDate =
                    dateColumn >=
                            0 &&
                        dateColumn <
                            cells.Length &&
                        DateOnly.TryParseExact(
                            cells[dateColumn].Trim(),
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out DateOnly parsedDate)
                        ? parsedDate
                        : null;

                string status =
                    statusColumn >=
                            0 &&
                        statusColumn <
                            cells.Length
                        ? cells[statusColumn].Trim()
                        : string.Empty;

                milestones.Add(
                    new MilestoneSnapshot(
                        document.RelativePath,
                        index + 1,
                        name,
                        targetDate,
                        status));
            }
        }

        return milestones;
    }

    /// <summary>
    /// Reads one metadata value from task segments.
    /// </summary>
    /// <param name="segments">Task body segments.</param>
    /// <param name="keys">Accepted metadata keys.</param>
    /// <returns>The metadata value or an empty string.</returns>
    private static string ReadMetadata(
            IReadOnlyList<string> segments,
            params string[] keys)
    {
        foreach (string segment in segments.Skip(
                     1))
        {
            int separator =
                segment.IndexOf(
                    ':');

            if (separator <=
                0)
            {
                continue;
            }

            string key =
                segment[..separator].Trim();

            if (!keys.Any(candidate =>
                    string.Equals(
                        candidate,
                        key,
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            return segment[(separator + 1)..].Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// Determines whether one task is explicitly marked as blocking.
    /// </summary>
    /// <param name="task">The parsed task.</param>
    /// <returns>Whether it is blocking.</returns>
    private static bool IsBlockingTask(
            TaskSnapshot task)
    {
        return string.Equals(
                   task.Priority,
                   "Bloquante",
                   StringComparison.CurrentCultureIgnoreCase) ||
               string.Equals(
                   task.Priority,
                   "Blocking",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   task.Priority,
                   "Blocker",
                   StringComparison.OrdinalIgnoreCase) ||
               task.Tags.Contains(
                   "#blocking",
                   StringComparison.OrdinalIgnoreCase) ||
               task.Tags.Contains(
                   "bloquant",
                   StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Determines whether a milestone status denotes completion.
    /// </summary>
    /// <param name="status">Milestone status.</param>
    /// <returns>Whether the milestone is completed.</returns>
    private static bool IsCompletedStatus(
            string status)
    {
        return string.Equals(
                   status,
                   "Terminé",
                   StringComparison.CurrentCultureIgnoreCase) ||
               string.Equals(
                   status,
                   "Termine",
                   StringComparison.CurrentCultureIgnoreCase) ||
               string.Equals(
                   status,
                   "Done",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   status,
                   "Completed",
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the body of one Markdown heading section.
    /// </summary>
    /// <param name="lines">Document lines.</param>
    /// <param name="headingName">Heading name to locate.</param>
    /// <returns>The section body.</returns>
    private static string ExtractSectionBody(
            IReadOnlyList<string> lines,
            string headingName)
    {
        int headingLevel =
            0;
        bool collecting =
            false;
        List<string> body =
            new List<string>();

        foreach (string line in lines)
        {
            Match heading =
                HeadingPattern().Match(
                    line);

            if (heading.Success)
            {
                int level =
                    heading.Groups["marks"].Value.Length;
                string title =
                    heading.Groups["title"].Value.Trim();

                if (collecting &&
                    level <=
                        headingLevel)
                {
                    break;
                }

                if (string.Equals(
                        title,
                        headingName,
                        StringComparison.CurrentCultureIgnoreCase))
                {
                    collecting =
                        true;
                    headingLevel =
                        level;
                }

                continue;
            }

            if (collecting)
            {
                body.Add(
                    line);
            }
        }

        return string.Join(
            "\n",
            body)
            .Trim();
    }

    /// <summary>
    /// Removes common empty template placeholders from a section body.
    /// </summary>
    /// <param name="value">Section body.</param>
    /// <returns>The meaningful remainder.</returns>
    private static string StripPlaceholder(
            string value)
    {
        return value
            .Replace(
                "_À compléter._",
                string.Empty,
                StringComparison.CurrentCultureIgnoreCase)
            .Replace(
                "_A compléter._",
                string.Empty,
                StringComparison.CurrentCultureIgnoreCase)
            .Replace(
                "À compléter",
                string.Empty,
                StringComparison.CurrentCultureIgnoreCase)
            .Replace(
                "A compléter",
                string.Empty,
                StringComparison.CurrentCultureIgnoreCase)
            .Replace(
                "-",
                string.Empty,
                StringComparison.Ordinal)
            .Trim();
    }

    /// <summary>
    /// Counts meaningful non-heading, non-metadata characters.
    /// </summary>
    /// <param name="content">Markdown content.</param>
    /// <returns>The meaningful character count.</returns>
    private static int CountMeaningfulCharacters(
            string content)
    {
        return NormalizeNewlines(
                content)
            .Split(
                '\n')
            .Where(line =>
                !string.IsNullOrWhiteSpace(
                    line) &&
                !line.TrimStart().StartsWith(
                    "#",
                    StringComparison.Ordinal) &&
                !line.TrimStart().StartsWith(
                    "**",
                    StringComparison.Ordinal))
            .Sum(line =>
                line.Trim().Length);
    }

    /// <summary>
    /// Builds one or two source references.
    /// </summary>
    /// <param name="firstPath">Primary source path.</param>
    /// <param name="firstLine">Primary source line.</param>
    /// <param name="secondPath">Optional secondary path.</param>
    /// <param name="secondLine">Optional secondary line.</param>
    /// <returns>The source references.</returns>
    private static IReadOnlyList<ProjectCoverageSource> BuildSources(
            string firstPath,
            int? firstLine,
            string? secondPath,
            int? secondLine)
    {
        List<ProjectCoverageSource> sources =
            new List<ProjectCoverageSource>
            {
                new ProjectCoverageSource
                {
                    RelativePath =
                        firstPath,
                    LineNumber =
                        firstLine
                }
            };

        if (!string.IsNullOrWhiteSpace(
                secondPath))
        {
            sources.Add(
                new ProjectCoverageSource
                {
                    RelativePath =
                        secondPath,
                    LineNumber =
                        secondLine
                });
        }

        return sources;
    }

    /// <summary>
    /// Finds one table column by accepted normalized labels.
    /// </summary>
    /// <param name="cells">Header cells.</param>
    /// <param name="labels">Accepted labels.</param>
    /// <returns>The zero-based column, or -1.</returns>
    private static int FindColumn(
            IReadOnlyList<string> cells,
            params string[] labels)
    {
        for (int index = 0;
             index < cells.Count;
             index++)
        {
            string normalized =
                NormalizeComparable(
                    cells[index]);

            if (labels.Any(label =>
                    string.Equals(
                        NormalizeComparable(
                            label),
                        normalized,
                        StringComparison.Ordinal)))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Splits one Markdown table row.
    /// </summary>
    /// <param name="line">Markdown row.</param>
    /// <returns>The table cells.</returns>
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
    /// Detects a Markdown table separator row.
    /// </summary>
    /// <param name="cells">The table cells.</param>
    /// <returns>Whether every cell is a separator cell.</returns>
    private static bool IsSeparatorRow(
            IReadOnlyList<string> cells)
    {
        return cells.Count >
                0 &&
            cells.All(cell =>
                Regex.IsMatch(
                    cell.Trim(),
                    "^:?-{3,}:?$",
                    RegexOptions.CultureInvariant));
    }

    /// <summary>
    /// Determines whether a project-relative document belongs to a top-level section directory.
    /// </summary>
    /// <param name="relativePath">Project-relative path.</param>
    /// <param name="directoryName">Section directory name.</param>
    /// <returns>Whether the path is in that section.</returns>
    private static bool IsInTopLevelDirectory(
            string relativePath,
            string directoryName)
    {
        string normalized =
            NormalizeRelativePath(
                relativePath);

        return normalized.StartsWith(
            directoryName + "/",
            StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Determines whether a heading is too generic to represent a testable feature.
    /// </summary>
    /// <param name="title">Heading title.</param>
    /// <returns>Whether the title is generic.</returns>
    private static bool IsGenericHeading(
            string title)
    {
        string normalized =
            NormalizeComparable(
                title);

        return GenericHeadings.Contains(
            normalized);
    }

    /// <summary>
    /// Normalizes text for deterministic loose matching.
    /// </summary>
    /// <param name="value">Text to normalize.</param>
    /// <returns>The comparison representation.</returns>
    private static string NormalizeComparable(
            string value)
    {
        string decomposed =
            value.Normalize(
                System.Text.NormalizationForm.FormD);

        string withoutMarks =
            new string(
                decomposed
                    .Where(character =>
                        CharUnicodeInfo.GetUnicodeCategory(
                            character) !=
                        UnicodeCategory.NonSpacingMark)
                    .ToArray());

        return Regex.Replace(
                withoutMarks.ToLowerInvariant(),
                @"[^a-z0-9]+",
                " ")
            .Trim();
    }

    /// <summary>
    /// Normalizes newline conventions.
    /// </summary>
    /// <param name="value">Text to normalize.</param>
    /// <returns>LF-only text.</returns>
    private static string NormalizeNewlines(
            string value)
    {
        return value
            .Replace(
                "\r\n",
                "\n",
                StringComparison.Ordinal)
            .Replace(
                '\r',
                '\n');
    }

    /// <summary>
    /// Normalizes a relative path.
    /// </summary>
    /// <param name="value">Path to normalize.</param>
    /// <returns>A forward-slash path.</returns>
    private static string NormalizeRelativePath(
            string value)
    {
        return value.Replace(
            '\\',
            '/');
    }

    /// <summary>
    /// Verifies that one path belongs to this workspace.
    /// </summary>
    /// <param name="path">Absolute path.</param>
    /// <returns>Whether the path is inside the workspace.</returns>
    private bool IsWithinWorkspace(
            string path)
    {
        string relative =
            Path.GetRelativePath(
                _workspaceRoot,
                path);

        return !string.Equals(
                   relative,
                   "..",
                   StringComparison.Ordinal) &&
               !relative.StartsWith(
                   ".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal);
    }

    /// <summary>Gets the Markdown checkbox parser.</summary>
    /// <returns>The compiled checkbox expression.</returns>
    [GeneratedRegex(
        @"^\s*[-*+]\s+\[(?<checked>[ xX])\]\s+(?<body>.+?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CheckboxPattern();

    /// <summary>Gets the Markdown heading parser.</summary>
    /// <returns>The compiled heading expression.</returns>
    [GeneratedRegex(
        @"^\s{0,3}(?<marks>#{1,6})\s+(?<title>.+?)\s*#*\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HeadingPattern();

    /// <summary>Gets the uppercase business-term parser.</summary>
    /// <returns>The compiled term expression.</returns>
    [GeneratedRegex(
        @"\b[A-ZÀ-ÖØ-Þ][A-ZÀ-ÖØ-Þ0-9_-]{2,11}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex UppercaseTermPattern();

    private static readonly HashSet<string> GenericHeadings =
        new HashSet<string>(
            new[]
            {
                "contexte",
                "description",
                "objectif",
                "objectifs",
                "notes",
                "introduction",
                "general",
                "generale",
                "details",
                "exemples"
            },
            StringComparer.Ordinal);

    private static readonly HashSet<string> IgnoredUppercaseTerms =
        new HashSet<string>(
            new[]
            {
                "API",
                "HTTP",
                "HTTPS",
                "JSON",
                "YAML",
                "XML",
                "HTML",
                "CSS",
                "SQL",
                "URL",
                "URI",
                "UTF",
                "TODO",
                "WIP"
            },
            StringComparer.Ordinal);

    private sealed record DocumentSnapshot(
        string FullPath,
        string RelativePath,
        string ProjectRelativePath,
        string Content,
        string[] Lines);

    private sealed record TaskSnapshot(
        string RelativePath,
        int LineNumber,
        string Text,
        bool Completed,
        string Priority,
        string Tags,
        DateOnly? DueDate);

    private sealed record MilestoneSnapshot(
        string RelativePath,
        int LineNumber,
        string Name,
        DateOnly? TargetDate,
        string Status);
}
