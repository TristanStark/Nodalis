using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Analyzes, plans, and commits read-only-source bulk Markdown imports.
/// </summary>
public sealed class WorkspaceMarkdownBulkImportService
{
    private static readonly Regex MarkdownLinkPattern =
        new Regex(
            @"!?\[[^\]]*\]\((?<target>[^)\r\n]+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a bulk Markdown import service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceMarkdownBulkImportService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
    }

    /// <summary>
    /// Analyzes every Markdown file below a source directory and discovers referenced attachments.
    /// </summary>
    /// <param name="sourceDirectory">The source directory to inspect recursively.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only import preview.</returns>
    public async Task<MarkdownBulkImportPreview> PrepareFolderPreviewAsync(
            string sourceDirectory,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceDirectory);

        string sourceRoot =
            Path.GetFullPath(
                sourceDirectory);

        if (!Directory.Exists(
                sourceRoot))
        {
            throw new DirectoryNotFoundException(
                "Le dossier Markdown source est introuvable.");
        }

        string[] markdownFiles =
            await Task.Run(
                () =>
                    Directory
                        .EnumerateFiles(
                            sourceRoot,
                            "*.md",
                            SearchOption.AllDirectories)
                        .OrderBy(
                            path => path,
                            StringComparer.CurrentCultureIgnoreCase)
                        .ToArray(),
                cancellationToken);

        if (markdownFiles.Length == 0)
        {
            throw new InvalidDataException(
                "Le dossier sélectionné ne contient aucun fichier Markdown.");
        }

        string displayName =
            Path.GetFileName(
                sourceRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

        return await PreparePreviewAsync(
            sourceRoot,
            string.IsNullOrWhiteSpace(
                    displayName)
                ? "Markdown"
                : displayName,
            markdownFiles,
            cancellationToken);
    }

    /// <summary>
    /// Analyzes explicitly selected Markdown files and discovers referenced attachments that stay below their common source root.
    /// </summary>
    /// <param name="sourceFiles">The selected Markdown source files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only import preview.</returns>
    public async Task<MarkdownBulkImportPreview> PrepareFilesPreviewAsync(
            IReadOnlyCollection<string> sourceFiles,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            sourceFiles);

        string[] markdownFiles =
            sourceFiles
                .Where(path =>
                    !string.IsNullOrWhiteSpace(
                        path))
                .Select(
                    Path.GetFullPath)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    path => path,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        if (markdownFiles.Length == 0)
        {
            throw new InvalidDataException(
                "Sélectionnez au moins un fichier Markdown.");
        }

        foreach (string markdownFile in markdownFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(
                    markdownFile))
            {
                throw new FileNotFoundException(
                    "Un fichier Markdown sélectionné est introuvable.",
                    markdownFile);
            }

