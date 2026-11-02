using Nodalis.Core.Risks;
using Nodalis.Core.Tasks;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Source resolution never substitutes checkbox state for a human measure
/// qualification. It describes only the task-catalog snapshot at inspection.
/// </summary>
public enum ProjectGateTaskSourceState
{
    NotInspected,
    VerifiedInProject,
    Missing,
    Ambiguous,
    OtherProject,
    InvalidSource
}

/// <summary>
/// One persistent GUID reference from the current RIS treatment revision.
/// A checked checkbox is contextual information, not verified evidence.
/// </summary>
public sealed record ProjectGateTaskSource(
    Guid TaskId, ProjectGateTaskSourceState State,
    string? RelativePath = null, int? LineNumber = null,
    bool? IsCheckboxChecked = null)
{
    /// <summary>Source-aware diagnostic without inventing a file location.</summary>
    public string Detail => TaskId.ToString("D") + " · " + Describe();

    /// <summary>Renders an explicit, nonauthoritative source state.</summary>
    private string Describe() => State switch
    {
        ProjectGateTaskSourceState.VerifiedInProject =>
            "source unique : " + RelativePath + ":" + LineNumber +
            (IsCheckboxChecked == true ? " · case cochée" : " · case ouverte"),
        ProjectGateTaskSourceState.Missing => "source disparue ou introuvable",
        ProjectGateTaskSourceState.Ambiguous => "GUID dupliqué · source ambiguë",
        ProjectGateTaskSourceState.OtherProject => "tâche d'un autre projet",
        ProjectGateTaskSourceState.InvalidSource => "source non vérifiable",
        _ => "référence non contrôlée"
    };
}

/// <summary>
/// Independent read-only task-source and human-evidence assessment.
/// Evidence is descriptive free text, never a cryptographic file proof.
/// </summary>
public sealed record ProjectGateRiskTaskProvenance(
    IReadOnlyList<ProjectGateTaskSource> TaskSources,
    int HumanCompletedMeasures, int DeclaredCompletionEvidence)
{
    /// <summary>Empty provenance for missing/unresolved canonical RIS.</summary>
    public static ProjectGateRiskTaskProvenance Empty { get; } = new([], 0, 0);

    /// <summary>References found uniquely within the same project snapshot.</summary>
    public int VerifiedSourceCount => TaskSources.Count(source =>
        source.State == ProjectGateTaskSourceState.VerifiedInProject);

    /// <summary>References that require human source correction or review.</summary>
    public int ProblemSourceCount => TaskSources.Count(source =>
        source.State is ProjectGateTaskSourceState.Missing or
            ProjectGateTaskSourceState.Ambiguous or
            ProjectGateTaskSourceState.OtherProject or
            ProjectGateTaskSourceState.InvalidSource);

    /// <summary>References that were not resolved because no task scan ran.</summary>
    public int NotInspectedCount => TaskSources.Count(source =>
        source.State == ProjectGateTaskSourceState.NotInspected);

    /// <summary>Checkbox marks only; these never complete a RIS measure.</summary>
    public int CheckedCheckboxCount => TaskSources.Count(source =>
        source.State == ProjectGateTaskSourceState.VerifiedInProject &&
        source.IsCheckboxChecked == true);
}

/// <summary>
/// Indexes one complete workspace task-catalog snapshot, including hidden
/// duplicate identities. All risk/reservation joins stay in memory afterward.
/// </summary>
public sealed class ProjectGateTaskProvenanceIndex
{
    private readonly TaskCollection? _catalog;
    private readonly IReadOnlyDictionary<Guid, TaskItem[]> _byId;

    /// <summary>Creates one snapshot index, with null meaning uninspected.</summary>
    /// <param name="catalog">Full workspace catalog, never project-filtered.</param>
    public ProjectGateTaskProvenanceIndex(TaskCollection? catalog)
    {
        _catalog = catalog;
        _byId = catalog is null ? new Dictionary<Guid, TaskItem[]>() :
            catalog.Tasks.Where(item => item.PersistentId is Guid id &&
                    id != Guid.Empty)
                .GroupBy(item => item.PersistentId!.Value)
                .ToDictionary(group => group.Key, group => group.ToArray());
    }

    /// <summary>
    /// Evaluates only the current treatment; old plan links and evidence
    /// remain historical and cannot qualify the revised plan.
    /// </summary>
    /// <param name="entry">Validated current RIS with history.</param>
    /// <param name="projectId">Gate project identity for scope checks.</param>
    /// <returns>Distinct stable GUID statuses and independent human progress.</returns>
    public ProjectGateRiskTaskProvenance Evaluate(
        ProjectRiskEntry entry, Guid projectId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.CurrentTreatment is null)
        {
            return ProjectGateRiskTaskProvenance.Empty;
        }

        Guid currentPlanId = entry.Treatments[^1].Id;
        ProjectGateTaskSource[] sources = Enum.GetValues<ProjectRiskMeasureKind>()
            .SelectMany(entry.LinkedTaskIds)
            .Distinct()
            .OrderBy(id => id)
            .Select(id => Resolve(id, projectId))
            .ToArray();
        ProjectRiskMeasureEvent[] completed = Enum
            .GetValues<ProjectRiskMeasureKind>()
            .Select(kind => entry.MeasureEvents.LastOrDefault(item =>
                item.TreatmentId == currentPlanId && item.Kind == kind))
            .OfType<ProjectRiskMeasureEvent>()
            .Where(item => item.Status == ProjectRiskMeasureStatus.Completed)
            .ToArray();
        return new ProjectGateRiskTaskProvenance(sources, completed.Length,
            completed.Count(item => !string.IsNullOrWhiteSpace(item.Evidence)));
    }

    /// <summary>Never claims a source is valid when GUID collisions exist.</summary>
    private ProjectGateTaskSource Resolve(Guid id, Guid projectId)
    {
        if (_catalog is null)
        {
            return new ProjectGateTaskSource(
                id, ProjectGateTaskSourceState.NotInspected);
        }

        if (_catalog.ConflictingPersistentIds.Contains(id))
        {
            return new ProjectGateTaskSource(
                id, ProjectGateTaskSourceState.Ambiguous);
        }

        if (!_byId.TryGetValue(id, out TaskItem[]? matches) ||
            matches.Length == 0)
        {
            return new ProjectGateTaskSource(id, ProjectGateTaskSourceState.Missing);
        }

        if (matches.Length != 1)
        {
            return new ProjectGateTaskSource(id, ProjectGateTaskSourceState.Ambiguous);
        }

        TaskItem task = matches[0];
        if (task.ProjectId != projectId)
        {
            return new ProjectGateTaskSource(
                id, ProjectGateTaskSourceState.OtherProject);
        }

        if (task.LineNumber < 1 ||
            string.IsNullOrWhiteSpace(task.SourceRelativePath))
        {
            return new ProjectGateTaskSource(
                id, ProjectGateTaskSourceState.InvalidSource);
        }

        return new ProjectGateTaskSource(id,
            ProjectGateTaskSourceState.VerifiedInProject,
            task.SourceRelativePath, task.LineNumber, task.IsCompleted);
    }
}
