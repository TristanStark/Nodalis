using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Core.Links;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Importing;

public sealed class WorkspaceDocxImportService
{
    private readonly string _workspaceRoot;
    private readonly DocxParser _parser = new();

    public WorkspaceDocxImportService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<DocxImportResult> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var staged = await StageAsync(
            sourcePath,
            cancellationToken);

        try
        {
            var sourceCopyPath = await CommitStagedCopyAsync(
                staged,
                cancellationToken);

            return new DocxImportResult
            {
                SourceCopyPath = sourceCopyPath,
                Document = staged.Document
            };
        }
        catch
        {
            DiscardStagedCopy(
                staged);
            throw;
        }
    }

    public async Task<DocxImportPreview> PreparePreviewAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var staged = await StageAsync(
            sourcePath,
            cancellationToken);

        try
        {
            var analyzer = new DocxImportAnalyzer(
                _workspaceRoot);

            var analysis = await analyzer.AnalyzeAsync(
                staged.Document,
                Path.GetFileName(sourcePath),
                cancellationToken);

            var indexService = new WorkspaceLinkIndexService(
                _workspaceRoot);

            var index = await indexService.RefreshAsync(
                cancellationToken);

            var applications = index.Targets
                .Where(target =>
                    target.Kind == LinkTargetKind.Application)
                .Select(target =>
                    new DocxImportTargetOption
                    {
                        Id = target.Id,
                        DisplayName = target.DisplayName,
                        QualifiedName = target.QualifiedName,
                        RelativePath = target.RelativePath,
                        ApplicationId = target.Id
                    })
                .OrderBy(target =>
                    target.QualifiedName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var projects = new List<DocxImportTargetOption>();

            foreach (var target in index.Targets
                         .Where(target =>
                             target.Kind == LinkTargetKind.Project)
                         .OrderBy(target =>
                             target.QualifiedName,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var projectDirectory = ResolveRelativePath(
                    target.RelativePath);
                var manifestPath = Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.ProjectManifestFileName);

                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                    manifestPath,
                    cancellationToken);

                projects.Add(
                    new DocxImportTargetOption
                    {
                        Id = target.Id,
                        DisplayName = target.DisplayName,
                        QualifiedName = target.QualifiedName,
                        RelativePath = target.RelativePath,
                        ApplicationId = manifest.ApplicationId
                    });
            }

            var sections = BuildSectionPreviews(
                analysis);

            var suggestedApplicationId =
                analysis.ApplicationCandidates.Count == 1
                    ? analysis.ApplicationCandidates[0].Id
                    : null;

            var suggestedProjectId =
                analysis.ProjectCandidates.Count == 1
                    ? analysis.ProjectCandidates[0].Id
                    : null;

            if (suggestedProjectId is Guid projectId)
            {
                suggestedApplicationId = projects
                    .FirstOrDefault(project =>
                        project.Id == projectId)
                    ?.ApplicationId ??
                    suggestedApplicationId;
            }

            if (suggestedApplicationId is null &&
                applications.Count == 1)
            {
                suggestedApplicationId =
                    applications[0].Id;
            }

            var suggestedNewProjectName =
                suggestedProjectId is null
                    ? analysis.ProposedProjectName ??
                      Path.GetFileNameWithoutExtension(
                          staged.OriginalSourcePath)
                    : null;

            return new DocxImportPreview
            {
                StagedImport = staged,
                Analysis = analysis,
                Applications = applications,
                Projects = projects,
                SuggestedApplicationId = suggestedApplicationId,
                SuggestedProjectId = suggestedProjectId,
                SuggestedNewProjectName = suggestedNewProjectName,
                Sections = sections,
                Conflicts = BuildPreviewWarnings(
                    applications,
                    analysis,
                    sections)
            };
        }
        catch
        {
            DiscardStagedCopy(
                staged);
            throw;
        }
    }

    public async Task<DocxImportPlan> BuildPlanAsync(
        DocxImportPreview preview,
        DocxImportCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(request);

        ValidateCommitRequest(
            preview,
            request);

        var selected = request.Sections
            .Where(section => section.Include)
            .Select(section =>
            {
                var previewSection = preview.Sections
                    .Single(candidate =>
                        candidate.Index == section.SectionIndex);

                return new SelectedSection(
                    previewSection,
                    section.TargetSection.Trim());
            })
            .ToArray();

        var changes = new List<DocxImportPlannedChange>();
        var warnings = preview.Conflicts.ToList();

        string projectDirectory;
        string projectDisplayName;
        ProjectManifest? existingProject = null;
        var createsProject = request.ProjectId is null;

        if (request.ProjectId is Guid existingProjectId)
        {
            var target = preview.Projects.Single(project =>
                project.Id == existingProjectId);

            if (target.ApplicationId != request.ApplicationId)
            {
                throw new InvalidDataException(
                    "Le projet sélectionné n'appartient pas à l'application choisie.");
            }

            projectDirectory = ResolveRelativePath(
                target.RelativePath);
            projectDisplayName = target.QualifiedName;

            existingProject = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.ProjectManifestFileName),
                cancellationToken);

            var requestedSections = selected
                .Select(section => section.TargetSection)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            var missingSections = requestedSections
                .Where(section =>
                    !existingProject.Sections.Any(existing =>
                        string.Equals(
                            existing.Name,
                            section,
                            StringComparison.CurrentCultureIgnoreCase)))
                .ToArray();

            if (missingSections.Length > 0)
            {
                changes.Add(
                    PlanChange(
                        "Modifier",
                        Path.Combine(
                            projectDirectory,
                            WorkspaceLayout.ProjectManifestFileName),
                        $"Ajouter {missingSections.Length} section(s) au projet : {string.Join(", ", missingSections)}"));
            }
        }
        else
        {
            var discovery = new ProjectCreationTargetDiscovery();
            var targets = await discovery.DiscoverAsync(
                _workspaceRoot,
                cancellationToken);

            var applicationTarget = targets
                .FirstOrDefault(target =>
                    target.ApplicationId == request.ApplicationId &&
                    target.ModuleId is null &&
                    target.ParentProjectId is null)
                ?? throw new InvalidDataException(
                    "L'application choisie n'est plus disponible.");

            var projectsDirectory = Path.Combine(
                applicationTarget.ParentDirectory,
                WorkspaceLayout.ProjectsDirectoryName);

            projectDirectory = WindowsPathRules.GetUniqueDirectoryPath(
                projectsDirectory,
                request.NewProjectName!.Trim());

            projectDisplayName =
                $"{applicationTarget.ApplicationName} / {Path.GetFileName(projectDirectory)}";

            changes.Add(
                PlanChange(
                    "Créer",
                    Path.Combine(
                        projectDirectory,
                        WorkspaceLayout.ProjectManifestFileName),
                    "Métadonnées du nouveau projet"));

            changes.Add(
                PlanChange(
                    "Créer",
                    Path.Combine(
                        projectDirectory,
                        "Présentation.md"),
                    "Présentation du nouveau projet"));

            changes.Add(
                PlanChange(
                    "Créer",
                    Path.Combine(
                        projectDirectory,
                        WorkspaceLayout.GlobalQuickNotesFileName),
                    "Notes rapides du projet"));

            var templateStore = new FileSystemTemplateStore(
                _workspaceRoot);

            var profileCatalog =
                await templateStore.LoadProjectProfilesAsync(
                    cancellationToken);

            var profile = profileCatalog.Profiles.SingleOrDefault(candidate =>
                    candidate.Complexity ==
                    request.NewProjectComplexity)
                ?? throw new InvalidDataException(
                    $"Aucun profil de projet '{request.NewProjectComplexity}' n'est disponible.");

            foreach (var section in profile.Sections
                         .Where(section =>
                             !string.IsNullOrWhiteSpace(
                                 section.TemplateKey))
                         .OrderBy(section =>
                             section.Order))
            {
                changes.Add(
                    PlanChange(
                        "Créer",
                        Path.Combine(
                            projectDirectory,
                            WindowsPathRules.SanitizeSegment(
                                section.Name),
                            WindowsPathRules.SanitizeSegment(
                                section.Name) + ".md"),
                        $"Document initial de la section {section.Name}"));
            }
        }

        var sourceStem =
            Path.GetFileNameWithoutExtension(
                preview.StagedImport.OriginalSourcePath);

        foreach (var group in selected
                     .GroupBy(
                         item => item.TargetSection,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            var sectionName = group.Key.Trim();
            var sectionDirectory = Path.Combine(
                projectDirectory,
                WindowsPathRules.SanitizeSegment(
                    sectionName));

            var desiredFileName =
                $"Import - {sourceStem}.md";

            var destination =
                WindowsPathRules.GetUniqueFilePath(
                    sectionDirectory,
                    desiredFileName);

            if (!string.Equals(
                    Path.GetFileName(destination),
                    desiredFileName,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                warnings.Add(
                    $"Un fichier d'import existe déjà dans la section « {sectionName} » ; " +
                    $"le nouveau fichier sera nommé « {Path.GetFileName(destination)} ».");
            }

            changes.Add(
                PlanChange(
                    "Créer",
                    destination,
                    $"{group.Count()} section(s) Word → section Nodalis « {sectionName} »"));
        }

        var sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        var sourceCopyPath =
            WindowsPathRules.GetUniqueFilePath(
                sourcesDirectory,
                Path.GetFileName(
                    preview.StagedImport.OriginalSourcePath));

        if (!string.Equals(
                Path.GetFileName(sourceCopyPath),
                Path.GetFileName(
                    preview.StagedImport.OriginalSourcePath),
                StringComparison.CurrentCultureIgnoreCase))
        {
            warnings.Add(
                $"Une copie source du même nom existe déjà ; la nouvelle copie sera nommée « {Path.GetFileName(sourceCopyPath)} ».");
        }

        changes.Add(
            PlanChange(
                "Copier",
                sourceCopyPath,
                "Copie locale du DOCX original ; le fichier source reste intact"));

        return new DocxImportPlan
        {
            TargetProjectDisplayName =
                projectDisplayName,
            TargetProjectRelativePath =
                ToWorkspaceRelativePath(
                    projectDirectory),
            CreatesProject =
                createsProject,
            Changes = changes,
            Warnings = warnings
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };
    }

    public async Task<DocxImportCommitResult> CommitAsync(
        DocxImportPreview preview,
        DocxImportCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(request);

        ValidateCommitRequest(
            preview,
            request);

        var selected = request.Sections
            .Where(section => section.Include)
            .Select(section =>
            {
                var previewSection = preview.Sections
                    .Single(candidate =>
                        candidate.Index == section.SectionIndex);

                return new SelectedSection(
                    previewSection,
                    section.TargetSection.Trim());
            })
            .ToArray();

        var projectDirectory = string.Empty;
        var projectId = Guid.Empty;
        var createdProject = false;
        string? manifestPath = null;
        string? originalManifest = null;
        var generatedFiles = new List<string>();
        var createdDirectories = new List<string>();

        try
        {
            ProjectManifest project;

            if (request.ProjectId is Guid existingProjectId)
            {
                var target = preview.Projects.Single(project =>
                    project.Id == existingProjectId);

                if (target.ApplicationId != request.ApplicationId)
                {
                    throw new InvalidDataException(
                        "Le projet sélectionné n'appartient pas à l'application choisie.");
                }

                projectDirectory = ResolveRelativePath(
                    target.RelativePath);

                manifestPath = Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.ProjectManifestFileName);

                originalManifest = await File.ReadAllTextAsync(
                    manifestPath,
                    cancellationToken);

                project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                    manifestPath,
                    cancellationToken);

                projectId = project.Id;
            }
            else
            {
                var discovery = new ProjectCreationTargetDiscovery();
                var targets = await discovery.DiscoverAsync(
                    _workspaceRoot,
                    cancellationToken);

                var applicationTarget = targets
                    .FirstOrDefault(target =>
                        target.ApplicationId == request.ApplicationId &&
                        target.ModuleId is null &&
                        target.ParentProjectId is null)
                    ?? throw new InvalidDataException(
                        "L'application choisie n'est plus disponible.");

                var creator = new FileSystemProjectCreator(
                    _workspaceRoot);

                var created = await creator.CreateAsync(
                    new ProjectCreationRequest
                    {
                        Name = request.NewProjectName!.Trim(),
                        Complexity = request.NewProjectComplexity,
                        Target = applicationTarget
                    },
                    cancellationToken);

                projectDirectory = created.ProjectDirectory;
                projectId = created.Project.Id;
                project = created.Project;
                createdProject = true;
                manifestPath = Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.ProjectManifestFileName);
            }

            var updatedProject = EnsureSections(
                project,
                selected.Select(section =>
                    section.TargetSection));

            if (!ReferenceEquals(
                    updatedProject,
                    project))
            {
                await AtomicJsonFile.WriteAsync(
                    manifestPath!,
                    updatedProject,
                    cancellationToken);
            }

            foreach (var group in selected
                         .GroupBy(
                             item => item.TargetSection,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sectionName = group.Key.Trim();
                var sectionDirectory = Path.Combine(
                    projectDirectory,
                    WindowsPathRules.SanitizeSegment(
                        sectionName));

                if (!Directory.Exists(sectionDirectory))
                {
                    Directory.CreateDirectory(
                        sectionDirectory);
                    createdDirectories.Add(
                        sectionDirectory);
                }

                var sourceStem =
                    Path.GetFileNameWithoutExtension(
                        preview.StagedImport.OriginalSourcePath);

                var destination = WindowsPathRules.GetUniqueFilePath(
                    sectionDirectory,
                    $"Import - {sourceStem}.md");

                var markdown = BuildImportedMarkdown(
                    Path.GetFileName(
                        preview.StagedImport.OriginalSourcePath),
                    group);

                await AtomicFileWriter.WriteAllTextAsync(
                    destination,
                    markdown,
                    cancellationToken);

                generatedFiles.Add(
                    destination);
            }

            var sourceCopyPath = await CommitStagedCopyAsync(
                preview.StagedImport,
                cancellationToken);

            return new DocxImportCommitResult
            {
                ProjectId = projectId,
                ProjectDirectory = projectDirectory,
                SourceCopyPath = sourceCopyPath,
                GeneratedFiles = generatedFiles
            };
        }
        catch
        {
            if (createdProject &&
                !string.IsNullOrWhiteSpace(projectDirectory) &&
                Directory.Exists(projectDirectory))
            {
                try
                {
                    Directory.Delete(
                        projectDirectory,
                        recursive: true);
                }
                catch
                {
                    // Preserve the original import exception.
                }
            }
            else
            {
                foreach (var file in generatedFiles)
                {
                    TryDelete(
                        file);
                }

                foreach (var directory in createdDirectories
                             .OrderByDescending(path =>
                                 path.Length))
                {
                    TryDeleteEmptyDirectory(
                        directory);
                }

                if (!string.IsNullOrWhiteSpace(manifestPath) &&
                    originalManifest is not null)
                {
                    try
                    {
                        await AtomicFileWriter.WriteAllTextAsync(
                            manifestPath,
                            originalManifest,
                            CancellationToken.None);
                    }
                    catch
                    {
                        // Preserve the original import exception.
                    }
                }
            }

            DiscardStagedCopy(
                preview.StagedImport);

            throw;
        }
    }

    public async Task<DocxStagedImport> StageAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullSourcePath = Path.GetFullPath(sourcePath);

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException(
                "Le fichier DOCX source est introuvable.",
                fullSourcePath);
        }

        if (!string.Equals(
                Path.GetExtension(fullSourcePath),
                ".docx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Seuls les fichiers .docx sont acceptés par l'import Word.");
        }

        var stagingDirectory = GetStagingDirectory();

        Directory.CreateDirectory(
            stagingDirectory);

        var stagedPath = Path.Combine(
            stagingDirectory,
            $"{Guid.NewGuid():N}-{WindowsPathRules.SanitizeSegment(Path.GetFileNameWithoutExtension(fullSourcePath))}.docx");

        try
        {
            await CopyAsync(
                fullSourcePath,
                stagedPath,
                cancellationToken);

            var document = await _parser.ParseAsync(
                stagedPath,
                cancellationToken);

            return new DocxStagedImport
            {
                OriginalSourcePath = fullSourcePath,
                StagedCopyPath = stagedPath,
                Document = document
            };
        }
        catch
        {
            TryDelete(
                stagedPath);
            throw;
        }
    }

    public Task<string> CommitStagedCopyAsync(
        DocxStagedImport staged,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();

        var stagedPath = Path.GetFullPath(
            staged.StagedCopyPath);

        EnsureIsStagingPath(
            stagedPath);

        if (!File.Exists(stagedPath))
        {
            throw new FileNotFoundException(
                "La copie temporaire DOCX est introuvable.",
                stagedPath);
        }

        var sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        Directory.CreateDirectory(
            sourcesDirectory);

        var destination = WindowsPathRules.GetUniqueFilePath(
            sourcesDirectory,
            Path.GetFileName(staged.OriginalSourcePath));

        File.Move(
            stagedPath,
            destination);

        return Task.FromResult(
            destination);
    }

    public void DiscardStagedCopy(
        DocxStagedImport staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        var stagedPath = Path.GetFullPath(
            staged.StagedCopyPath);

        EnsureIsStagingPath(
            stagedPath);

        TryDelete(
            stagedPath);
    }

    private static List<DocxImportSectionPreview> BuildSectionPreviews(
        DocxImportAnalysis analysis)
    {
        var result = new List<DocxImportSectionPreview>();
        var index = 0;

        foreach (var mapped in analysis.MappedSections)
        {
            result.Add(
                new DocxImportSectionPreview
                {
                    Index = index++,
                    SourceHeading = mapped.SourceHeading,
                    SuggestedTargetSection = mapped.TargetSection,
                    BlockCount = mapped.Blocks.Count,
                    MarkdownPreview =
                        DocxMarkdownConverter.ConvertBlocks(
                            mapped.Blocks)
                });
        }

        if (analysis.UnmappedBlocks.Count > 0)
        {
            result.Add(
                new DocxImportSectionPreview
                {
                    Index = index,
                    SourceHeading = "Contenu non mappé",
                    SuggestedTargetSection = "Documentation",
                    BlockCount = analysis.UnmappedBlocks.Count,
                    MarkdownPreview =
                        DocxMarkdownConverter.ConvertBlocks(
                            analysis.UnmappedBlocks)
                });
        }

        return result;
    }

    private static List<string> BuildPreviewWarnings(
        IReadOnlyList<DocxImportTargetOption> applications,
        DocxImportAnalysis analysis,
        IReadOnlyList<DocxImportSectionPreview> sections)
    {
        var warnings = new List<string>();

        if (applications.Count == 0)
        {
            warnings.Add(
                "Aucune application Nodalis n'existe encore : créez-en une avant de valider l'import.");
        }

        if (analysis.HasAmbiguousApplication)
        {
            warnings.Add(
                "Plusieurs applications correspondent au document : vérifiez la cible.");
        }

        if (analysis.HasAmbiguousProject)
        {
            warnings.Add(
                "Plusieurs projets correspondent au document : vérifiez la cible.");
        }

        if (sections.Count == 0)
        {
            warnings.Add(
                "Aucun contenu exploitable n'a été détecté dans le document.");
        }

        return warnings;
    }

    private static void ValidateCommitRequest(
        DocxImportPreview preview,
        DocxImportCommitRequest request)
    {
        if (!preview.Applications.Any(application =>
                application.Id == request.ApplicationId))
        {
            throw new InvalidDataException(
                "L'application sélectionnée n'est pas disponible.");
        }

        if (request.ProjectId is Guid projectId &&
            !preview.Projects.Any(project =>
                project.Id == projectId))
        {
            throw new InvalidDataException(
                "Le projet sélectionné n'est pas disponible.");
        }

        if (request.ProjectId is null &&
            string.IsNullOrWhiteSpace(
                request.NewProjectName))
        {
            throw new InvalidDataException(
                "Choisissez un projet existant ou indiquez le nom du nouveau projet.");
        }

        if (request.Sections.Count == 0 ||
            request.Sections.All(section =>
                !section.Include))
        {
            throw new InvalidDataException(
                "Sélectionnez au moins une section à importer.");
        }

        var availableIndexes = preview.Sections
            .Select(section => section.Index)
            .ToHashSet();

        foreach (var section in request.Sections)
        {
            if (!availableIndexes.Contains(
                    section.SectionIndex))
            {
                throw new InvalidDataException(
                    "La sélection de sections ne correspond plus à la prévisualisation.");
            }

            if (section.Include &&
                string.IsNullOrWhiteSpace(
                    section.TargetSection))
            {
                throw new InvalidDataException(
                    "Chaque section incluse doit avoir une section Nodalis cible.");
            }
        }
    }

    private static ProjectManifest EnsureSections(
        ProjectManifest project,
        IEnumerable<string> targetSections)
    {
        var sections = project.Sections.ToList();
        var nextOrder = sections.Count == 0
            ? 10
            : sections.Max(section =>
                section.Order) + 10;
        var changed = false;

        foreach (var targetSection in targetSections
                     .Select(name => name.Trim())
                     .Where(name =>
                         !string.IsNullOrWhiteSpace(name))
                     .Distinct(
                         StringComparer.CurrentCultureIgnoreCase))
        {
            if (sections.Any(section =>
                    string.Equals(
                        section.Name,
                        targetSection,
                        StringComparison.CurrentCultureIgnoreCase)))
            {
                continue;
            }

            sections.Add(
                new SectionManifest
                {
                    Id = Guid.NewGuid(),
                    Name = targetSection,
                    Order = nextOrder,
                    IsSingleton = false
                });

            nextOrder += 10;
            changed = true;
        }

        return changed
            ? project with
            {
                Sections = sections
            }
            : project;
    }

    private static string BuildImportedMarkdown(
        string sourceFileName,
        IEnumerable<SelectedSection> sections)
    {
        var sourceStem =
            Path.GetFileNameWithoutExtension(
                sourceFileName);

        var builder = new StringBuilder();

        builder.AppendLine(
            $"# Import — {sourceStem}");
        builder.AppendLine();
        builder.AppendLine(
            $"> Source : {sourceFileName}");
        builder.AppendLine();

        foreach (var section in sections)
        {
            builder.AppendLine(
                $"## {section.Preview.SourceHeading}");
            builder.AppendLine();

            var markdown =
                section.Preview.MarkdownPreview.Trim();

            if (!string.IsNullOrWhiteSpace(markdown))
            {
                builder.AppendLine(
                    markdown);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private DocxImportPlannedChange PlanChange(
        string action,
        string fullPath,
        string description) =>
        new()
        {
            Action = action,
            RelativePath =
                ToWorkspaceRelativePath(
                    fullPath),
            Description = description
        };

    private string ToWorkspaceRelativePath(
        string fullPath) =>
        Path.GetRelativePath(
                _workspaceRoot,
                Path.GetFullPath(fullPath))
            .Replace(
                Path.DirectorySeparatorChar,
                '/');

    private string ResolveRelativePath(
        string relativePath) =>
        Path.GetFullPath(
            Path.Combine(
                _workspaceRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar)));

    private string GetStagingDirectory() =>
        Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportStagingDirectoryName);

    private void EnsureIsStagingPath(string path)
    {
        var root = Path.GetFullPath(
                GetStagingDirectory())
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (!path.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La copie DOCX indiquée ne se trouve pas dans la zone temporaire d'import.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best-effort. Import operations keep their original error.
        }
    }

    private static void TryDeleteEmptyDirectory(
        string path)
    {
        try
        {
            if (Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // Cleanup is best-effort. Import operations keep their original error.
        }
    }

    private static async Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await source.CopyToAsync(
            destination,
            cancellationToken);

        await destination.FlushAsync(
            cancellationToken);
    }

    private sealed record SelectedSection(
        DocxImportSectionPreview Preview,
        string TargetSection);
}
