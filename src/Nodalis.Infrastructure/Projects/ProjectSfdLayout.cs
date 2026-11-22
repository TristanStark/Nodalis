using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Initial SFD document layout. Business objects are deliberately NOT fabricated
/// during creation; folders for PM, ACT and FCT remain empty until explicitly authored.
/// </summary>
public static class ProjectSfdLayout
{
    /// <summary>Creates stable section identities for a new SFD project.</summary>
    /// <returns>Sections used by the existing worktree, milestones, tests and glossary services.</returns>
    public static List<SectionManifest> CreateSections() =>
    [
        Section("SPECS FONCTIONNELLES", 10),
        Section("SPECS TECHNIQUES", 20),
        Section("DOCUMENTATION", 30),
        Section("QUALIFICATION", 40),
        Section("MANAGEMENT", 50),
        Section("Réunions", 60, "MANAGEMENT/Réunions"),
        Section("Décisions", 70, "MANAGEMENT/Décisions"),
        Section("Jalons", 80, "MANAGEMENT", "milestones"),
        Section("Tests", 90, "QUALIFICATION/TESTS", "tests"),
        Section("Glossaire", 100, templateKey: "glossary")
    ];

    /// <summary>
    /// Creates only agreed fixed folders and the single shared business-rule catalog.
    /// This runs in the creator's private staging directory before its atomic rename.
    /// </summary>
    /// <param name="stagingRoot">Fresh, isolated project staging directory.</param>
    /// <param name="cancellationToken">Cancellation requested before final project publication.</param>
    /// <returns>The completed skeleton preparation.</returns>
    public static async Task InitializeAsync(
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);

        string[] directories =
        [
            "SPECS FONCTIONNELLES/PROCESSUS METIERS",
            "SPECS FONCTIONNELLES/FONCTIONNALITES",
            "SPECS TECHNIQUES/dmep",
            "SPECS TECHNIQUES/APPLICATIONS HOSTS",
            "SPECS TECHNIQUES/APPLICATIONS OPEN",
            "SPECS TECHNIQUES/APPLICATIONS LINUX",
            "DOCUMENTATION",
            "QUALIFICATION/TESTS",
            "MANAGEMENT/Réunions",
            "MANAGEMENT/Décisions"
        ];

        foreach (string directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = ProjectSectionDirectoryLayout.Resolve(
                stagingRoot, "SFD", directory);
            Directory.CreateDirectory(destination);
        }

        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(stagingRoot, "SPECS FONCTIONNELLES",
                "presentation projet.md"),
            "# Présentation du projet\n\n" +
            "_Décrire ici le contexte, la cible, le périmètre et les décisions validées._\n",
            cancellationToken);

        string rulesCatalog = Path.Combine(
            stagingRoot, "DOCUMENTATION", "liste des règles métiers.md");
        await AtomicFileWriter.WriteAllTextAsync(
            Path.Combine(stagingRoot, "MANAGEMENT", "Tâches.md"),
            "# Tâches\n\n",
            cancellationToken);

        await AtomicFileWriter.WriteAllTextAsync(
            rulesCatalog,
            "# Liste des règles métiers\n\n" +
            "Catalogue canonique des règles métiers du projet. " +
            "Chaque règle possède un identifiant stable et est référencée " +
            "depuis les sous-conditions, sans copie de sa définition.\n\n" +
            "_Aucune règle métier définie._\n",
            cancellationToken);
    }

    /// <summary>Builds one section independently of its document path or business cardinalities.</summary>
    /// <param name="name">Worktree label.</param>
    /// <param name="order">Display order.</param>
    /// <param name="relativeDirectory">Optional canonical project-relative folder.</param>
    /// <param name="templateKey">Existing Nodalis Markdown template role.</param>
    /// <returns>A fresh section manifest.</returns>
    private static SectionManifest Section(
        string name,
        int order,
        string? relativeDirectory = null,
        string? templateKey = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Order = order,
            IsSingleton = true,
            RelativeDirectory = relativeDirectory,
            TemplateKey = templateKey
        };
}
