namespace Nodalis.Core.Projects;

/// <summary>
/// One real, user-customized operation in the project-specific Pilotage menu.
/// This is a non-owning read-only projection, never a duplicated workflow.
/// </summary>
/// <param name="Id">Stable operation identity in the owning journey.</param>
/// <param name="Name">Current human-facing operation name.</param>
/// <param name="PromptKey">Optional external prompt template reference.</param>
public sealed record ProjectJourneyPilotageOperation(
    Guid Id, string Name, string? PromptKey);

/// <summary>
/// One stage of the actual project journey, sorted by its custom order.
/// </summary>
/// <param name="Id">Stable stage GUID.</param>
/// <param name="Order">Explicit per-project stage order.</param>
/// <param name="Name">Latest user-customized title.</param>
/// <param name="Status">Current journey status, not a task status.</param>
/// <param name="Operations">Every operation in its edited order.</param>
public sealed record ProjectJourneyPilotageStage(
    Guid Id, int Order, string Name, ProjectJourneyStepStatus Status,
    IReadOnlyList<ProjectJourneyPilotageOperation> Operations);

/// <summary>
/// Deterministic, bounded read-only projection for the Pilotage 2.0 menu.
/// Reuses real journey IDs, names, prompt keys and ordering. Never reads a
/// second register or silently substitutes the original template defaults.
/// </summary>
public static class ProjectJourneyPilotageProjection
{
    /// <summary>
    /// Builds all stages and operations, including post-template edits,
    /// or refuses an ambiguous identity instead of showing wrong shortcuts.
    /// </summary>
    /// <param name="journey">Already loaded project-specific journey.</param>
    /// <returns>Ordered, detached menu rows with stable source identities.</returns>
    public static IReadOnlyList<ProjectJourneyPilotageStage> Build(
        ProjectJourneyDocument journey)
    {
        ArgumentNullException.ThrowIfNull(journey);
        if (journey.ProjectId == Guid.Empty || journey.Steps is null ||
            journey.Steps.Count > 250)
        {
            throw new InvalidDataException(
                "Parcours introuvable ou trop volumineux pour Pilotage.");
        }

        if (journey.Steps.Any(step => step is null))
        {
            throw new InvalidDataException(
                "Une étape du parcours est absente : menu indisponible.");
        }

        HashSet<Guid> identities = [];
        HashSet<int> orders = [];
        List<ProjectJourneyPilotageStage> stages = [];
        foreach (ProjectJourneyStep step in journey.Steps
            .OrderBy(item => item.Order))
        {
            if (step.Id == Guid.Empty || step.Order < 1 ||
                !identities.Add(step.Id) || !orders.Add(step.Order) ||
                !ValidName(step.Name) ||
                !Enum.IsDefined(step.Status) || step.Operations is null ||
                step.Operations.Count > 500)
            {
                throw new InvalidDataException(
                    "Étape du parcours invalide ou ambiguë pour Pilotage.");
            }

            List<ProjectJourneyPilotageOperation> operations = [];
            foreach (ProjectJourneyOperation operation in step.Operations)
            {
                if (operation is null || operation.Id == Guid.Empty ||
                    !identities.Add(operation.Id) ||
                    !ValidName(operation.Name) ||
                    operation.PromptKey is { Length: > 160 })
                {
                    throw new InvalidDataException(
                        "Opération ou prompt du parcours ambigu pour Pilotage.");
                }

                operations.Add(new ProjectJourneyPilotageOperation(
                    operation.Id, operation.Name, operation.PromptKey));
            }

            stages.Add(new ProjectJourneyPilotageStage(
                step.Id, step.Order, step.Name, step.Status, operations));
        }

        return stages;
    }

    /// <summary>Accepts only bounded, single-line menu names.</summary>
    /// <param name="name">Stage or operation title.</param>
    /// <returns>True for nonempty, safe human-facing text.</returns>
    private static bool ValidName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 240 &&
        !name.Any(char.IsControl);
}
