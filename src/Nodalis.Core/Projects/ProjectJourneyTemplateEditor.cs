using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

/// <summary>
/// Pure transformations of workspace template defaults. These commands never
/// mutate a project journey, an already-initialized project or the input lists.
/// </summary>
public static class ProjectJourneyTemplateEditor
{
    /// <summary>Adds a named opt-in template without changing existing ones.</summary>
    public static ProjectJourneyTemplateCatalog AddTemplate(
        ProjectJourneyTemplateCatalog catalog, string key, string name,
        ProjectComplexity complexity)
    {
        ValidateCatalog(catalog);
        ValidateKey(key);
        string label = ValidateName(name);
        if (!Enum.IsDefined(complexity) || catalog.Templates.Any(item =>
                string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Profil de modèle invalide ou clé déjà utilisée.");
        }

        return catalog with
        {
            Templates =
            [
                .. catalog.Templates,
                new ProjectJourneyTemplate
                {
                    Key = key,
                    Name = label,
                    Complexity = complexity
                }
            ]
        };
    }

    /// <summary>Renames a template while retaining its stable logical key.</summary>
    public static ProjectJourneyTemplateCatalog RenameTemplate(
        ProjectJourneyTemplateCatalog catalog, string templateKey, string name)
    {
        string label = ValidateName(name);
        return UpdateTemplate(catalog, templateKey, template =>
            template with { Name = label });
    }

    /// <summary>
    /// Removes only a workspace template proposal. Existing project journeys
    /// retain their materialized steps and original template provenance.
    /// </summary>
    public static ProjectJourneyTemplateCatalog RemoveTemplate(
        ProjectJourneyTemplateCatalog catalog, string templateKey)
    {
        ValidateCatalog(catalog);
        List<ProjectJourneyTemplate> templates = catalog.Templates.ToList();
        if (templates.RemoveAll(item =>
                string.Equals(item.Key, templateKey, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new KeyNotFoundException("Modèle de parcours introuvable.");
        }

        return catalog with { Templates = templates };
    }

    /// <summary>Adds a stage with a portable, caller-generated stable key.</summary>
    public static ProjectJourneyTemplateCatalog AddStep(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string name)
    {
        ValidateKey(stepKey);
        string label = ValidateName(name);
        return UpdateTemplate(catalog, templateKey, template =>
        {
            if (template.Steps.Any(step => string.Equals(
                    step.Key, stepKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Clé d'étape déjà utilisée.");
            }

            int order = template.Steps.Count == 0
                ? 0
                : checked(template.Steps.Max(step => step.Order) + 1);
            return template with
            {
                Steps =
                [
                    .. template.Steps,
                    new ProjectJourneyStepTemplate
                    {
                        Key = stepKey,
                        Name = label,
                        Order = order
                    }
                ]
            };
        });
    }

    /// <summary>Renames the stage but preserves the matching template key.</summary>
    public static ProjectJourneyTemplateCatalog RenameStep(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string name)
    {
        string label = ValidateName(name);
        return UpdateStep(catalog, templateKey, stepKey, step =>
            step with { Name = label });
    }

    /// <summary>Moves the chosen stage one place without changing any keys.</summary>
    public static ProjectJourneyTemplateCatalog MoveStep(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, int direction)
    {
        ValidateDirection(direction);
        return UpdateTemplate(catalog, templateKey, template =>
        {
            List<ProjectJourneyStepTemplate> stages = template.Steps
                .OrderBy(item => item.Order).ToList();
            int index = stages.FindIndex(item =>
                string.Equals(item.Key, stepKey, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new KeyNotFoundException("Étape de modèle introuvable.");
            }

            int destination = index + direction;
            if (destination < 0 || destination >= stages.Count)
            {
                return template;
            }

            (stages[index], stages[destination]) =
                (stages[destination], stages[index]);
            return template with
            {
                Steps = stages.Select((item, order) =>
                    item with { Order = order }).ToList()
            };
        });
    }

    /// <summary>Removes an optional proposal stage, never its project data.</summary>
    public static ProjectJourneyTemplateCatalog RemoveStep(
        ProjectJourneyTemplateCatalog catalog, string templateKey, string stepKey) =>
        UpdateTemplate(catalog, templateKey, template =>
        {
            List<ProjectJourneyStepTemplate> steps = template.Steps
                .OrderBy(item => item.Order).ToList();
            if (steps.RemoveAll(item => string.Equals(
                    item.Key, stepKey, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                throw new KeyNotFoundException("Étape de modèle introuvable.");
            }

            return template with
            {
                Steps = steps.Select((item, order) =>
                    item with { Order = order }).ToList()
            };
        });

    /// <summary>Adds a repeatable operation proposal to a template stage.</summary>
    public static ProjectJourneyTemplateCatalog AddOperation(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string operationKey, string name)
    {
        ValidateKey(operationKey);
        string label = ValidateName(name);
        return UpdateStep(catalog, templateKey, stepKey, step =>
        {
            if (step.Operations.Any(item => string.Equals(
                    item.Key, operationKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Clé d'opération déjà utilisée.");
            }

            return step with
            {
                Operations =
                [
                    .. step.Operations,
                    new ProjectJourneyOperationTemplate
                    {
                        Key = operationKey,
                        Name = label
                    }
                ]
            };
        });
    }

    /// <summary>Renames an operation without replacing its stable key or prompt.</summary>
    public static ProjectJourneyTemplateCatalog RenameOperation(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string operationKey, string name)
    {
        string label = ValidateName(name);
        return UpdateOperation(catalog, templateKey, stepKey, operationKey,
            operation => operation with { Name = label });
    }

    /// <summary>Sets an optional prompt identifier; never executes an AI request.</summary>
    public static ProjectJourneyTemplateCatalog SetPromptKey(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string operationKey, string? promptKey)
    {
        if (promptKey is not null)
        {
            ValidateKey(promptKey);
        }

        return UpdateOperation(catalog, templateKey, stepKey, operationKey,
            operation => operation with { PromptKey = promptKey });
    }

    /// <summary>Moves a repeatable operation one place in its stage.</summary>
    public static ProjectJourneyTemplateCatalog MoveOperation(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string operationKey, int direction)
    {
        ValidateDirection(direction);
        return UpdateStep(catalog, templateKey, stepKey, step =>
        {
            List<ProjectJourneyOperationTemplate> operations = step.Operations.ToList();
            int index = operations.FindIndex(item => string.Equals(
                item.Key, operationKey, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                throw new KeyNotFoundException("Opération de modèle introuvable.");
            }

            int destination = index + direction;
            if (destination < 0 || destination >= operations.Count)
            {
                return step;
            }

            (operations[index], operations[destination]) =
                (operations[destination], operations[index]);
            return step with { Operations = operations };
        });
    }

    /// <summary>Removes an operation proposal without deleting project evidence.</summary>
    public static ProjectJourneyTemplateCatalog RemoveOperation(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        string stepKey, string operationKey) =>
        UpdateStep(catalog, templateKey, stepKey, step =>
        {
            List<ProjectJourneyOperationTemplate> operations = step.Operations.ToList();
            if (operations.RemoveAll(item => string.Equals(
                    item.Key, operationKey, StringComparison.OrdinalIgnoreCase)) != 1)
            {
                throw new KeyNotFoundException("Opération de modèle introuvable.");
            }

            return step with { Operations = operations };
        });

    /// <summary>Returns a new catalog, replacing exactly the selected template.</summary>
    private static ProjectJourneyTemplateCatalog UpdateTemplate(
        ProjectJourneyTemplateCatalog catalog, string templateKey,
        Func<ProjectJourneyTemplate, ProjectJourneyTemplate> edit)
    {
        ValidateCatalog(catalog);
        bool found = false;
        List<ProjectJourneyTemplate> templates = catalog.Templates.Select(item =>
        {
            if (!string.Equals(item.Key, templateKey, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            found = true;
            return edit(item);
        }).ToList();
        if (!found)
        {
            throw new KeyNotFoundException("Modèle de parcours introuvable.");
        }

        return catalog with { Templates = templates };
    }

    /// <summary>Replaces exactly one stage in one template.</summary>
    private static ProjectJourneyTemplateCatalog UpdateStep(
        ProjectJourneyTemplateCatalog catalog, string templateKey, string stepKey,
        Func<ProjectJourneyStepTemplate, ProjectJourneyStepTemplate> edit) =>
        UpdateTemplate(catalog, templateKey, template =>
        {
            bool found = false;
            List<ProjectJourneyStepTemplate> steps = template.Steps.Select(item =>
            {
                if (!string.Equals(item.Key, stepKey, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                found = true;
                return edit(item);
            }).ToList();
            if (!found)
            {
                throw new KeyNotFoundException("Étape de modèle introuvable.");
            }

            return template with { Steps = steps };
        });

    /// <summary>Replaces exactly one operation in one template stage.</summary>
    private static ProjectJourneyTemplateCatalog UpdateOperation(
        ProjectJourneyTemplateCatalog catalog, string templateKey, string stepKey,
        string operationKey,
        Func<ProjectJourneyOperationTemplate, ProjectJourneyOperationTemplate> edit) =>
        UpdateStep(catalog, templateKey, stepKey, step =>
        {
            bool found = false;
            List<ProjectJourneyOperationTemplate> operations = step.Operations.Select(item =>
            {
                if (!string.Equals(item.Key, operationKey, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                found = true;
                return edit(item);
            }).ToList();
            if (!found)
            {
                throw new KeyNotFoundException("Opération de modèle introuvable.");
            }

            return step with { Operations = operations };
        });

    /// <summary>Rejects invalid or malformed source models before any edit.</summary>
    private static void ValidateCatalog(ProjectJourneyTemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.SchemaVersion != ProjectJourneyTemplateCatalog.CurrentSchemaVersion ||
            catalog.Templates is null ||
            catalog.Templates.Any(template => template is null ||
                template.Steps is null || template.Steps.Any(step =>
                    step is null || step.Operations is null)))
        {
            throw new InvalidDataException("Catalogue de parcours incohérent.");
        }
    }

    /// <summary>Validates a portable logical key.</summary>
    private static void ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80 ||
            !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException("Clé de modèle invalide.", nameof(key));
        }
    }

    /// <summary>Validates and trims a user-facing label.</summary>
    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string label = name.Trim();
        if (label.Length > 200 || label.Any(char.IsControl))
        {
            throw new ArgumentException("Libellé de modèle invalide.", nameof(name));
        }

        return label;
    }

    /// <summary>Restricts stage movement to a single place.</summary>
    private static void ValidateDirection(int direction)
    {
        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
    }
}
