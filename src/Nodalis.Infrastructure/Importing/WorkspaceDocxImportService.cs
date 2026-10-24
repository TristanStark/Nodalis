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
    private const long MaxStagedDocxBytes = 128L * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly DocxParser _parser = new();
    private readonly Action<string>? _afterProjectManifestCheckpoint;
    private readonly Action<int, string>? _afterMarkdownCreatedCheckpoint;
    private readonly Action<string, string>? _afterDurableCheckpoint;

    /// <summary>Gets the workspace root for local-only user profile storage.</summary>
    public string WorkspaceRoot => _workspaceRoot;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceDocxImportService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    /// <param name="afterProjectManifestCheckpoint">Optional fault-injection hook for manifest rollback regression tests.</param>
    /// <param name="afterMarkdownCreatedCheckpoint">Optional fault-injection hook after an import creates a Markdown file.</param>
    /// <param name="afterDurableCheckpoint">Optional crash-test hook at completed project, media, DOCX source or provenance checkpoints.</param>
    public WorkspaceDocxImportService(string workspaceRoot,
        Action<string>? afterProjectManifestCheckpoint = null,
        Action<int, string>? afterMarkdownCreatedCheckpoint = null,
        Action<string, string>? afterDurableCheckpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _afterProjectManifestCheckpoint = afterProjectManifestCheckpoint;
        _afterMarkdownCreatedCheckpoint = afterMarkdownCreatedCheckpoint;
        _afterDurableCheckpoint = afterDurableCheckpoint;
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
                        ApplicationId = manifest.ApplicationId,
                        ExistingSections = manifest.Sections
                            .OrderBy(section => section.Order)
                            .Select(section => section.Name)
                            .ToList()
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
        await ValidateStagedCopyAsync(preview.StagedImport, cancellationToken);

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

        DocxBlock[] selectedSourceBlocks = selected
            .SelectMany(section => section.Preview.Blocks
                .Where(block => section.SelectedBlockIndexes is null ||
                                section.SelectedBlockIndexes.Contains(block.BlockIndex))
                .Select(block => block.Block))
            .ToArray();
        IReadOnlyList<DocxInlineMedia> selectedMedia =
            DocxSelectedMediaAssets.Select(selectedSourceBlocks);

        global::System.Collections.Generic.List<global::Nodalis.Core.Importing.DocxImportPlannedChange> changes = new List<DocxImportPlannedChange>();
        global::System.Collections.Generic.List<string> warnings = preview.Conflicts.ToList();
        warnings.AddRange(DocxSelectedMediaAssets.Warnings(selectedSourceBlocks));

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
            ValidateActiveImportProject(existingProject, existingProjectId,
                request.ApplicationId);

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

            bool ultraSimple = request.NewProjectComplexity == ProjectComplexity.UltraSimple;
            changes.Add(
                PlanChange(
                    "Créer",
                    Path.Combine(projectDirectory,
                        ultraSimple ? "Fiche de Maintenance.md" : "Présentation.md"),
                    ultraSimple
                        ? "Fiche de maintenance du nouveau projet Ultra-simple"
                        : "Présentation du nouveau projet"));

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
                string? singletonFileName =
                    ProjectSingletonDocumentLayout.GetRootDocumentFileName(
                        section.TemplateKey);
                string sectionFilePath = singletonFileName is not null
                    ? Path.Combine(projectDirectory, singletonFileName)
                    : Path.Combine(projectDirectory,
                        WindowsPathRules.SanitizeSegment(section.Name),
                        WindowsPathRules.SanitizeSegment(section.Name) + ".md");
                changes.Add(PlanChange("Créer", sectionFilePath,
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
            if (group.Count() > 1)
            {
                warnings.Add(
                    "Fusion ordonnée : " + group.Count() +
                    " sections Word rejoignent « " + sectionName +
                    " » dans l'ordre choisi dans l'onglet Affectation.");
            }

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

        foreach (DocxInlineMedia media in selectedMedia)
        {
            string destination = Path.Combine(projectDirectory,
                DocxSelectedMediaAssets.MediaFolder,
                Path.GetFileName(media.MarkdownRelativePath!));
            changes.Add(PlanChange(
                File.Exists(destination) ? "Réutiliser" : "Créer",
                destination,
                "Image Word SHA256 · pièce jointe locale, sans téléchargement"));
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

        changes.Add(PlanChange(
            "Créer",
            new DocxImportManifestStore(_workspaceRoot).GetManifestPath(
                projectDirectory, preview.ImportId),
            "Manifeste de provenance DOCX (empreintes et snapshots de réimport)"));

        string contentBeforeCommit = string.Join(
            "\n--TARGET--\n",
            selected.GroupBy(item => item.TargetSection, StringComparer.CurrentCultureIgnoreCase)
                .Select(group =>
                    group.Key + "\n" + BuildImportedMarkdown(
                        Path.GetFileName(preview.StagedImport.OriginalSourcePath), group)));

        DocxImportPlan plan = new DocxImportPlan
        {
            MappingProfileSha256 = request.AppliedMappingProfile is null
                ? string.Empty
                : DocxImportManifestStore.ComputeSha256(
                    System.Text.Json.JsonSerializer.Serialize(request.AppliedMappingProfile)),
            ProjectedContentSha256 = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(contentBeforeCommit))).ToLowerInvariant(),
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

        return plan with { Fingerprint = DocxImportPlanFingerprint.Compute(plan) };
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
        await ValidateStagedCopyAsync(preview.StagedImport, cancellationToken);

        // Freeze all destinations before the first write. A collision during
        // commit must fail, never silently rename an approved Markdown or DOCX.
        DocxImportPlan currentPlan = await BuildPlanAsync(preview, request, cancellationToken);
        if (request.ExpectedPlanFingerprint is string approved &&
            (approved.Length != 64 ||
             !string.Equals(approved, currentPlan.Fingerprint, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Le plan DOCX a changé depuis la prévisualisation. Recalculez et validez le plan avant d'importer.");
        }

        string plannedProjectDirectory = ResolveRelativePath(
            currentPlan.TargetProjectRelativePath);
        string plannedSourceCopyPath = ResolvePlannedChangePath(currentPlan,
            "Copier", "Copie locale du DOCX original ; le fichier source reste intact");

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

        DocxBlock[] selectedSourceBlocks = selected
            .SelectMany(section => section.Preview.Blocks
                .Where(block => section.SelectedBlockIndexes is null ||
                                section.SelectedBlockIndexes.Contains(block.BlockIndex))
                .Select(block => block.Block))
            .ToArray();
        IReadOnlyList<DocxInlineMedia> selectedMedia =
            DocxSelectedMediaAssets.Select(selectedSourceBlocks);

        // Write the crash-recovery journal BEFORE creating the project or
        // changing its manifest, documents, media, source copy or provenance.
        DocxImportManifestStore initialPaths = new DocxImportManifestStore(_workspaceRoot);
        List<DocxFirstImportFile> expectedOutputs = [];
        foreach (IGrouping<string, SelectedSection> group in selected.GroupBy(
                     section => section.TargetSection, StringComparer.CurrentCultureIgnoreCase))
        {
            string sectionName = group.Key.Trim();
            string description =
                $"{group.Count()} section(s) Word → section Nodalis « {sectionName} »";
            string destination = ResolvePlannedChangePath(currentPlan,
                "Créer", description);
            string markdown = BuildImportedMarkdown(
                Path.GetFileName(preview.StagedImport.OriginalSourcePath), group);
            expectedOutputs.Add(new DocxFirstImportFile
            {
                RelativePath = initialPaths.ToWorkspaceRelativePath(destination),
                Sha256 = DocxImportManifestStore.ComputeSha256(markdown)
            });
        }

        foreach (DocxInlineMedia media in selectedMedia)
        {
            string mediaDestination = Path.Combine(plannedProjectDirectory,
                DocxSelectedMediaAssets.MediaFolder,
                Path.GetFileName(media.MarkdownRelativePath!));
            if (!File.Exists(mediaDestination))
            {
                expectedOutputs.Add(new DocxFirstImportFile
                {
                    RelativePath = initialPaths.ToWorkspaceRelativePath(mediaDestination),
                    Sha256 = media.Sha256!
                });
            }
        }

        expectedOutputs.Add(new DocxFirstImportFile
        {
            RelativePath = initialPaths.ToWorkspaceRelativePath(plannedSourceCopyPath),
            Sha256 = preview.StagedImport.StagedCopySha256
        });
        DocxFirstImportRecoveryService recovery =
            new DocxFirstImportRecoveryService(_workspaceRoot);
        using DocxFirstImportSession recoverySession = await recovery.BeginAsync(
            currentPlan, preview.ImportId, request.ProjectId, expectedOutputs,
            cancellationToken);

        string projectDirectory = string.Empty;
        global::System.Guid projectId = Guid.Empty;
        bool createdProject = false;
        string? manifestPath = null;
        byte[]? originalManifestBytes = null;
        string? writtenManifestSha256 = null;
        global::System.Collections.Generic.List<string> generatedFiles = new List<string>();
        Dictionary<string, string> generatedMarkdownHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> createdProjectScaffoldHashes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int completedMarkdownWrites = 0;
        global::System.Collections.Generic.List<string> createdDirectories = new List<string>();
        DocxImportManifestStore provenanceStore = new DocxImportManifestStore(_workspaceRoot);
        List<DocxImportManifestFragment> provenanceFragments = [];
        string? committedSourceCopyPath = null;
        string? provenancePath = null;

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

                originalManifestBytes = await File.ReadAllBytesAsync(
                    manifestPath,
                    cancellationToken);

                project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
                    manifestPath,
                    cancellationToken);
                ValidateActiveImportProject(project, existingProjectId,
                    request.ApplicationId);

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
                _afterDurableCheckpoint?.Invoke("project", projectDirectory);
                // A new project already contains template files. Record their
                // exact original bytes so exception cleanup will never delete
                // a newly edited template or an unexpected user file.
                foreach (string path in Directory.EnumerateFiles(projectDirectory,
                             "*", SearchOption.AllDirectories))
                {
                    createdProjectScaffoldHashes.Add(path,
                        await DocxImportManifestStore.ComputeFileSha256Async(
                            path, cancellationToken));
                }

                manifestPath = Path.Combine(
                    projectDirectory,
                    WorkspaceLayout.ProjectManifestFileName);
                // Newly created projects may still require a custom DOCX
                // destination section not present in their initial profile.
                // Capture the creator's manifest for the same optimistic check.
                originalManifestBytes = await File.ReadAllBytesAsync(
                    manifestPath, cancellationToken);
            }

            if (!string.Equals(Path.GetFullPath(projectDirectory),
                    Path.GetFullPath(plannedProjectDirectory), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Le projet cible a changé depuis le plan DOCX approuvé. Relancez la prévisualisation.");
            }

            global::Nodalis.Core.Domain.ProjectManifest updatedProject = EnsureSections(
                project,
                selected.Select(section =>
                    section.TargetSection));

            if (!ReferenceEquals(updatedProject, project))
            {
                // Reject edits by another process between reading and updating
                // the existing project. Never roll back someone else's changes.
                string beforeSha256 = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        originalManifestBytes!)).ToLowerInvariant();
                string currentSha256 = await DocxImportManifestStore.ComputeFileSha256Async(
                    manifestPath!, cancellationToken);
                if (!string.Equals(currentSha256, beforeSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Le manifeste projet a changé pendant l'import : nouvelle prévisualisation requise.");
                }

                writtenManifestSha256 = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                            updatedProject, JsonDefaults.Options))).ToLowerInvariant();
                if (!createdProject)
                {
                    await recovery.ExpectProjectManifestAsync(
                        recoverySession, writtenManifestSha256, cancellationToken);
                }

                await AtomicJsonFile.WriteAsync(
                    manifestPath!,
                    updatedProject,
                    cancellationToken);
                _afterProjectManifestCheckpoint?.Invoke(manifestPath!);
            }

            await DocxSelectedMediaAssets.CopyAsync(
                preview.StagedImport.StagedCopyPath,
                projectDirectory, selectedMedia, generatedFiles, createdDirectories,
                cancellationToken);
            _afterDurableCheckpoint?.Invoke("media", projectDirectory);

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

                string description =
                    $"{group.Count()} section(s) Word → section Nodalis « {sectionName} »";
                string destination = ResolvePlannedChangePath(
                    currentPlan, "Créer", description);
                if (!string.Equals(Path.GetDirectoryName(destination),
                        sectionDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "La destination DOCX ne correspond plus à la section approuvée.");
                }

                string markdown = BuildImportedMarkdown(
                    Path.GetFileName(
                        preview.StagedImport.OriginalSourcePath),
                    group);

                // Create-only: a document appeared after the approved plan
                // must never be overwritten by a concurrent import.
                await AtomicFileWriter.WriteNewAllTextAsync(
                    destination,
                    markdown,
                    cancellationToken);

                generatedFiles.Add(destination);
                string destinationHash = DocxImportManifestStore.ComputeSha256(markdown);
                generatedMarkdownHashes.Add(destination, destinationHash);
                completedMarkdownWrites++;
                _afterMarkdownCreatedCheckpoint?.Invoke(
                    completedMarkdownWrites, destination);
                foreach (SelectedSection sourceSection in group)
                {
                    int[] indexes = (sourceSection.SelectedBlockIndexes is null
                            ? sourceSection.Preview.Blocks.Select(block => block.BlockIndex)
                            : sourceSection.SelectedBlockIndexes)
                        .OrderBy(index => index)
                        .ToArray();

                    string snapshot = DocxImportSelectionRenderer.Render(
                        sourceSection.Preview, indexes).TrimEnd();

                    DocxMappingSourceSection wordContext =
                        DocxMappingSourceBuilder.Build(
                            sourceSection.Preview, preview.StagedImport.Document);
                    provenanceFragments.Add(new DocxImportManifestFragment
                    {
                        SourceParentPath = wordContext.ParentPath,
                        SourceWordStyleId = wordContext.WordStyleId,
                        SourceHeadingLevel = wordContext.HeadingLevel,
                        FragmentId = Guid.NewGuid(),
                        SourceSectionIndex = sourceSection.Preview.Index,
                        HeadingBlockIndex = sourceSection.Preview.HeadingBlockIndex,
                        SourceHeading = sourceSection.Preview.SourceHeading,
                        TargetSection = sectionName,
                        TargetSectionId = updatedProject.Sections.First(section =>
                            string.Equals(section.Name, sectionName,
                                StringComparison.CurrentCultureIgnoreCase)).Id,
                        DestinationRelativePath = provenanceStore.ToWorkspaceRelativePath(destination),
                        SelectedBlockIndexes = indexes.ToList(),
                        OriginalSourceBlockCount = sourceSection.Preview.Blocks.Count,
                        FragmentSha256 = DocxImportManifestStore.ComputeSha256(snapshot),
                        BaseMarkdown = snapshot,
                        DestinationSha256 = destinationHash
                    });
                }
            }

            string sourceCopyPath = await CommitStagedCopyAsync(
                preview.StagedImport,
                cancellationToken,
                plannedDestinationPath: plannedSourceCopyPath);
            committedSourceCopyPath = sourceCopyPath;
            _afterDurableCheckpoint?.Invoke("source", sourceCopyPath);

            DocxImportManifest provenance = new DocxImportManifest
            {
                ImportId = preview.ImportId,
                ProjectId = projectId,
                ImportedAtUtc = DateTimeOffset.UtcNow,
                SourceFileName = Path.GetFileName(preview.StagedImport.OriginalSourcePath),
                SourceSha256 = await DocxImportManifestStore.ComputeFileSha256Async(
                    sourceCopyPath, cancellationToken),
                SourceCopyRelativePath = provenanceStore.ToWorkspaceRelativePath(sourceCopyPath),
                ApprovedPlanFingerprint = request.ExpectedPlanFingerprint,
                AppliedMappingProfile = request.AppliedMappingProfile,
                ExcludedSectionIndexes = request.Sections
                    .Where(section => !section.Include)
                    .Select(section => section.SectionIndex)
                    .ToList(),
                Fragments = provenanceFragments
            };

            string provenanceDirectory = Path.GetDirectoryName(
                provenanceStore.GetManifestPath(projectDirectory, preview.ImportId))!;
            if (!Directory.Exists(provenanceDirectory))
            {
                Directory.CreateDirectory(provenanceDirectory);
                createdDirectories.Add(provenanceDirectory);
            }

            await recovery.ExpectProvenanceAsync(
                recoverySession, provenance, cancellationToken);
            provenancePath = await provenanceStore.SaveNewAsync(
                projectDirectory, provenance, cancellationToken);
            _afterDurableCheckpoint?.Invoke("provenance", provenancePath);
            await recovery.CompleteAsync(recoverySession, cancellationToken);

            return new DocxImportCommitResult
            {
                ProjectId = projectId,
                ProjectDirectory = projectDirectory,
                SourceCopyPath = sourceCopyPath,
                GeneratedFiles = generatedFiles,
                ManifestPath = provenancePath
            };
        }
        catch
        {
            if (provenancePath is not null)
            {
                TryDelete(provenancePath);
            }

            if (committedSourceCopyPath is not null &&
                File.Exists(committedSourceCopyPath))
            {
                // Never delete a preserved DOCX edited by another process after
                // our move. A failed import cannot claim modified user data.
                try
                {
                    string actual = await DocxImportManifestStore.ComputeFileSha256Async(
                        committedSourceCopyPath, CancellationToken.None);
                    if (string.Equals(actual, preview.StagedImport.StagedCopySha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(committedSourceCopyPath);
                    }
                }
                catch (IOException)
                {
                    // Ownership could not be proven: keep the copy.
                }
            }

            // Do not remove an external edit made after an import created a
            // Markdown file, even if the transaction itself later fails.
            bool externalMarkdownEdit = false;
            foreach ((string file, string expectedHash) in generatedMarkdownHashes)
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    string actualHash = await DocxImportManifestStore.ComputeFileSha256Async(
                        file, CancellationToken.None);
                    if (!string.Equals(actualHash, expectedHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        externalMarkdownEdit = true;
                    }
                }
                catch
                {
                    // Unable to prove that this file is still ours: keep it.
                    externalMarkdownEdit = true;
                }
            }

            if (createdProject)
            {
                // Never recursively delete a newly created project when an
                // unknown file, junction or modified template is present.
                // Recovery preserves the scaffold for explicit inspection.
                if (!externalMarkdownEdit &&
                    !string.IsNullOrWhiteSpace(projectDirectory) &&
                    Directory.Exists(projectDirectory) &&
                    await IsOriginalImportProjectTreeAsync(projectDirectory,
                        createdProjectScaffoldHashes, expectedOutputs))
                {
                    try
                    {
                        Directory.Delete(projectDirectory, recursive: true);
                    }
                    catch
                    {
                        // Preserve the original import exception.
                    }
                }
            }
            else
            {
                foreach (string file in generatedFiles)
                {
                    // A media file may have been modified just like Markdown.
                    // Remove only an import-owned file whose exact checksum
                    // still matches the prewritten transaction journal.
                    string relative = provenanceStore.ToWorkspaceRelativePath(file);
                    DocxFirstImportFile? owned = expectedOutputs.FirstOrDefault(
                        entry => string.Equals(entry.RelativePath, relative,
                            StringComparison.OrdinalIgnoreCase));
                    if (owned is null || !File.Exists(file))
                    {
                        continue;
                    }

                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                            !string.Equals(
                                await DocxImportManifestStore.ComputeFileSha256Async(
                                    file, CancellationToken.None),
                                owned.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        TryDelete(file);
                    }
                    catch (IOException)
                    {
                        // Another process owns or changed this file: preserve it.
                    }
                }

                foreach (string directory in createdDirectories
                             .OrderByDescending(path =>
                                 path.Length))
                {
                    TryDeleteEmptyDirectory(
                        directory);
                }

                if (!string.IsNullOrWhiteSpace(manifestPath) &&
                    originalManifestBytes is not null &&
                    writtenManifestSha256 is not null)
                {
                    try
                    {
                        // Restore only a manifest still matching our own write.
                        // If an external editor changed it, preserve that edit.
                        if (File.Exists(manifestPath) &&
                            string.Equals(
                                await DocxImportManifestStore.ComputeFileSha256Async(
                                    manifestPath, CancellationToken.None),
                                writtenManifestSha256,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            await AtomicFileWriter.WriteAsync(manifestPath,
                                async (stream, token) =>
                                {
                                    await stream.WriteAsync(
                                        originalManifestBytes, token);
                                }, CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // Preserve the original import exception and recovery data.
                    }
                }
            }

            DiscardStagedCopy(
                preview.StagedImport);

            try
            {
                await recovery.RollbackAsync(
                    recoverySession, CancellationToken.None);
            }
            catch
            {
                // A third-party edit may block automatic recovery. Keep the
                // prepared journal and backups for a later explicit inspection.
            }

            throw;
        }
    }

    /// <summary>
    /// Proves every remaining file in a newly created project belongs to the
    /// unchanged template scaffold or is a checksum-verified import output.
    /// Unexpected user files prevent recursive rollback deletion.
    /// </summary>
    /// <param name="projectDirectory">New project root.</param>
    /// <param name="scaffold">Checksums captured immediately after the project creator returned.</param>
    /// <param name="expectedOutputs">Checksums durably declared by the import plan.</param>
    /// <returns>True only when the entire project tree can be safely removed.</returns>
    private async Task<bool> IsOriginalImportProjectTreeAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string> scaffold,
        IReadOnlyList<DocxFirstImportFile> expectedOutputs)
    {
        try
        {
            if ((File.GetAttributes(projectDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            string projectRoot = Path.GetFullPath(projectDirectory);
            DocxImportManifestStore paths = new DocxImportManifestStore(_workspaceRoot);
            Dictionary<string, string> owned = new Dictionary<string, string>(
                scaffold, StringComparer.OrdinalIgnoreCase);
            foreach (DocxFirstImportFile output in expectedOutputs)
            {
                string full = paths.ResolveWorkspaceRelativePath(output.RelativePath);
                string within = Path.GetRelativePath(projectRoot, full);
                if (Path.IsPathRooted(within) || within == ".." ||
                    within.StartsWith(".." + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                owned.TryAdd(full, output.Sha256);
            }

            foreach (string directory in Directory.EnumerateDirectories(
                         projectRoot, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }

            foreach (string file in Directory.EnumerateFiles(
                         projectRoot, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                    !owned.TryGetValue(file, out string? hash) ||
                    !string.Equals(
                        await DocxImportManifestStore.ComputeFileSha256Async(
                            file, CancellationToken.None),
                        hash, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return scaffold.Count > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
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

        if (new FileInfo(fullSourcePath).Length > MaxStagedDocxBytes)
        {
            throw new InvalidDataException(
                "Le DOCX dépasse la limite de 128 Mio pour une copie temporaire.");
        }

        string stagingDirectory = GetStagingDirectory();
        if (Directory.Exists(stagingDirectory) &&
            (File.GetAttributes(stagingDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "La zone temporaire DOCX ne peut pas être un lien symbolique.");
        }

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

            string stagedSha256 = await DocxImportManifestStore.ComputeFileSha256Async(
                stagedPath, cancellationToken);
            global::Nodalis.Core.Importing.ParsedDocxDocument document = await _parser.ParseAsync(
                stagedPath,
                cancellationToken);
            string parsedSha256 = await DocxImportManifestStore.ComputeFileSha256Async(
                stagedPath, cancellationToken);
            if (!string.Equals(stagedSha256, parsedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "La copie Word a été modifiée pendant l'analyse : import interrompu.");
            }

            return new DocxStagedImport
            {
                OriginalSourcePath = fullSourcePath,
                StagedCopyPath = stagedPath,
                StagedCopySha256 = stagedSha256,
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
    /// Preserves an analyzed Word package without replacing an existing file.
    /// The optional destination pins the exact path displayed in an approved plan.
    /// </summary>
    /// <param name="staged">Verified staged source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="plannedDestinationPath">Exact path chosen at preview time, when known.</param>
    /// <returns>Path of the immutable DOCX source copy.</returns>
    public async Task<string> CommitStagedCopyAsync(
            DocxStagedImport staged,
            CancellationToken cancellationToken = default,
            string? plannedDestinationPath = null)
    {
        ArgumentNullException.ThrowIfNull(staged);
        await ValidateStagedCopyAsync(staged, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        string stagedPath = Path.GetFullPath(staged.StagedCopyPath);

        string sourcesDirectory = Path.Combine(
            _workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName,
            WorkspaceLayout.ImportSourcesDirectoryName);

        Directory.CreateDirectory(
            sourcesDirectory);

        string destination = plannedDestinationPath is null
            ? WindowsPathRules.GetUniqueFilePath(
                sourcesDirectory, Path.GetFileName(staged.OriginalSourcePath))
            : Path.GetFullPath(plannedDestinationPath);

        if (!string.Equals(Path.GetDirectoryName(destination),
                sourcesDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(destination), ".docx",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "La copie source DOCX prévue est extérieure au dossier Imports/Sources.");
        }

        // No overwrite: an interprocess collision must abort the import.
        File.Move(stagedPath, destination);

        return destination;
    }

    /// <summary>Locates exactly one destination already declared in the validated dry-run.</summary>
    /// <param name="plan">Frozen import plan.</param>
    /// <param name="action">Planned action.</param>
    /// <param name="description">Stable description generated with the plan.</param>
    /// <returns>Absolute path of the approved output.</returns>
    private string ResolvePlannedChangePath(DocxImportPlan plan,
        string action, string description)
    {
        DocxImportPlannedChange[] matches = plan.Changes
            .Where(change => string.Equals(change.Action, action,
                StringComparison.Ordinal) &&
                string.Equals(change.Description, description,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                "Plan DOCX ambigu : aucune destination unique pour l'écriture.");
        }

        return ResolveRelativePath(matches[0].RelativePath);
    }

    /// <summary>
    /// Confirms the staged DOCX remains byte-for-byte identical to the analyzed preview.
    /// Rejects deletion, replacement, symlinks and oversized packages before writes.
    /// </summary>
    private async Task ValidateStagedCopyAsync(
        DocxStagedImport staged,
        CancellationToken cancellationToken)
    {
        string stagedPath = Path.GetFullPath(staged.StagedCopyPath);
        EnsureIsStagingPath(stagedPath);
        if (!File.Exists(stagedPath) ||
            (File.GetAttributes(stagedPath) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(stagedPath).Length > MaxStagedDocxBytes ||
            staged.StagedCopySha256.Length != 64)
        {
            throw new InvalidDataException(
                "La copie DOCX de prévisualisation n'est plus disponible ou sûre.");
        }

        string currentHash = await DocxImportManifestStore.ComputeFileSha256Async(
            stagedPath, cancellationToken);
        if (!string.Equals(currentHash, staged.StagedCopySha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le DOCX a changé depuis la prévisualisation : relancez l'analyse.");
        }
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
                        row.Cells.Sum(cell => cell.ColumnSpan));

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

        foreach (DocxImportSectionPreview section in sections)
        {
            foreach (DocxImportBlockPreview block in section.Blocks)
            {
                if (block.Block.Table is not DocxTable table)
                {
                    continue;
                }

                foreach (string warning in table.Warnings)
                {
                    warnings.Add("Tableau Word #" + block.BlockIndex +
                        " (« " + section.SourceHeading + " ») : " + warning);
                }
            }
        }

        return warnings.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
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
    /// Prevents importing into a closed project or a folder whose manifest
    /// no longer matches the selected project and application identities.
    /// </summary>
    /// <param name="project">Manifest freshly loaded from disk.</param>
    /// <param name="requestedProjectId">Project ID selected in the Word assistant.</param>
    /// <param name="requestedApplicationId">Owning application selected in the assistant.</param>
    private static void ValidateActiveImportProject(ProjectManifest project,
        Guid requestedProjectId, Guid requestedApplicationId)
    {
        if (project.Id != requestedProjectId ||
            project.ApplicationId != requestedApplicationId)
        {
            throw new InvalidDataException(
                "Le manifeste du projet cible ne correspond plus à la sélection Word. " +
                "Recalculez la prévisualisation avant d'importer.");
        }

        if (project.ArchivedUtc.HasValue ||
            project.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Ce projet est clos et archivé : réouvrez-le avant d'importer un DOCX.");
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

        byte[] buffer = new byte[81920];
        long copied = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            copied += count;
            if (copied > MaxStagedDocxBytes)
            {
                throw new InvalidDataException(
                    "Le DOCX source a dépassé 128 Mio pendant la copie.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
    }

    private sealed record SelectedSection(
        DocxImportSectionPreview Preview,
        string TargetSection,
        IReadOnlySet<int>? SelectedBlockIndexes);
}
