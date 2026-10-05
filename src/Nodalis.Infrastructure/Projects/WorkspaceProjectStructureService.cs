using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Safely edits the filesystem and manifest representation of project sections.
/// </summary>
public sealed class WorkspaceProjectStructureService
{
    private readonly string _workspaceRoot;
    private readonly FileSystemTemplateStore _templateStore;
    private readonly WorkspaceLinkIndexService _linkIndex;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkspaceProjectStructureService"/>.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root.</param>
    public WorkspaceProjectStructureService(
            string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workspaceRoot);

        _workspaceRoot =
            Path.GetFullPath(
                workspaceRoot);
        _templateStore =
            new FileSystemTemplateStore(
                _workspaceRoot);
        _linkIndex =
            new WorkspaceLinkIndexService(
                _workspaceRoot);
    }

    /// <summary>
    /// Resolves the nearest project directory for a file or directory context.
    /// </summary>
    /// <param name="contextPath">The current file or directory.</param>
    /// <returns>The nearest project directory, or <see langword="null"/> outside a project.</returns>
    public string? GetProjectDirectoryForContext(
            string? contextPath)
    {
        if (string.IsNullOrWhiteSpace(
                contextPath))
        {
            return null;
        }

        string fullPath =
            Path.GetFullPath(
                contextPath);

        string? current =
            File.Exists(
                fullPath) ||
            Path.HasExtension(
                fullPath)
                ? Path.GetDirectoryName(
                    fullPath)
                : fullPath;

        while (!string.IsNullOrWhiteSpace(
                   current) &&
               IsInsideOrEqual(
                   current,
                   _workspaceRoot))
        {
            if (File.Exists(
                    Path.Combine(
                        current,
                        WorkspaceLayout.ProjectManifestFileName)))
            {
                return current;
            }

            if (string.Equals(
                    current,
                    _workspaceRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current =
                Directory.GetParent(
                    current)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Loads the editable project structure from the manifest and filesystem.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current project structure.</returns>
    public async Task<ProjectStructureState> GetStructureAsync(
            string projectDirectory,
            CancellationToken cancellationToken = default)
    {
        string directory =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest manifest =
            await LoadManifestAsync(
                directory,
                cancellationToken);

        ProjectSectionState[] sections =
            manifest.Sections
                .OrderBy(section =>
                    section.Order)
                .ThenBy(
                    section => section.Name,
                    StringComparer.CurrentCultureIgnoreCase)
                .Select(section =>
                    CreateSectionState(
                        directory,
                        section))
                .ToArray();

        return new ProjectStructureState
        {
            Project =
                manifest,
            ProjectDirectory =
                directory,
            Sections =
                sections
        };
    }

    /// <summary>
    /// Loads templates that can initialize a newly added project section.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Templates ordered by category and display name.</returns>
    public async Task<IReadOnlyList<MarkdownTemplateDefinition>> GetAvailableTemplatesAsync(
            CancellationToken cancellationToken = default)
    {
        TemplateCatalog catalog =
            await _templateStore.LoadTemplateCatalogAsync(
                cancellationToken);

        return catalog.Templates
            .OrderBy(
                template => template.Category,
                StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(
                template => template.DisplayName,
                StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Adds a section and optionally initializes it from a Markdown template.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="name">The new section name.</param>
    /// <param name="templateKey">The optional template key.</param>
    /// <param name="isSingleton">Whether the section is intended to contain one canonical document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The newly created section.</returns>
    public async Task<ProjectSectionState> AddSectionAsync(
            string projectDirectory,
            string name,
            string? templateKey = null,
            bool isSingleton = false,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        string directory =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest manifest =
            await LoadManifestAsync(
                directory,
                cancellationToken);

        string normalizedName =
            name.Trim();

        ValidateUniqueSectionName(
            manifest,
            normalizedName,
            excludedId: null);

        string? normalizedTemplateKey =
            NormalizeOptional(
                templateKey);

        if (normalizedTemplateKey is not null)
        {
            TemplateCatalog catalog =
                await _templateStore.LoadTemplateCatalogAsync(
                    cancellationToken);

            if (!catalog.Templates.Any(template =>
                    string.Equals(
                        template.Key,
                        normalizedTemplateKey,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new KeyNotFoundException(
                    $"Le template « {normalizedTemplateKey} » est introuvable.");
            }
        }

        Guid sectionId =
            Guid.NewGuid();

        SectionManifest section =
            new SectionManifest
            {
                Id =
                    sectionId,
                Name =
                    normalizedName,
                Order =
                    GetNextOrder(
                        manifest.Sections),
                IsSingleton =
                    isSingleton,
                TemplateKey =
                    normalizedTemplateKey
            };

        string sectionDirectory =
            ResolveSectionDirectory(
                directory,
                section);

        if (Directory.Exists(
                sectionDirectory) ||
            File.Exists(
                sectionDirectory))
        {
            throw new IOException(
                $"Le chemin de section « {sectionDirectory} » existe déjà.");
        }

        Directory.CreateDirectory(
            sectionDirectory);

        try
        {
            if (normalizedTemplateKey is not null)
            {
                IReadOnlyDictionary<string, string> variables =
                    await CreateTemplateVariablesAsync(
                        directory,
                        manifest,
                        section,
                        cancellationToken);

                string rendered =
                    await _templateStore.RenderAsync(
                        normalizedTemplateKey,
                        variables,
                        cancellationToken);

                await AtomicFileWriter.WriteAllTextAsync(
                    Path.Combine(
                        sectionDirectory,
                        WindowsPathRules.SanitizeSegment(
                            normalizedName) +
                        ".md"),
                    rendered,
                    cancellationToken);
            }

            List<SectionManifest> sections =
                manifest.Sections
                    .Concat(
                        new[]
                        {
                            section
                        })
                    .OrderBy(item =>
                        item.Order)
                    .ToList();

            ProjectManifest updated =
                manifest with
                {
                    Sections =
                        sections
                };

            ValidateRequiredRoles(
                updated);

            await SaveManifestAsync(
                directory,
                updated,
                cancellationToken);
        }
        catch
        {
            if (Directory.Exists(
                    sectionDirectory))
            {
                Directory.Delete(
                    sectionDirectory,
                    recursive: true);
            }

            throw;
        }

        await RefreshDerivedIndexesAsync(
            cancellationToken);

        return CreateSectionState(
            directory,
            section);
    }

    /// <summary>
    /// Renames a section while preserving its stable identifier and template role.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="sectionId">The section identifier.</param>
    /// <param name="newName">The new display name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The renamed section.</returns>
    public async Task<ProjectSectionState> RenameSectionAsync(
            string projectDirectory,
            Guid sectionId,
            string newName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            newName);

        string directory =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest manifest =
            await LoadManifestAsync(
                directory,
                cancellationToken);

        SectionManifest section =
            FindSection(
                manifest,
                sectionId);

        string normalizedName =
            newName.Trim();

        ValidateUniqueSectionName(
            manifest,
            normalizedName,
            sectionId);

        string sourceDirectory =
            ResolveSectionDirectory(
                directory,
                section);

        SectionManifest renamed =
            section with
            {
                Name =
                    normalizedName
            };

        string destinationDirectory =
            ResolveSectionDirectory(
                directory,
                renamed);

        bool directoryMoved =
            !string.Equals(
                sourceDirectory,
                destinationDirectory,
                StringComparison.Ordinal);

        string? temporaryDirectory =
            null;

        if (directoryMoved)
        {
            if (!Directory.Exists(
                    sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"Le dossier de section « {sourceDirectory} » est introuvable.");
            }

            if (!string.Equals(
                    sourceDirectory,
                    destinationDirectory,
                    StringComparison.OrdinalIgnoreCase) &&
                (Directory.Exists(
                     destinationDirectory) ||
                 File.Exists(
                     destinationDirectory)))
            {
                throw new IOException(
                    $"Le chemin « {destinationDirectory} » existe déjà.");
            }

            if (string.Equals(
                    sourceDirectory,
                    destinationDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                temporaryDirectory =
                    Path.Combine(
                        directory,
                        $".nodalis-section-rename-{Guid.NewGuid():N}.tmp");

                Directory.Move(
                    sourceDirectory,
                    temporaryDirectory);
                Directory.Move(
                    temporaryDirectory,
                    destinationDirectory);
                temporaryDirectory =
                    null;
            }
            else
            {
                Directory.Move(
                    sourceDirectory,
                    destinationDirectory);
            }
        }

        try
        {
            ProjectManifest updated =
                manifest with
                {
                    Sections =
                        manifest.Sections
                            .Select(item =>
                                item.Id == sectionId
                                    ? renamed
                                    : item)
                            .ToList()
                };

            ValidateRequiredRoles(
                updated);

            await SaveManifestAsync(
                directory,
                updated,
                cancellationToken);
        }
        catch
        {
            if (directoryMoved &&
                Directory.Exists(
                    destinationDirectory) &&
                !Directory.Exists(
                    sourceDirectory))
            {
                Directory.Move(
                    destinationDirectory,
                    sourceDirectory);
            }

            if (temporaryDirectory is not null &&
                Directory.Exists(
                    temporaryDirectory) &&
                !Directory.Exists(
                    sourceDirectory))
            {
                Directory.Move(
                    temporaryDirectory,
                    sourceDirectory);
            }

            throw;
        }

        await RefreshDerivedIndexesAsync(
            cancellationToken);

        return CreateSectionState(
            directory,
            renamed);
    }

    /// <summary>
    /// Reorders every manifest section using the supplied complete identifier sequence.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="orderedSectionIds">All section identifiers in the desired order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task ReorderSectionsAsync(
            string projectDirectory,
            IReadOnlyList<Guid> orderedSectionIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            orderedSectionIds);

        string directory =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest manifest =
            await LoadManifestAsync(
                directory,
                cancellationToken);

        Guid[] existingIds =
            manifest.Sections
                .Select(section =>
                    section.Id)
                .OrderBy(id =>
                    id)
                .ToArray();

        Guid[] requestedIds =
            orderedSectionIds
                .OrderBy(id =>
                    id)
                .ToArray();

        if (existingIds.Length !=
                requestedIds.Length ||
            !existingIds.SequenceEqual(
                requestedIds))
        {
            throw new InvalidDataException(
                "Le nouvel ordre doit contenir exactement toutes les sections du projet.");
        }

        Dictionary<Guid, SectionManifest> byId =
            manifest.Sections
                .ToDictionary(section =>
                    section.Id);

        List<SectionManifest> reordered =
            new List<SectionManifest>(
                orderedSectionIds.Count);

        for (int index = 0;
             index < orderedSectionIds.Count;
             index++)
        {
            SectionManifest section =
                byId[orderedSectionIds[index]];

            reordered.Add(
                section with
                {
                    Order =
                        (index + 1) *
                        10
                });
        }

        ProjectManifest updated =
            manifest with
            {
                Sections =
                    reordered
            };

        ValidateRequiredRoles(
            updated);

        await SaveManifestAsync(
            directory,
            updated,
            cancellationToken);
    }

    /// <summary>
    /// Moves one Markdown document from any managed section into another managed section.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="documentPath">The Markdown document to move.</param>
    /// <param name="targetSectionId">The target section identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The document's new absolute path.</returns>
    public async Task<string> MoveDocumentAsync(
            string projectDirectory,
            string documentPath,
            Guid targetSectionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            documentPath);

        string directory =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest manifest =
            await LoadManifestAsync(
                directory,
                cancellationToken);

        string source =
            Path.GetFullPath(
                documentPath);

        if (!File.Exists(
                source))
        {
            throw new FileNotFoundException(
                "Le document à déplacer est introuvable.",
                source);
        }

        if (!string.Equals(
                Path.GetExtension(
                    source),
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Seuls les documents Markdown peuvent être déplacés entre sections.");
        }

        SectionManifest? sourceSection =
            manifest.Sections.FirstOrDefault(section =>
                IsInsideOrEqual(
                    source,
                    ResolveSectionDirectory(
                        directory,
                        section)));

        if (sourceSection is null)
        {
            throw new InvalidOperationException(
                "Le document n'appartient à aucune section gérée par le projet.");
        }

        SectionManifest targetSection =
            FindSection(
                manifest,
                targetSectionId);

        if (sourceSection.Id ==
            targetSection.Id)
        {
            return source;
        }

        string targetDirectory =
            ResolveSectionDirectory(
                directory,
                targetSection);

        Directory.CreateDirectory(
            targetDirectory);

        string destination =
            Path.Combine(
                targetDirectory,
                Path.GetFileName(
                    source));

        if (File.Exists(
                destination) ||
            Directory.Exists(
                destination))
        {
            throw new IOException(
                $"Un élément nommé « {Path.GetFileName(source)} » existe déjà dans « {targetSection.Name} ».");
        }

        File.Move(
            source,
            destination);

        try
        {
            await RefreshDerivedIndexesAsync(
                cancellationToken);
        }
        catch
        {
            if (File.Exists(
                    destination) &&
                !File.Exists(
                    source))
            {
                File.Move(
                    destination,
                    source);
            }

            throw;
        }

        return destination;
    }

    /// <summary>
    /// Removes an optional section. Non-empty content must be moved explicitly first.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="sectionId">The section to remove.</param>
    /// <param name="moveContentsToSectionId">The explicit destination for remaining content, or <see langword="null"/> when the section must already be empty.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task RemoveSectionAsync(
            string projectDirectory,
            Guid sectionId,
            Guid? moveContentsToSectionId = null,
            CancellationToken cancellationToken = default)
    {
        string directory =
            ValidateProjectDirectory(
                projectDirectory);

        ProjectManifest manifest =
            await LoadManifestAsync(
                directory,
                cancellationToken);

        SectionManifest section =
            FindSection(
                manifest,
                sectionId);

        string? requiredRole =
            ProjectRequiredSectionPolicy.GetRequiredRole(
                section.TemplateKey);

        if (requiredRole is not null)
        {
            bool hasAnotherRole =
                manifest.Sections.Any(candidate =>
                    candidate.Id !=
                        section.Id &&
                    string.Equals(
                        ProjectRequiredSectionPolicy.GetRequiredRole(
                            candidate.TemplateKey),
                        requiredRole,
                        StringComparison.OrdinalIgnoreCase));

            if (!hasAnotherRole)
            {
                throw new InvalidOperationException(
                    $"La section « {section.Name} » assure le rôle minimal « {requiredRole} » et ne peut pas être supprimée.");
            }
        }

        string sourceDirectory =
            ResolveSectionDirectory(
                directory,
                section);

        string[] entries =
            Directory.Exists(
                    sourceDirectory)
                ? Directory
                    .EnumerateFileSystemEntries(
                        sourceDirectory)
                    .ToArray()
                : [];

        SectionManifest? targetSection =
            null;
        string? targetDirectory =
            null;

        if (entries.Length > 0)
        {
            if (moveContentsToSectionId is not Guid targetId)
            {
                throw new ProjectSectionNotEmptyException(
                    section.Name,
                    entries.Length);
            }

            targetSection =
                FindSection(
                    manifest,
                    targetId);

            if (targetSection.Id ==
                section.Id)
            {
                throw new InvalidOperationException(
                    "La section de destination doit être différente de la section supprimée.");
            }

            targetDirectory =
                ResolveSectionDirectory(
                    directory,
                    targetSection);

            Directory.CreateDirectory(
                targetDirectory);

            foreach (string entry in entries)
            {
                string destination =
                    Path.Combine(
                        targetDirectory,
                        Path.GetFileName(
                            entry));

                if (File.Exists(
                        destination) ||
                    Directory.Exists(
                        destination))
                {
                    throw new IOException(
                        $"Impossible de déplacer « {Path.GetFileName(entry)} » : un élément du même nom existe déjà dans « {targetSection.Name} ».");
                }
            }
        }

        List<(string Source, string Destination)> moved =
            new List<(string Source, string Destination)>();

        try
        {
            if (entries.Length > 0 &&
                targetDirectory is not null)
            {
                foreach (string entry in entries)
                {
                    string destination =
                        Path.Combine(
                            targetDirectory,
                            Path.GetFileName(
                                entry));

                    if (Directory.Exists(
                            entry))
                    {
                        Directory.Move(
                            entry,
                            destination);
                    }
                    else
                    {
                        File.Move(
                            entry,
                            destination);
                    }

                    moved.Add(
                        (entry, destination));
                }
            }

            if (Directory.Exists(
                    sourceDirectory))
            {
                Directory.Delete(
                    sourceDirectory,
                    recursive: false);
            }

            ProjectManifest updated =
                manifest with
                {
                    Sections =
                        manifest.Sections
                            .Where(candidate =>
                                candidate.Id !=
                                sectionId)
                            .OrderBy(candidate =>
                                candidate.Order)
                            .Select((candidate, index) =>
                                candidate with
                                {
                                    Order =
                                        (index + 1) *
                                        10
                                })
                            .ToList()
                };

            ValidateRequiredRoles(
                updated);

            await SaveManifestAsync(
                directory,
                updated,
                cancellationToken);
        }
        catch
        {
            Directory.CreateDirectory(
                sourceDirectory);

            foreach ((string source, string destination) in moved.AsEnumerable().Reverse())
            {
                if (Directory.Exists(
                        destination))
                {
                    Directory.Move(
                        destination,
                        source);
                }
                else if (File.Exists(
                             destination))
                {
                    File.Move(
                        destination,
                        source);
                }
            }

            throw;
        }

        await RefreshDerivedIndexesAsync(
            cancellationToken);
    }

    /// <summary>
    /// Loads one project manifest.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The project manifest.</returns>
    private static Task<ProjectManifest> LoadManifestAsync(
            string projectDirectory,
            CancellationToken cancellationToken) =>
        AtomicJsonFile.ReadAsync<ProjectManifest>(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            cancellationToken);

    /// <summary>
    /// Persists one project manifest atomically.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="manifest">The manifest to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static Task SaveManifestAsync(
            string projectDirectory,
            ProjectManifest manifest,
            CancellationToken cancellationToken) =>
        AtomicJsonFile.WriteAsync(
            Path.Combine(
                projectDirectory,
                WorkspaceLayout.ProjectManifestFileName),
            manifest,
            cancellationToken);

    /// <summary>
    /// Builds a filesystem-backed state for one manifest section.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="section">The section manifest.</param>
    /// <returns>The current section state.</returns>
    private static ProjectSectionState CreateSectionState(
            string projectDirectory,
            SectionManifest section)
    {
        string sectionDirectory =
            ResolveSectionDirectory(
                projectDirectory,
                section);

        string[] documents =
            Directory.Exists(
                    sectionDirectory)
                ? Directory
                    .EnumerateFiles(
                        sectionDirectory,
                        "*.md",
                        SearchOption.AllDirectories)
                    .Select(path =>
                        NormalizeRelativePath(
                            Path.GetRelativePath(
                                sectionDirectory,
                                path)))
                    .OrderBy(
                        path => path,
                        StringComparer.CurrentCultureIgnoreCase)
                    .ToArray()
                : [];

        return new ProjectSectionState
        {
            Id =
                section.Id,
            Name =
                section.Name,
            Order =
                section.Order,
            IsSingleton =
                section.IsSingleton,
            TemplateKey =
                section.TemplateKey,
            IsRequired =
                ProjectRequiredSectionPolicy.IsRequiredTemplateKey(
                    section.TemplateKey),
            DirectoryPath =
                sectionDirectory,
            Documents =
                documents
        };
    }

    /// <summary>
    /// Returns a section manifest by identifier.
    /// </summary>
    /// <param name="manifest">The project manifest.</param>
    /// <param name="sectionId">The requested identifier.</param>
    /// <returns>The matching section.</returns>
    private static SectionManifest FindSection(
            ProjectManifest manifest,
            Guid sectionId) =>
        manifest.Sections.FirstOrDefault(section =>
            section.Id ==
            sectionId) ??
        throw new KeyNotFoundException(
            $"La section « {sectionId:D} » est introuvable.");

    /// <summary>
    /// Validates that no other section has the same logical or sanitized directory name.
    /// </summary>
    /// <param name="manifest">The project manifest.</param>
    /// <param name="name">The requested name.</param>
    /// <param name="excludedId">A section identifier excluded during rename.</param>
    private static void ValidateUniqueSectionName(
            ProjectManifest manifest,
            string name,
            Guid? excludedId)
    {
        string sanitized =
            WindowsPathRules.SanitizeSegment(
                name);

        bool duplicate =
            manifest.Sections.Any(section =>
                section.Id !=
                    excludedId &&
                (string.Equals(
                     section.Name,
                     name,
                     StringComparison.CurrentCultureIgnoreCase) ||
                 string.Equals(
                     WindowsPathRules.SanitizeSegment(
                         section.Name),
                     sanitized,
                     StringComparison.OrdinalIgnoreCase)));

        if (duplicate)
        {
            throw new InvalidDataException(
                $"Une section utilisant le nom ou chemin « {name} » existe déjà.");
        }
    }

    /// <summary>
    /// Ensures all four required logical roles remain present.
    /// </summary>
    /// <param name="manifest">The project manifest.</param>
    private static void ValidateRequiredRoles(
            ProjectManifest manifest)
    {
        string[] presentRoles =
            manifest.Sections
                .Select(section =>
                    ProjectRequiredSectionPolicy.GetRequiredRole(
                        section.TemplateKey))
                .Where(role =>
                    role is not null)
                .Select(role =>
                    role!)
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        string[] missing =
            ProjectRequiredSectionPolicy.RequiredRoles
                .Where(role =>
                    !presentRoles.Contains(
                        role,
                        StringComparer.OrdinalIgnoreCase))
                .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                "Le projet doit conserver les quatre rôles minimaux. Manquant(s) : " +
                string.Join(
                    ", ",
                    missing) +
                ".");
        }
    }

    /// <summary>
    /// Returns the next stable manifest order.
    /// </summary>
    /// <param name="sections">Existing project sections.</param>
    /// <returns>The next order value.</returns>
    private static int GetNextOrder(
            IReadOnlyCollection<SectionManifest> sections) =>
        sections.Count == 0
            ? 10
            : sections.Max(section =>
                section.Order) +
              10;

    /// <summary>
    /// Resolves a section's physical directory from its logical name.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="section">The section manifest.</param>
    /// <returns>The absolute directory path.</returns>
    private static string ResolveSectionDirectory(
            string projectDirectory,
            SectionManifest section) =>
        Path.Combine(
            projectDirectory,
            WindowsPathRules.SanitizeSegment(
                section.Name));

    /// <summary>
    /// Creates standard template variables for a section added after project creation.
    /// </summary>
    /// <param name="projectDirectory">The project directory.</param>
    /// <param name="project">The project manifest.</param>
    /// <param name="section">The new section.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The template variables.</returns>
    private async Task<IReadOnlyDictionary<string, string>> CreateTemplateVariablesAsync(
            string projectDirectory,
            ProjectManifest project,
            SectionManifest section,
            CancellationToken cancellationToken)
    {
        string applicationName =
            string.Empty;
        string moduleName =
            string.Empty;

        string? current =
            Directory.GetParent(
                projectDirectory)?.FullName;

        while (!string.IsNullOrWhiteSpace(
                   current) &&
               IsInsideOrEqual(
                   current,
                   _workspaceRoot))
        {
            string applicationManifestPath =
                Path.Combine(
                    current,
                    WorkspaceLayout.ApplicationManifestFileName);

            if (applicationName.Length == 0 &&
                File.Exists(
                    applicationManifestPath))
            {
                ApplicationManifest application =
                    await AtomicJsonFile.ReadAsync<ApplicationManifest>(
                        applicationManifestPath,
                        cancellationToken);

                applicationName =
                    application.Name;
            }

            string moduleManifestPath =
                Path.Combine(
                    current,
                    WorkspaceLayout.ModuleManifestFileName);

            if (moduleName.Length == 0 &&
                File.Exists(
                    moduleManifestPath))
            {
                ModuleManifest module =
                    await AtomicJsonFile.ReadAsync<ModuleManifest>(
                        moduleManifestPath,
                        cancellationToken);

                moduleName =
                    module.Name;
            }

            current =
                Directory.GetParent(
                    current)?.FullName;
        }

        return MarkdownTemplateRenderer.CreateStandardVariables(
            section.Name,
            section.Id,
            DateTimeOffset.Now,
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["project.name"] =
                    project.Name,
                ["project.id"] =
                    project.Id.ToString(
                        "D"),
                ["application.name"] =
                    applicationName,
                ["module.name"] =
                    moduleName
            });
    }

    /// <summary>
    /// Refreshes the derived local link index after filesystem changes.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async Task RefreshDerivedIndexesAsync(
            CancellationToken cancellationToken)
    {
        await _linkIndex.RefreshAsync(
            cancellationToken);
    }

    /// <summary>
    /// Validates and normalizes a project directory inside the current workspace.
    /// </summary>
    /// <param name="projectDirectory">The candidate directory.</param>
    /// <returns>The normalized project directory.</returns>
    private string ValidateProjectDirectory(
            string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            projectDirectory);

        string directory =
            Path.GetFullPath(
                projectDirectory);

        if (!IsInsideOrEqual(
                directory,
                _workspaceRoot))
        {
            throw new InvalidOperationException(
                "Le projet doit appartenir au workspace courant.");
        }

        if (!Directory.Exists(
                directory) ||
            !File.Exists(
                Path.Combine(
                    directory,
                    WorkspaceLayout.ProjectManifestFileName)))
        {
            throw new DirectoryNotFoundException(
                $"Le projet « {directory} » est introuvable ou ne possède pas de manifest.");
        }

        return directory;
    }

    /// <summary>
    /// Tests whether a path is equal to or contained by a directory.
    /// </summary>
    /// <param name="candidate">The candidate path.</param>
    /// <param name="root">The root boundary.</param>
    /// <returns><see langword="true"/> when the path is inside the root.</returns>
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

    /// <summary>
    /// Normalizes an optional compact string.
    /// </summary>
    /// <param name="value">The optional value.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    private static string? NormalizeOptional(
            string? value) =>
        string.IsNullOrWhiteSpace(
            value)
            ? null
            : value.Trim();

    /// <summary>
    /// Normalizes a relative path for portable display.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>A slash-separated path.</returns>
    private static string NormalizeRelativePath(
            string path) =>
        path.Replace(
            Path.DirectorySeparatorChar,
            '/');
}
