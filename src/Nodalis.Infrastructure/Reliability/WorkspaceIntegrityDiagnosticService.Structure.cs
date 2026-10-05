using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Reliability;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Reliability;

public sealed partial class WorkspaceIntegrityDiagnosticService
{
    /// <summary>
    /// Scans one application directory and its nested modules and projects.
    /// </summary>
    /// <param name="applicationDirectory">The physical application directory.</param>
    /// <param name="identities">The collected stable identities.</param>
    /// <param name="applications">The collected application manifests.</param>
    /// <param name="modules">The collected module manifests.</param>
    /// <param name="projects">The collected project manifests.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the scan.</returns>
    private async Task ScanApplicationAsync(
            string applicationDirectory,
            ICollection<IdentityEntry> identities,
            ICollection<ApplicationEntry> applications,
            ICollection<ModuleEntry> modules,
            ICollection<ProjectEntry> projects,
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string manifestPath = Path.Combine(
            applicationDirectory,
            WorkspaceLayout.ApplicationManifestFileName);

        ApplicationManifest? manifest = await ReadManifestAsync<ApplicationManifest>(
            manifestPath,
            "APPLICATION_MANIFEST_MISSING",
            "APPLICATION_MANIFEST_INVALID",
            "Le dossier d'application ne contient pas de .application.json.",
            "Le manifest d'application est illisible ou corrompu.",
            issues,
            cancellationToken);

        Guid? applicationId = manifest?.Id;

        if (manifest is not null)
        {
            applications.Add(new ApplicationEntry
            {
                Manifest = manifest,
                DirectoryPath = applicationDirectory
            });

            AddIdentity(
                identities,
                manifest.Id,
                "application",
                manifest.Name,
                NormalizeRelativePath(applicationDirectory),
                "workspace");
        }

        string modulesRoot = Path.Combine(
            applicationDirectory,
            WorkspaceLayout.ModulesDirectoryName);

        if (Directory.Exists(modulesRoot))
        {
            await ScanModulesRootAsync(
                modulesRoot,
                applicationId,
                null,
                identities,
                modules,
                projects,
                issues,
                cancellationToken);
        }

        string projectsRoot = Path.Combine(
            applicationDirectory,
            WorkspaceLayout.ProjectsDirectoryName);

        if (Directory.Exists(projectsRoot))
        {
            await ScanProjectsRootAsync(
                projectsRoot,
                applicationId,
                null,
                null,
                identities,
                projects,
                issues,
                cancellationToken);
        }
    }

    /// <summary>
    /// Scans all direct modules under one physical Modules directory.
    /// </summary>
    /// <param name="modulesRoot">The Modules directory.</param>
    /// <param name="physicalApplicationId">The application implied by the physical location.</param>
    /// <param name="physicalParentModuleId">The parent module implied by the physical location.</param>
    /// <param name="identities">The collected identities.</param>
    /// <param name="modules">The collected module manifests.</param>
    /// <param name="projects">The collected project manifests.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the scan.</returns>
    private async Task ScanModulesRootAsync(
            string modulesRoot,
            Guid? physicalApplicationId,
            Guid? physicalParentModuleId,
            ICollection<IdentityEntry> identities,
            ICollection<ModuleEntry> modules,
            ICollection<ProjectEntry> projects,
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
    {
        foreach (string moduleDirectory in EnumerateChildDirectories(
                     modulesRoot,
                     issues))
        {
            await ScanModuleAsync(
                moduleDirectory,
                physicalApplicationId,
                physicalParentModuleId,
                identities,
                modules,
                projects,
                issues,
                cancellationToken);
        }
    }

    /// <summary>
    /// Scans one module directory, validates its physical scope and descends into children.
    /// </summary>
    /// <param name="moduleDirectory">The physical module directory.</param>
    /// <param name="physicalApplicationId">The application implied by the physical location.</param>
    /// <param name="physicalParentModuleId">The parent module implied by the physical location.</param>
    /// <param name="identities">The collected identities.</param>
    /// <param name="modules">The collected module manifests.</param>
    /// <param name="projects">The collected project manifests.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the scan.</returns>
    private async Task ScanModuleAsync(
            string moduleDirectory,
            Guid? physicalApplicationId,
            Guid? physicalParentModuleId,
            ICollection<IdentityEntry> identities,
            ICollection<ModuleEntry> modules,
            ICollection<ProjectEntry> projects,
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string manifestPath = Path.Combine(
            moduleDirectory,
            WorkspaceLayout.ModuleManifestFileName);

        ModuleManifest? manifest = await ReadManifestAsync<ModuleManifest>(
            manifestPath,
            "MODULE_MANIFEST_MISSING",
            "MODULE_MANIFEST_INVALID",
            "Le dossier de module ne contient pas de .module.json.",
            "Le manifest de module est illisible ou corrompu.",
            issues,
            cancellationToken);

        Guid? moduleId = manifest?.Id;

        if (manifest is not null)
        {
            modules.Add(new ModuleEntry
            {
                Manifest = manifest,
                DirectoryPath = moduleDirectory
            });

            AddIdentity(
                identities,
                manifest.Id,
                "module",
                manifest.Name,
                NormalizeRelativePath(moduleDirectory),
                $"module:{manifest.ApplicationId:D}:{manifest.ParentModuleId?.ToString("D") ?? "-"}");

            if (physicalApplicationId is Guid expectedApplicationId &&
                manifest.ApplicationId != expectedApplicationId)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "SCOPE_COLLISION",
                    $"Le module déclare l'application {manifest.ApplicationId:D}, mais son emplacement appartient à {expectedApplicationId:D}.",
                    NormalizeRelativePath(manifestPath),
                    manifest.Id));
            }

