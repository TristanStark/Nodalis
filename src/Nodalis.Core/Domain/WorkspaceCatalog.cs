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

    /// <summary>
    /// Performs the <c>CreateApplication</c> operation.
    /// </summary>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ApplicationManifest CreateApplication(string name)
    {
        global::Nodalis.Core.Domain.ApplicationManifest application = new ApplicationManifest
        {
            Id = Guid.NewGuid(),
            Name = NormalizeName(name)
        };

        _applications.Add(application.Id, application);
        return application;
    }

    /// <summary>
    /// Performs the <c>RenameApplication</c> operation.
    /// </summary>
    /// <param name="applicationId">The <c>applicationId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ApplicationManifest RenameApplication(Guid applicationId, string name)
    {
        global::Nodalis.Core.Domain.ApplicationManifest existing = GetApplication(applicationId);
        global::Nodalis.Core.Domain.ApplicationManifest updated = existing with { Name = NormalizeName(name) };
        _applications[applicationId] = updated;
        return updated;
    }

    /// <summary>
    /// Performs the <c>DeleteApplication</c> operation.
    /// </summary>
    /// <param name="applicationId">The <c>applicationId</c> value.</param>
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

    /// <summary>
    /// Performs the <c>CreateModule</c> operation.
    /// </summary>
    /// <param name="applicationId">The <c>applicationId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <param name="parentModuleId">The <c>parentModuleId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ModuleManifest CreateModule(
            Guid applicationId,
            string name,
            Guid? parentModuleId = null)
    {
        GetApplication(applicationId);

        global::Nodalis.Core.Domain.ModuleManifest module = new ModuleManifest
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

    /// <summary>
    /// Performs the <c>RenameModule</c> operation.
    /// </summary>
    /// <param name="moduleId">The <c>moduleId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ModuleManifest RenameModule(Guid moduleId, string name)
    {
        global::Nodalis.Core.Domain.ModuleManifest existing = GetModule(moduleId);
        global::Nodalis.Core.Domain.ModuleManifest updated = existing with { Name = NormalizeName(name) };
        _modules[moduleId] = updated;
        return updated;
    }

    /// <summary>
    /// Performs the <c>MoveModule</c> operation.
    /// </summary>
    /// <param name="moduleId">The <c>moduleId</c> value.</param>
    /// <param name="newParentModuleId">The <c>newParentModuleId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ModuleManifest MoveModule(Guid moduleId, Guid? newParentModuleId)
    {
        global::Nodalis.Core.Domain.ModuleManifest existing = GetModule(moduleId);
        global::Nodalis.Core.Domain.ModuleManifest updated = existing with { ParentModuleId = newParentModuleId };

        HierarchyValidator.ValidateModule(
            updated,
            Applications,
            Modules);

        _modules[moduleId] = updated;
        return updated;
    }

    /// <summary>
    /// Performs the <c>DeleteModule</c> operation.
    /// </summary>
    /// <param name="moduleId">The <c>moduleId</c> value.</param>
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

    /// <summary>
    /// Performs the <c>CreateProject</c> operation.
    /// </summary>
    /// <param name="applicationId">The <c>applicationId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <param name="initialComplexity">The <c>initialComplexity</c> value.</param>
    /// <param name="moduleId">The <c>moduleId</c> value.</param>
    /// <param name="parentProjectId">The <c>parentProjectId</c> value.</param>
    /// <param name="sections">The <c>sections</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ProjectManifest CreateProject(
            Guid applicationId,
            string name,
            ProjectComplexity initialComplexity,
            Guid? moduleId = null,
            Guid? parentProjectId = null,
            IEnumerable<SectionManifest>? sections = null)
    {
        GetApplication(applicationId);

        global::Nodalis.Core.Domain.ProjectManifest project = new ProjectManifest
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

    /// <summary>
    /// Performs the <c>RenameProject</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ProjectManifest RenameProject(Guid projectId, string name)
    {
        global::Nodalis.Core.Domain.ProjectManifest existing = GetProject(projectId);
        global::Nodalis.Core.Domain.ProjectManifest updated = existing with { Name = NormalizeName(name) };
        _projects[projectId] = updated;
        return updated;
    }

    /// <summary>
    /// Performs the <c>MoveProject</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="newModuleId">The <c>newModuleId</c> value.</param>
    /// <param name="newParentProjectId">The <c>newParentProjectId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ProjectManifest MoveProject(
            Guid projectId,
            Guid? newModuleId,
            Guid? newParentProjectId)
    {
        global::Nodalis.Core.Domain.ProjectManifest existing = GetProject(projectId);
        global::Nodalis.Core.Domain.ProjectManifest updated = existing with
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
            global::System.Collections.Generic.IReadOnlyCollection<global::Nodalis.Core.Domain.ProjectManifest> descendants = GetProjectDescendants(projectId);
            if (descendants.Any(descendant => descendant.ModuleId != newModuleId))
            {
                throw new DomainValidationException(
                    "Move child projects first or keep the project inside its current module.");
            }
        }

        _projects[projectId] = updated;
        return updated;
    }

    /// <summary>
    /// Performs the <c>DeleteProject</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
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

    /// <summary>
    /// Performs the <c>AddSection</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <param name="isSingleton">The <c>isSingleton</c> value.</param>
    /// <param name="templateKey">The <c>templateKey</c> value.</param>
    /// <param name="order">The <c>order</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public SectionManifest AddSection(
            Guid projectId,
            string name,
            bool isSingleton,
            string? templateKey = null,
            int? order = null)
    {
        global::Nodalis.Core.Domain.ProjectManifest project = GetProject(projectId);
        string normalizedName = NormalizeName(name);

        if (isSingleton &&
            project.Sections.Any(section =>
                section.IsSingleton &&
                string.Equals(section.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                $"Singleton section '{normalizedName}' already exists.");
        }

        int nextOrder = order ??
            (project.Sections.Count == 0 ? 10 : project.Sections.Max(section => section.Order) + 10);

        global::Nodalis.Core.Domain.SectionManifest section = new SectionManifest
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

    /// <summary>
    /// Performs the <c>RenameSection</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="sectionId">The <c>sectionId</c> value.</param>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public SectionManifest RenameSection(
            Guid projectId,
            Guid sectionId,
            string name)
    {
        global::Nodalis.Core.Domain.ProjectManifest project = GetProject(projectId);
        global::Nodalis.Core.Domain.SectionManifest section = GetSection(project, sectionId);
        string normalizedName = NormalizeName(name);

        if (section.IsSingleton &&
            project.Sections.Any(candidate =>
                candidate.Id != sectionId &&
                candidate.IsSingleton &&
                string.Equals(candidate.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainValidationException(
                $"Singleton section '{normalizedName}' already exists.");
        }

        global::Nodalis.Core.Domain.SectionManifest updatedSection = section with { Name = normalizedName };

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Select(candidate => candidate.Id == sectionId ? updatedSection : candidate)
                .ToList()
        });

        return updatedSection;
    }

    /// <summary>
    /// Performs the <c>ReorderSection</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="sectionId">The <c>sectionId</c> value.</param>
    /// <param name="order">The <c>order</c> value.</param>
    public void ReorderSection(Guid projectId, Guid sectionId, int order)
    {
        global::Nodalis.Core.Domain.ProjectManifest project = GetProject(projectId);
        global::Nodalis.Core.Domain.SectionManifest section = GetSection(project, sectionId);
        global::Nodalis.Core.Domain.SectionManifest updatedSection = section with { Order = order };

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Select(candidate => candidate.Id == sectionId ? updatedSection : candidate)
                .OrderBy(candidate => candidate.Order)
                .ToList()
        });
    }

    /// <summary>
    /// Performs the <c>DeleteSection</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="sectionId">The <c>sectionId</c> value.</param>
    public void DeleteSection(Guid projectId, Guid sectionId)
    {
        global::Nodalis.Core.Domain.ProjectManifest project = GetProject(projectId);
        GetSection(project, sectionId);

        ReplaceProject(project with
        {
            Sections = project.Sections
                .Where(section => section.Id != sectionId)
                .ToList()
        });
    }

    /// <summary>
    /// Performs the <c>GetApplication</c> operation.
    /// </summary>
    /// <param name="applicationId">The <c>applicationId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ApplicationManifest GetApplication(Guid applicationId) =>
            _applications.TryGetValue(applicationId, out global::Nodalis.Core.Domain.ApplicationManifest? application)
                ? application
                : throw new KeyNotFoundException($"Application '{applicationId}' was not found.");

    /// <summary>
    /// Performs the <c>GetModule</c> operation.
    /// </summary>
    /// <param name="moduleId">The <c>moduleId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ModuleManifest GetModule(Guid moduleId) =>
            _modules.TryGetValue(moduleId, out global::Nodalis.Core.Domain.ModuleManifest? module)
                ? module
                : throw new KeyNotFoundException($"Module '{moduleId}' was not found.");

    /// <summary>
    /// Performs the <c>GetProject</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public ProjectManifest GetProject(Guid projectId) =>
            _projects.TryGetValue(projectId, out global::Nodalis.Core.Domain.ProjectManifest? project)
                ? project
                : throw new KeyNotFoundException($"Project '{projectId}' was not found.");

    /// <summary>
    /// Performs the <c>ValidateParentProjectPlacement</c> operation.
    /// </summary>
    /// <param name="project">The <c>project</c> value.</param>
    private void ValidateParentProjectPlacement(ProjectManifest project)
    {
        if (project.ParentProjectId is not Guid parentProjectId)
        {
            return;
        }

        global::Nodalis.Core.Domain.ProjectManifest parent = GetProject(parentProjectId);

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

    /// <summary>
    /// Performs the <c>HasProjectDescendants</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private bool HasProjectDescendants(Guid projectId) =>
            _projects.Values.Any(project => project.ParentProjectId == projectId);

    /// <summary>
    /// Performs the <c>GetProjectDescendants</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private IReadOnlyCollection<ProjectManifest> GetProjectDescendants(Guid projectId)
    {
        global::System.Collections.Generic.List<global::Nodalis.Core.Domain.ProjectManifest> descendants = new List<ProjectManifest>();
        global::System.Collections.Generic.Queue<global::System.Guid> pending = new Queue<Guid>();
        pending.Enqueue(projectId);

        while (pending.Count > 0)
        {
            global::System.Guid parentId = pending.Dequeue();

            foreach (global::Nodalis.Core.Domain.ProjectManifest child in _projects.Values.Where(
                         project => project.ParentProjectId == parentId))
            {
                descendants.Add(child);
                pending.Enqueue(child.Id);
            }
        }

        return descendants;
    }

    /// <summary>
    /// Performs the <c>GetSection</c> operation.
    /// </summary>
    /// <param name="project">The <c>project</c> value.</param>
    /// <param name="sectionId">The <c>sectionId</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static SectionManifest GetSection(ProjectManifest project, Guid sectionId) =>
            project.Sections.SingleOrDefault(section => section.Id == sectionId)
            ?? throw new KeyNotFoundException(
                $"Section '{sectionId}' was not found in project '{project.Id}'.");

    /// <summary>
    /// Performs the <c>ReplaceProject</c> operation.
    /// </summary>
    /// <param name="project">The <c>project</c> value.</param>
    private void ReplaceProject(ProjectManifest project) =>
            _projects[project.Id] = project;

    /// <summary>
    /// Performs the <c>NormalizeName</c> operation.
    /// </summary>
    /// <param name="name">The <c>name</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }
}
