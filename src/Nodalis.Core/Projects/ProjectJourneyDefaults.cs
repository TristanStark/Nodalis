using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

/// <summary>
/// Built-in journey defaults. These are proposals only: reading a project
/// never materializes stages or performs their operations.
/// </summary>
public static class ProjectJourneyDefaults
{
    /// <summary>
    /// Creates a fresh catalog on each call so consumers cannot mutate
    /// another workspace's default objects through shared list references.
    /// Ultra-simple projects intentionally have no imposed journey.
    /// </summary>
    /// <returns>Built-in templates for Simple, Medium and Complex projects.</returns>
    public static ProjectJourneyTemplateCatalog CreateCatalog() => new()
    {
        Templates =
        [
            new ProjectJourneyTemplate
            {
                Key = "simple-v1",
                Name = "Parcours Simple",
                Complexity = ProjectComplexity.Simple,
                Steps =
                [
                    Step("cadrage", "Cadrage", 10, ("besoin", "Clarifier le besoin")),
                    Step("realisation", "Réalisation", 20,
                        ("solution", "Réaliser et documenter la solution")),
                    Step("cloture", "Clôture", 30,
                        ("bilan", "Établir le bilan et les suites"))
                ]
            },
            new ProjectJourneyTemplate
            {
                Key = "medium-v1",
                Name = "Parcours Moyen",
                Complexity = ProjectComplexity.Medium,
                Steps =
                [
                    Step("cadrage", "Cadrage", 10, ("besoin", "Clarifier le besoin")),
                    Step("conception", "Conception", 20,
                        ("solution", "Définir la solution")),
                    Step("realisation", "Réalisation", 30,
                        ("implementation", "Documenter les changements")),
                    Step("tests", "Tests", 40,
                        ("verification", "Préparer et exécuter les vérifications")),
                    Step("cloture", "Clôture", 50,
                        ("bilan", "Clore et transmettre les éléments utiles"))
                ]
            },
            new ProjectJourneyTemplate
            {
                Key = "complex-v1",
                Name = "Parcours Complexe",
                Complexity = ProjectComplexity.Complex,
                Steps =
                [
                    Step("cadrage", "Cadrage", 10,
                        ("contexte", "Décrire le contexte et les objectifs")),
                    Step("exigences", "Exigences", 20,
                        ("inventaire", "Recueillir les exigences"),
                        ("criteres", "Identifier les critères de validation")),
                    Step("risques", "Risques", 30,
                        ("identifier", "Identifier les risques"),
                        ("reevaluer", "Prévoir les réévaluations")),
                    Step("conception", "Conception", 40,
                        ("architecture", "Documenter la conception")),
                    Step("preparation-developpement", "Préparation développement", 50,
                        ("plan", "Préparer les travaux")),
                    Step("realisation", "Réalisation et changements", 60,
                        ("changements", "Tracer les réalisations et changements")),
                    Step("tests", "Tests", 70,
                        ("campagnes", "Préparer les tests et leurs preuves")),
                    Step("mise-en-production", "Mise en production", 80,
                        ("deploiement", "Préparer le déploiement et le retour arrière")),
                    Step("cloture", "Clôture", 90,
                        ("passation", "Préparer le bilan et la passation"))
                ]
            }
        ]
    };

    /// <summary>Creates a stage with ordered, repeatable operation definitions.</summary>
    /// <param name="key">Stable stage key.</param>
    /// <param name="name">Stage display name.</param>
    /// <param name="order">Default stage order.</param>
    /// <param name="operations">Stable operation keys and labels.</param>
    /// <returns>A fresh stage definition.</returns>
    private static ProjectJourneyStepTemplate Step(string key, string name,
        int order, params (string Key, string Name)[] operations) => new()
        {
            Key = key,
            Name = name,
            Order = order,
            Operations = operations.Select(item => new ProjectJourneyOperationTemplate
            {
                Key = item.Key,
                Name = item.Name,
                PromptKey = SuggestedPromptKey(key, item.Key)
            }).ToList()
        };

    /// <summary>
    /// Recommends authentic bundled prompts for INITIAL templates only.
    /// Existing project journeys and customized workspace templates never
    /// receive an automatic migration or retroactive prompt replacement.
    /// </summary>
    /// <param name="step">Logical step key.</param>
    /// <param name="operation">Logical operation key.</param>
    /// <returns>Versioned user-prompt key or null when not mapped.</returns>
    private static string? SuggestedPromptKey(
        string step, string operation) => (step, operation) switch
        {
            ("cadrage", "besoin") => "frt100-05",
            ("cadrage", "contexte") => "frt100-02",
            ("exigences", "inventaire") => "frt100-01",
            ("exigences", "criteres") => "frt100-07",
            ("risques", "identifier") => "frt100-15",
            ("risques", "reevaluer") => "frt100-14",
            ("conception", "architecture" or "solution") => "frt100-11",
            ("preparation-developpement", "plan") => "frt100-17",
            ("realisation", "solution") => "frt100-11",
            ("realisation", "implementation") => "frt100-17",
            ("realisation", "changements") => "frt100-16",
            ("tests", "verification" or "campagnes") => "frt100-10",
            ("mise-en-production", "deploiement") => "frt100-18",
            ("cloture", "bilan" or "passation") => "frt100-30",
            _ => null
        };
}
