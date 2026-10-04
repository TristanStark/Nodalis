using Nodalis.Core.Validation;

namespace Nodalis.Core.Domain;

public sealed class WorkspaceCatalog
{
    private readonly Dictionary<Guid, ApplicationManifest> _applications = [];
    private readonly Dictionary<Guid, ModuleManifest> _modules = [];
    private readonly Dictionary<Guid, ProjectManifest> _projects = [];

    public IReadOnlyCollection<ApplicationManifest> Applications => _applications.Values.ToArray();

    public IReadOnlyCollection<ModuleManifest> Modules => _modules.Values.ToArray();

    public IReadOnlyCollection<ProjectManifest> Projects => _projects.Values.ToArray();

    public ApplicationManifest CreateApplication(string name)
    {
        var application = new ApplicationManifest
        {
            Id = Guid.NewGuid(),
            Name = NormalizeName(name)
        };

        _applications.Add(application.Id, application);
        return application;
    }

    public ApplicationManifest RenameApplication(Guid applicationId, string name)
    {
        var existing = GetApplication(applicationId);
        var updated = existing with { Name = NormalizeName(name) };
        _applications[applicationId] = updated;
        return updated;
    }

    public void DeleteApplication(Guid applicationId)
    {
        GetApplication(applicationId);

        if (_modules.Values.Any(module => module.ApplicationId == applicationId) ||
            _projects.Values.Any(project => project.ApplicationId == applicationId))
        {
            throw new DomainValidationException(
                "An application containing modules or projects cannot be deleted.");
        }

        _applications.Remove(applicationId);
    }

    public ModuleManifest CreateModule(
        Guid applicationId,
        string name,
        Guid? parentModuleId = null)
    {
        GetApplication(applicationId);

        var module = new ModuleManifest
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            ParentModuleId = parentModuleId,
            Name = NormalizeName(name)
        };

        HierarchyValidator.ValidateModule(
            module,
            Applications,
            Modules);

