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

    /// <summary>
    /// Links an existing source by a portable, workspace-relative path.
    /// No source file is copied, edited, imported or created.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Operation.</param>
    /// <param name="relativePath">Validated workspace-relative source.</param>
    /// <returns>New journey with one non-owning reference.</returns>
    public static ProjectJourneyDocument AddSource(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        string relativePath)
    {
        string source = ProjectJourneySourcePath.RequireSafe(relativePath);
        return UpdateOperation(document, stepId, operationId, operation =>
        {
            if (operation.SourcePaths.Any(path => string.Equals(
                    path, source, StringComparison.OrdinalIgnoreCase)))
            {
                return operation;
            }

            return operation with
            {
                SourcePaths = [.. operation.SourcePaths, source]
            };
        });
    }

    /// <summary>
    /// Removes only a non-owning source reference. The underlying file and
    /// its contents are never deleted.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Operation.</param>
    /// <param name="relativePath">Exact stored source path.</param>
    /// <returns>New journey without that source reference.</returns>
    public static ProjectJourneyDocument RemoveSource(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        string relativePath)
    {
        ProjectJourneySourcePath.RequireSafe(relativePath);
        return UpdateOperation(document, stepId, operationId, operation =>
        {
            List<string> paths = operation.SourcePaths.ToList();
            if (paths.RemoveAll(path => string.Equals(
                    path, relativePath, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                throw new KeyNotFoundException("Référence de source introuvable.");
            }

            return operation with { SourcePaths = paths };
        });
    }

    /// <summary>
    /// Links a named local Markdown prompt to an operation, without reading
    /// it, copying its content or invoking any AI service.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Operation.</param>
    /// <param name="promptKey">Portable prompt key; null clears the link.</param>
    /// <returns>New journey with the reviewed prompt reference.</returns>
    public static ProjectJourneyDocument SetPromptKey(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        string? promptKey)
    {
        if (promptKey is not null &&
            (promptKey.Length is < 1 or > 80 ||
             !promptKey.All(character => char.IsAsciiLetterOrDigit(character) ||
                 character is '-' or '_')))
        {
            throw new ArgumentException("Clé de prompt invalide.", nameof(promptKey));
        }

        return UpdateOperation(document, stepId, operationId,
            operation => operation with { PromptKey = promptKey });
    }

    /// <summary>
    /// Adds a non-owning relation to an existing persisted milestone GUID.
    /// The caller must validate that the milestone truly exists in the
    /// project's canonical Markdown table before proposing this edit.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Operation to link.</param>
    /// <param name="milestoneId">Verified persisted milestone identity.</param>
    /// <returns>New journey with a single relation.</returns>
    public static ProjectJourneyDocument AddMilestoneLink(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        Guid milestoneId)
    {
        RequireRelatedId(milestoneId);
        return UpdateOperation(document, stepId, operationId, operation =>
        {
            if (operation.MilestoneIds.Contains(milestoneId))
            {
                return operation;
            }

            return operation with
            {
                MilestoneIds = [.. operation.MilestoneIds, milestoneId]
            };
        });
    }

    /// <summary>
    /// Removes only the milestone relation, not the milestone, its tasks,
    /// dependencies, dates or other references.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Operation to unlink.</param>
    /// <param name="milestoneId">Stored relation identifier.</param>
    /// <returns>New journey without the selected relation.</returns>
    public static ProjectJourneyDocument RemoveMilestoneLink(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        Guid milestoneId)
    {
        RequireRelatedId(milestoneId);
        return UpdateOperation(document, stepId, operationId, operation =>
        {
            if (!operation.MilestoneIds.Contains(milestoneId))
            {
                throw new KeyNotFoundException("Lien vers le jalon introuvable.");
            }

            return operation with
            {
                MilestoneIds = operation.MilestoneIds.Where(id =>
                    id != milestoneId).ToList()
            };
        });
    }

    /// <summary>
    /// Links one gate with an ID confirmed in the canonical project registry.
    /// Does not create a gate, change its verdict or change a milestone.
    /// Callers must resolve the same project's registry before this command.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Owning operation.</param>
    /// <param name="gateId">Verified existing gate identity.</param>
    /// <returns>New journey with a non-owning gate reference.</returns>
    public static ProjectJourneyDocument AddGateLink(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        Guid gateId)
    {
        RequireRelatedId(gateId);
        return UpdateOperation(document, stepId, operationId, operation =>
            operation.GateIds.Contains(gateId)
                ? operation
                : operation with { GateIds = [.. operation.GateIds, gateId] });
    }

    /// <summary>
    /// Removes a previously persisted gate relation; removing an orphan
    /// never grants or revokes a gate decision.
    /// </summary>
    /// <param name="document">Current journey.</param>
    /// <param name="stepId">Owning stage.</param>
    /// <param name="operationId">Operation to unlink.</param>
    /// <param name="gateId">Existing stored gate identity.</param>
    /// <returns>New journey with the reference removed only.</returns>
    public static ProjectJourneyDocument RemoveGateLink(
        ProjectJourneyDocument document, Guid stepId, Guid operationId,
        Guid gateId)
    {
        RequireRelatedId(gateId);
        return UpdateOperation(document, stepId, operationId, operation =>
        {
            if (!operation.GateIds.Contains(gateId))
            {
                throw new KeyNotFoundException("Lien vers le gate introuvable.");
            }

            return operation with
            {
                GateIds = operation.GateIds.Where(id => id != gateId).ToList()
            };
        });
    }

    /// <summary>Rejects empty relation identities.</summary>
    /// <param name="id">Candidate linked GUID.</param>
    private static void RequireRelatedId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifiant de relation vide.", nameof(id));
        }
    }

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
