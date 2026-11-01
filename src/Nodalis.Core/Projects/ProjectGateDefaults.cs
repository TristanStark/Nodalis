namespace Nodalis.Core.Projects;

/// <summary>
/// In-memory, optional starter gates for distinct project checkpoints.
/// Never creates or updates a gate registry without explicit user approval.
/// </summary>
public static class ProjectGateDefaults
{
    /// <summary>Names of the optional built-in checkpoint proposals.</summary>
    private static readonly (string Name, string[] Criteria)[] Standards =
    [
        ("Fin de cadrage", [
            "Périmètre et objectifs qualifiés",
            "Principales exigences et parties prenantes identifiées",
            "Risques et dépendances revus"]),
        ("Prêt pour développement", [
            "Critères d'acceptation clarifiés",
            "Préparation technique et impacts évalués",
            "Points bloquants et arbitrages examinés"]),
        ("Validation recette", [
            "Résultats des tests qualifiés",
            "Anomalies ouvertes et risques résiduels évalués",
            "Écarts et preuves de recette examinés"]),
        ("GO/NO-GO production", [
            "Périmètre, version et environnement confirmés",
            "Procédure et retour arrière revus",
            "Qualité, incidents, risques et réserves explicités"]),
        ("Clôture", [
            "Livrables et documentation passés en revue",
            "Jalons et réserves examinés",
            "Passation et suites éventuelles décidées"])
    ];

    /// <summary>
    /// Adds missing proposed gates, without modifying any existing gate,
    /// criterion, decision or milestone; idempotent by gate name.
    /// </summary>
    /// <param name="registry">Read registry.</param>
    /// <returns>Proposed registry with missing standards added.</returns>
    public static ProjectGateRegistry ProposeMissing(ProjectGateRegistry registry)
    {
        ProjectGateValidation.Validate(registry);
        List<ProjectGateDefinition> gates = registry.Gates.ToList();
        foreach ((string name, string[] criteria) in Standards)
        {
            if (gates.Any(item => string.Equals(
                    item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            gates.Add(new ProjectGateDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                Criteria = criteria.Select(label => new ProjectGateCriterion
                {
                    Id = Guid.NewGuid(),
                    Name = label
                }).ToList()
            });
        }

        return registry with { Gates = gates };
    }
}
