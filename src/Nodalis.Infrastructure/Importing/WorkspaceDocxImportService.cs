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

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceDocxImportService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public WorkspaceDocxImportService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>ImportAsync</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<DocxImportResult> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Importing.DocxStagedImport staged = await StageAsync(
            sourcePath,
            cancellationToken);

        try
        {
            string sourceCopyPath = await CommitStagedCopyAsync(
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

    /// <summary>
    /// Performs the <c>PreparePreviewAsync</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<DocxImportPreview> PreparePreviewAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Core.Importing.DocxStagedImport staged = await StageAsync(
            sourcePath,
            cancellationToken);

        try
        {
            global::Nodalis.Infrastructure.Importing.DocxImportAnalyzer analyzer = new DocxImportAnalyzer(
                _workspaceRoot);

            global::Nodalis.Core.Importing.DocxImportAnalysis analysis = await analyzer.AnalyzeAsync(
                staged.Document,
                Path.GetFileName(sourcePath),
                cancellationToken);

            global::Nodalis.Infrastructure.Links.WorkspaceLinkIndexService indexService = new WorkspaceLinkIndexService(
                _workspaceRoot);

            global::Nodalis.Core.Links.LinkIndexCatalog index = await indexService.RefreshAsync(
                cancellationToken);

            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportTargetOption> applications = index.Targets
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

            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportTargetOption> projects = new List<DocxImportTargetOption>();

            foreach (global::Nodalis.Core.Links.LinkTargetEntry target in index.Targets
                         .Where(target =>
                             target.Kind == LinkTargetKind.Project)
                         .OrderBy(target =>
                             target.QualifiedName,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string projectDirectory = ResolveRelativePath(
                    target.RelativePath);
                string manifestPath = Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.ProjectManifestFileName);

                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                global::Nodalis.Core.Domain.ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
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

            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportSectionPreview> sections = BuildSectionPreviews(
                analysis,
                staged.Document);

            Guid? suggestedApplicationId =
                analysis.ApplicationCandidates.Count == 1
                    ? analysis.ApplicationCandidates[0].Id
                    : null;

            Guid? suggestedProjectId =
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

            string? suggestedNewProjectName =
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

    /// <summary>
    /// Performs the <c>BuildPlanAsync</c> operation.
    /// </summary>
    /// <param name="preview">The <c>preview</c> value.</param>
    /// <param name="request">The <c>request</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

        global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService.SelectedSection[] selected = request.Sections
            .Where(section => section.Include)
            .Select(section =>
            {
                global::Nodalis.Core.Importing.DocxImportSectionPreview previewSection = preview.Sections
                    .Single(candidate =>
                        candidate.Index == section.SectionIndex);

                return new SelectedSection(
                    previewSection,
                    section.TargetSection.Trim(),
                    section.SelectedBlockIndexes?.ToHashSet());
            })
            .ToArray();

        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportPlannedChange> changes = new List<DocxImportPlannedChange>();
        global::System.Collections.Generic.List<string> warnings = preview.Conflicts.ToList();

        string projectDirectory;
        string projectDisplayName;
        ProjectManifest? existingProject = null;
        bool createsProject = request.ProjectId is null;

        if (request.ProjectId is Guid existingProjectId)
        {
            global::Nodalis.Core.Importing.DocxImportTargetOption target = preview.Projects.Single(project =>
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

            string[] requestedSections = selected
                .Select(section => section.TargetSection)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            string[] missingSections = requestedSections
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
            global::Nodalis.Infrastructure.Projects.ProjectCreationTargetDiscovery discovery = new ProjectCreationTargetDiscovery();
            global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Projects.ProjectCreationTarget> targets = await discovery.DiscoverAsync(
                _workspaceRoot,
                cancellationToken);

            global::Nodalis.Core.Projects.ProjectCreationTarget applicationTarget = targets
                .FirstOrDefault(target =>
                    target.ApplicationId == request.ApplicationId &&
                    target.ModuleId is null &&
                    target.ParentProjectId is null)
                ?? throw new InvalidDataException(
                    "L'application choisie n'est plus disponible.");

            string projectsDirectory = Path.Combine(
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

            global::Nodalis.Infrastructure.Templates.FileSystemTemplateStore templateStore = new FileSystemTemplateStore(
                _workspaceRoot);

            global::Nodalis.Core.Templates.ProjectProfileCatalog profileCatalog =
                await templateStore.LoadProjectProfilesAsync(
                    cancellationToken);

            global::Nodalis.Core.Templates.ProjectProfileDefinition profile = profileCatalog.Profiles.SingleOrDefault(candidate =>
                    candidate.Complexity ==
                    request.NewProjectComplexity)
                ?? throw new InvalidDataException(
                    $"Aucun profil de projet '{request.NewProjectComplexity}' n'est disponible.");

            foreach (global::Nodalis.Core.Templates.ProjectSectionTemplateDefinition section in profile.Sections
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

        string sourceStem =
            Path.GetFileNameWithoutExtension(
                preview.StagedImport.OriginalSourcePath);

        foreach (global::System.Linq.IGrouping<string, global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService.SelectedSection> group in selected
                     .GroupBy(
                         item => item.TargetSection,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            string sectionName = group.Key.Trim();
            string sectionDirectory = Path.Combine(
                projectDirectory,
                WindowsPathRules.SanitizeSegment(
                    sectionName));

            string desiredFileName =
                $"Import - {sourceStem}.md";

            string destination =
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

        string sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        string sourceCopyPath =
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

    /// <summary>
    /// Performs the <c>CommitAsync</c> operation.
    /// </summary>
    /// <param name="preview">The <c>preview</c> value.</param>
    /// <param name="request">The <c>request</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

        global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService.SelectedSection[] selected = request.Sections
            .Where(section => section.Include)
            .Select(section =>
            {
                global::Nodalis.Core.Importing.DocxImportSectionPreview previewSection = preview.Sections
                    .Single(candidate =>
                        candidate.Index == section.SectionIndex);

                return new SelectedSection(
                    previewSection,
                    section.TargetSection.Trim(),
                    section.SelectedBlockIndexes?.ToHashSet());
            })
            .ToArray();

        string projectDirectory = string.Empty;
        global::System.Guid projectId = Guid.Empty;
        bool createdProject = false;
        string? manifestPath = null;
        string? originalManifest = null;
        global::System.Collections.Generic.List<string> generatedFiles = new List<string>();
        global::System.Collections.Generic.List<string> createdDirectories = new List<string>();

        try
        {
            ProjectManifest project;

            if (request.ProjectId is Guid existingProjectId)
            {
                global::Nodalis.Core.Importing.DocxImportTargetOption target = preview.Projects.Single(project =>
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
                global::Nodalis.Infrastructure.Projects.ProjectCreationTargetDiscovery discovery = new ProjectCreationTargetDiscovery();
                global::System.Collections.Generic.IReadOnlyList<global::Nodalis.Core.Projects.ProjectCreationTarget> targets = await discovery.DiscoverAsync(
                    _workspaceRoot,
                    cancellationToken);

                global::Nodalis.Core.Projects.ProjectCreationTarget applicationTarget = targets
                    .FirstOrDefault(target =>
                        target.ApplicationId == request.ApplicationId &&
                        target.ModuleId is null &&
                        target.ParentProjectId is null)
                    ?? throw new InvalidDataException(
                        "L'application choisie n'est plus disponible.");

                global::Nodalis.Infrastructure.Projects.FileSystemProjectCreator creator = new FileSystemProjectCreator(
                    _workspaceRoot);

                global::Nodalis.Core.Projects.ProjectCreationResult created = await creator.CreateAsync(
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

            global::Nodalis.Core.Domain.ProjectManifest updatedProject = EnsureSections(
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

            foreach (global::System.Linq.IGrouping<string, global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService.SelectedSection> group in selected
                         .GroupBy(
                             item => item.TargetSection,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string sectionName = group.Key.Trim();
                string sectionDirectory = Path.Combine(
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

                string sourceStem =
                    Path.GetFileNameWithoutExtension(
                        preview.StagedImport.OriginalSourcePath);

                string destination = WindowsPathRules.GetUniqueFilePath(
                    sectionDirectory,
                    $"Import - {sourceStem}.md");

                string markdown = BuildImportedMarkdown(
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

            string sourceCopyPath = await CommitStagedCopyAsync(
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
                foreach (string file in generatedFiles)
                {
                    TryDelete(
                        file);
                }

                foreach (string directory in createdDirectories
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

    /// <summary>
    /// Performs the <c>StageAsync</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<DocxStagedImport> StageAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        string fullSourcePath = Path.GetFullPath(sourcePath);

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

        string stagingDirectory = GetStagingDirectory();

        Directory.CreateDirectory(
            stagingDirectory);

        string stagedPath = Path.Combine(
            stagingDirectory,
            $"{Guid.NewGuid():N}-{WindowsPathRules.SanitizeSegment(Path.GetFileNameWithoutExtension(fullSourcePath))}.docx");

        try
        {
            await CopyAsync(
                fullSourcePath,
                stagedPath,
                cancellationToken);

            global::Nodalis.Core.Importing.ParsedDocxDocument document = await _parser.ParseAsync(
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

    /// <summary>
    /// Performs the <c>CommitStagedCopyAsync</c> operation.
    /// </summary>
    /// <param name="staged">The <c>staged</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public Task<string> CommitStagedCopyAsync(
            DocxStagedImport staged,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        cancellationToken.ThrowIfCancellationRequested();

        string stagedPath = Path.GetFullPath(
            staged.StagedCopyPath);

        EnsureIsStagingPath(
            stagedPath);

        if (!File.Exists(stagedPath))
        {
            throw new FileNotFoundException(
                "La copie temporaire DOCX est introuvable.",
                stagedPath);
        }

        string sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        Directory.CreateDirectory(
            sourcesDirectory);

        string destination = WindowsPathRules.GetUniqueFilePath(
            sourcesDirectory,
            Path.GetFileName(staged.OriginalSourcePath));

        File.Move(
            stagedPath,
            destination);

        return Task.FromResult(
            destination);
    }

    /// <summary>
    /// Performs the <c>DiscardStagedCopy</c> operation.
    /// </summary>
    /// <param name="staged">The <c>staged</c> value.</param>
    public void DiscardStagedCopy(
            DocxStagedImport staged)
    {
        ArgumentNullException.ThrowIfNull(staged);

        string stagedPath = Path.GetFullPath(
            staged.StagedCopyPath);

        EnsureIsStagingPath(
            stagedPath);

        TryDelete(
            stagedPath);
    }

    /// <summary>
    /// Performs the <c>BuildSectionPreviews</c> operation.
    /// </summary>
    /// <param name="analysis">The <c>analysis</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<DocxImportSectionPreview> BuildSectionPreviews(
            DocxImportAnalysis analysis,
            ParsedDocxDocument document)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportSectionPreview> result = new List<DocxImportSectionPreview>();
        int index = 0;

        foreach (global::Nodalis.Core.Importing.DocxMappedSection mapped in analysis.MappedSections)
        {
            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportBlockPreview> blocks = BuildBlockPreviews(
                mapped.Blocks,
                document.Blocks);

            result.Add(
                CreateSectionPreview(
                    index++,
                    mapped.SourceHeading,
                    mapped.TargetSection,
                    mapped.HeadingBlockIndex,
                    blocks));
        }

        if (analysis.UnmappedBlocks.Count > 0)
        {
            global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportBlockPreview> blocks = BuildBlockPreviews(
                analysis.UnmappedBlocks,
                document.Blocks);

            result.Add(
                CreateSectionPreview(
                    index,
                    "Contenu non mappé",
                    "Documentation",
                    null,
                    blocks));
        }

        return result;
    }

    /// <summary>
    /// Creates one DOCX section preview and its initial all-selected Markdown rendering.
    /// </summary>
    /// <param name="index">The section index.</param>
    /// <param name="sourceHeading">The readable source heading.</param>
    /// <param name="targetSection">The suggested Nodalis target section.</param>
    /// <param name="headingBlockIndex">The original mapped heading index, when available.</param>
    /// <param name="blocks">The selectable source blocks.</param>
    /// <returns>The initialized section preview.</returns>
    private static DocxImportSectionPreview CreateSectionPreview(
            int index,
            string sourceHeading,
            string targetSection,
            int? headingBlockIndex,
            List<DocxImportBlockPreview> blocks)
    {
        global::Nodalis.Core.Importing.DocxImportSectionPreview preview =
            new DocxImportSectionPreview
            {
                Index = index,
                SourceHeading = sourceHeading,
                SuggestedTargetSection = targetSection,
                BlockCount = blocks.Count,
                HeadingBlockIndex = headingBlockIndex,
                Blocks = blocks,
                MarkdownPreview = string.Empty
            };

        return preview with
        {
            MarkdownPreview =
                DocxImportSelectionRenderer.RenderAll(
                    preview)
        };
    }

    /// <summary>
    /// Builds selectable block metadata while preserving source order and nested heading relationships.
    /// </summary>
    /// <param name="sectionBlocks">The source blocks belonging to one preview section.</param>
    /// <param name="documentBlocks">All source document blocks.</param>
    /// <returns>The selectable block previews.</returns>
    private static List<DocxImportBlockPreview> BuildBlockPreviews(
            IReadOnlyList<DocxBlock> sectionBlocks,
            IReadOnlyList<DocxBlock> documentBlocks)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportBlockPreview> result = new List<DocxImportBlockPreview>();
        global::System.Collections.Generic.List<(int BlockIndex, int HeadingLevel)> headings =
            new List<(int BlockIndex, int HeadingLevel)>();
        int? previousBlockIndex = null;

        foreach (global::Nodalis.Core.Importing.DocxBlock block in sectionBlocks)
        {
            int blockIndex =
                FindSourceBlockIndex(
                    documentBlocks,
                    block);

            if (blockIndex < 0)
            {
                continue;
            }

            if (previousBlockIndex is int previous &&
                blockIndex != previous + 1)
            {
                headings.Clear();
            }

            int? parentBlockIndex;

            if (block.Kind == DocxBlockKind.Paragraph &&
                block.Paragraph?.HeadingLevel is int headingLevel)
            {
                while (headings.Count > 0 &&
                       headings[^1].HeadingLevel >= headingLevel)
                {
                    headings.RemoveAt(
                        headings.Count - 1);
                }

                parentBlockIndex =
                    headings.Count == 0
                        ? null
                        : headings[^1].BlockIndex;

                headings.Add(
                    (
                        blockIndex,
                        headingLevel));
            }
            else
            {
                parentBlockIndex =
                    headings.Count == 0
                        ? null
                        : headings[^1].BlockIndex;
            }

            result.Add(
                new DocxImportBlockPreview
                {
                    BlockIndex = blockIndex,
                    Block = block,
                    KindLabel = GetBlockKindLabel(
                        block),
                    DisplayText = GetBlockDisplayText(
                        block),
                    MarkdownPreview =
                        DocxMarkdownConverter.ConvertBlocks(
                            new[]
                            {
                                block
                            }),
                    ParentBlockIndex = parentBlockIndex
                });

            previousBlockIndex =
                blockIndex;
        }

        return result;
    }

    /// <summary>
    /// Finds the original document index for a parsed block by object identity.
    /// </summary>
    /// <param name="documentBlocks">The original document blocks.</param>
    /// <param name="block">The block to locate.</param>
    /// <returns>The zero-based source index, or -1 when absent.</returns>
    private static int FindSourceBlockIndex(
            IReadOnlyList<DocxBlock> documentBlocks,
            DocxBlock block)
    {
        for (int index = 0;
             index < documentBlocks.Count;
             index++)
        {
            if (ReferenceEquals(
                    documentBlocks[index],
                    block))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns a compact localized label for one selectable DOCX block.
    /// </summary>
    /// <param name="block">The parsed block.</param>
    /// <returns>The block kind label.</returns>
    private static string GetBlockKindLabel(
            DocxBlock block)
    {
        if (block.Kind == DocxBlockKind.Table)
        {
            return "Tableau";
        }

        if (block.Paragraph?.HeadingLevel is int headingLevel)
        {
            return "Titre " +
                   headingLevel;
        }

        if (block.Paragraph?.IsListItem == true)
        {
            return "Liste";
        }

        return "Paragraphe";
    }

    /// <summary>
    /// Returns a readable one-line summary for one selectable DOCX block.
    /// </summary>
    /// <param name="block">The parsed block.</param>
    /// <returns>The block display text.</returns>
    private static string GetBlockDisplayText(
            DocxBlock block)
    {
        if (block.Kind == DocxBlockKind.Table &&
            block.Table is DocxTable table)
        {
            int columns =
                table.Rows.Count == 0
                    ? 0
                    : table.Rows.Max(row =>
                        row.Cells.Count);

            return "Tableau · " +
                   table.Rows.Count +
                   " ligne(s) × " +
                   columns +
                   " colonne(s)";
        }

        string text =
            block.Paragraph?.Text.Trim() ??
            string.Empty;

        if (!string.IsNullOrWhiteSpace(
                text))
        {
            return text;
        }

        return block.Paragraph?.IsListItem == true
            ? "(élément de liste vide)"
            : block.Paragraph?.HeadingLevel is not null
                ? "(titre vide)"
                : "(paragraphe vide)";
    }

    /// <summary>
    /// Performs the <c>BuildPreviewWarnings</c> operation.
    /// </summary>
    /// <param name="applications">The <c>applications</c> value.</param>
    /// <param name="analysis">The <c>analysis</c> value.</param>
    /// <param name="sections">The <c>sections</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static List<string> BuildPreviewWarnings(
            IReadOnlyList<DocxImportTargetOption> applications,
            DocxImportAnalysis analysis,
            IReadOnlyList<DocxImportSectionPreview> sections)
    {
        global::System.Collections.Generic.List<string> warnings = new List<string>();

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

    /// <summary>
    /// Performs the <c>ValidateCommitRequest</c> operation.
    /// </summary>
    /// <param name="preview">The <c>preview</c> value.</param>
    /// <param name="request">The <c>request</c> value.</param>
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

        global::System.Collections.Generic.HashSet<int> availableIndexes = preview.Sections
            .Select(section => section.Index)
            .ToHashSet();

        foreach (global::Nodalis.Core.Importing.DocxImportSectionSelection section in request.Sections)
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

            global::Nodalis.Core.Importing.DocxImportSectionPreview previewSection = preview.Sections.Single(candidate =>
                candidate.Index == section.SectionIndex);

            if (section.SelectedBlockIndexes is { Count: > 0 } selectedBlockIndexes)
            {
                global::System.Collections.Generic.HashSet<int> availableBlockIndexes = previewSection.Blocks
                    .Select(block =>
                        block.BlockIndex)
                    .ToHashSet();

                if (selectedBlockIndexes.Any(blockIndex =>
                        !availableBlockIndexes.Contains(
                            blockIndex)))
                {
                    throw new InvalidDataException(
                        "La sélection fine des paragraphes ne correspond plus à la prévisualisation DOCX.");
                }
            }
        }
    }

    /// <summary>
    /// Performs the <c>EnsureSections</c> operation.
    /// </summary>
    /// <param name="project">The <c>project</c> value.</param>
    /// <param name="targetSections">The <c>targetSections</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static ProjectManifest EnsureSections(
            ProjectManifest project,
            IEnumerable<string> targetSections)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Domain.SectionManifest> sections = project.Sections.ToList();
        int nextOrder = sections.Count == 0
            ? 10
            : sections.Max(section =>
                section.Order) + 10;
        bool changed = false;

        foreach (string targetSection in targetSections
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

    /// <summary>
    /// Performs the <c>BuildImportedMarkdown</c> operation.
    /// </summary>
    /// <param name="sourceFileName">The <c>sourceFileName</c> value.</param>
    /// <param name="sections">The <c>sections</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string BuildImportedMarkdown(
            string sourceFileName,
            IEnumerable<SelectedSection> sections)
    {
        string sourceStem =
            Path.GetFileNameWithoutExtension(
                sourceFileName);

        global::System.Text.StringBuilder builder = new StringBuilder();

        builder.AppendLine(
            $"# Import — {sourceStem}");
        builder.AppendLine();
        builder.AppendLine(
            $"> Source : {sourceFileName}");
        builder.AppendLine();

        foreach (global::Nodalis.Infrastructure.Importing.WorkspaceDocxImportService.SelectedSection section in sections)
        {
            global::System.Collections.Generic.IReadOnlyCollection<int> selectedBlockIndexes =
                section.SelectedBlockIndexes is null
                    ? section.Preview.Blocks
                        .Select(block =>
                            block.BlockIndex)
                        .ToArray()
                    : section.SelectedBlockIndexes;

            string markdown =
                DocxImportSelectionRenderer.Render(
                        section.Preview,
                        selectedBlockIndexes)
                    .TrimEnd();

            if (!string.IsNullOrWhiteSpace(
                    markdown))
            {
                builder.AppendLine(
                    markdown);
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Performs the <c>PlanChange</c> operation.
    /// </summary>
    /// <param name="action">The <c>action</c> value.</param>
    /// <param name="fullPath">The <c>fullPath</c> value.</param>
    /// <param name="description">The <c>description</c> value.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Performs the <c>ToWorkspaceRelativePath</c> operation.
    /// </summary>
    /// <param name="fullPath">The <c>fullPath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private string ToWorkspaceRelativePath(
            string fullPath) =>
            Path.GetRelativePath(
                    _workspaceRoot,
                    Path.GetFullPath(fullPath))
                .Replace(
                    Path.DirectorySeparatorChar,
                    '/');

    /// <summary>
    /// Performs the <c>ResolveRelativePath</c> operation.
    /// </summary>
    /// <param name="relativePath">The <c>relativePath</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private string ResolveRelativePath(
            string relativePath) =>
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

    /// <summary>
    /// Performs the <c>GetStagingDirectory</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private string GetStagingDirectory() =>
            Path.Combine(
                _workspaceRoot,
                WorkspaceLayout.ImportsDirectoryName,
                WorkspaceLayout.ImportStagingDirectoryName);

    /// <summary>
    /// Performs the <c>EnsureIsStagingPath</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    private void EnsureIsStagingPath(string path)
    {
        string root = Path.GetFullPath(
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

    /// <summary>
    /// Performs the <c>TryDelete</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
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

    /// <summary>
    /// Performs the <c>TryDeleteEmptyDirectory</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
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

    /// <summary>
    /// Performs the <c>CopyAsync</c> operation.
    /// </summary>
    /// <param name="sourcePath">The <c>sourcePath</c> value.</param>
    /// <param name="destinationPath">The <c>destinationPath</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task CopyAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
    {
        await using global::System.IO.FileStream source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        await using global::System.IO.FileStream destination = new FileStream(
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
        string TargetSection,
        IReadOnlySet<int>? SelectedBlockIndexes);
}
