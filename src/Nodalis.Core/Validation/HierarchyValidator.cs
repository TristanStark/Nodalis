using Nodalis.Core.Domain;

namespace Nodalis.Core.Validation;

public static class HierarchyValidator
{
    public static void ValidateProject(
        ProjectManifest project,
        IReadOnlyCollection<ApplicationManifest> applications,
        IReadOnlyCollection<ModuleManifest> modules,
        IReadOnlyCollection<ProjectManifest> projects)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            throw new DomainValidationException("A project name cannot be empty.");
        }

        if (!applications.Any(application => application.Id == project.ApplicationId))
        {
            throw new DomainValidationException("The project must belong to an existing application.");
        }

        if (project.ModuleId is Guid moduleId)
        {
            var module = modules.SingleOrDefault(candidate => candidate.Id == moduleId)
                ?? throw new DomainValidationException("The selected module does not exist.");

            if (module.ApplicationId != project.ApplicationId)
            {
                throw new DomainValidationException("A project module must belong to the same application.");
            }
        }

        if (project.ParentProjectId is Guid parentProjectId)
        {
            var parent = projects.SingleOrDefault(candidate => candidate.Id == parentProjectId)
                ?? throw new DomainValidationException("The parent project does not exist.");

            if (parent.ApplicationId != project.ApplicationId)
            {
                throw new DomainValidationException("A sub-project must belong to the same application as its parent.");
            }

            EnsureNoProjectCycle(project.Id, parentProjectId, projects);
        }
    }

    public static void ValidateModule(
        ModuleManifest module,
        IReadOnlyCollection<ApplicationManifest> applications,
        IReadOnlyCollection<ModuleManifest> modules)
    {
        if (string.IsNullOrWhiteSpace(module.Name))
        {
            throw new DomainValidationException("A module name cannot be empty.");
        }

        if (!applications.Any(application => application.Id == module.ApplicationId))
        {
            throw new DomainValidationException("The module must belong to an existing application.");
        }

        if (module.ParentModuleId is Guid parentModuleId)
        {
            var parent = modules.SingleOrDefault(candidate => candidate.Id == parentModuleId)
                ?? throw new DomainValidationException("The parent module does not exist.");

            if (parent.ApplicationId != module.ApplicationId)
            {
                throw new DomainValidationException("Nested modules must stay inside the same application.");
            }

            EnsureNoModuleCycle(module.Id, parentModuleId, modules);
        }
    }

    private static void EnsureNoProjectCycle(
        Guid projectId,
        Guid parentProjectId,
        IReadOnlyCollection<ProjectManifest> projects)
    {
        var visited = new HashSet<Guid> { projectId };
        Guid? currentId = parentProjectId;

        while (currentId is Guid id)
        {
            if (!visited.Add(id))
            {
                throw new DomainValidationException("A project hierarchy cannot contain a cycle.");
            }

            currentId = projects.SingleOrDefault(candidate => candidate.Id == id)?.ParentProjectId;
        }
    }

    private static void EnsureNoModuleCycle(
        Guid moduleId,
        Guid parentModuleId,
        IReadOnlyCollection<ModuleManifest> modules)
    {
        var visited = new HashSet<Guid> { moduleId };
        Guid? currentId = parentModuleId;

        while (currentId is Guid id)
        {
            if (!visited.Add(id))
            {
                throw new DomainValidationException("A module hierarchy cannot contain a cycle.");
            }

            currentId = modules.SingleOrDefault(candidate => candidate.Id == id)?.ParentModuleId;
        }
    }
}
