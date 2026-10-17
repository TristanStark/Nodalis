using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nodalis.Core.Domain;
using Nodalis.Core.Exporting;
using Nodalis.Core.Markdown;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Projects;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Exporting;

/// <summary>
/// Creates read-only project exports in portable Markdown, standalone HTML, and native DOCX formats.
/// </summary>
public sealed class WorkspaceProjectExportService
{
    private static readonly Regex InternalLinkPattern =
        new Regex(
            @"\[\[(?<target>[^\]|\r\n]+)(?:\|(?<alias>[^\]\r\n]+))?\]\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownLinkPattern =
        new Regex(
            @"(?<image>!)?\[(?<text>[^\]]*)\]\((?<target>[^)\r\n]+)\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _workspaceRoot;

    /// <summary>
    /// Initializes a project export service for one workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    public WorkspaceProjectExportService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
    }

    /// <summary>
    /// Analyzes a project without modifying it and returns the sections available for export.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read-only export preview.</returns>
    public async Task<ProjectExportPreview> PreparePreviewAsync(
            string projectDirectory,
            CancellationToken cancellationToken = default)
    {
        string projectRoot =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest project =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                Path.Combine(
                    projectRoot,
                    WorkspaceLayout.ProjectManifestFileName),
                cancellationToken);

        List<ProjectExportSectionOption> sections =
            new List<ProjectExportSectionOption>();
        List<string> allDocuments =
            new List<string>();

        foreach (SectionManifest section in project.Sections.OrderBy(section =>
                     section.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? rootDocumentFileName =
                ProjectSingletonDocumentLayout.GetRootDocumentFileName(
                    section.TemplateKey);
            string sectionStoragePath;
            string[] documents;

            if (rootDocumentFileName is not null)
            {
                ProjectSingletonDocumentResolution resolution =
                    ProjectSingletonDocumentLayout.Resolve(
                        projectRoot,
                        section,
                        migrateIfSafe: false);

                sectionStoragePath =
                    resolution.ActivePath;
                documents =
                    File.Exists(
                        resolution.ActivePath)
                        ? [resolution.ActivePath]
                        : [];
            }
            else
            {
                string sectionDirectory =
                    Path.Combine(
                        projectRoot,
                        WindowsPathRules.SanitizeSegment(
                            section.Name));

                sectionStoragePath =
                    sectionDirectory;
                documents =
                    Directory.Exists(
                            sectionDirectory)
                        ? await Task.Run(
                            () =>
                                Directory
                                    .EnumerateFiles(
                                        sectionDirectory,
                                        "*.md",
                                        SearchOption.AllDirectories)
                                    .OrderBy(
                                        path => path,
                                        StringComparer.CurrentCultureIgnoreCase)
                                    .ToArray(),
                            cancellationToken)
                        : [];
            }

            allDocuments.AddRange(
                documents);

            sections.Add(
                new ProjectExportSectionOption
                {
                    SectionId =
                        section.Id,
                    Name =
                        section.Name,
                    SourceOrder =
                        section.Order,
                    DocumentCount =
                        documents.Length,
                    RelativeDirectory =
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                projectRoot,
                                sectionStoragePath))
                });
        }

        HashSet<string> attachments =
            await DiscoverReferencedAttachmentsAsync(
                allDocuments,
                projectRoot,
                cancellationToken);

