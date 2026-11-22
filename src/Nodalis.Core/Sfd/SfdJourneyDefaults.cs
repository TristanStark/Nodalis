using Nodalis.Core.Projects;

namespace Nodalis.Core.Sfd;

/// <summary>
/// Six short SFD journey stages backed by existing 2.0 journey identities
/// and pure editing operations. Defaults are proposals, never auto-created.
/// </summary>
public static class SfdJourneyDefaults
{
    public const string TemplateKey = "sfd-v1";

    /// <summary>
    /// Returns a fresh, uncommitted SFD journey with five specialized prompts
    /// followed by independent project pilotage. Master prompts are copy-only
    /// Markdown text; legacy four-step JSON imports keep separate keys.
    /// </summary>
    /// <param name="projectId">Owning SFD project's GUID.</param>
    /// <returns>Unpersisted, editable project journey.</returns>
    public static ProjectJourneyDocument Create(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Identifiant projet requis.", nameof(projectId));
        }

        return new ProjectJourneyDocument
        {
            ProjectId = projectId,
            TemplateKey = TemplateKey,
            Steps =
            [
                Step("sfd-processes", "Cartographie des processus métiers", 10,
                    ("determine-processes", "Déterminer les processus métiers", "sfd-master-01")),
                Step("sfd-activities", "Détail des activités métier", 20,
                    ("detail-activity", "Générer le prompt Activité", "sfd-master-02")),
                Step("sfd-features", "Assemblage des fonctionnalités", 30,
                    ("assemble-features", "Assembler les fonctionnalités", "sfd-master-03")),
                Step("sfd-requirements", "Exigences métier par FCT et ACT", 40,
                    ("generate-requirements", "Préparer les EXU / EXS / EXR de la FCT/ACT", "sfd-master-04")),
                Step("sfd-implementation", "Spécification transversale des fonctionnalités", 50,
                    ("generate-emo", "Préparer les EMO transversales de la FCT", "sfd-master-05")),
                Step("sfd-pilotage", "Pilotage et qualification", 60,
                    ("meetings", "Réunions et décisions", null),
                    ("milestones", "Jalons et tâches", null),
                    ("tests", "Qualification et tests", null))
            ]
        };
    }

    /// <summary>
    /// Builds a reviewed migration proposal for a legacy four-prompt SFD journey.
    /// Preserves user-added operations, linked source pointers and custom stages.
    /// Resets changed stage statuses because old completion is not proof of new work.
    /// </summary>
    /// <param name="previous">Previously persisted journey to convert.</param>
    /// <returns>Independent complete journey awaiting explicit optimistic save.</returns>
    public static ProjectJourneyDocument UpgradeToMasterPrompts(
        ProjectJourneyDocument previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous.TemplateKey != TemplateKey)
        {
            throw new InvalidDataException("Modèle de parcours SFD inattendu.");
        }

        ProjectJourneyDocument defaults = Create(previous.ProjectId);
        string[] oldKeys =
        [
            "sfd-01", "sfd-01", "sfd-03", "sfd-02", "sfd-04"
        ];
        List<ProjectJourneyStep> migrated = [];
        foreach (ProjectJourneyStep step in defaults.Steps)
        {
            ProjectJourneyStep? original = previous.Steps.FirstOrDefault(item =>
                item.TemplateKey == step.TemplateKey);
            if (step.TemplateKey == "sfd-pilotage")
            {
                migrated.Add(original is null ? step :
                    original with { Order = step.Order });
                continue;
            }

            ProjectJourneyOperation proposed = step.Operations.Single();
            int index = migrated.Count;
            string oldKey = oldKeys[index];
            ProjectJourneyOperation? oldOperation = previous.Steps
                .SelectMany(item => item.Operations)
                .FirstOrDefault(item =>
                    item.PromptKey == proposed.PromptKey);
            oldOperation ??= previous.Steps.SelectMany(item => item.Operations)
                .FirstOrDefault(item => item.PromptKey == oldKey);
            ProjectJourneyOperation resolved = oldOperation is null
                ? proposed
                : proposed with
                {
                    Id = oldOperation.PromptKey == "sfd-master-02" ||
                        oldOperation.PromptKey == oldKey &&
                        !migrated.SelectMany(item => item.Operations)
                            .Any(item => item.Id == oldOperation.Id)
                            ? oldOperation.Id : proposed.Id,
                    SourcePaths = oldOperation.SourcePaths.ToList()
                };

            List<ProjectJourneyOperation> additional = original?.Operations
                .Where(item => item.PromptKey is null ||
                    (!item.PromptKey.StartsWith("sfd-", StringComparison.Ordinal) &&
                     !item.PromptKey.StartsWith("sfd-master-", StringComparison.Ordinal)))
                .ToList() ?? [];
            migrated.Add(step with
            {
                Id = original?.Id ?? step.Id,
                Status = ProjectJourneyStepStatus.Todo,
                Operations = new[] { resolved }.Concat(additional).ToList()
            });
        }

        HashSet<string> standardKeys = defaults.Steps
            .Select(item => item.TemplateKey!)
            .ToHashSet(StringComparer.Ordinal);
        int nextOrder = 70;
        foreach (ProjectJourneyStep other in previous.Steps.Where(item =>
            item.TemplateKey is null || !standardKeys.Contains(item.TemplateKey)))
        {
            migrated.Add(other with { Order = nextOrder });
            nextOrder += 10;
        }

        return previous with { Steps = migrated };
    }

    /// <summary>Creates a stage with a stable semantic key and fresh instance GUIDs.</summary>
    /// <param name="key">Fixed semantic stage key.</param>
    /// <param name="name">User-visible label.</param>
    /// <param name="order">Default presentation order.</param>
    /// <param name="operations">Proposed non-executing operations.</param>
    /// <returns>New editable journey stage.</returns>
    private static ProjectJourneyStep Step(string key, string name, int order,
        params (string Key, string Name, string? PromptKey)[] operations) => new()
        {
            Id = Guid.NewGuid(),
            TemplateKey = key,
            Name = name,
            Order = order,
            Operations = operations.Select(item => new ProjectJourneyOperation
            {
                Id = Guid.NewGuid(),
                TemplateKey = item.Key,
                Name = item.Name,
                PromptKey = item.PromptKey
            }).ToList()
        };
}
