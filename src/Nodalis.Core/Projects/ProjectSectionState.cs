namespace Nodalis.Core.Projects;

/// <summary>
/// Describes one project section as represented by its manifest and filesystem content.
/// </summary>
public sealed record ProjectSectionState
{
    /// <summary>
    /// Gets the stable section identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the user-visible section name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the manifest ordering value.
    /// </summary>
    public required int Order { get; init; }

    /// <summary>
    /// Gets a value indicating whether the section is intended to hold one canonical document.
    /// </summary>
    public bool IsSingleton { get; init; }

    /// <summary>
    /// Gets the optional template role retained from project creation.
    /// </summary>
    public string? TemplateKey { get; init; }

    /// <summary>
    /// Gets a value indicating whether this section fulfills one of Nodalis's four required project roles.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Gets the section directory.
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Gets Markdown documents contained in this section, relative to the section directory.
    /// </summary>
    public IReadOnlyList<string> Documents { get; init; } = [];

    /// <summary>
    /// Gets the number of Markdown documents currently contained in the section.
    /// </summary>
    public int DocumentCount =>
        Documents.Count;
}