        _modules.Add(module.Id, module);
        return module;
    }

    public ModuleManifest RenameModule(Guid moduleId, string name)
    {
        var existing = GetModule(moduleId);
        var updated = existing with { Name = NormalizeName(name) };
        _modules[moduleId] = updated;
        return updated;
    }

    public ModuleManifest MoveModule(Guid moduleId, Guid? newParentModuleId)
    {
        var existing = GetModule(moduleId);
        var updated = existing with { ParentModuleId = newParentModuleId };

        HierarchyValidator.ValidateModule(
            updated,
            Applications,
            Modules);

        _modules[moduleId] = updated;
        return updated;
    }

    public void DeleteModule(Guid moduleId)
    {
        GetModule(moduleId);

        if (_modules.Values.Any(module => module.ParentModuleId == moduleId))
        {
            throw new DomainValidationException(
                "A module containing child modules cannot be deleted.");
        }

        if (_projects.Values.Any(project => project.ModuleId == moduleId))
        {
            throw new DomainValidationException(
                "A module containing projects cannot be deleted.");
        }

        _modules.Remove(moduleId);
    }

    public ProjectManifest CreateProject(
        Guid applicationId,
        string name,
        ProjectComplexity initialComplexity,
        Guid? moduleId = null,
        Guid? parentProjectId = null,
        IEnumerable<SectionManifest>? sections = null)
    {
        GetApplication(applicationId);

        var project = new ProjectManifest
        {
            Id = Guid.NewGuid(),
            Name = NormalizeName(name),
            ApplicationId = applicationId,
            ModuleId = moduleId,
            ParentProjectId = parentProjectId,
            InitialComplexity = initialComplexity,
            Sections = sections?.OrderBy(section => section.Order).ToList() ?? []
        };

        ValidateParentProjectPlacement(project);

        HierarchyValidator.ValidateProject(
            project,
            Applications,
            Modules,
            Projects);

        _projects.Add(project.Id, project);
        return project;
    }

    public ProjectManifest RenameProject(Guid projectId, string name)
    {
        var existing = GetProject(projectId);
        var updated = existing with { Name = NormalizeName(name) };
        _projects[projectId] = updated;
        return updated;
    }

    public ProjectManifest MoveProject(
        Guid projectId,
        Guid? newModuleId,
        Guid? newParentProjectId)
    {
        var existing = GetProject(projectId);
        var updated = existing with
        {
            ModuleId = newModuleId,
            ParentProjectId = newParentProjectId
        };

        ValidateParentProjectPlacement(updated);

        HierarchyValidator.ValidateProject(
            updated,
            Applications,
            Modules,
            Projects);

        if (HasProjectDescendants(projectId))
        {
            var descendants = GetProjectDescendants(projectId);
            if (descendants.Any(descendant => descendant.ModuleId != newModuleId))
            {
                throw new DomainValidationException(
                    "Move child projects first or keep the project inside its current module.");
            }
        }

        _projects[projectId] = updated;
        return updated;
    }

    public void DeleteProject(Guid projectId)
    {
        GetProject(projectId);

        if (_projects.Values.Any(project => project.ParentProjectId == projectId))
        {
            throw new DomainValidationException(
                "A project containing sub-projects cannot be deleted.");
        }

        _projects.Remove(projectId);
    }

    public SectionManifest AddSection(
        Guid projectId,
        string name,
        bool isSingleton,
        string? templateKey = null,
        int? order = null)
    {
        var project = GetProject(projectId);
        var normalizedName = NormalizeName(name);

        if (isSingleton &&
            project.Sections.Any(section =>
                section.IsSingleton &&
                string.Equals(section.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                $"Singleton section '{normalizedName}' already exists.");
        }

        var nextOrder = order ??
            (project.Sections.Count == 0 ? 10 : project.Sections.Max(section => section.Order) + 10);

        var section = new SectionManifest
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            IsSingleton = isSingleton,
            TemplateKey = string.IsNullOrWhiteSpace(templateKey) ? null : templateKey.Trim(),
            Order = nextOrder
        };

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Append(section)
                .OrderBy(candidate => candidate.Order)
                .ToList()
        });

        return section;
    }

    public SectionManifest RenameSection(
        Guid projectId,
        Guid sectionId,
        string name)
    {
        var project = GetProject(projectId);
        var section = GetSection(project, sectionId);
        var normalizedName = NormalizeName(name);

        if (section.IsSingleton &&
            project.Sections.Any(candidate =>
                candidate.Id != sectionId &&
                candidate.IsSingleton &&
                string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                $"Singleton section '{normalizedName}' already exists.");
        }

        var updatedSection = section with { Name = normalizedName };

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Select(candidate => candidate.Id == sectionId ? updatedSection : candidate)
                .ToList()
        });

        return updatedSection;
    }

    public void ReorderSection(Guid projectId, Guid sectionId, int order)
    {
        var project = GetProject(projectId);
        var section = GetSection(project, sectionId);
        var updatedSection = section with { Order = order };

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Select(candidate => candidate.Id == sectionId ? updatedSection : candidate)
                .OrderBy(candidate => candidate.Order)
                .ToList()
        });
    }

    public void DeleteSection(Guid projectId, Guid sectionId)
    {
        var project = GetProject(projectId);
        GetSection(project, sectionId);

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Where(section => section.Id != sectionId)
                .ToList()
        });
    }

    public ApplicationManifest GetApplication(Guid applicationId) =>
        _applications.TryGetValue(applicationId, out var application)
            ? application
            : throw new KeyNotFoundException($"Application '{applicationId}' was not found.");

    public ModuleManifest GetModule(Guid moduleId) =>
        _modules.TryGetValue(moduleId, out var module)
            ? module
            : throw new KeyNotFoundException($"Module '{moduleId}' was not found.");

    public ProjectManifest GetProject(Guid projectId) =>
        _projects.TryGetValue(projectId, out var project)
            ? project
            : throw new KeyNotFoundException($"Project '{projectId}' was not found.");

    private void ValidateParentProjectPlacement(ProjectManifest project)
    {
        if (project.ParentProjectId is not Guid parentProjectId)
        {
            return;
        }

        var parent = GetProject(parentProjectId);

        if (parent.ApplicationId != project.ApplicationId)
        {
            throw new DomainValidationException(
                "A sub-project must stay in the same application as its parent.");
        }

        if (parent.ModuleId != project.ModuleId)
        {
            throw new DomainValidationException(
                "A sub-project must stay in the same module as its parent.");
        }
    }

    private bool HasProjectDescendants(Guid projectId) =>
        _projects.Values.Any(project => project.ParentProjectId == projectId);

    private IReadOnlyCollection<ProjectManifest> GetProjectDescendants(Guid projectId)
    {
        var descendants = new List<ProjectManifest>();
        var pending = new Queue<Guid>();
        pending.Enqueue(projectId);

        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();

            foreach (var child in _projects.Values.Where(
                         project => project.ParentProjectId == parentId))
            {
                descendants.Add(child);
                pending.Enqueue(child.Id);
            }
        }

        return descendants;
    }

    private static SectionManifest GetSection(ProjectManifest project, Guid sectionId) =>
        project.Sections.SingleOrDefault(section => section.Id == sectionId)
        ?? throw new KeyNotFoundException(
            $"Section '{sectionId}' was not found in project '{project.Id}'.");

    private void ReplaceProject(ProjectManifest project) =>
        _projects[project.Id] = project;

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}
