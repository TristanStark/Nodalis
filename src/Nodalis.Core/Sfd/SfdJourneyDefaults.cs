using Nodalis.Core.Projects;

namespace Nodalis.Core.Sfd;

/// <summary>
/// Five short SFD journey stages backed by existing 2.0 journey identities
/// and pure editing operations. Defaults are proposals, never auto-created.
/// </summary>
public static class SfdJourneyDefaults
{
    public const string TemplateKey = "sfd-v1";

    /// <summary>
    /// Returns a fresh, uncommitted SFD journey with five independent stages.
    /// Four specialized prompt references are placeholders until the SFD
    /// contract/preparer validates and embeds their full output schema.
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
                Step("sfd-processes", "Processus métiers et activités", 10,
                    ("generate-pm-act", "Préparer la génération des PM et ACT", "sfd-01")),
                Step("sfd-requirements", "Exigences métier", 20,
                    ("generate-exu-exs-exr",
                        "Préparer les EXU / EXS / EXR, conditions, SC et RM", "sfd-02")),
                Step("sfd-features", "Fonctionnalités", 30,
                    ("generate-features",
                        "Préparer les FCT avec ou sans IHM et liens ACT/FCT", "sfd-03")),
                Step("sfd-implementation", "Mise en œuvre fonctionnelle", 40,
                    ("generate-emo",
                        "Préparer les EMO et références aux RM", "sfd-04")),
                Step("sfd-pilotage", "Pilotage et qualification", 50,
                    ("meetings", "Réunions et décisions", null),
                    ("milestones", "Jalons et tâches", null),
                    ("tests", "Qualification et tests", null))
            ]
        };
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
