namespace Nodalis.Core.Projects;

/// <summary>
/// Pure, non-persisting transformations of a project journey. Every command
/// returns a new document; stages, operations, and evidence GUIDs are preserved
/// unless a user explicitly creates or removes one of them.
/// </summary>
public static class ProjectJourneyEditor
{
    /// <summary>Creates a stage at the end of the journey.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="name">User-supplied stage name.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument AddStep(
        ProjectJourneyDocument document, string name)
    {
        ValidateDocument(document);
        string label = NormalizeName(name);
        List<ProjectJourneyStep> steps = Ordered(document);
        steps.Add(new ProjectJourneyStep
        {
            Id = Guid.NewGuid(),
            Name = label,
            Order = steps.Count
        });
        return Renumber(document, steps);
    }

    /// <summary>Renames an existing stage without changing its identity.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Stable stage ID.</param>
    /// <param name="name">New display name.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument RenameStep(
        ProjectJourneyDocument document, Guid stepId, string name)
    {
        string label = NormalizeName(name);
        return UpdateStep(document, stepId, step => step with { Name = label });
    }

    /// <summary>Updates a stage's progress, independently of tasks and milestones.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Stable stage ID.</param>
    /// <param name="status">Explicitly chosen progress status.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument SetStatus(
        ProjectJourneyDocument document, Guid stepId, ProjectJourneyStepStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return UpdateStep(document, stepId, step => step with { Status = status });
    }

    /// <summary>Moves a stage one place up (-1) or down (+1).</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Stable stage ID.</param>
    /// <param name="direction">-1 or +1.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument MoveStep(
        ProjectJourneyDocument document, Guid stepId, int direction)
    {
        ValidateDirection(direction);
        List<ProjectJourneyStep> steps = Ordered(document);
        int index = steps.FindIndex(step => step.Id == stepId);
        if (index < 0)
        {
            throw new KeyNotFoundException("Étape introuvable.");
        }

        int target = index + direction;
        if (target < 0 || target >= steps.Count)
        {
            return document;
        }

        (steps[index], steps[target]) = (steps[target], steps[index]);
        return Renumber(document, steps);
    }

    /// <summary>
    /// Removes the selected stage from the journey only. Calling code must
    /// obtain human confirmation; canonical documents are never deleted.
    /// </summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Stable stage ID.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument RemoveStep(
        ProjectJourneyDocument document, Guid stepId)
    {
        List<ProjectJourneyStep> steps = Ordered(document);
        if (steps.RemoveAll(step => step.Id == stepId) != 1)
        {
            throw new KeyNotFoundException("Étape introuvable.");
        }

        return Renumber(document, steps);
    }

    /// <summary>Adds a repeatable operation to a selected stage.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Stable stage ID.</param>
    /// <param name="name">Operation display name.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument AddOperation(
        ProjectJourneyDocument document, Guid stepId, string name)
    {
        string label = NormalizeName(name);
        return UpdateStep(document, stepId, step => step with
        {
            Operations =
            [
                .. step.Operations,
                new ProjectJourneyOperation { Id = Guid.NewGuid(), Name = label }
            ]
        });
    }

    /// <summary>Renames a repeatable operation without changing its GUID.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Owning stage ID.</param>
    /// <param name="operationId">Stable operation ID.</param>
    /// <param name="name">New display name.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument RenameOperation(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        string name)
    {
        string label = NormalizeName(name);
        return UpdateOperation(document, stepId, operationId,
            operation => operation with { Name = label });
    }

    /// <summary>Moves an operation within a stage without reallocating GUIDs.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Owning stage ID.</param>
    /// <param name="operationId">Operation ID.</param>
    /// <param name="direction">-1 for up, +1 for down.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument MoveOperation(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        int direction)
    {
        ValidateDirection(direction);
        return UpdateStep(document, stepId, step =>
        {
            List<ProjectJourneyOperation> operations = step.Operations.ToList();
            int index = operations.FindIndex(item => item.Id == operationId);
            if (index < 0)
            {
                throw new KeyNotFoundException("Opération introuvable.");
            }

            int target = index + direction;
            if (target < 0 || target >= operations.Count)
            {
                return step;
            }

            (operations[index], operations[target]) =
                (operations[target], operations[index]);
            return step with { Operations = operations };
        });
    }

    /// <summary>
    /// Removes one operation reference from this stage without deleting the
    /// files, evidence, milestones or gates to which it may refer.
    /// </summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Owning stage ID.</param>
    /// <param name="operationId">Stable operation ID.</param>
    /// <returns>New document, without writing to disk.</returns>
    public static ProjectJourneyDocument RemoveOperation(
        ProjectJourneyDocument document, Guid stepId, Guid operationId) =>
        UpdateStep(document, stepId, step =>
        {
            List<ProjectJourneyOperation> operations = step.Operations.ToList();
            if (operations.RemoveAll(item => item.Id == operationId) != 1)
            {
                throw new KeyNotFoundException("Opération introuvable.");
            }

            return step with { Operations = operations };
        });

    /// <summary>Updates a single operation while preserving its surrounding stage.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Owning stage ID.</param>
    /// <param name="operationId">Stable operation ID.</param>
    /// <param name="edit">Non-persisting edit.</param>
    /// <returns>New document.</returns>
    private static ProjectJourneyDocument UpdateOperation(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        Func<ProjectJourneyOperation, ProjectJourneyOperation> edit) =>
        UpdateStep(document, stepId, step =>
        {
            bool found = false;
            List<ProjectJourneyOperation> operations = step.Operations.Select(item =>
            {
                if (item.Id != operationId)
                {
                    return item;
                }

                found = true;
                return edit(item);
            }).ToList();
            if (!found)
            {
                throw new KeyNotFoundException("Opération introuvable.");
            }

            return step with { Operations = operations };
        });

    /// <summary>Updates exactly one stage without modifying the caller's list.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <param name="stepId">Stable stage ID.</param>
    /// <param name="edit">Pure transformation.</param>
    /// <returns>New document.</returns>
    private static ProjectJourneyDocument UpdateStep(
        ProjectJourneyDocument document, Guid stepId,
        Func<ProjectJourneyStep, ProjectJourneyStep> edit)
    {
        ValidateDocument(document);
        bool found = false;
        List<ProjectJourneyStep> steps = document.Steps.Select(step =>
        {
            if (step.Id != stepId)
            {
                return step;
            }

            found = true;
            return edit(step);
        }).ToList();
        if (!found)
        {
            throw new KeyNotFoundException("Étape introuvable.");
        }

        return document with { Steps = steps };
    }

    /// <summary>Returns a deterministic sorted copy of the existing stages.</summary>
    /// <param name="document">Current journey snapshot.</param>
    /// <returns>Stages in display order.</returns>
    private static List<ProjectJourneyStep> Ordered(ProjectJourneyDocument document)
    {
        ValidateDocument(document);
        return document.Steps.OrderBy(step => step.Order).ToList();
    }

    /// <summary>Assigns unique dense orders after a move, addition or removal.</summary>
    /// <param name="document">Current document.</param>
    /// <param name="steps">Stages in approved display order.</param>
    /// <returns>Reordered document.</returns>
    private static ProjectJourneyDocument Renumber(
        ProjectJourneyDocument document, IReadOnlyList<ProjectJourneyStep> steps) =>
        document with
        {
            Steps = steps.Select((step, index) => step with { Order = index }).ToList()
        };

    /// <summary>Validates user-facing labels.</summary>
    /// <param name="name">Proposed name.</param>
    /// <returns>Trimmed label.</returns>
    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string label = name.Trim();
        if (label.Length > 200 || label.Any(char.IsControl))
        {
            throw new ArgumentException("Nom d'étape ou d'opération invalide.", nameof(name));
        }

        return label;
    }

    /// <summary>Validates direction.</summary>
    /// <param name="direction">Move delta.</param>
    private static void ValidateDirection(int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }

    /// <summary>Refuses obvious malformed in-memory state before edits.</summary>
    /// <param name="document">Journey snapshot.</param>
    private static void ValidateDocument(ProjectJourneyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Steps is null ||
            document.Steps.Any(step => step is null || step.Operations is null) ||
            document.Steps.Select(step => step.Id).Distinct().Count() !=
                document.Steps.Count)
        {
            throw new InvalidDataException("Parcours incohérent : modification refusée.");
        }
    }
}
