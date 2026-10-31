using System.Text;
using Nodalis.Core.Abstractions;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Core.Templates;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;
using Nodalis.Infrastructure.Templates;

namespace Nodalis.Infrastructure.Projects;

public sealed class FileSystemProjectCreator : IProjectCreator
{
    private readonly FileSystemTemplateStore _templateStore;

    /// <summary>
    /// Initializes a new instance of <see cref="FileSystemProjectCreator"/>.
    /// </summary>
    /// <param name="workspaceRoot">The <c>workspaceRoot</c> value.</param>
    public FileSystemProjectCreator(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _templateStore = new FileSystemTemplateStore(workspaceRoot);
    }

    /// <summary>
    /// Performs the <c>CreateAsync</c> operation.
    /// </summary>
    /// <param name="request">The <c>request</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<ProjectCreationResult> CreateAsync(
            ProjectCreationRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.Target);

        string name = request.Name.Trim();
        global::Nodalis.Core.Templates.ProjectProfileCatalog profileCatalog = await _templateStore.LoadProjectProfilesAsync(
            cancellationToken);

        global::Nodalis.Core.Templates.ProjectProfileDefinition profile = profileCatalog.Profiles.SingleOrDefault(
            candidate => candidate.Complexity == request.Complexity)
            ?? throw new InvalidDataException(
                $"No project profile exists for '{request.Complexity}'.");

        ValidateProfile(profile);

        string containerDirectory = request.Target.ParentProjectId is null
            ? Path.Combine(
                request.Target.ParentDirectory,
                WorkspaceLayout.ProjectsDirectoryName)
            : Path.Combine(
                request.Target.ParentDirectory,
                WorkspaceLayout.SubProjectsDirectoryName);

        Directory.CreateDirectory(containerDirectory);

        string finalDirectory = WindowsPathRules.GetUniqueDirectoryPath(
            containerDirectory,
            name);

        string stagingDirectory = Path.Combine(
            containerDirectory,
            $".nodalis-project-{Guid.NewGuid():N}.tmp");

        global::System.Guid projectId = Guid.NewGuid();
        global::System.Collections.Generic.List<string> createdPaths = new List<string>();

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            global::System.Collections.Generic.List<global::Nodalis.Core.Domain.SectionManifest> sections = profile.Sections
                .OrderBy(section => section.Order)
                .Select(section => new SectionManifest
                {
                    Id = Guid.NewGuid(),
                    Name = section.Name.Trim(),
                    Order = section.Order,
                    IsSingleton = section.IsSingleton,
                    TemplateKey = string.IsNullOrWhiteSpace(section.TemplateKey)
                        ? null
                        : section.TemplateKey.Trim(),
                    RelativeDirectory = section.RelativeDirectory
                })
                .ToList();

            global::Nodalis.Core.Domain.ProjectManifest project = new ProjectManifest
            {
                Id = projectId,
                Name = name,
                ApplicationId = request.Target.ApplicationId,
                ModuleId = request.Target.ModuleId,
                ParentProjectId = request.Target.ParentProjectId,
                InitialComplexity = request.Complexity,
                Sections = sections
            };

            await AtomicJsonFile.WriteAsync(
                Path.Combine(
                    stagingDirectory,
                    WorkspaceLayout.ProjectManifestFileName),
                project,
                cancellationToken);

            await AtomicFileWriter.WriteAllTextAsync(
                Path.Combine(
                    stagingDirectory,
                    WorkspaceLayout.GlobalQuickNotesFileName),
                "# Notes rapides\n\n",
                cancellationToken);

            bool ultraSimple = request.Complexity == ProjectComplexity.UltraSimple;
            string initialDocumentName = ultraSimple
                ? "Fiche de Maintenance.md"
                : "Présentation.md";
            string initialDocumentPath = Path.Combine(
                stagingDirectory,
                initialDocumentName);

            if (ultraSimple)
            {
                Dictionary<string, string> variables = MarkdownTemplateRenderer.CreateStandardVariables(
                    name,
                    projectId,
                    DateTimeOffset.Now,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["project.name"] = name,
                        ["project.id"] = projectId.ToString("D"),
                        ["application.name"] = request.Target.ApplicationName,
                        ["module.name"] = request.Target.ModuleName ?? string.Empty
                    });

                string maintenanceContent = await _templateStore.RenderAsync(
                    "maintenance",
                    variables,
                    cancellationToken);

                await AtomicFileWriter.WriteAllTextAsync(
                    initialDocumentPath,
                    maintenanceContent,
                    cancellationToken);
            }
            else
            {
                await AtomicFileWriter.WriteAllTextAsync(
                    initialDocumentPath,
                    BuildOverview(request, project),
                    cancellationToken);
            }

            foreach (global::Nodalis.Core.Domain.SectionManifest section in sections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? rootDocumentFileName =
                    ProjectSingletonDocumentLayout.GetRootDocumentFileName(
                        section.TemplateKey);

                string documentPath = rootDocumentFileName is null
                    ? Path.Combine(
                        ProjectSectionDirectoryLayout.Resolve(stagingDirectory, section),
                        WindowsPathRules.SanitizeSegment(section.Name) + ".md")
                    : ProjectSingletonDocumentLayout.Resolve(
                        stagingDirectory, section, migrateIfSafe: false).PreferredPath;

                string sectionDirectory = Path.GetDirectoryName(documentPath)
                    ?? throw new InvalidDataException("Destination de section invalide.");

                Directory.CreateDirectory(sectionDirectory);

                if (section.TemplateKey is null)
                {
                    continue;
                }

                global::System.Collections.Generic.Dictionary<string, string> variables = MarkdownTemplateRenderer.CreateStandardVariables(
                    section.Name,
                    section.Id,
                    DateTimeOffset.Now,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["project.name"] = name,
                        ["project.id"] = projectId.ToString("D"),
                        ["application.name"] = request.Target.ApplicationName,
                        ["module.name"] = request.Target.ModuleName ?? string.Empty
                    });

                string content = await _templateStore.RenderAsync(
                    section.TemplateKey,
                    variables,
                    cancellationToken);

                await AtomicFileWriter.WriteAllTextAsync(
                    documentPath,
                    content,
                    cancellationToken);
            }

            Directory.Move(stagingDirectory, finalDirectory);

            createdPaths.Add(finalDirectory);

            return new ProjectCreationResult
            {
                Project = project,
                ProjectDirectory = finalDirectory,
                OverviewFilePath = Path.Combine(
                    finalDirectory,
                    initialDocumentName),
                CreatedPaths = createdPaths
            };
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            throw;
        }
    }

    /// <summary>
    /// Performs the <c>BuildOverview</c> operation.
    /// </summary>
    /// <param name="request">The <c>request</c> value.</param>
    /// <param name="project">The <c>project</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string BuildOverview(
            ProjectCreationRequest request,
            ProjectManifest project)
    {
        global::System.Text.StringBuilder builder = new StringBuilder();

        builder.AppendLine($"# {project.Name}");
        builder.AppendLine();
        builder.AppendLine($"**Application :** {request.Target.ApplicationName}");

        if (!string.IsNullOrWhiteSpace(request.Target.ModuleName))
        {
            builder.AppendLine($"**Module :** {request.Target.ModuleName}");
        }

        if (!string.IsNullOrWhiteSpace(request.Target.ParentProjectName))
        {
            builder.AppendLine($"**Projet parent :** {request.Target.ParentProjectName}");
        }

        builder.AppendLine($"**Niveau initial :** {project.InitialComplexity}");
        builder.AppendLine($"**Créé le :** {DateTimeOffset.Now:yyyy-MM-dd}");
        builder.AppendLine();
        builder.AppendLine("## Présentation");
        builder.AppendLine();
        builder.AppendLine("## Liens métier");
        builder.AppendLine();

        string[] links = request.BusinessLinks
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Select(link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (links.Length == 0)
        {
            builder.AppendLine("_Aucun lien renseigné._");
        }
        else
        {
            foreach (string link in links)
            {
                builder.AppendLine($"- {link}");
            }
        }

        builder.AppendLine();
        return builder.ToString();
    }

    /// <summary>
    /// Performs the <c>ValidateProfile</c> operation.
    /// </summary>
    /// <param name="profile">The <c>profile</c> value.</param>
    private static void ValidateProfile(ProjectProfileDefinition profile)
    {
        global::System.Linq.IGrouping<string, global::Nodalis.Core.Templates.ProjectSectionTemplateDefinition>? duplicate = profile.Sections
            .GroupBy(
                section => section.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException(
                $"Project profile '{profile.DisplayName}' contains duplicate section '{duplicate.Key}'.");
        }

        if (profile.Sections.Any(section => string.IsNullOrWhiteSpace(section.Name)))
        {
            throw new InvalidDataException(
                $"Project profile '{profile.DisplayName}' contains an unnamed section.");
        }

        HashSet<string> physicalDirectories = new(
            StringComparer.OrdinalIgnoreCase);

        foreach (ProjectSectionTemplateDefinition section in profile.Sections)
        {
            bool rootDocument = ProjectSingletonDocumentLayout
                .GetRootDocumentFileName(section.TemplateKey) is not null;

            if (rootDocument && section.RelativeDirectory is not null)
            {
                if (!string.Equals(section.TemplateKey, "milestones",
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"La section « {section.Name} » est un document racine protégé.");
                }

                ProjectSectionDirectoryLayout.ValidateRelativeDirectory(
                    section.RelativeDirectory);
            }

            if (rootDocument)
            {
                continue;
            }

            string directory = section.RelativeDirectory is null
                ? WindowsPathRules.SanitizeSegment(section.Name)
                : ProjectSectionDirectoryLayout.ValidateRelativeDirectory(
                    section.RelativeDirectory);

            if (!physicalDirectories.Add(directory))
            {
                throw new InvalidDataException(
                    $"Le profil « {profile.DisplayName} » contient deux sections au même emplacement « {directory} ».");
            }
        }
    }
}
