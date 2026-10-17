using System.Text;

namespace Nodalis.Core.Impact;

/// <summary>
/// Indicates whether a typed relation can propagate a possible change from its target to its source.
/// </summary>
public enum ImpactPropagation
{
    None,
    TargetToSource
}

/// <summary>
/// Distinguishes a structural dependency from a contextual suggestion.
/// Neither value claims that a real-world impact has certainly occurred.
/// </summary>
public enum ImpactEvidence
{
    Explicit,
    Suggested
}

/// <summary>
/// Describes the propagation contract for one user-authored relation type.
/// </summary>
public sealed record ImpactRelationRule(
    string RelationType,
    ImpactPropagation Propagation,
    ImpactEvidence Evidence,
    string Explanation);

/// <summary>
/// Centralizes the conservative, direction-aware semantics used by the impact engine.
/// Unknown relation types are retained as context, never silently promoted to dependencies.
/// </summary>
public static class ImpactRelationPolicy
{
    private static readonly IReadOnlyList<ImpactRelationRule> Rules =
    [
        new("dépend de", ImpactPropagation.TargetToSource, ImpactEvidence.Explicit,
            "Si B change et A dépend de B, A pourrait être affecté."),
        new("implémente", ImpactPropagation.TargetToSource, ImpactEvidence.Explicit,
            "Un changement de l'élément implémenté pourrait affecter son implémentation."),
        new("teste", ImpactPropagation.TargetToSource, ImpactEvidence.Explicit,
            "Un changement de l'élément testé pourrait nécessiter une révision du test."),
        new("documente", ImpactPropagation.TargetToSource, ImpactEvidence.Explicit,
            "Un changement de l'élément documenté pourrait nécessiter une révision de sa documentation."),
        new("bloque", ImpactPropagation.None, ImpactEvidence.Suggested,
            "Le blocage décrit un état de travail, pas une dépendance technique certaine."),
        new("remplace", ImpactPropagation.None, ImpactEvidence.Suggested,
            "La substitution historique ne prouve pas que le remplaçant dépend de l'ancien élément."),
        new("est lié à", ImpactPropagation.None, ImpactEvidence.Suggested,
            "Une association ne suffit pas à établir un sens de propagation.")
    ];

    /// <summary>
    /// Gets the documented semantics of the built-in relation types.
    /// </summary>
    public static IReadOnlyList<ImpactRelationRule> KnownRules => Rules;

    /// <summary>
    /// Returns the conservative propagation rule for a relation, preserving unknown custom types.
    /// </summary>
    /// <param name="relationType">Relation text stored in Markdown.</param>
    /// <returns>The matching rule or a non-propagating contextual fallback.</returns>
    public static ImpactRelationRule GetRule(string? relationType)
    {
        string normalized = (relationType ?? string.Empty)
            .Trim()
            .Normalize(NormalizationForm.FormC);

        foreach (ImpactRelationRule rule in Rules)
        {
            if (string.Equals(rule.RelationType, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return new ImpactRelationRule(
            normalized,
            ImpactPropagation.None,
            ImpactEvidence.Suggested,
            "Type personnalisé ou inconnu : relation affichée sans propagation automatique.");
    }
}
