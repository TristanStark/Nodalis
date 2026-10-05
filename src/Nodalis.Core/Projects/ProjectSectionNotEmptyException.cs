namespace Nodalis.Core.Projects;

/// <summary>
/// Signals that a project section cannot be removed until its content is handled explicitly.
/// </summary>
public sealed class ProjectSectionNotEmptyException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of <see cref="ProjectSectionNotEmptyException"/>.
    /// </summary>
    /// <param name="sectionName">The non-empty section name.</param>
    /// <param name="entryCount">The number of filesystem entries still present.</param>
    public ProjectSectionNotEmptyException(
            string sectionName,
            int entryCount)
        : base(
            $"La section « {sectionName} » contient encore {entryCount} élément(s). " +
            "Choisissez explicitement une section de destination avant de la supprimer.")
    {
        SectionName =
            sectionName;
        EntryCount =
            entryCount;
    }

    /// <summary>
    /// Gets the non-empty section name.
    /// </summary>
    public string SectionName { get; }

    /// <summary>
    /// Gets the number of remaining filesystem entries.
    /// </summary>
    public int EntryCount { get; }
}