            if (!string.Equals(
                    Path.GetExtension(
                        markdownFile),
                    ".md",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "La sélection contient un fichier qui n'est pas du Markdown : " +
                    markdownFile);
            }
        }

        string sourceRoot =
            GetCommonDirectory(
                markdownFiles);

        string displayName =
            markdownFiles.Length == 1
                ? Path.GetFileNameWithoutExtension(
                    markdownFiles[0])
                : Path.GetFileName(
                      sourceRoot.TrimEnd(
                          Path.DirectorySeparatorChar,
                          Path.AltDirectorySeparatorChar)) +
                  " - sélection";

        return await PreparePreviewAsync(
            sourceRoot,
            displayName,
            markdownFiles,
            cancellationToken);
    }

    /// <summary>
    /// Builds a deterministic write plan without changing either the source or the workspace.
    /// </summary>
    /// <param name="preview">The source preview.</param>
    /// <param name="request">The selected target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete write plan.</returns>
    public async Task<MarkdownBulkImportPlan> BuildPlanAsync(
            MarkdownBulkImportPreview preview,
            MarkdownBulkImportRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preview);
        ArgumentNullException.ThrowIfNull(
            request);

        MarkdownBulkImportTargetOption projectTarget =
            preview.Projects.SingleOrDefault(project =>
                project.Id ==
                request.ProjectId)
            ?? throw new InvalidDataException(
                "Le projet cible n'est plus disponible.");

        if (projectTarget.ApplicationId !=
            request.ApplicationId)
        {
            throw new InvalidDataException(
                "Le projet choisi n'appartient pas à l'application sélectionnée.");
        }

        string targetSection =
            request.TargetSection.Trim();

        if (string.IsNullOrWhiteSpace(
                targetSection))
        {
            throw new InvalidDataException(
                "La section cible ne peut pas être vide.");
        }

        string projectDirectory =
            ResolveWorkspaceRelativePath(
                projectTarget.RelativePath);

        string manifestPath =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

        ProjectManifest project =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                manifestPath,
                cancellationToken);

        SectionManifest? existingSection =
            project.Sections.FirstOrDefault(section =>
                string.Equals(
                    section.Name,
                    targetSection,
                    StringComparison.CurrentCultureIgnoreCase));

        string effectiveSectionName =
            existingSection?.Name ??
            targetSection;

        string sectionDirectory =
            Path.Combine(
                projectDirectory,
                WindowsPathRules.SanitizeSegment(
                    effectiveSectionName));

        string desiredImportDirectoryName =
            WindowsPathRules.SanitizeSegment(
                "Import - " +
                preview.SourceDisplayName);

        string importDirectory =
            WindowsPathRules.GetUniqueDirectoryPath(
                sectionDirectory,
                desiredImportDirectoryName);

        List<string> warnings =
            preview.Warnings.ToList();

        if (!string.Equals(
                Path.GetFileName(
                    importDirectory),
                desiredImportDirectoryName,
                StringComparison.CurrentCultureIgnoreCase))
        {
            warnings.Add(
                "Une importation portant le même nom existe déjà dans cette section ; " +
                "le nouvel import sera créé dans « " +
                Path.GetFileName(
                    importDirectory) +
                " ».");
        }

        Dictionary<string, string> duplicateMarkdown =
            await FindExistingMarkdownHashesAsync(
                projectDirectory,
                preview.Items,
                cancellationToken);

        foreach (MarkdownBulkImportSourceItem markdown in preview.Items.Where(item =>
                     item.Kind ==
                     MarkdownBulkImportItemKind.Markdown))
        {
            if (duplicateMarkdown.TryGetValue(
                    markdown.Sha256,
                    out string? existingRelativePath))
            {
                warnings.Add(
                    "Doublon de contenu détecté : « " +
                    markdown.RelativePath +
                    " » est identique à « " +
                    existingRelativePath +
                    " » dans le projet cible.");
            }
        }

        List<MarkdownBulkImportPlannedChange> changes =
            new List<MarkdownBulkImportPlannedChange>();

        if (existingSection is null)
        {
            changes.Add(
                new MarkdownBulkImportPlannedChange
                {
                    Operation =
                        "Modifier",
                    SourceRelativePath =
                        "—",
                    DestinationRelativePath =
                        ToWorkspaceRelativePath(
                            manifestPath),
                    Detail =
                        "Ajouter la section « " +
                        effectiveSectionName +
                        " » au manifest du projet."
                });
        }

        foreach (MarkdownBulkImportSourceItem item in preview.Items.OrderBy(item =>
                     item.RelativePath,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            string destination =
                Path.GetFullPath(
                    Path.Combine(
                        importDirectory,
                        item.RelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));

            if (!IsPathWithin(
                    destination,
                    importDirectory))
            {
                throw new InvalidDataException(
                    "Un chemin source sortirait du dossier d'import : " +
                    item.RelativePath);
            }

            changes.Add(
                new MarkdownBulkImportPlannedChange
                {
                    Operation =
                        "Copier",
                    SourceRelativePath =
                        item.RelativePath,
                    DestinationRelativePath =
                        ToWorkspaceRelativePath(
                            destination),
                    Detail =
                        item.Kind ==
                        MarkdownBulkImportItemKind.Markdown
                            ? "Document Markdown ; arborescence et liens relatifs conservés."
                            : "Pièce jointe référencée par le Markdown."
                });
        }

        return new MarkdownBulkImportPlan
        {
            TargetProjectDisplayName =
                projectTarget.QualifiedName,
            TargetProjectRelativePath =
                projectTarget.RelativePath,
            ImportDirectoryRelativePath =
                ToWorkspaceRelativePath(
                    importDirectory),
            CreatesSection =
                existingSection is null,
            Changes =
                changes,
            Warnings =
                warnings
                    .Distinct(
                        StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(
                        warning => warning,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
        };
    }

    /// <summary>
    /// Commits a previously previewed bulk Markdown import after validating the source files again.
    /// </summary>
    /// <param name="preview">The source preview.</param>
    /// <param name="request">The selected target.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed import result.</returns>
    public async Task<MarkdownBulkImportCommitResult> CommitAsync(
            MarkdownBulkImportPreview preview,
            MarkdownBulkImportRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preview);
        ArgumentNullException.ThrowIfNull(
            request);

        MarkdownBulkImportPlan plan =
            await BuildPlanAsync(
                preview,
                request,
                cancellationToken);

        string projectDirectory =
            ResolveWorkspaceRelativePath(
                plan.TargetProjectRelativePath);
        string importDirectory =
            ResolveWorkspaceRelativePath(
                plan.ImportDirectoryRelativePath);
        string sectionDirectory =
            Path.GetDirectoryName(
                importDirectory)
            ?? throw new InvalidDataException(
                "Le dossier de section cible est invalide.");

        string manifestPath =
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName);

        string stagingRoot =
            Path.Combine(
                _workspaceRoot,
                WorkspaceLayout.ImportsDirectoryName,
                WorkspaceLayout.ImportStagingDirectoryName,
                "markdown-" +
                Guid.NewGuid().ToString(
                    "N"));
        string stagingPayload =
            Path.Combine(
                stagingRoot,
                "payload");

        string? originalManifestContent =
            null;
        bool manifestChanged =
            false;
        bool payloadMoved =
            false;
        bool sectionDirectoryCreated =
            false;

        try
        {
            Directory.CreateDirectory(
                stagingPayload);

            foreach (MarkdownBulkImportSourceItem item in preview.Items.OrderBy(item =>
                         item.RelativePath,
                         StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(
                        item.SourcePath))
                {
                    throw new FileNotFoundException(
                        "Un fichier source a disparu depuis la prévisualisation.",
                        item.SourcePath);
                }

                string currentHash =
                    await ComputeSha256Async(
                        item.SourcePath,
                        cancellationToken);

                if (!string.Equals(
                        currentHash,
                        item.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException(
                        "Le fichier source a changé depuis la prévisualisation : " +
                        item.SourcePath);
                }

                string stagedPath =
                    Path.GetFullPath(
                        Path.Combine(
                            stagingPayload,
                            item.RelativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));

                if (!IsPathWithin(
                        stagedPath,
                        stagingPayload))
                {
                    throw new InvalidDataException(
                        "Un chemin source sortirait du dossier de staging : " +
                        item.RelativePath);
                }

                string? stagedDirectory =
                    Path.GetDirectoryName(
                        stagedPath);

                if (!string.IsNullOrWhiteSpace(
                        stagedDirectory))
                {
                    Directory.CreateDirectory(
                        stagedDirectory);
                }

                await CopyFileAsync(
                    item.SourcePath,
                    stagedPath,
                    cancellationToken);
            }

            if (Directory.Exists(
                    importDirectory) ||
                File.Exists(
                    importDirectory))
            {
                throw new IOException(
                    "La destination calculée existe déjà. Recalculez le plan d'import.");
            }

            if (!Directory.Exists(
                    sectionDirectory))
            {
                Directory.CreateDirectory(
                    sectionDirectory);
                sectionDirectoryCreated =
                    true;
            }

            Directory.Move(
                stagingPayload,
                importDirectory);
            payloadMoved =
                true;

            if (plan.CreatesSection)
            {
                originalManifestContent =
                    await File.ReadAllTextAsync(
                        manifestPath,
                        cancellationToken);

                ProjectManifest project =
                    await AtomicJsonFile.ReadAsync<ProjectManifest>(
                        manifestPath,
                        cancellationToken);

                bool sectionAlreadyExists =
                    project.Sections.Any(section =>
                        string.Equals(
                            section.Name,
                            request.TargetSection.Trim(),
                            StringComparison.CurrentCultureIgnoreCase));

                if (!sectionAlreadyExists)
                {
                    int nextOrder =
                        project.Sections.Count == 0
                            ? 10
                            : project.Sections.Max(section =>
                                  section.Order) +
                              10;

                    List<SectionManifest> sections =
                        project.Sections.ToList();

                    sections.Add(
                        new SectionManifest
                        {
                            Id =
                                Guid.NewGuid(),
                            Name =
                                request.TargetSection.Trim(),
                            Order =
                                nextOrder,
                            IsSingleton =
                                false,
                            TemplateKey =
                                null
                        });

                    ProjectManifest updated =
                        project with
                        {
                            Sections =
                                sections
                        };

                    await AtomicJsonFile.WriteAsync(
                        manifestPath,
                        updated,
                        cancellationToken);

                    manifestChanged =
                        true;
                }
            }

            List<string> importedFiles =
                preview.Items
                    .OrderBy(item =>
                        item.RelativePath,
                        StringComparer.CurrentCultureIgnoreCase)
                    .Select(item =>
                        Path.Combine(
                            importDirectory,
                            item.RelativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar)))
                    .ToList();

            return new MarkdownBulkImportCommitResult
            {
                ProjectDirectory =
                    projectDirectory,
                ImportDirectory =
                    importDirectory,
                ImportedFiles =
                    importedFiles
            };
        }
        catch
        {
            if (manifestChanged &&
                originalManifestContent is not null)
            {
                try
                {
                    await AtomicFileWriter.WriteAllTextAsync(
                        manifestPath,
                        originalManifestContent,
                        CancellationToken.None);
                }
                catch
                {
                }
            }

            if (payloadMoved &&
                Directory.Exists(
                    importDirectory))
            {
                try
                {
                    Directory.Delete(
                        importDirectory,
                        recursive: true);
                }
                catch
                {
                }
            }

            if (sectionDirectoryCreated &&
                Directory.Exists(
                    sectionDirectory) &&
                !Directory.EnumerateFileSystemEntries(
                        sectionDirectory)
                    .Any())
            {
                try
                {
                    Directory.Delete(
                        sectionDirectory);
                }
                catch
                {
                }
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(
                    stagingRoot))
            {
                try
                {
                    Directory.Delete(
                        stagingRoot,
                        recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// Performs the common read-only source analysis used by folder and explicit-file imports.
    /// </summary>
    /// <param name="sourceRoot">The common source root.</param>
    /// <param name="sourceDisplayName">The readable source name.</param>
    /// <param name="markdownFiles">The Markdown files selected for import.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source preview.</returns>
    private async Task<MarkdownBulkImportPreview> PreparePreviewAsync(
            string sourceRoot,
            string sourceDisplayName,
            IReadOnlyCollection<string> markdownFiles,
            CancellationToken cancellationToken)
    {
        HashSet<string> selectedMarkdown =
            new HashSet<string>(
                markdownFiles.Select(
                    Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> attachmentPaths =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        List<string> warnings =
            new List<string>();
        int relativeLinkCount =
            0;

        foreach (string markdownFile in selectedMarkdown.OrderBy(path =>
                     path,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content =
                await File.ReadAllTextAsync(
                    markdownFile,
                    cancellationToken);

            foreach (Match match in MarkdownLinkPattern.Matches(
                         content))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string rawTarget =
                    match.Groups["target"].Value.Trim();
                string? localTarget =
                    NormalizeLocalLinkTarget(
                        rawTarget);

                if (localTarget is null)
                {
                    continue;
                }

                relativeLinkCount++;

                string? resolvedPath =
                    TryResolveRelativeLink(
                        markdownFile,
                        localTarget);

                if (resolvedPath is null)
                {
                    warnings.Add(
                        "Lien relatif invalide dans « " +
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                sourceRoot,
                                markdownFile)) +
                        " » : " +
                        rawTarget);
                    continue;
                }

                if (!IsPathWithin(
                        resolvedPath,
                        sourceRoot))
                {
                    warnings.Add(
                        "Lien relatif hors du périmètre source, non copié : " +
                        rawTarget +
                        " depuis « " +
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                sourceRoot,
                                markdownFile)) +
                        " ».");
                    continue;
                }

                if (Directory.Exists(
                        resolvedPath))
                {
                    warnings.Add(
                        "Lien vers un dossier détecté ; le dossier n'est pas copié automatiquement : " +
                        rawTarget);
                    continue;
                }

                if (!File.Exists(
                        resolvedPath))
                {
                    warnings.Add(
                        "Lien relatif cassé détecté : " +
                        rawTarget +
                        " depuis « " +
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                sourceRoot,
                                markdownFile)) +
                        " ».");
                    continue;
                }

                if (string.Equals(
                        Path.GetExtension(
                            resolvedPath),
                        ".md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!selectedMarkdown.Contains(
                            resolvedPath))
                    {
                        warnings.Add(
                            "Le document lié « " +
                            NormalizeRelativePath(
                                Path.GetRelativePath(
                                    sourceRoot,
                                    resolvedPath)) +
                            " » n'est pas sélectionné pour l'import.");
                    }

                    continue;
                }

                attachmentPaths.Add(
                    resolvedPath);
            }
        }

        List<MarkdownBulkImportSourceItem> items =
            new List<MarkdownBulkImportSourceItem>();

        foreach (string markdownFile in selectedMarkdown.OrderBy(path =>
                     path,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(
                await CreateSourceItemAsync(
                    sourceRoot,
                    markdownFile,
                    MarkdownBulkImportItemKind.Markdown,
                    cancellationToken));
        }

        foreach (string attachmentPath in attachmentPaths.OrderBy(path =>
                     path,
                     StringComparer.CurrentCultureIgnoreCase))
        {
            items.Add(
                await CreateSourceItemAsync(
                    sourceRoot,
                    attachmentPath,
                    MarkdownBulkImportItemKind.Attachment,
                    cancellationToken));
        }

        TargetCatalog targets =
            await LoadTargetsAsync(
                cancellationToken);

        MarkdownBulkImportTargetOption? suggestedProject =
            FindSuggestedTarget(
                targets.Projects,
                sourceDisplayName,
                markdownFiles);

        Guid? suggestedApplicationId =
            suggestedProject?.ApplicationId;

        if (suggestedApplicationId is null)
        {
            MarkdownBulkImportTargetOption? suggestedApplication =
                FindSuggestedTarget(
                    targets.Applications,
                    sourceDisplayName,
                    markdownFiles);

            suggestedApplicationId =
                suggestedApplication?.Id;
        }

        if (suggestedApplicationId is null &&
            targets.Applications.Count ==
            1)
        {
            suggestedApplicationId =
                targets.Applications[0].Id;
        }

        if (suggestedProject is null &&
            suggestedApplicationId is Guid applicationId)
        {
            MarkdownBulkImportTargetOption[] applicationProjects =
                targets.Projects
                    .Where(project =>
                        project.ApplicationId ==
                        applicationId)
                    .ToArray();

            if (applicationProjects.Length ==
                1)
            {
                suggestedProject =
                    applicationProjects[0];
            }
        }

        return new MarkdownBulkImportPreview
        {
            SourceRoot =
                sourceRoot,
            SourceDisplayName =
                sourceDisplayName,
            Items =
                items
                    .OrderBy(item =>
                        item.RelativePath,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            Warnings =
                warnings
                    .Distinct(
                        StringComparer.CurrentCultureIgnoreCase)
                    .OrderBy(
                        warning => warning,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToList(),
            Applications =
                targets.Applications,
            Projects =
                targets.Projects,
            SuggestedApplicationId =
                suggestedApplicationId,
            SuggestedProjectId =
                suggestedProject?.Id,
            SuggestedSection =
                SuggestSection(
                    sourceRoot,
                    markdownFiles),
            RelativeLinkCount =
                relativeLinkCount
        };
    }

    /// <summary>
    /// Creates one immutable source-item snapshot, including a content hash used to detect changes before commit.
    /// </summary>
    /// <param name="sourceRoot">The common source root.</param>
    /// <param name="sourcePath">The source file path.</param>
    /// <param name="kind">The source item kind.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source item.</returns>
    private static async Task<MarkdownBulkImportSourceItem> CreateSourceItemAsync(
            string sourceRoot,
            string sourcePath,
            MarkdownBulkImportItemKind kind,
            CancellationToken cancellationToken)
    {
        string fullSourcePath =
            Path.GetFullPath(
                sourcePath);

        if (!IsPathWithin(
                fullSourcePath,
                sourceRoot))
        {
            throw new InvalidDataException(
                "Un fichier source se trouve hors du périmètre d'import : " +
                fullSourcePath);
        }

        FileInfo file =
            new FileInfo(
                fullSourcePath);

        return new MarkdownBulkImportSourceItem
        {
            SourcePath =
                fullSourcePath,
            RelativePath =
                NormalizeRelativePath(
                    Path.GetRelativePath(
                        sourceRoot,
                        fullSourcePath)),
            Kind =
                kind,
            Length =
                file.Length,
            Sha256 =
                await ComputeSha256Async(
                    fullSourcePath,
                    cancellationToken)
        };
    }

    /// <summary>
    /// Loads application and project targets directly from manifests without changing workspace indexes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The target catalog.</returns>
    private async Task<TargetCatalog> LoadTargetsAsync(
            CancellationToken cancellationToken)
    {
        string applicationsRoot =
            Path.Combine(
                _workspaceRoot,
                WorkspaceLayout.ApplicationsDirectoryName);

        List<MarkdownBulkImportTargetOption> applications =
            new List<MarkdownBulkImportTargetOption>();
        List<MarkdownBulkImportTargetOption> projects =
            new List<MarkdownBulkImportTargetOption>();

        if (!Directory.Exists(
                applicationsRoot))
        {
            return new TargetCatalog(
                applications,
                projects);
        }

        foreach (string applicationDirectory in Directory
                     .EnumerateDirectories(
                         applicationsRoot)
                     .OrderBy(
                         path => path,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string applicationManifestPath =
                Path.Combine(
                    applicationDirectory,
                    WorkspaceLayout.ApplicationManifestFileName);

            if (!File.Exists(
                    applicationManifestPath))
            {
                continue;
            }

            ApplicationManifest application =
                await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                    applicationManifestPath,
                    cancellationToken);

            applications.Add(
                new MarkdownBulkImportTargetOption
                {
                    Id =
                        application.Id,
                    DisplayName =
                        application.Name,
                    QualifiedName =
                        application.Name,
                    RelativePath =
                        ToWorkspaceRelativePath(
                            applicationDirectory),
                    ApplicationId =
                        application.Id
                });

            foreach (string projectManifestPath in Directory
                         .EnumerateFiles(
                             applicationDirectory,
                             WorkspaceLayout.ProjectManifestFileName,
                             SearchOption.AllDirectories)
                         .OrderBy(
                             path => path,
                             StringComparer.CurrentCultureIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                ProjectManifest project =
                    await AtomicJsonFile.ReadAsync<ProjectManifest>(
                        projectManifestPath,
                        cancellationToken);

                if (project.ApplicationId !=
                    application.Id)
                {
                    continue;
                }

                string projectDirectory =
                    Path.GetDirectoryName(
                        projectManifestPath)
                    ?? throw new InvalidDataException(
                        "Le manifest projet n'a pas de dossier parent.");

                projects.Add(
                    new MarkdownBulkImportTargetOption
                    {
                        Id =
                            project.Id,
                        DisplayName =
                            project.Name,
                        QualifiedName =
                            application.Name +
                            " / " +
                            project.Name,
                        RelativePath =
                            ToWorkspaceRelativePath(
                                projectDirectory),
                        ApplicationId =
                            application.Id
                    });
            }
        }

        return new TargetCatalog(
            applications
                .OrderBy(
                    target => target.QualifiedName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            projects
                .OrderBy(
                    target => target.QualifiedName,
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList());
    }

    /// <summary>
    /// Finds a unique application or project whose name matches the source context.
    /// </summary>
    /// <param name="targets">The candidate targets.</param>
    /// <param name="sourceDisplayName">The source display name.</param>
    /// <param name="markdownFiles">The selected Markdown files.</param>
    /// <returns>The suggested target, or <see langword="null"/> when no unique match exists.</returns>
    private static MarkdownBulkImportTargetOption? FindSuggestedTarget(
            IReadOnlyCollection<MarkdownBulkImportTargetOption> targets,
            string sourceDisplayName,
            IReadOnlyCollection<string> markdownFiles)
    {
        HashSet<string> candidateNames =
            new HashSet<string>(
                StringComparer.CurrentCultureIgnoreCase)
                {
                    sourceDisplayName.Trim()
                };

        foreach (string markdownFile in markdownFiles)
        {
            candidateNames.Add(
                Path.GetFileNameWithoutExtension(
                    markdownFile));
        }

        MarkdownBulkImportTargetOption[] exact =
            targets
                .Where(target =>
                    candidateNames.Contains(
                        target.DisplayName))
                .ToArray();

        if (exact.Length ==
            1)
        {
            return exact[0];
        }

        MarkdownBulkImportTargetOption[] partial =
            targets
                .Where(target =>
                    candidateNames.Any(candidate =>
                        candidate.Length >=
                            3 &&
                        (target.DisplayName.Contains(
                             candidate,
                             StringComparison.CurrentCultureIgnoreCase) ||
                         candidate.Contains(
                             target.DisplayName,
                             StringComparison.CurrentCultureIgnoreCase))))
                .ToArray();

        return partial.Length ==
               1
            ? partial[0]
            : null;
    }

    /// <summary>
    /// Suggests a conventional Nodalis section from the imported source hierarchy.
    /// </summary>
    /// <param name="sourceRoot">The common source root.</param>
    /// <param name="markdownFiles">The selected Markdown files.</param>
    /// <returns>The suggested section name.</returns>
    private static string SuggestSection(
            string sourceRoot,
            IReadOnlyCollection<string> markdownFiles)
    {
        string rootName =
            Path.GetFileName(
                sourceRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));

        string? conventionalRoot =
            NormalizeConventionalSection(
                rootName);

        if (conventionalRoot is not null)
        {
            return conventionalRoot;
        }

        string[] firstSegments =
            markdownFiles
                .Select(path =>
                    NormalizeRelativePath(
                        Path.GetRelativePath(
                            sourceRoot,
                            path)))
                .Select(path =>
                    path.Split(
                        '/',
                        StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault())
                .Where(segment =>
                    !string.IsNullOrWhiteSpace(
                        segment))
                .Select(segment =>
                    segment!)
                .Distinct(
                    StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

        if (firstSegments.Length ==
            1)
        {
            string? conventionalSegment =
                NormalizeConventionalSection(
                    firstSegments[0]);

            if (conventionalSegment is not null)
            {
                return conventionalSegment;
            }
        }

        return "Technique";
    }

    /// <summary>
    /// Normalizes a conventional section name when one is recognized.
    /// </summary>
    /// <param name="candidate">The candidate folder or file name.</param>
    /// <returns>The canonical section name, or <see langword="null"/> when no convention matches.</returns>
    private static string? NormalizeConventionalSection(
            string? candidate)
    {
        if (string.IsNullOrWhiteSpace(
                candidate))
        {
            return null;
        }

        string stem =
            Path.GetFileNameWithoutExtension(
                candidate.Trim());

        if (string.Equals(
                stem,
                "Jalons",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Jalons";
        }

        if (string.Equals(
                stem,
                "Technique",
                StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(
                stem,
                "Technical",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Technique";
        }

        if (string.Equals(
                stem,
                "Glossaire",
                StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(
                stem,
                "Glossary",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Glossaire";
        }

        if (string.Equals(
                stem,
                "Tests",
                StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(
                stem,
                "Test",
                StringComparison.CurrentCultureIgnoreCase))
        {
            return "Tests";
        }

        return null;
    }

    /// <summary>
    /// Detects identical Markdown already present in the target project.
    /// </summary>
    /// <param name="projectDirectory">The target project directory.</param>
    /// <param name="sourceItems">The source items.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A map from source hash to the first matching target document.</returns>
    private static async Task<Dictionary<string, string>> FindExistingMarkdownHashesAsync(
            string projectDirectory,
            IReadOnlyCollection<MarkdownBulkImportSourceItem> sourceItems,
            CancellationToken cancellationToken)
    {
        HashSet<string> sourceHashes =
            sourceItems
                .Where(item =>
                    item.Kind ==
                    MarkdownBulkImportItemKind.Markdown)
                .Select(item =>
                    item.Sha256)
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> matches =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (sourceHashes.Count ==
            0 ||
            !Directory.Exists(
                projectDirectory))
        {
            return matches;
        }

        foreach (string existingFile in Directory
                     .EnumerateFiles(
                         projectDirectory,
                         "*.md",
                         SearchOption.AllDirectories)
                     .OrderBy(
                         path => path,
                         StringComparer.CurrentCultureIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string hash =
                await ComputeSha256Async(
                    existingFile,
                    cancellationToken);

            if (!sourceHashes.Contains(
                    hash) ||
                matches.ContainsKey(
                    hash))
            {
                continue;
            }

            matches[hash] =
                NormalizeRelativePath(
                    Path.GetRelativePath(
                        projectDirectory,
                        existingFile));

            if (matches.Count ==
                sourceHashes.Count)
            {
                break;
            }
        }

        return matches;
    }

    /// <summary>
    /// Converts a Markdown link destination into a local relative path when appropriate.
    /// </summary>
    /// <param name="rawTarget">The raw link target captured from Markdown.</param>
    /// <returns>The local relative path, or <see langword="null"/> for non-local links and anchors.</returns>
    private static string? NormalizeLocalLinkTarget(
            string rawTarget)
    {
        string target =
            rawTarget.Trim();

        if (target.Length ==
            0 ||
            target.StartsWith(
                "#",
                StringComparison.Ordinal))
        {
            return null;
        }

        if (target.StartsWith(
                "<",
                StringComparison.Ordinal))
        {
            int closing =
                target.IndexOf(
                    '>');

            if (closing >
                1)
            {
                target =
                    target[1..closing];
            }
        }
        else
        {
            string[] titleSeparators =
            [
                " \"",
                " '",
                " ("
            ];

            foreach (string separator in titleSeparators)
            {
                int titleIndex =
                    target.IndexOf(
                        separator,
                        StringComparison.Ordinal);

                if (titleIndex >
                    0)
                {
                    target =
                        target[..titleIndex];
                    break;
                }
            }
        }

        if (Uri.TryCreate(
                target,
                UriKind.Absolute,
                out Uri? absoluteUri) &&
            absoluteUri.IsAbsoluteUri)
        {
            return null;
        }

        int fragmentIndex =
            target.IndexOfAny(
                ['#', '?']);

        if (fragmentIndex >=
            0)
        {
            target =
                target[..fragmentIndex];
        }

        if (string.IsNullOrWhiteSpace(
                target))
        {
            return null;
        }

        try
        {
            return Uri.UnescapeDataString(
                target)
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar);
        }
        catch (UriFormatException)
        {
            return target.Replace(
                '/',
                Path.DirectorySeparatorChar);
        }
    }

    /// <summary>
    /// Resolves one local Markdown link against its source document.
    /// </summary>
    /// <param name="markdownFile">The Markdown source path.</param>
    /// <param name="localTarget">The normalized relative link target.</param>
    /// <returns>The resolved full path, or <see langword="null"/> when the path is invalid.</returns>
    private static string? TryResolveRelativeLink(
            string markdownFile,
            string localTarget)
    {
        try
        {
            string? sourceDirectory =
                Path.GetDirectoryName(
                    markdownFile);

            if (string.IsNullOrWhiteSpace(
                    sourceDirectory))
            {
                return null;
            }

            return Path.GetFullPath(
                Path.Combine(
                    sourceDirectory,
                    localTarget));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Computes the common containing directory of explicitly selected source files.
    /// </summary>
    /// <param name="files">The selected full paths.</param>
    /// <returns>The common containing directory.</returns>
    private static string GetCommonDirectory(
            IReadOnlyList<string> files)
    {
        string? common =
            Path.GetDirectoryName(
                files[0]);

        while (!string.IsNullOrWhiteSpace(
                   common))
        {
            bool containsAll =
                files.All(file =>
                    IsPathWithin(
                        file,
                        common));

            if (containsAll)
            {
                return common;
            }

            common =
                Path.GetDirectoryName(
                    common);
        }

        throw new InvalidDataException(
            "Les fichiers sélectionnés n'ont pas de dossier source commun.");
    }

    /// <summary>
    /// Computes a stable SHA-256 hash for one source or target file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The lowercase hexadecimal SHA-256 value.</returns>
    private static async Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken)
    {
        await using FileStream stream =
            new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                options: FileOptions.Asynchronous |
                         FileOptions.SequentialScan);

        using SHA256 sha256 =
            SHA256.Create();

        byte[] hash =
            await sha256.ComputeHashAsync(
                stream,
                cancellationToken);

        return Convert
            .ToHexString(
                hash)
            .ToLowerInvariant();
    }

    /// <summary>
    /// Copies a source file asynchronously into staging without modifying the source.
    /// </summary>
    /// <param name="sourcePath">The source path.</param>
    /// <param name="destinationPath">The staging destination path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the copy.</returns>
    private static async Task CopyFileAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken)
    {
        await using FileStream input =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                options: FileOptions.Asynchronous |
                         FileOptions.SequentialScan);

        await using FileStream output =
            new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                options: FileOptions.Asynchronous |
                         FileOptions.SequentialScan);

        await input.CopyToAsync(
            output,
            81920,
            cancellationToken);
        await output.FlushAsync(
            cancellationToken);
    }

    /// <summary>
    /// Resolves a workspace-relative path and rejects paths that escape the workspace.
    /// </summary>
    /// <param name="relativePath">The workspace-relative path.</param>
    /// <returns>The full path.</returns>
    private string ResolveWorkspaceRelativePath(
            string relativePath)
    {
        string resolved =
            Path.GetFullPath(
                Path.Combine(
                    _workspaceRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

        if (!IsPathWithin(
                resolved,
                _workspaceRoot))
        {
            throw new InvalidDataException(
                "Un chemin cible sort du workspace.");
        }

        return resolved;
    }

    /// <summary>
    /// Converts a full path to a normalized workspace-relative path.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns>The normalized relative path.</returns>
    private string ToWorkspaceRelativePath(
            string path)
    {
        string fullPath =
            Path.GetFullPath(
                path);

        if (!IsPathWithin(
                fullPath,
                _workspaceRoot))
        {
            throw new InvalidDataException(
                "Le chemin ne se trouve pas dans le workspace.");
        }

        return NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath));
    }

    /// <summary>
    /// Determines whether a path is equal to or nested below a containing directory.
    /// </summary>
    /// <param name="path">The candidate path.</param>
    /// <param name="directory">The containing directory.</param>
    /// <returns><see langword="true"/> when the candidate stays inside the directory.</returns>
    private static bool IsPathWithin(
            string path,
            string directory)
    {
        string fullPath =
            Path.GetFullPath(
                path)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        string fullDirectory =
            Path.GetFullPath(
                directory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullPath,
                   fullDirectory,
                   StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(
                   fullDirectory +
                   Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a relative path for portable display and persisted planning.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The slash-separated path.</returns>
    private static string NormalizeRelativePath(
            string path) =>
            path.Replace(
                Path.DirectorySeparatorChar,
                '/')
                .Replace(
                    Path.AltDirectorySeparatorChar,
                    '/');

    private sealed record TargetCatalog(
        List<MarkdownBulkImportTargetOption> Applications,
        List<MarkdownBulkImportTargetOption> Projects);
}
