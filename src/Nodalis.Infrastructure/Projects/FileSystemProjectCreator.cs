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

    public FileSystemProjectCreator(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _templateStore = new FileSystemTemplateStore(workspaceRoot);
    }

    public async Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.Target);

        var name = request.Name.Trim();
        var profileCatalog = await _templateStore.LoadProjectProfilesAsync(
            cancellationToken);

        var profile = profileCatalog.Profiles.SingleOrDefault(
            candidate => candidate.Complexity == request.Complexity)
            ?? throw new InvalidDataException(
                $"No project profile exists for '{request.Complexity}'.");

        ValidateProfile(profile);

        var containerDirectory = request.Target.ParentProjectId is null
            ? Path.Combine(
                request.Target.ParentDirectory,
                WorkspaceLayout.ProjectsDirectoryName)
            : Path.Combine(
                request.Target.ParentDirectory,
                WorkspaceLayout.SubProjectsDirectoryName);

        Directory.CreateDirectory(containerDirectory);

        var finalDirectory = WindowsPathRules.GetUniqueDirectoryPath(
            containerDirectory,
            name);

        var stagingDirectory = Path.Combine(
            containerDirectory,
            $".nodalis-project-{Guid.NewGuid():N}.tmp");

        var projectId = Guid.NewGuid();
        var createdPaths = new List<string>();

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            var sections = profile.Sections
                .OrderBy(section => section.Order)
                .Select(section => new SectionManifest
                {
                    Id = Guid.NewGuid(),
                    Name = section.Name.Trim(),
                    Order = section.Order,
                    IsSingleton = section.IsSingleton,
                    TemplateKey = string.IsNullOrWhiteSpace(section.TemplateKey)
                        ? null
                        : section.TemplateKey.Trim()
                })
                .ToList();

            var project = new ProjectManifest
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

            var overviewPath = Path.Combine(
                stagingDirectory,
                "Présentation.md");

            await AtomicFileWriter.WriteAllTextAsync(
                overviewPath,
                BuildOverview(request, project),
                cancellationToken);

            foreach (var section in sections)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sectionDirectory = Path.Combine(
                    stagingDirectory,
                    WindowsPathRules.SanitizeSegment(section.Name));

                Directory.CreateDirectory(sectionDirectory);

                if (section.TemplateKey is null)
                {
                    continue;
                }

                var variables = MarkdownTemplateRenderer.CreateStandardVariables(
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

                var content = await _templateStore.RenderAsync(
                    section.TemplateKey,
                    variables,
                    cancellationToken);

                await AtomicFileWriter.WriteAllTextAsync(
                    Path.Combine(
                        sectionDirectory,
                        WindowsPathRules.SanitizeSegment(section.Name) + ".md"),
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
                    "Présentation.md"),
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

    private static string BuildOverview(
        ProjectCreationRequest request,
        ProjectManifest project)
    {
        var builder = new StringBuilder();

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

        var links = request.BusinessLinks
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
            foreach (var link in links)
            {
                builder.AppendLine($"- {link}");
            }
        }

        builder.AppendLine();
        return builder.ToString();
    }

    private static void ValidateProfile(ProjectProfileDefinition profile)
    {
        var duplicate = profile.Sections
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
    }
}
