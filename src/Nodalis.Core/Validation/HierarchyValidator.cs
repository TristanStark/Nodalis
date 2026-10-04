using Nodalis.Core.Domain;

namespace Nodalis.Core.Validation;

public static class HierarchyValidator
{
    /// <summary>
    /// Performs the <c>ValidateProject</c> operation.
    /// </summary>
    /// <param name="project">The <c>project</c> value.</param>
    /// <param name="applications">The <c>applications</c> value.</param>
    /// <param name="modules">The <c>modules</c> value.</param>
    /// <param name="projects">The <c>projects</c> value.</param>
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
            global::Nodalis.Core.Domain.ModuleManifest module = modules.SingleOrDefault(candidate => candidate.Id == moduleId)
                ?? throw new DomainValidationException("The selected module does not exist.");

            if (module.ApplicationId != project.ApplicationId)
            {
                throw new DomainValidationException("A project module must belong to the same application.");
            }
        }

        if (project.ParentProjectId is Guid parentProjectId)
        {
            global::Nodalis.Core.Domain.ProjectManifest parent = projects.SingleOrDefault(candidate => candidate.Id == parentProjectId)
                ?? throw new DomainValidationException("The parent project does not exist.");

            if (parent.ApplicationId != project.ApplicationId)
            {
                throw new DomainValidationException("A sub-project must belong to the same application as its parent.");
            }

            EnsureNoProjectCycle(project.Id, parentProjectId, projects);
        }
    }

    /// <summary>
    /// Performs the <c>ValidateModule</c> operation.
    /// </summary>
    /// <param name="module">The <c>module</c> value.</param>
    /// <param name="applications">The <c>applications</c> value.</param>
    /// <param name="modules">The <c>modules</c> value.</param>
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
            global::Nodalis.Core.Domain.ModuleManifest parent = modules.SingleOrDefault(candidate => candidate.Id == parentModuleId)
                ?? throw new DomainValidationException("The parent module does not exist.");

            if (parent.ApplicationId != module.ApplicationId)
            {
                throw new DomainValidationException("Nested modules must stay inside the same application.");
            }

            EnsureNoModuleCycle(module.Id, parentModuleId, modules);
        }
    }

    /// <summary>
    /// Performs the <c>EnsureNoProjectCycle</c> operation.
    /// </summary>
    /// <param name="projectId">The <c>projectId</c> value.</param>
    /// <param name="parentProjectId">The <c>parentProjectId</c> value.</param>
    /// <param name="projects">The <c>projects</c> value.</param>
    private static void EnsureNoProjectCycle(
            Guid projectId,
            Guid parentProjectId,
            IReadOnlyCollection<ProjectManifest> projects)
    {
        global::System.Collections.Generic.HashSet<global::System.Guid> visited = new HashSet<Guid> { projectId };
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

    /// <summary>
    /// Performs the <c>EnsureNoModuleCycle</c> operation.
    /// </summary>
    /// <param name="moduleId">The <c>moduleId</c> value.</param>
    /// <param name="parentModuleId">The <c>parentModuleId</c> value.</param>
    /// <param name="modules">The <c>modules</c> value.</param>
    private static void EnsureNoModuleCycle(
            Guid moduleId,
            Guid parentModuleId,
            IReadOnlyCollection<ModuleManifest> modules)
    {
        global::System.Collections.Generic.HashSet<global::System.Guid> visited = new HashSet<Guid> { moduleId };
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