        return new ProjectExportPreview
        {
            ProjectId =
                project.Id,
            ProjectName =
                project.Name,
            ProjectDirectory =
                projectRoot,
            ProjectRelativePath =
                ToWorkspaceRelativePath(
                    projectRoot),
            Sections =
                sections,
            MarkdownDocumentCount =
                allDocuments.Count,
            AttachmentCount =
                attachments.Count,
            SuggestedBaseName =
                WindowsPathRules.SanitizeSegment(
                    project.Name +
                    " - Export")
        };
    }

    /// <summary>
    /// Exports the selected project sections without changing any project or workspace source file.
    /// </summary>
    /// <param name="preview">The project preview shown to the user.</param>
    /// <param name="request">The validated export request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The produced output paths and counts.</returns>
    public async Task<ProjectExportResult> ExportAsync(
            ProjectExportPreview preview,
            ProjectExportRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            preview);
        ArgumentNullException.ThrowIfNull(
            request);

        ValidateRequest(
            preview,
            request);

        ExportSnapshot snapshot =
            await BuildSnapshotAsync(
                preview,
                request,
                cancellationToken);

        string destinationRoot =
            Path.GetFullPath(
                request.DestinationDirectory);

        Directory.CreateDirectory(
            destinationRoot);

        string safeBaseName =
            WindowsPathRules.SanitizeSegment(
                request.BaseName.Trim());

        List<string> outputs =
            new List<string>();

        foreach (ProjectExportFormat format in request.Formats.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (format)
            {
                case ProjectExportFormat.PortableMarkdown:
                    outputs.Add(
                        await ExportPortableMarkdownAsync(
                            snapshot,
                            destinationRoot,
                            safeBaseName,
                            request.IncludeAttachments,
                            cancellationToken));
                    break;

                case ProjectExportFormat.StandaloneHtml:
                    outputs.Add(
                        await ExportStandaloneHtmlAsync(
                            snapshot,
                            destinationRoot,
                            safeBaseName,
                            request.IncludeAttachments,
                            cancellationToken));
                    break;

                case ProjectExportFormat.NativeDocx:
                    outputs.Add(
                        await ExportNativeDocxAsync(
                            snapshot,
                            destinationRoot,
                            safeBaseName,
                            cancellationToken));
                    break;

                default:
                    throw new InvalidDataException(
                        "Format d'export inconnu.");
            }
        }

        return new ProjectExportResult
        {
            OutputPaths =
                outputs,
            ExportedDocumentCount =
                snapshot.Documents.Count,
            ExportedAttachmentCount =
                request.IncludeAttachments
                    ? snapshot.Attachments.Count
                    : 0
        };
    }

    /// <summary>
    /// Validates a project directory and rejects paths outside the current workspace.
    /// </summary>
    /// <param name="projectDirectory">The candidate project directory.</param>
    /// <returns>The normalized project directory.</returns>
    private string ValidateProjectDirectory(
            string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);

        string projectRoot =
            Path.GetFullPath(
                projectDirectory);

        if (!IsInsideOrEqual(
                projectRoot,
                _workspaceRoot))
        {
            throw new InvalidDataException(
                "Le projet à exporter ne se trouve pas dans le workspace courant.");
        }

        if (!File.Exists(
                Path.Combine(
                    projectRoot,
                    WorkspaceLayout.ProjectManifestFileName)))
        {
            throw new InvalidDataException(
                "Le dossier sélectionné n'est pas un projet Nodalis.");
        }

        return projectRoot;
    }

    /// <summary>
    /// Validates an explicit export request before any destination is created.
    /// </summary>
    /// <param name="preview">The current project preview.</param>
    /// <param name="request">The export request.</param>
    private void ValidateRequest(
            ProjectExportPreview preview,
            ProjectExportRequest request)
    {
        if (request.Formats.Count ==
            0)
        {
            throw new InvalidDataException(
                "Sélectionnez au moins un format d'export.");
        }

        if (request.Sections.Count ==
            0)
        {
            throw new InvalidDataException(
                "Sélectionnez au moins une section à exporter.");
        }

        if (string.IsNullOrWhiteSpace(
                request.BaseName))
        {
            throw new InvalidDataException(
                "Le nom de l'export ne peut pas être vide.");
        }

        if (string.IsNullOrWhiteSpace(
                request.DestinationDirectory))
        {
            throw new InvalidDataException(
                "Sélectionnez un dossier de destination.");
        }

        Guid[] validSectionIds =
            preview.Sections
                .Select(section =>
                    section.SectionId)
                .ToArray();

        if (request.Sections.Any(selection =>
                !validSectionIds.Contains(
                    selection.SectionId)) ||
            request.Sections
                .GroupBy(selection =>
                    selection.SectionId)
                .Any(group =>
                    group.Count() >
                    1))
        {
            throw new InvalidDataException(
                "La sélection de sections n'est plus cohérente avec le projet.");
        }

        string destination =
            Path.GetFullPath(
                request.DestinationDirectory);

        if (IsInsideOrEqual(
                destination,
                _workspaceRoot))
        {
            throw new InvalidOperationException(
                "Le dossier d'export doit être extérieur au workspace afin que l'export ne modifie jamais les données Nodalis.");
        }
    }

    /// <summary>
    /// Reads the selected project content into an immutable export snapshot.
    /// </summary>
    /// <param name="preview">The project preview.</param>
    /// <param name="request">The selected sections and order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export snapshot.</returns>
    private async Task<ExportSnapshot> BuildSnapshotAsync(
            ProjectExportPreview preview,
            ProjectExportRequest request,
            CancellationToken cancellationToken)
    {
        string projectRoot =
            ValidateProjectDirectory(
                preview.ProjectDirectory);

        ProjectManifest project =
            await AtomicJsonFile.ReadAsync<ProjectManifest>(
                Path.Combine(
                    projectRoot,
                    WorkspaceLayout.ProjectManifestFileName),
                cancellationToken);

        if (project.Id !=
            preview.ProjectId)
        {
            throw new InvalidDataException(
                "Le projet a changé depuis la prévisualisation.");
        }

        List<ExportSectionSnapshot> sections =
            new List<ExportSectionSnapshot>();
        List<ExportDocumentSnapshot> documents =
            new List<ExportDocumentSnapshot>();

        foreach (ProjectExportSectionSelection selection in request.Sections
                     .OrderBy(selection =>
                         selection.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();

            SectionManifest section =
                project.Sections.SingleOrDefault(candidate =>
                    candidate.Id ==
                    selection.SectionId)
                ?? throw new InvalidDataException(
                    "Une section sélectionnée n'existe plus.");

            string? rootDocumentFileName =
                ProjectSingletonDocumentLayout.GetRootDocumentFileName(
                    section.TemplateKey);
            string sectionDirectory;
            string[] markdownFiles;

            if (rootDocumentFileName is not null)
            {
                ProjectSingletonDocumentResolution resolution =
                    ProjectSingletonDocumentLayout.Resolve(
                        projectRoot,
                        section,
                        migrateIfSafe: false);

                sectionDirectory =
                    Path.GetDirectoryName(
                        resolution.ActivePath) ??
                    projectRoot;
                markdownFiles =
                    File.Exists(
                        resolution.ActivePath)
                        ? [resolution.ActivePath]
                        : [];
            }
            else
            {
                sectionDirectory =
                    Path.Combine(
                        projectRoot,
                        WindowsPathRules.SanitizeSegment(
                            section.Name));

                markdownFiles =
                    Directory.Exists(
                            sectionDirectory)
                        ? await Task.Run(
                            () =>
                                Directory
                                    .EnumerateFiles(
                                        sectionDirectory,
                                        "*.md",
                                        SearchOption.AllDirectories)
                                    .OrderBy(
                                        path => path,
                                        StringComparer.CurrentCultureIgnoreCase)
                                    .ToArray(),
                            cancellationToken)
                        : [];
            }

            ExportSectionSnapshot sectionSnapshot =
                new ExportSectionSnapshot(
                    section.Id,
                    section.Name,
                    selection.Order,
                    sectionDirectory);

            sections.Add(
                sectionSnapshot);

            foreach (string markdownPath in markdownFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string markdown =
                    await File.ReadAllTextAsync(
                        markdownPath,
                        cancellationToken);

                string relativeWithinSection =
                    rootDocumentFileName is not null
                        ? Path.GetFileName(
                            markdownPath)
                        : NormalizeRelativePath(
                            Path.GetRelativePath(
                                sectionDirectory,
                                markdownPath));

                string exportRelativePath =
                    NormalizeRelativePath(
                        Path.Combine(
                            WindowsPathRules.SanitizeSegment(
                                section.Name),
                            relativeWithinSection.Replace(
                                '/',
                                Path.DirectorySeparatorChar)));

                string displayName =
                    GetDocumentDisplayName(
                        markdownPath,
                        markdown);

                documents.Add(
                    new ExportDocumentSnapshot(
                        section.Id,
                        section.Name,
                        markdownPath,
                        relativeWithinSection,
                        exportRelativePath,
                        displayName,
                        markdown));
            }
        }

        Dictionary<string, ExportDocumentSnapshot> sourceDocumentMap =
            documents
                .ToDictionary(
                    document =>
                        Path.GetFullPath(
                            document.SourcePath),
                    document =>
                        document,
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ExportDocumentSnapshot> aliasMap =
            BuildAliasMap(
                documents);

        Dictionary<string, ExportAttachmentSnapshot> attachments =
            request.IncludeAttachments
                ? await BuildAttachmentMapAsync(
                    documents,
                    projectRoot,
                    cancellationToken)
                : new Dictionary<string, ExportAttachmentSnapshot>(
                    StringComparer.OrdinalIgnoreCase);

        return new ExportSnapshot(
            project,
            projectRoot,
            sections,
            documents,
            sourceDocumentMap,
            aliasMap,
            attachments);
    }

    /// <summary>
    /// Creates a portable Markdown directory while preserving selected document structure and rewriting Nodalis-only links.
    /// </summary>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="destinationRoot">The external destination directory.</param>
    /// <param name="baseName">The sanitized export base name.</param>
    /// <param name="includeAttachments">Whether attachments should be copied.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final portable directory.</returns>
    private async Task<string> ExportPortableMarkdownAsync(
            ExportSnapshot snapshot,
            string destinationRoot,
            string baseName,
            bool includeAttachments,
            CancellationToken cancellationToken)
    {
        string finalDirectory =
            WindowsPathRules.GetUniqueDirectoryPath(
                destinationRoot,
                baseName +
                " - Markdown");
        string stagingDirectory =
            Path.Combine(
                destinationRoot,
                ".nodalis-export-" +
                Guid.NewGuid().ToString(
                    "N") +
                ".tmp");

        try
        {
            Directory.CreateDirectory(
                stagingDirectory);

            await AtomicFileWriter.WriteAllTextAsync(
                Path.Combine(
                    stagingDirectory,
                    "README.md"),
                BuildPortableReadme(
                    snapshot),
                cancellationToken);

            foreach (ExportDocumentSnapshot document in snapshot.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string destination =
                    Path.Combine(
                        stagingDirectory,
                        document.ExportRelativePath.Replace(
                            '/',
                            Path.DirectorySeparatorChar));

                string rewritten =
                    RewriteMarkdownForPortable(
                        document,
                        snapshot,
                        includeAttachments);

                await AtomicFileWriter.WriteAllTextAsync(
                    destination,
                    rewritten,
                    cancellationToken);
            }

            if (includeAttachments)
            {
                foreach (ExportAttachmentSnapshot attachment in snapshot.Attachments.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string destination =
                        Path.Combine(
                            stagingDirectory,
                            "Attachments",
                            attachment.ExportFileName);

                    string? parent =
                        Path.GetDirectoryName(
                            destination);

                    if (!string.IsNullOrWhiteSpace(
                            parent))
                    {
                        Directory.CreateDirectory(
                            parent);
                    }

                    await CopyFileAsync(
                        attachment.SourcePath,
                        destination,
                        cancellationToken);
                }
            }

            Directory.Move(
                stagingDirectory,
                finalDirectory);

            return finalDirectory;
        }
        catch
        {
            if (Directory.Exists(
                    stagingDirectory))
            {
                try
                {
                    Directory.Delete(
                        stagingDirectory,
                        recursive: true);
                }
                catch
                {
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Creates a single standalone HTML document with inline CSS and inline local attachment data.
    /// </summary>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="destinationRoot">The external destination directory.</param>
    /// <param name="baseName">The sanitized export base name.</param>
    /// <param name="includeAttachments">Whether local attachments should be embedded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final HTML file path.</returns>
    private async Task<string> ExportStandaloneHtmlAsync(
            ExportSnapshot snapshot,
            string destinationRoot,
            string baseName,
            bool includeAttachments,
            CancellationToken cancellationToken)
    {
        string outputPath =
            WindowsPathRules.GetUniqueFilePath(
                destinationRoot,
                baseName +
                ".html");

        string html =
            await BuildStandaloneHtmlAsync(
                snapshot,
                includeAttachments,
                cancellationToken);

        await AtomicFileWriter.WriteAllTextAsync(
            outputPath,
            html,
            cancellationToken);

        return outputPath;
    }

    /// <summary>
    /// Creates a dependency-free native Open XML DOCX containing the consolidated selected content.
    /// </summary>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="destinationRoot">The external destination directory.</param>
    /// <param name="baseName">The sanitized export base name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The final DOCX file path.</returns>
    private async Task<string> ExportNativeDocxAsync(
            ExportSnapshot snapshot,
            string destinationRoot,
            string baseName,
            CancellationToken cancellationToken)
    {
        string outputPath =
            WindowsPathRules.GetUniqueFilePath(
                destinationRoot,
                baseName +
                ".docx");
        string temporaryPath =
            Path.Combine(
                destinationRoot,
                "." +
                Path.GetFileName(
                    outputPath) +
                "." +
                Guid.NewGuid().ToString(
                    "N") +
                ".tmp");

        try
        {
            await using (FileStream stream =
                         new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             bufferSize: 81920,
                             options: FileOptions.Asynchronous |
                                      FileOptions.SequentialScan))
            using (ZipArchive archive =
                   new ZipArchive(
                       stream,
                       ZipArchiveMode.Create,
                       leaveOpen: true))
            {
                await WriteZipTextEntryAsync(
                    archive,
                    "[Content_Types].xml",
                    BuildDocxContentTypes(),
                    cancellationToken);
                await WriteZipTextEntryAsync(
                    archive,
                    "_rels/.rels",
                    BuildDocxPackageRelationships(),
                    cancellationToken);
                await WriteZipTextEntryAsync(
                    archive,
                    "word/_rels/document.xml.rels",
                    BuildDocxDocumentRelationships(),
                    cancellationToken);
                await WriteZipTextEntryAsync(
                    archive,
                    "word/styles.xml",
                    BuildDocxStyles(),
                    cancellationToken);
                await WriteZipTextEntryAsync(
                    archive,
                    "word/document.xml",
                    BuildDocxDocument(
                        snapshot),
                    cancellationToken);
            }

            File.Move(
                temporaryPath,
                outputPath);

            return outputPath;
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath);
            }
        }
    }

    /// <summary>
    /// Builds the README and table of contents for a portable Markdown export.
    /// </summary>
    /// <param name="snapshot">The export snapshot.</param>
    /// <returns>The README Markdown.</returns>
    private static string BuildPortableReadme(
            ExportSnapshot snapshot)
    {
        StringBuilder builder =
            new StringBuilder();

        builder.AppendLine(
            "# " +
            snapshot.Project.Name);
        builder.AppendLine();
        builder.AppendLine(
            "> Export portable Nodalis. Les fichiers Markdown sont lisibles sans Nodalis.");
        builder.AppendLine();
        builder.AppendLine(
            "## Sommaire");
        builder.AppendLine();

        foreach (ExportSectionSnapshot section in snapshot.Sections.OrderBy(section =>
                     section.Order))
        {
            builder.AppendLine(
                "### " +
                section.Name);
            builder.AppendLine();

            ExportDocumentSnapshot[] sectionDocuments =
                snapshot.Documents
                    .Where(document =>
                        document.SectionId ==
                        section.SectionId)
                    .OrderBy(
                        document => document.ExportRelativePath,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();

            if (sectionDocuments.Length ==
                0)
            {
                builder.AppendLine(
                    "_Aucun document Markdown._");
                builder.AppendLine();
                continue;
            }

            foreach (ExportDocumentSnapshot document in sectionDocuments)
            {
                builder.AppendLine(
                    "- [" +
                    document.DisplayName +
                    "](" +
                    EscapeMarkdownPath(
                        document.ExportRelativePath) +
                    ")");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Rewrites Nodalis internal links and local file links for one portable Markdown document.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="snapshot">The complete export snapshot.</param>
    /// <param name="includeAttachments">Whether attachments are included.</param>
    /// <returns>Portable Markdown content.</returns>
    private static string RewriteMarkdownForPortable(
            ExportDocumentSnapshot document,
            ExportSnapshot snapshot,
            bool includeAttachments)
    {
        string currentExportDirectory =
            Path.GetDirectoryName(
                document.ExportRelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar))
            ?? string.Empty;

        string rewritten =
            InternalLinkPattern.Replace(
                document.Markdown,
                match =>
                {
                    string target =
                        match.Groups["target"].Value.Trim();
                    string alias =
                        match.Groups["alias"].Success
                            ? match.Groups["alias"].Value.Trim()
                            : target;

                    if (!snapshot.AliasMap.TryGetValue(
                            target,
                            out ExportDocumentSnapshot? targetDocument))
                    {
                        return alias;
                    }

                    string relative =
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                string.IsNullOrWhiteSpace(
                                        currentExportDirectory)
                                    ? "."
                                    : currentExportDirectory,
                                targetDocument.ExportRelativePath.Replace(
                                    '/',
                                    Path.DirectorySeparatorChar)));

                    return "[" +
                           alias +
                           "](" +
                           EscapeMarkdownPath(
                               relative) +
                           ")";
                });

        rewritten =
            MarkdownLinkPattern.Replace(
                rewritten,
                match =>
                {
                    string text =
                        match.Groups["text"].Value;
                    bool image =
                        match.Groups["image"].Success;
                    string rawTarget =
                        match.Groups["target"].Value.Trim();

                    LocalLinkResolution resolution =
                        ResolveLocalLink(
                            document.SourcePath,
                            rawTarget);

                    if (!resolution.IsLocal ||
                        resolution.FullPath is null)
                    {
                        return match.Value;
                    }

                    string fullTarget =
                        resolution.FullPath;

                    if (snapshot.SourceDocumentMap.TryGetValue(
                            fullTarget,
                            out ExportDocumentSnapshot? linkedDocument))
                    {
                        string relative =
                            NormalizeRelativePath(
                                Path.GetRelativePath(
                                    string.IsNullOrWhiteSpace(
                                            currentExportDirectory)
                                        ? "."
                                        : currentExportDirectory,
                                    linkedDocument.ExportRelativePath.Replace(
                                        '/',
                                        Path.DirectorySeparatorChar)));

                        return (image
                                   ? "!"
                                   : string.Empty) +
                               "[" +
                               text +
                               "](" +
                               EscapeMarkdownPath(
                                   relative) +
                               resolution.Fragment +
                               ")";
                    }

                    if (Path.GetExtension(
                            fullTarget)
                        .Equals(
                            ".md",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return image
                            ? "[image non exportée : " +
                              text +
                              "]"
                            : text;
                    }

                    if (includeAttachments &&
                        snapshot.Attachments.TryGetValue(
                            fullTarget,
                            out ExportAttachmentSnapshot? attachment))
                    {
                        string attachmentRelative =
                            NormalizeRelativePath(
                                Path.GetRelativePath(
                                    string.IsNullOrWhiteSpace(
                                            currentExportDirectory)
                                        ? "."
                                        : currentExportDirectory,
                                    Path.Combine(
                                        "Attachments",
                                        attachment.ExportFileName)));

                        return (image
                                   ? "!"
                                   : string.Empty) +
                               "[" +
                               text +
                               "](" +
                               EscapeMarkdownPath(
                                   attachmentRelative) +
                               ")";
                    }

                    return image
                        ? "[image exclue : " +
                          text +
                          "]"
                        : text;
                });

        return rewritten;
    }

    /// <summary>
    /// Builds a fully standalone HTML representation of all selected documents.
    /// </summary>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="includeAttachments">Whether local attachments should be embedded.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The standalone HTML document.</returns>
    private static async Task<string> BuildStandaloneHtmlAsync(
            ExportSnapshot snapshot,
            bool includeAttachments,
            CancellationToken cancellationToken)
    {
        Dictionary<string, string> dataUris =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (includeAttachments)
        {
            foreach (ExportAttachmentSnapshot attachment in snapshot.Attachments.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] bytes =
                    await File.ReadAllBytesAsync(
                        attachment.SourcePath,
                        cancellationToken);

                dataUris[attachment.SourcePath] =
                    "data:" +
                    GetMimeType(
                        attachment.SourcePath) +
                    ";base64," +
                    Convert.ToBase64String(
                        bytes);
            }
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\">");
        builder.Append(
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        builder.Append(
            "<title>");
        builder.Append(
            Html(
                snapshot.Project.Name));
        builder.Append(
            "</title><style>");
        builder.Append(
            "body{font-family:Segoe UI,Arial,sans-serif;max-width:1100px;margin:0 auto;padding:32px;color:#1f2328;line-height:1.55}" +
            "nav{background:#f6f8fa;border:1px solid #d0d7de;border-radius:8px;padding:18px;margin:0 0 28px}" +
            "article{border-top:1px solid #d8dee4;padding-top:22px;margin-top:30px}" +
            "code,pre{font-family:Consolas,monospace}pre{background:#f6f8fa;padding:14px;border-radius:6px;overflow:auto}" +
            "blockquote{border-left:4px solid #8c959f;margin-left:0;padding-left:14px;color:#57606a}" +
            "table{border-collapse:collapse;width:100%;margin:12px 0}th,td{border:1px solid #d0d7de;padding:6px 9px;text-align:left}" +
            "img{max-width:100%;height:auto}a{color:#0969da} .muted{color:#6e7781}");
        builder.Append(
            "</style></head><body><header><h1>");
        builder.Append(
            Html(
                snapshot.Project.Name));
        builder.Append(
            "</h1><p class=\"muted\">Export HTML autonome généré par Nodalis.</p></header><nav><h2>Sommaire</h2><ul>");

        foreach (ExportSectionSnapshot section in snapshot.Sections.OrderBy(section =>
                     section.Order))
        {
            builder.Append(
                "<li><a href=\"#section-");
            builder.Append(
                SectionAnchor(
                    section.SectionId));
            builder.Append(
                "\">");
            builder.Append(
                Html(
                    section.Name));
            builder.Append(
                "</a><ul>");

            foreach (ExportDocumentSnapshot document in snapshot.Documents.Where(document =>
                         document.SectionId ==
                         section.SectionId))
            {
                builder.Append(
                    "<li><a href=\"#");
                builder.Append(
                    DocumentAnchor(
                        document));
                builder.Append(
                    "\">");
                builder.Append(
                    Html(
                        document.DisplayName));
                builder.Append(
                    "</a></li>");
            }

            builder.Append(
                "</ul></li>");
        }

        builder.Append(
            "</ul></nav>");

        foreach (ExportSectionSnapshot section in snapshot.Sections.OrderBy(section =>
                     section.Order))
        {
            builder.Append(
                "<section id=\"section-");
            builder.Append(
                SectionAnchor(
                    section.SectionId));
            builder.Append(
                "\"><h2>");
            builder.Append(
                Html(
                    section.Name));
            builder.Append(
                "</h2>");

            ExportDocumentSnapshot[] sectionDocuments =
                snapshot.Documents
                    .Where(document =>
                        document.SectionId ==
                        section.SectionId)
                    .ToArray();

            if (sectionDocuments.Length ==
                0)
            {
                builder.Append(
                    "<p class=\"muted\">Aucun document Markdown.</p>");
            }

            foreach (ExportDocumentSnapshot document in sectionDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                builder.Append(
                    "<article id=\"");
                builder.Append(
                    DocumentAnchor(
                        document));
                builder.Append(
                    "\"><h3>");
                builder.Append(
                    Html(
                        document.DisplayName));
                builder.Append(
                    "</h3>");
                builder.Append(
                    RenderMarkdownAsHtml(
                        document,
                        snapshot,
                        includeAttachments,
                        dataUris));
                builder.Append(
                    "</article>");
            }

            builder.Append(
                "</section>");
        }

        builder.Append(
            "</body></html>");

        return builder.ToString();
    }

    /// <summary>
    /// Renders one Markdown document into dependency-free HTML.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="includeAttachments">Whether attachments are embedded.</param>
    /// <param name="dataUris">Preloaded attachment data URIs.</param>
    /// <returns>HTML fragment for the document.</returns>
    private static string RenderMarkdownAsHtml(
            ExportDocumentSnapshot document,
            ExportSnapshot snapshot,
            bool includeAttachments,
            IReadOnlyDictionary<string, string> dataUris)
    {
        StringBuilder builder =
            new StringBuilder();

        foreach (MarkdownBlock block in MarkdownDocumentParser.Parse(
                     document.Markdown))
        {
            switch (block.Kind)
            {
                case MarkdownBlockKind.Heading:
                    int headingLevel =
                        Math.Clamp(
                            block.Level +
                            2,
                            3,
                            6);

                    builder.Append(
                        "<h");
                    builder.Append(
                        headingLevel);
                    builder.Append(
                        ">");
                    builder.Append(
                        RenderInlineHtml(
                            block.Text,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    builder.Append(
                        "</h");
                    builder.Append(
                        headingLevel);
                    builder.Append(
                        ">");
                    break;

                case MarkdownBlockKind.UnorderedListItem:
                    builder.Append(
                        "<ul><li>");
                    builder.Append(
                        RenderInlineHtml(
                            block.Text,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    builder.Append(
                        "</li></ul>");
                    break;

                case MarkdownBlockKind.OrderedListItem:
                    builder.Append(
                        "<ol start=\"");
                    builder.Append(
                        block.OrderedListNumber ??
                        1);
                    builder.Append(
                        "\"><li>");
                    builder.Append(
                        RenderInlineHtml(
                            block.Text,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    builder.Append(
                        "</li></ol>");
                    break;

                case MarkdownBlockKind.ChecklistItem:
                    builder.Append(
                        "<p>");
                    builder.Append(
                        block.IsChecked ==
                        true
                            ? "☒ "
                            : "☐ ");
                    builder.Append(
                        RenderInlineHtml(
                            block.Text,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    builder.Append(
                        "</p>");
                    break;

                case MarkdownBlockKind.Quote:
                    builder.Append(
                        "<blockquote>");
                    builder.Append(
                        RenderInlineHtml(
                            block.Text,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    builder.Append(
                        "</blockquote>");
                    break;

                case MarkdownBlockKind.CodeBlock:
                    builder.Append(
                        "<pre><code>");
                    builder.Append(
                        Html(
                            block.Text));
                    builder.Append(
                        "</code></pre>");
                    break;

                case MarkdownBlockKind.Table:
                    builder.Append(
                        RenderTableHtml(
                            block,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    break;

                default:
                    builder.Append(
                        "<p>");
                    builder.Append(
                        RenderInlineHtml(
                            block.Text,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    builder.Append(
                        "</p>");
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders Markdown inline constructs to safe HTML and resolves exported document and attachment links.
    /// </summary>
    /// <param name="text">The inline Markdown text.</param>
    /// <param name="document">The owner document.</param>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="includeAttachments">Whether attachments are embedded.</param>
    /// <param name="dataUris">Preloaded attachment data URIs.</param>
    /// <returns>Safe HTML.</returns>
    private static string RenderInlineHtml(
            string text,
            ExportDocumentSnapshot document,
            ExportSnapshot snapshot,
            bool includeAttachments,
            IReadOnlyDictionary<string, string> dataUris)
    {
        StringBuilder builder =
            new StringBuilder();

        foreach (MarkdownInline inline in MarkdownInlineParser.Parse(
                     text))
        {
            switch (inline.Kind)
            {
                case MarkdownInlineKind.Bold:
                    builder.Append(
                        "<strong>");
                    builder.Append(
                        Html(
                            inline.Text));
                    builder.Append(
                        "</strong>");
                    break;

                case MarkdownInlineKind.Italic:
                    builder.Append(
                        "<em>");
                    builder.Append(
                        Html(
                            inline.Text));
                    builder.Append(
                        "</em>");
                    break;

                case MarkdownInlineKind.Code:
                    builder.Append(
                        "<code>");
                    builder.Append(
                        Html(
                            inline.Text));
                    builder.Append(
                        "</code>");
                    break;

                case MarkdownInlineKind.InternalLink:
                    string internalTarget =
                        inline.Target ??
                        inline.Text;

                    if (snapshot.AliasMap.TryGetValue(
                            internalTarget,
                            out ExportDocumentSnapshot? internalDocument))
                    {
                        builder.Append(
                            "<a href=\"#");
                        builder.Append(
                            DocumentAnchor(
                                internalDocument));
                        builder.Append(
                            "\">");
                        builder.Append(
                            Html(
                                inline.Text));
                        builder.Append(
                            "</a>");
                    }
                    else
                    {
                        builder.Append(
                            Html(
                                inline.Text));
                    }

                    break;

                case MarkdownInlineKind.Link:
                    builder.Append(
                        RenderHtmlLink(
                            inline,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    break;

                case MarkdownInlineKind.Image:
                    builder.Append(
                        RenderHtmlImage(
                            inline,
                            document,
                            snapshot,
                            includeAttachments,
                            dataUris));
                    break;

                default:
                    builder.Append(
                        Html(
                            inline.Text));
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders a normal Markdown link for standalone HTML.
    /// </summary>
    /// <param name="inline">The parsed link.</param>
    /// <param name="document">The owner document.</param>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="includeAttachments">Whether attachments are embedded.</param>
    /// <param name="dataUris">Preloaded attachment data URIs.</param>
    /// <returns>HTML for the link.</returns>
    private static string RenderHtmlLink(
            MarkdownInline inline,
            ExportDocumentSnapshot document,
            ExportSnapshot snapshot,
            bool includeAttachments,
            IReadOnlyDictionary<string, string> dataUris)
    {
        string target =
            inline.Target ??
            string.Empty;

        LocalLinkResolution resolution =
            ResolveLocalLink(
                document.SourcePath,
                target);

        if (!resolution.IsLocal ||
            resolution.FullPath is null)
        {
            return "<a href=\"" +
                   HtmlAttribute(
                       target) +
                   "\">" +
                   Html(
                       inline.Text) +
                   "</a>";
        }

        if (snapshot.SourceDocumentMap.TryGetValue(
                resolution.FullPath,
                out ExportDocumentSnapshot? linkedDocument))
        {
            return "<a href=\"#" +
                   DocumentAnchor(
                       linkedDocument) +
                   "\">" +
                   Html(
                       inline.Text) +
                   "</a>";
        }

        if (includeAttachments &&
            dataUris.TryGetValue(
                resolution.FullPath,
                out string? dataUri))
        {
            return "<a download=\"" +
                   HtmlAttribute(
                       Path.GetFileName(
                           resolution.FullPath)) +
                   "\" href=\"" +
                   dataUri +
                   "\">" +
                   Html(
                       inline.Text) +
                   "</a>";
        }

        return Html(
            inline.Text);
    }

    /// <summary>
    /// Renders a Markdown image for standalone HTML, embedding local image bytes when attachments are included.
    /// </summary>
    /// <param name="inline">The parsed image.</param>
    /// <param name="document">The owner document.</param>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="includeAttachments">Whether attachments are embedded.</param>
    /// <param name="dataUris">Preloaded attachment data URIs.</param>
    /// <returns>HTML for the image or a readable placeholder.</returns>
    private static string RenderHtmlImage(
            MarkdownInline inline,
            ExportDocumentSnapshot document,
            ExportSnapshot snapshot,
            bool includeAttachments,
            IReadOnlyDictionary<string, string> dataUris)
    {
        string target =
            inline.Target ??
            string.Empty;

        LocalLinkResolution resolution =
            ResolveLocalLink(
                document.SourcePath,
                target);

        if (resolution.IsLocal &&
            resolution.FullPath is not null &&
            includeAttachments &&
            snapshot.Attachments.ContainsKey(
                resolution.FullPath) &&
            dataUris.TryGetValue(
                resolution.FullPath,
                out string? dataUri))
        {
            return "<img alt=\"" +
                   HtmlAttribute(
                       inline.Text) +
                   "\" src=\"" +
                   dataUri +
                   "\">";
        }

        return "<span class=\"muted\">[image : " +
               Html(
                   inline.Text) +
               "]</span>";
    }

    /// <summary>
    /// Renders a parsed Markdown table to HTML.
    /// </summary>
    /// <param name="block">The table block.</param>
    /// <param name="document">The owner document.</param>
    /// <param name="snapshot">The export snapshot.</param>
    /// <param name="includeAttachments">Whether attachments are embedded.</param>
    /// <param name="dataUris">Preloaded attachment data URIs.</param>
    /// <returns>HTML table markup.</returns>
    private static string RenderTableHtml(
            MarkdownBlock block,
            ExportDocumentSnapshot document,
            ExportSnapshot snapshot,
            bool includeAttachments,
            IReadOnlyDictionary<string, string> dataUris)
    {
        if (block.TableRows.Count ==
            0)
        {
            return string.Empty;
        }

        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "<table>");

        for (int rowIndex = 0;
             rowIndex < block.TableRows.Count;
             rowIndex++)
        {
            string cellTag =
                rowIndex ==
                0
                    ? "th"
                    : "td";

            builder.Append(
                "<tr>");

            foreach (string cell in block.TableRows[rowIndex])
            {
                builder.Append(
                    "<");
                builder.Append(
                    cellTag);
                builder.Append(
                    ">");
                builder.Append(
                    RenderInlineHtml(
                        cell,
                        document,
                        snapshot,
                        includeAttachments,
                        dataUris));
                builder.Append(
                    "</");
                builder.Append(
                    cellTag);
                builder.Append(
                    ">");
            }

            builder.Append(
                "</tr>");
        }

        builder.Append(
            "</table>");

        return builder.ToString();
    }

    /// <summary>
    /// Builds the Open XML main document part for the native DOCX export.
    /// </summary>
    /// <param name="snapshot">The export snapshot.</param>
    /// <returns>The DOCX document XML.</returns>
    private static string BuildDocxDocument(
            ExportSnapshot snapshot)
    {
        StringBuilder builder =
            new StringBuilder();

        builder.Append(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:body>");

        AppendDocxParagraph(
            builder,
            snapshot.Project.Name,
            "Heading1");

        AppendDocxParagraph(
            builder,
            "Sommaire",
            "Heading2");

        foreach (ExportSectionSnapshot section in snapshot.Sections.OrderBy(section =>
                     section.Order))
        {
            AppendDocxParagraph(
                builder,
                section.Name,
                null);
        }

        foreach (ExportSectionSnapshot section in snapshot.Sections.OrderBy(section =>
                     section.Order))
        {
            AppendDocxParagraph(
                builder,
                section.Name,
                "Heading1");

            ExportDocumentSnapshot[] sectionDocuments =
                snapshot.Documents
                    .Where(document =>
                        document.SectionId ==
                        section.SectionId)
                    .ToArray();

            if (sectionDocuments.Length ==
                0)
            {
                AppendDocxParagraph(
                    builder,
                    "Aucun document Markdown.",
                    null);
            }

            foreach (ExportDocumentSnapshot document in sectionDocuments)
            {
                AppendDocxParagraph(
                    builder,
                    document.DisplayName,
                    "Heading2");

                foreach (MarkdownBlock block in MarkdownDocumentParser.Parse(
                             document.Markdown))
                {
                    switch (block.Kind)
                    {
                        case MarkdownBlockKind.Heading:
                            AppendDocxParagraph(
                                builder,
                                RenderPlainInlineText(
                                    block.Text),
                                "Heading" +
                                Math.Clamp(
                                    block.Level +
                                    2,
                                    3,
                                    6));
                            break;

                        case MarkdownBlockKind.UnorderedListItem:
                            AppendDocxParagraph(
                                builder,
                                "• " +
                                RenderPlainInlineText(
                                    block.Text),
                                null);
                            break;

                        case MarkdownBlockKind.OrderedListItem:
                            AppendDocxParagraph(
                                builder,
                                (block.OrderedListNumber ??
                                 1) +
                                ". " +
                                RenderPlainInlineText(
                                    block.Text),
                                null);
                            break;

                        case MarkdownBlockKind.ChecklistItem:
                            AppendDocxParagraph(
                                builder,
                                (block.IsChecked ==
                                 true
                                    ? "☒ "
                                    : "☐ ") +
                                RenderPlainInlineText(
                                    block.Text),
                                null);
                            break;

                        case MarkdownBlockKind.Quote:
                            AppendDocxParagraph(
                                builder,
                                "> " +
                                RenderPlainInlineText(
                                    block.Text),
                                null);
                            break;

                        case MarkdownBlockKind.CodeBlock:
                            AppendDocxParagraph(
                                builder,
                                block.Text,
                                "Code");
                            break;

                        case MarkdownBlockKind.Table:
                            AppendDocxTable(
                                builder,
                                block);
                            break;

                        default:
                            AppendDocxParagraph(
                                builder,
                                RenderPlainInlineText(
                                    block.Text),
                                null);
                            break;
                    }
                }
            }
        }

        builder.Append(
            "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>" +
            "</w:body></w:document>");

        return builder.ToString();
    }

    /// <summary>
    /// Appends one WordprocessingML paragraph with an optional paragraph style.
    /// </summary>
    /// <param name="builder">The target XML builder.</param>
    /// <param name="text">The paragraph text.</param>
    /// <param name="style">The optional paragraph style identifier.</param>
    private static void AppendDocxParagraph(
            StringBuilder builder,
            string text,
            string? style)
    {
        builder.Append(
            "<w:p>");

        if (!string.IsNullOrWhiteSpace(
                style))
        {
            builder.Append(
                "<w:pPr><w:pStyle w:val=\"");
            builder.Append(
                Xml(
                    style));
            builder.Append(
                "\"/></w:pPr>");
        }

        string[] lines =
            text.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Split(
                    '\n');

        builder.Append(
            "<w:r>");

        for (int index = 0;
             index < lines.Length;
             index++)
        {
            if (index >
                0)
            {
                builder.Append(
                    "<w:br/>");
            }

            builder.Append(
                "<w:t xml:space=\"preserve\">");
            builder.Append(
                Xml(
                    lines[index]));
            builder.Append(
                "</w:t>");
        }

        builder.Append(
            "</w:r></w:p>");
    }

    /// <summary>
    /// Appends one basic WordprocessingML table.
    /// </summary>
    /// <param name="builder">The target XML builder.</param>
    /// <param name="block">The parsed Markdown table.</param>
    private static void AppendDocxTable(
            StringBuilder builder,
            MarkdownBlock block)
    {
        builder.Append(
            "<w:tbl><w:tblPr><w:tblBorders>" +
            "<w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/>" +
            "<w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/>" +
            "<w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/>" +
            "</w:tblBorders></w:tblPr>");

        foreach (List<string> row in block.TableRows)
        {
            builder.Append(
                "<w:tr>");

            foreach (string cell in row)
            {
                builder.Append(
                    "<w:tc>");
                AppendDocxParagraph(
                    builder,
                    RenderPlainInlineText(
                        cell),
                    null);
                builder.Append(
                    "</w:tc>");
            }

            builder.Append(
                "</w:tr>");
        }

        builder.Append(
            "</w:tbl>");
    }

    /// <summary>
    /// Builds the minimal DOCX content type declarations.
    /// </summary>
    /// <returns>The content-types XML.</returns>
    private static string BuildDocxContentTypes() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
        "</Types>";

    /// <summary>
    /// Builds the package relationship that points to the DOCX main document part.
    /// </summary>
    /// <returns>The package relationship XML.</returns>
    private static string BuildDocxPackageRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    /// <summary>
    /// Builds the document relationship to the local style part.
    /// </summary>
    /// <returns>The document relationship XML.</returns>
    private static string BuildDocxDocumentRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    /// <summary>
    /// Builds the small style catalog used by the dependency-free DOCX export.
    /// </summary>
    /// <returns>The style XML.</returns>
    private static string BuildDocxStyles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        BuildHeadingStyle(
            "Heading1",
            "Titre 1",
            32) +
        BuildHeadingStyle(
            "Heading2",
            "Titre 2",
            28) +
        BuildHeadingStyle(
            "Heading3",
            "Titre 3",
            24) +
        BuildHeadingStyle(
            "Heading4",
            "Titre 4",
            22) +
        BuildHeadingStyle(
            "Heading5",
            "Titre 5",
            20) +
        BuildHeadingStyle(
            "Heading6",
            "Titre 6",
            18) +
        "<w:style w:type=\"paragraph\" w:styleId=\"Code\"><w:name w:val=\"Code\"/><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/><w:sz w:val=\"18\"/></w:rPr></w:style>" +
        "</w:styles>";

    /// <summary>
    /// Builds one heading style declaration for the generated DOCX.
    /// </summary>
    /// <param name="styleId">The style identifier.</param>
    /// <param name="displayName">The readable style name.</param>
    /// <param name="halfPoints">The Word font size in half-points.</param>
    /// <returns>The style XML.</returns>
    private static string BuildHeadingStyle(
            string styleId,
            string displayName,
            int halfPoints) =>
        "<w:style w:type=\"paragraph\" w:styleId=\"" +
        Xml(
            styleId) +
        "\"><w:name w:val=\"" +
        Xml(
            displayName) +
        "\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:rPr><w:b/><w:sz w:val=\"" +
        halfPoints +
        "\"/></w:rPr></w:style>";

    /// <summary>
    /// Writes one UTF-8 text entry to a ZIP archive.
    /// </summary>
    /// <param name="archive">The target archive.</param>
    /// <param name="entryName">The ZIP entry name.</param>
    /// <param name="content">The UTF-8 text content.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the write.</returns>
    private static async Task WriteZipTextEntryAsync(
            ZipArchive archive,
            string entryName,
            string content,
            CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(
                entryName,
                CompressionLevel.Optimal);

        await using Stream stream =
            entry.Open();
        await using StreamWriter writer =
            new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                bufferSize: 4096,
                leaveOpen: true);

        await writer.WriteAsync(
            content.AsMemory(),
            cancellationToken);
        await writer.FlushAsync(
            cancellationToken);
    }

    /// <summary>
    /// Converts Markdown inline constructs to readable plain text for DOCX output.
    /// </summary>
    /// <param name="text">The Markdown inline text.</param>
    /// <returns>Readable plain text.</returns>
    private static string RenderPlainInlineText(
            string text)
    {
        StringBuilder builder =
            new StringBuilder();

        foreach (MarkdownInline inline in MarkdownInlineParser.Parse(
                     text))
        {
            builder.Append(
                inline.Kind ==
                MarkdownInlineKind.Image
                    ? "[Image : " +
                      inline.Text +
                      "]"
                    : inline.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds a unique, deterministic attachment map for all local files referenced by selected documents.
    /// </summary>
    /// <param name="documents">The selected documents.</param>
    /// <param name="projectRoot">The source project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The attachment map keyed by full source path.</returns>
    private async Task<Dictionary<string, ExportAttachmentSnapshot>> BuildAttachmentMapAsync(
            IReadOnlyCollection<ExportDocumentSnapshot> documents,
            string projectRoot,
            CancellationToken cancellationToken)
    {
        Dictionary<string, ExportAttachmentSnapshot> attachments =
            new Dictionary<string, ExportAttachmentSnapshot>(
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> usedNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (ExportDocumentSnapshot document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (Match match in MarkdownLinkPattern.Matches(
                         document.Markdown))
            {
                string rawTarget =
                    match.Groups["target"].Value.Trim();

                LocalLinkResolution resolution =
                    ResolveLocalLink(
                        document.SourcePath,
                        rawTarget);

                if (!resolution.IsLocal ||
                    resolution.FullPath is null ||
                    !File.Exists(
                        resolution.FullPath) ||
                    string.Equals(
                        Path.GetExtension(
                            resolution.FullPath),
                        ".md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath =
                    Path.GetFullPath(
                        resolution.FullPath);

                if (attachments.ContainsKey(
                        fullPath))
                {
                    continue;
                }

                string exportName =
                    BuildUniqueAttachmentName(
                        fullPath,
                        usedNames);

                FileInfo info =
                    new FileInfo(
                        fullPath);

                attachments[fullPath] =
                    new ExportAttachmentSnapshot(
                        fullPath,
                        exportName,
                        info.Length,
                        IsInsideOrEqual(
                            fullPath,
                            projectRoot));
            }
        }

        await Task.CompletedTask;

        return attachments;
    }

    /// <summary>
    /// Discovers all unique local non-Markdown files referenced by the supplied documents.
    /// </summary>
    /// <param name="documents">The source Markdown document paths.</param>
    /// <param name="projectRoot">The project root used for path validation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The unique attachment paths.</returns>
    private static async Task<HashSet<string>> DiscoverReferencedAttachmentsAsync(
            IReadOnlyCollection<string> documents,
            string projectRoot,
            CancellationToken cancellationToken)
    {
        HashSet<string> attachments =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (string document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string markdown =
                await File.ReadAllTextAsync(
                    document,
                    cancellationToken);

            foreach (Match match in MarkdownLinkPattern.Matches(
                         markdown))
            {
                LocalLinkResolution resolution =
                    ResolveLocalLink(
                        document,
                        match.Groups["target"].Value.Trim());

                if (!resolution.IsLocal ||
                    resolution.FullPath is null ||
                    !File.Exists(
                        resolution.FullPath) ||
                    string.Equals(
                        Path.GetExtension(
                            resolution.FullPath),
                        ".md",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fullPath =
                    Path.GetFullPath(
                        resolution.FullPath);

                if (IsInsideOrEqual(
                        fullPath,
                        projectRoot) ||
                    Path.IsPathFullyQualified(
                        fullPath))
                {
                    attachments.Add(
                        fullPath);
                }
            }
        }

        return attachments;
    }

    /// <summary>
    /// Builds a unique alias lookup for internal Nodalis links, excluding ambiguous aliases.
    /// </summary>
    /// <param name="documents">The selected documents.</param>
    /// <returns>The unique alias map.</returns>
    private static Dictionary<string, ExportDocumentSnapshot> BuildAliasMap(
            IReadOnlyCollection<ExportDocumentSnapshot> documents)
    {
        Dictionary<string, List<ExportDocumentSnapshot>> candidates =
            new Dictionary<string, List<ExportDocumentSnapshot>>(
                StringComparer.CurrentCultureIgnoreCase);

        foreach (ExportDocumentSnapshot document in documents)
        {
            HashSet<string> aliases =
                new HashSet<string>(
                    StringComparer.CurrentCultureIgnoreCase)
                {
                    Path.GetFileNameWithoutExtension(
                        document.SourcePath),
                    document.DisplayName,
                    document.RelativeWithinSection,
                    document.ExportRelativePath
                };

            foreach (MarkdownOutlineEntry heading in MarkdownOutlineParser.Parse(
                         document.Markdown))
            {
                aliases.Add(
                    heading.Title);
            }

            foreach (string alias in aliases.Where(alias =>
                         !string.IsNullOrWhiteSpace(
                             alias)))
            {
                if (!candidates.TryGetValue(
                        alias,
                        out List<ExportDocumentSnapshot>? mapped))
                {
                    mapped =
                        new List<ExportDocumentSnapshot>();
                    candidates[alias] =
                        mapped;
                }

                if (!mapped.Contains(
                        document))
                {
                    mapped.Add(
                        document);
                }
            }
        }

        return candidates
            .Where(pair =>
                pair.Value.Count ==
                1)
            .ToDictionary(
                pair =>
                    pair.Key,
                pair =>
                    pair.Value[0],
                StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Gets a document display name from its first heading, falling back to the file stem.
    /// </summary>
    /// <param name="path">The source file path.</param>
    /// <param name="markdown">The Markdown content.</param>
    /// <returns>The display name.</returns>
    private static string GetDocumentDisplayName(
            string path,
            string markdown)
    {
        MarkdownOutlineEntry? firstHeading =
            MarkdownOutlineParser.Parse(
                    markdown)
                .OrderBy(heading =>
                    heading.Offset)
                .FirstOrDefault();

        return firstHeading?.Title ??
               Path.GetFileNameWithoutExtension(
                   path);
    }

    /// <summary>
    /// Resolves one Markdown target relative to its owner document while preserving an optional fragment.
    /// </summary>
    /// <param name="ownerDocumentPath">The owner Markdown file.</param>
    /// <param name="rawTarget">The raw Markdown target.</param>
    /// <returns>The local-link resolution.</returns>
    private static LocalLinkResolution ResolveLocalLink(
            string ownerDocumentPath,
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
            return new LocalLinkResolution(
                false,
                null,
                string.Empty);
        }

        if (target.StartsWith(
                "<",
                StringComparison.Ordinal))
        {
            int close =
                target.IndexOf(
                    '>');

            if (close >
                1)
            {
                target =
                    target[1..close];
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
                out Uri? absoluteUri))
        {
            if (!absoluteUri.IsFile)
            {
                return new LocalLinkResolution(
                    false,
                    null,
                    string.Empty);
            }

            return new LocalLinkResolution(
                true,
                Path.GetFullPath(
                    absoluteUri.LocalPath),
                absoluteUri.Fragment);
        }

        string fragment =
            string.Empty;
        int fragmentIndex =
            target.IndexOf(
                '#');

        if (fragmentIndex >=
            0)
        {
            fragment =
                target[fragmentIndex..];
            target =
                target[..fragmentIndex];
        }

        int queryIndex =
            target.IndexOf(
                '?');

        if (queryIndex >=
            0)
        {
            target =
                target[..queryIndex];
        }

        if (string.IsNullOrWhiteSpace(
                target))
        {
            return new LocalLinkResolution(
                false,
                null,
                fragment);
        }

        try
        {
            string decoded =
                Uri.UnescapeDataString(
                    target)
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar);

            string? ownerDirectory =
                Path.GetDirectoryName(
                    ownerDocumentPath);

            if (string.IsNullOrWhiteSpace(
                    ownerDirectory))
            {
                return new LocalLinkResolution(
                    false,
                    null,
                    fragment);
            }

            string fullPath =
                Path.GetFullPath(
                    Path.IsPathRooted(
                            decoded)
                        ? decoded
                        : Path.Combine(
                            ownerDirectory,
                            decoded));

            return new LocalLinkResolution(
                true,
                fullPath,
                fragment);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            UriFormatException)
        {
            return new LocalLinkResolution(
                false,
                null,
                fragment);
        }
    }

    /// <summary>
    /// Builds a collision-resistant attachment file name while retaining the readable original name.
    /// </summary>
    /// <param name="sourcePath">The attachment source path.</param>
    /// <param name="usedNames">Names already assigned in this export.</param>
    /// <returns>The unique export file name.</returns>
    private static string BuildUniqueAttachmentName(
            string sourcePath,
            ISet<string> usedNames)
    {
        string originalName =
            WindowsPathRules.SanitizeSegment(
                Path.GetFileName(
                    sourcePath));
        string candidate =
            originalName;

        if (usedNames.Add(
                candidate))
        {
            return candidate;
        }

        string stem =
            Path.GetFileNameWithoutExtension(
                originalName);
        string extension =
            Path.GetExtension(
                originalName);
        string hash =
            Convert
                .ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            Path.GetFullPath(
                                sourcePath)
                                .ToUpperInvariant())))
                .ToLowerInvariant()[..10];

        candidate =
            stem +
            "-" +
            hash +
            extension;

        int suffix =
            2;

        while (!usedNames.Add(
                   candidate))
        {
            candidate =
                stem +
                "-" +
                hash +
                "-" +
                suffix +
                extension;
            suffix++;
        }

        return candidate;
    }

    /// <summary>
    /// Copies one file asynchronously without changing the source.
    /// </summary>
    /// <param name="sourcePath">The source file.</param>
    /// <param name="destinationPath">The destination file.</param>
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
    /// Gets a MIME type suitable for a standalone data URI.
    /// </summary>
    /// <param name="path">The attachment path.</param>
    /// <returns>The MIME type.</returns>
    private static string GetMimeType(
            string path)
    {
        return Path.GetExtension(
                path)
            .ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Builds a stable HTML anchor for one exported document.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The anchor identifier.</returns>
    private static string DocumentAnchor(
            ExportDocumentSnapshot document)
    {
        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    Path.GetFullPath(
                        document.SourcePath)
                        .ToUpperInvariant()));

        return "doc-" +
               Convert
                   .ToHexString(
                       hash)
                   .ToLowerInvariant()[..12];
    }

    /// <summary>
    /// Builds a stable HTML anchor for one project section.
    /// </summary>
    /// <param name="sectionId">The section identifier.</param>
    /// <returns>The anchor identifier.</returns>
    private static string SectionAnchor(
            Guid sectionId) =>
        sectionId.ToString(
            "N");

    /// <summary>
    /// Escapes a value for HTML element content.
    /// </summary>
    /// <param name="value">The source value.</param>
    /// <returns>The escaped HTML.</returns>
    private static string Html(
            string value) =>
        EscapeHtml(
            value);

    /// <summary>
    /// Escapes a value for an HTML attribute.
    /// </summary>
    /// <param name="value">The source value.</param>
    /// <returns>The escaped attribute value.</returns>
    private static string HtmlAttribute(
            string value) =>
        EscapeHtml(
            value);

    /// <summary>
    /// Escapes HTML locally without relying on any network-oriented framework namespace.
    /// </summary>
    /// <param name="value">The source text.</param>
    /// <returns>The escaped HTML text.</returns>
    private static string EscapeHtml(
            string value)
    {
        StringBuilder builder =
            new StringBuilder(
                value.Length);

        foreach (char character in value)
        {
            switch (character)
            {
                case '&':
                    builder.Append(
                        "&amp;");
                    break;

                case '<':
                    builder.Append(
                        "&lt;");
                    break;

                case '>':
                    builder.Append(
                        "&gt;");
                    break;

                case '"':
                    builder.Append(
                        "&quot;");
                    break;

                case '\'':
                    builder.Append(
                        "&#39;");
                    break;

                default:
                    builder.Append(
                        character);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes XML text for Open XML parts.
    /// </summary>
    /// <param name="value">The source value.</param>
    /// <returns>The escaped XML text.</returns>
    private static string Xml(
            string value) =>
        SecurityElement.Escape(
            value) ??
        string.Empty;

    /// <summary>
    /// Makes a relative Markdown path portable across Windows and other Markdown viewers.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The escaped slash-separated path.</returns>
    private static string EscapeMarkdownPath(
            string path) =>
        NormalizeRelativePath(
            path)
            .Replace(
                " ",
                "%20",
                StringComparison.Ordinal);

    /// <summary>
    /// Converts a path to slash-separated relative form.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizeRelativePath(
            string path) =>
        path.Replace(
                Path.DirectorySeparatorChar,
                '/')
            .Replace(
                Path.AltDirectorySeparatorChar,
                '/');

    /// <summary>
    /// Converts a workspace path to its normalized workspace-relative representation.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <returns>The workspace-relative path.</returns>
    private string ToWorkspaceRelativePath(
            string path)
    {
        string fullPath =
            Path.GetFullPath(
                path);

        if (!IsInsideOrEqual(
                fullPath,
                _workspaceRoot))
        {
            throw new InvalidDataException(
                "Le chemin du projet sort du workspace.");
        }

        return NormalizeRelativePath(
            Path.GetRelativePath(
                _workspaceRoot,
                fullPath));
    }

    /// <summary>
    /// Determines whether a path is equal to or below a directory.
    /// </summary>
    /// <param name="candidate">The candidate path.</param>
    /// <param name="root">The containing directory.</param>
    /// <returns><see langword="true"/> when the path is contained.</returns>
    private static bool IsInsideOrEqual(
            string candidate,
            string root)
    {
        string fullCandidate =
            Path.GetFullPath(
                candidate)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
        string fullRoot =
            Path.GetFullPath(
                root)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

        return string.Equals(
                   fullCandidate,
                   fullRoot,
                   StringComparison.OrdinalIgnoreCase) ||
               fullCandidate.StartsWith(
                   fullRoot +
                   Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ExportSnapshot(
        ProjectManifest Project,
        string ProjectRoot,
        List<ExportSectionSnapshot> Sections,
        List<ExportDocumentSnapshot> Documents,
        Dictionary<string, ExportDocumentSnapshot> SourceDocumentMap,
        Dictionary<string, ExportDocumentSnapshot> AliasMap,
        Dictionary<string, ExportAttachmentSnapshot> Attachments);

    private sealed record ExportSectionSnapshot(
        Guid SectionId,
        string Name,
        int Order,
        string SourceDirectory);

    private sealed record ExportDocumentSnapshot(
        Guid SectionId,
        string SectionName,
        string SourcePath,
        string RelativeWithinSection,
        string ExportRelativePath,
        string DisplayName,
        string Markdown);

    private sealed record ExportAttachmentSnapshot(
        string SourcePath,
        string ExportFileName,
        long Length,
        bool IsInsideProject);

    private sealed record LocalLinkResolution(
        bool IsLocal,
        string? FullPath,
        string Fragment);
}
