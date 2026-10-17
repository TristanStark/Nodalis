using Nodalis.Core.Domain;

namespace Nodalis.Core.Projects;

/// <summary>
/// Represents an editable snapshot of one project's current structure.
/// </summary>
public sealed record ProjectStructureState
{
    /// <summary>
    /// Gets the project manifest.
    /// </summary>
    public required ProjectManifest Project { get; init; }

    /// <summary>
    /// Gets the absolute project directory.
    /// </summary>
    public required string ProjectDirectory { get; init; }

    /// <summary>
    /// Gets project sections ordered by their manifest order.
    /// </summary>
    public IReadOnlyList<ProjectSectionState> Sections { get; init; } = [];
}