            if (manifest.ParentModuleId != physicalParentModuleId)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "SCOPE_COLLISION",
                    "Le parent logique du module ne correspond pas à son emplacement physique.",
                    NormalizeRelativePath(manifestPath),
                    manifest.Id));
            }
        }

        string nestedModulesRoot = Path.Combine(
            moduleDirectory,
            WorkspaceLayout.ModulesDirectoryName);

        if (Directory.Exists(nestedModulesRoot))
        {
            await ScanModulesRootAsync(
                nestedModulesRoot,
                physicalApplicationId,
                moduleId,
                identities,
                modules,
                projects,
                issues,
                cancellationToken);
        }

        string projectsRoot = Path.Combine(
            moduleDirectory,
            WorkspaceLayout.ProjectsDirectoryName);

        if (Directory.Exists(projectsRoot))
        {
            await ScanProjectsRootAsync(
                projectsRoot,
                physicalApplicationId,
                moduleId,
                null,
                identities,
                projects,
                issues,
                cancellationToken);
        }
    }

    /// <summary>
    /// Scans all direct projects under a Projects or Sous-projets directory.
    /// </summary>
    /// <param name="projectsRoot">The project collection directory.</param>
    /// <param name="physicalApplicationId">The application implied by the physical location.</param>
    /// <param name="physicalModuleId">The module implied by the physical location.</param>
    /// <param name="physicalParentProjectId">The parent project implied by the physical location.</param>
    /// <param name="identities">The collected identities.</param>
    /// <param name="projects">The collected project manifests.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the scan.</returns>
    private async Task ScanProjectsRootAsync(
            string projectsRoot,
            Guid? physicalApplicationId,
            Guid? physicalModuleId,
            Guid? physicalParentProjectId,
            ICollection<IdentityEntry> identities,
            ICollection<ProjectEntry> projects,
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
    {
        foreach (string projectDirectory in EnumerateChildDirectories(
                     projectsRoot,
                     issues))
        {
            await ScanProjectAsync(
                projectDirectory,
                physicalApplicationId,
                physicalModuleId,
                physicalParentProjectId,
                identities,
                projects,
                issues,
                cancellationToken);
        }
    }

    /// <summary>
    /// Scans one project manifest, its declared sections and nested sub-projects.
    /// </summary>
    /// <param name="projectDirectory">The physical project directory.</param>
    /// <param name="physicalApplicationId">The application implied by the physical location.</param>
    /// <param name="physicalModuleId">The module implied by the physical location.</param>
    /// <param name="physicalParentProjectId">The parent project implied by the physical location.</param>
    /// <param name="identities">The collected identities.</param>
    /// <param name="projects">The collected project manifests.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the scan.</returns>
    private async Task ScanProjectAsync(
            string projectDirectory,
            Guid? physicalApplicationId,
            Guid? physicalModuleId,
            Guid? physicalParentProjectId,
            ICollection<IdentityEntry> identities,
            ICollection<ProjectEntry> projects,
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string manifestPath = Path.Combine(
            projectDirectory,
            WorkspaceLayout.ProjectManifestFileName);

        ProjectManifest? manifest = await ReadManifestAsync<ProjectManifest>(
            manifestPath,
            "PROJECT_MANIFEST_MISSING",
            "PROJECT_MANIFEST_INVALID",
            "Le dossier de projet ne contient pas de .project.json.",
            "Le manifest de projet est illisible ou corrompu.",
            issues,
            cancellationToken);

        Guid? projectId = manifest?.Id;

        if (manifest is not null)
        {
            projects.Add(new ProjectEntry
            {
                Manifest = manifest,
                DirectoryPath = projectDirectory
            });

            AddIdentity(
                identities,
                manifest.Id,
                "project",
                manifest.Name,
                NormalizeRelativePath(projectDirectory),
                $"project:{manifest.ApplicationId:D}:{manifest.ModuleId?.ToString("D") ?? "-"}:{manifest.ParentProjectId?.ToString("D") ?? "-"}");

            if (physicalApplicationId is Guid expectedApplicationId &&
                manifest.ApplicationId != expectedApplicationId)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "SCOPE_COLLISION",
                    $"Le projet déclare l'application {manifest.ApplicationId:D}, mais son emplacement appartient à {expectedApplicationId:D}.",
                    NormalizeRelativePath(manifestPath),
                    manifest.Id));
            }

            if (manifest.ModuleId != physicalModuleId ||
                manifest.ParentProjectId != physicalParentProjectId)
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "SCOPE_COLLISION",
                    "Le module ou parent logique du projet ne correspond pas à son emplacement physique.",
                    NormalizeRelativePath(manifestPath),
                    manifest.Id));
            }

            global::System.Collections.Generic.HashSet<string> declaredSectionDirectories =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (SectionManifest section in manifest.Sections)
            {
                string expectedDirectory = Path.Combine(
                    projectDirectory,
                    WindowsPathRules.SanitizeSegment(section.Name));

                declaredSectionDirectories.Add(Path.GetFullPath(expectedDirectory));

                AddIdentity(
                    identities,
                    section.Id,
                    "section",
                    section.Name,
                    NormalizeRelativePath(expectedDirectory),
                    $"section:{manifest.Id:D}");

                if (!Directory.Exists(expectedDirectory))
                {
                    issues.Add(CreateIssue(
                        WorkspaceIntegritySeverity.Error,
                        "SECTION_DIRECTORY_MISSING",
                        $"La section « {section.Name} » est déclarée dans le projet mais son dossier est absent.",
                        NormalizeRelativePath(expectedDirectory),
                        section.Id));
                }
            }

            foreach (string childDirectory in EnumerateChildDirectories(
                         projectDirectory,
                         issues))
            {
                string childName = Path.GetFileName(childDirectory);

                if (childName.Equals(
                        WorkspaceLayout.SubProjectsDirectoryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!declaredSectionDirectories.Contains(
                        Path.GetFullPath(childDirectory)))
                {
                    issues.Add(CreateIssue(
                        WorkspaceIntegritySeverity.Warning,
                        "UNREFERENCED_SECTION_DIRECTORY",
                        "Ce dossier se trouve dans un projet mais n'est déclaré par aucune section du manifest.",
                        NormalizeRelativePath(childDirectory),
                        manifest.Id));
                }
            }
        }

        string subProjectsRoot = Path.Combine(
            projectDirectory,
            WorkspaceLayout.SubProjectsDirectoryName);

        if (Directory.Exists(subProjectsRoot))
        {
            await ScanProjectsRootAsync(
                subProjectsRoot,
                physicalApplicationId,
                physicalModuleId,
                projectId,
                identities,
                projects,
                issues,
                cancellationToken);
        }
    }

    /// <summary>
    /// Reads a required manifest while translating file and JSON failures into report findings.
    /// </summary>
    /// <typeparam name="T">The manifest type.</typeparam>
    /// <param name="path">The manifest path.</param>
    /// <param name="missingCode">The diagnostic code used when the file is absent.</param>
    /// <param name="invalidCode">The diagnostic code used when the file cannot be parsed.</param>
    /// <param name="missingMessage">The message used when the file is absent.</param>
    /// <param name="invalidMessage">The message used when the file cannot be parsed.</param>
    /// <param name="issues">The report findings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed manifest, or null when unavailable.</returns>
    private async Task<T?> ReadManifestAsync<T>(
            string path,
            string missingCode,
            string invalidCode,
            string missingMessage,
            string invalidMessage,
            ICollection<WorkspaceIntegrityIssue> issues,
            CancellationToken cancellationToken)
            where T : class
    {
        if (!File.Exists(path))
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                missingCode,
                missingMessage,
                NormalizeRelativePath(path)));

            return null;
        }

        try
        {
            return await AtomicJsonFile.ReadAsync<T>(
                path,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            JsonException)
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                invalidCode,
                $"{invalidMessage} {exception.Message}",
                NormalizeRelativePath(path)));

            return null;
        }
    }

    /// <summary>
    /// Detects empty identities, duplicate GUIDs and same-name collisions inside one logical scope.
    /// </summary>
    /// <param name="identities">The collected stable identities.</param>
    /// <param name="issues">The report findings.</param>
    private static void ValidateIdentityIntegrity(
            IReadOnlyCollection<IdentityEntry> identities,
            ICollection<WorkspaceIntegrityIssue> issues)
    {
        foreach (IdentityEntry identity in identities.Where(
                     identity => identity.Id == Guid.Empty))
        {
            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "EMPTY_GUID",
                $"L'identifiant de {identity.Kind} « {identity.Name} » est vide.",
                identity.RelativePath,
                identity.Id));
        }

        foreach (IGrouping<Guid, IdentityEntry> duplicate in identities
                     .Where(identity => identity.Id != Guid.Empty)
                     .GroupBy(identity => identity.Id)
                     .Where(group => group.Count() > 1))
        {
            string locations = string.Join(
                ", ",
                duplicate.Select(identity => identity.RelativePath));

            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "DUPLICATE_GUID",
                $"Le GUID {duplicate.Key:D} est utilisé par plusieurs entités : {locations}.",
                duplicate.First().RelativePath,
                duplicate.Key));
        }

        foreach (IGrouping<string, IdentityEntry> collision in identities
                     .GroupBy(
                         identity =>
                             identity.ScopeKey +
                             "\u001f" +
                             identity.Kind +
                             "\u001f" +
                             identity.Name,
                         StringComparer.CurrentCultureIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            IdentityEntry first = collision.First();

            issues.Add(CreateIssue(
                WorkspaceIntegritySeverity.Error,
                "SCOPE_COLLISION",
                $"Plusieurs {first.Kind}s portent le nom « {first.Name} » dans le même scope logique.",
                first.RelativePath,
                first.Id));
        }
    }

    /// <summary>
    /// Verifies that every manifest parent identifier resolves to a compatible entity.
    /// </summary>
    /// <param name="applications">The collected applications.</param>
    /// <param name="modules">The collected modules.</param>
    /// <param name="projects">The collected projects.</param>
    /// <param name="issues">The report findings.</param>
    private static void ValidateRelationships(
            IReadOnlyCollection<ApplicationEntry> applications,
            IReadOnlyCollection<ModuleEntry> modules,
            IReadOnlyCollection<ProjectEntry> projects,
            ICollection<WorkspaceIntegrityIssue> issues)
    {
        global::System.Collections.Generic.HashSet<Guid> applicationIds =
            applications.Select(entry => entry.Manifest.Id).ToHashSet();
        global::System.Collections.Generic.HashSet<Guid> moduleIds =
            modules.Select(entry => entry.Manifest.Id).ToHashSet();
        global::System.Collections.Generic.HashSet<Guid> projectIds =
            projects.Select(entry => entry.Manifest.Id).ToHashSet();

        foreach (ModuleEntry module in modules)
        {
            if (!applicationIds.Contains(module.Manifest.ApplicationId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "MODULE_APPLICATION_MISSING",
                    $"Le module référence une application inexistante ({module.Manifest.ApplicationId:D}).",
                    NormalizeStoredRelativePath(module.DirectoryPath),
                    module.Manifest.Id));
            }

            if (module.Manifest.ParentModuleId is Guid parentModuleId &&
                !moduleIds.Contains(parentModuleId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "MODULE_PARENT_MISSING",
                    $"Le module référence un parent inexistant ({parentModuleId:D}).",
                    NormalizeStoredRelativePath(module.DirectoryPath),
                    module.Manifest.Id));
            }
        }

        foreach (ProjectEntry project in projects)
        {
            if (!applicationIds.Contains(project.Manifest.ApplicationId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "PROJECT_APPLICATION_MISSING",
                    $"Le projet référence une application inexistante ({project.Manifest.ApplicationId:D}).",
                    NormalizeStoredRelativePath(project.DirectoryPath),
                    project.Manifest.Id));
            }

            if (project.Manifest.ModuleId is Guid moduleId &&
                !moduleIds.Contains(moduleId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "PROJECT_MODULE_MISSING",
                    $"Le projet référence un module inexistant ({moduleId:D}).",
                    NormalizeStoredRelativePath(project.DirectoryPath),
                    project.Manifest.Id));
            }

            if (project.Manifest.ParentProjectId is Guid parentProjectId &&
                !projectIds.Contains(parentProjectId))
            {
                issues.Add(CreateIssue(
                    WorkspaceIntegritySeverity.Error,
                    "PROJECT_PARENT_MISSING",
                    $"Le projet référence un parent inexistant ({parentProjectId:D}).",
                    NormalizeStoredRelativePath(project.DirectoryPath),
                    project.Manifest.Id));
            }
        }
    }
}
