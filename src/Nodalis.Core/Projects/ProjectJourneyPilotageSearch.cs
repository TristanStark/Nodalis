using System.Globalization;

namespace Nodalis.Core.Projects;

/// <summary>
/// One read-only search hit bound to a real stage or operation ID.
/// </summary>
/// <param name="StepId">Stable owning stage identity.</param>
/// <param name="OperationId">Stable operation identity, null for a stage.</param>
/// <param name="Title">Human-facing path.</param>
/// <param name="Description">Stage status or associated prompt reference.</param>
/// <param name="SearchText">Normalized context searched without losing names.</param>
public sealed record ProjectJourneyPilotageSearchRow(
    Guid StepId, Guid? OperationId, string Title,
    string Description, string SearchText);

/// <summary>
/// Bounded UI matches from a complete in-memory project-local index.
/// </summary>
/// <param name="Rows">Matching rows displayed to the user.</param>
/// <param name="TotalIndexed">Complete number of entries indexed.</param>
/// <param name="HasMore">Whether refinement may reveal additional matches.</param>
public sealed record ProjectJourneyPilotageSearchResult(
    IReadOnlyList<ProjectJourneyPilotageSearchRow> Rows,
    int TotalIndexed, bool HasMore);

/// <summary>
/// Pure, culture-aware, allocation-bounded searching for large customized
/// project journeys. It never reads/writes project data or invokes AI.
/// </summary>
public static class ProjectJourneyPilotageSearch
{
    private static readonly CompareInfo French =
        CultureInfo.GetCultureInfo("fr-FR").CompareInfo;

    /// <summary>
    /// Captures every stage and operation for the selected project.
    /// Underlying menu controls may then be created on demand.
    /// </summary>
    /// <param name="stages">Canonical stage/operation projection.</param>
    /// <returns>Complete searchable rows using the real source GUIDs.</returns>
    public static IReadOnlyList<ProjectJourneyPilotageSearchRow> Index(
        IReadOnlyList<ProjectJourneyPilotageStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        List<ProjectJourneyPilotageSearchRow> rows = [];
        int number = 0;
        foreach (ProjectJourneyPilotageStage stage in stages)
        {
            string path = (++number) + ". " + stage.Name;
            rows.Add(new ProjectJourneyPilotageSearchRow(
                stage.Id, null, "Étape · " + path,
                "État : " + HumanStatus(stage.Status) +
                    " · " + stage.Operations.Count + " opération(s)",
                path));
            foreach (ProjectJourneyPilotageOperation operation in
                stage.Operations)
            {
                string prompt = operation.PromptKey ??
                    "aucun prompt associé";
                rows.Add(new ProjectJourneyPilotageSearchRow(
                    stage.Id, operation.Id, path + " › " + operation.Name,
                    "Opération · " + prompt,
                    path + " " + operation.Name + " " + prompt));
            }
        }

        return rows;
    }

    /// <summary>
    /// Searches the whole project, displaying only up to the chosen limit;
    /// accent-insensitive lookup supports French titles and FRT references.
    /// </summary>
    /// <param name="index">Complete index of this selected project only.</param>
    /// <param name="query">Human free text.</param>
    /// <param name="limit">Maximum returned rows, never a limit on indexing.</param>
    /// <returns>Bounded results and a clear refinement indicator.</returns>
    public static ProjectJourneyPilotageSearchResult Find(
        IReadOnlyList<ProjectJourneyPilotageSearchRow> index,
        string? query, int limit = 250)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        string term = (query ?? string.Empty).Trim();
        List<ProjectJourneyPilotageSearchRow> results = [];
        bool more = false;
        foreach (ProjectJourneyPilotageSearchRow row in index)
        {
            if (term.Length > 0 && French.IndexOf(row.SearchText, term,
                    CompareOptions.IgnoreCase |
                    CompareOptions.IgnoreNonSpace) < 0)
            {
                continue;
            }

            if (results.Count == limit)
            {
                more = true;
                break;
            }

            results.Add(row);
        }

        return new ProjectJourneyPilotageSearchResult(
            results, index.Count, more);
    }

    /// <summary>Stable French stage labels for search details.</summary>
    /// <param name="status">Canonical stage progress.</param>
    /// <returns>Human label without changing project status.</returns>
    private static string HumanStatus(ProjectJourneyStepStatus status) =>
        status switch
        {
            ProjectJourneyStepStatus.Todo => "À faire",
            ProjectJourneyStepStatus.InProgress => "En cours",
            ProjectJourneyStepStatus.Completed => "Terminée",
            ProjectJourneyStepStatus.NotApplicable => "Non applicable",
            _ => "Inconnu"
        };
}
