namespace Nodalis.Core.AI;

/// <summary>
/// Identifies the requested family of tests generated from project documentation.
/// </summary>
public enum AiTestType
{
    /// <summary>Business or user-visible functional scenarios.</summary>
    Functional,

    /// <summary>Focused unit-level scenarios.</summary>
    Unit,

    /// <summary>Scenarios covering integration boundaries.</summary>
    Integration
}

/// <summary>
/// Identifies the documentation scope at which generated scenarios should be written.
/// </summary>
public enum AiTestLevel
{
    /// <summary>Tests focused on individual requirements or documented behaviors.</summary>
    Requirement,

    /// <summary>Tests focused on one component, module, or feature.</summary>
    Component,

    /// <summary>Tests focused on project-level or end-to-end behavior.</summary>
    Project
}

/// <summary>
/// Stores the explicit type and level selected before local test generation.
/// </summary>
public sealed record AiTestGenerationOptions
{
    /// <summary>Gets the selected test family.</summary>
    public required AiTestType Type { get; init; }

    /// <summary>Gets the selected test scope.</summary>
    public required AiTestLevel Level { get; init; }

    /// <summary>Gets the localized test-family label used in prompts and generated documents.</summary>
    public string TypeLabel =>
        Type switch
        {
            AiTestType.Functional => "Fonctionnel",
            AiTestType.Unit => "Unitaire",
            AiTestType.Integration => "Intégration",
            _ => Type.ToString()
        };

    /// <summary>Gets the localized test-scope label used in prompts and generated documents.</summary>
    public string LevelLabel =>
        Level switch
        {
            AiTestLevel.Requirement => "Exigence / comportement",
            AiTestLevel.Component => "Composant / module",
            AiTestLevel.Project => "Projet / bout en bout",
            _ => Level.ToString()
        };
}
