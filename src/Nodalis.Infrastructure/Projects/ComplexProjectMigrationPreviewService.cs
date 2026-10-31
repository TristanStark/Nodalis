using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Read-only state of one proposed Complex project folder move.</summary>
public enum ComplexMigrationMoveState
{
    /// <summary>Source exists and destination does not.</summary>
    Candidate,
    /// <summary>Section is already stored in the intended v2 folder.</summary>
    AlreadyPlaced,
    /// <summary>Source is missing or no longer at its original location.</summary>
    MissingSource,
    /// <summary>Collision, symlink, or ambiguous role requires manual correction.</summary>
    Blocked
}

/// <summary>One proposed move, not permission to modify any source.</summary>
/// <param name="SectionId">Stable identity of the existing section.</param>
/// <param name="SectionName">Human-readable original section.</param>
/// <param name="From">Project-relative original folder.</param>
/// <param name="To">Project-relative proposed folder.</param>
/// <param name="State">Read-only collision analysis.</param>
/// <param name="Diagnostic">Explanation for human qualification.</param>
public sealed record ComplexMigrationMove(
    Guid SectionId,
    string SectionName,
    string From,
    string To,
    ComplexMigrationMoveState State,
    string Diagnostic);

/// <summary>Read-only inventory; no migration application capability exists.</summary>
/// <param name="ProjectId">Existing project ID.</param>
/// <param name="Moves">Proposed folder moves, including conflicts.</param>
/// <param name="MissingDirectories">Suggested v2 folders absent from disk.</param>
/// <param name="Warnings">Items requiring human review.</param>
public sealed record ComplexProjectMigrationPreview(
    Guid ProjectId,
    IReadOnlyList<ComplexMigrationMove> Moves,
    IReadOnlyList<string> MissingDirectories,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Previews the Complex v2 layout against an existing project without writing,
/// creating, renaming or moving any file, directory, or project manifest.
/// </summary>
public sealed class ComplexProjectMigrationPreviewService
{
    private static readonly string[] Layout =
    [
        "Documentation",
        "Documentation/Exigences",
        "Documentation/Fonctionnel",
        "Documentation/Technique",
        "Documentation/Technique/Architecture",
        "Analyse",
        "Analyse/Risques",
        "Analyse/Dépendances",
        "Management",
        "Management/Réunions",
        "Management/Décisions",
        "Exploitation",
        "Exploitation/Déploiement",
        "Tests"
    ];

    private readonly string _workspaceRoot;

    /// <summary>Scopes all reads to one explicit workspace.</summary>
    /// <param name="workspaceRoot">Workspace root path.</param>
    public ComplexProjectMigrationPreviewService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    /// <summary>
    /// Lists only unambiguous default-section paths and detects collisions
    /// without changing existing custom files, links, or template profiles.
    /// </summary>
    /// <param name="projectDirectory">Existing project folder inside the workspace.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A human-reviewable, non-executable migration inventory.</returns>
    public async Task<ComplexProjectMigrationPreview> PreviewAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string root = Path.GetFullPath(projectDirectory);
        string relative = Path.GetRelativePath(_workspaceRoot, root);
        if (Path.IsPathRooted(relative) ||
            relative == "." ||
            relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Projet hors du workspace.");
        }

        string manifestPath = Path.Combine(root, WorkspaceLayout.ProjectManifestFileName);
        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (project.InitialComplexity != ProjectComplexity.Complex)
        {
            throw new InvalidOperationException("Inventaire réservé aux projets Complexes.");
        }

        List<string> warnings = [];
        List<(SectionManifest Section, string Destination)> proposed = [];
        foreach (SectionManifest section in project.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? destination = GetDestination(section);
            if (destination is null)
            {
                if (section.TemplateKey is null &&
                    !string.Equals(section.Name, "Jalons", StringComparison.OrdinalIgnoreCase) &&
                    (section.RelativeDirectory is null ||
                     !Layout.Contains(section.RelativeDirectory,
                         StringComparer.OrdinalIgnoreCase)))
                {
                    warnings.Add("Section hors plan conservée : " + section.Name);
                }

                continue;
            }

            proposed.Add((section, destination));
        }

        HashSet<string> duplicates = proposed
            .GroupBy(item => item.Destination, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<ComplexMigrationMove> moves = [];
        foreach ((SectionManifest section, string destination) in proposed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool milestone = string.Equals(section.TemplateKey,
                "milestones", StringComparison.OrdinalIgnoreCase);
            string source = milestone
                ? (section.RelativeDirectory is null
                    ? "Jalons.md"
                    : section.RelativeDirectory + "/Jalons.md")
                : section.RelativeDirectory ??
                    WindowsPathRules.SanitizeSegment(section.Name);

            string from = Resolve(root, source);
            string to = Resolve(root, destination);

            ComplexMigrationMoveState state = ComplexMigrationMoveState.Candidate;
            string detail = "Proposition seulement : liens, onglets et manifeste à vérifier.";
            if (duplicates.Contains(destination))
            {
                state = ComplexMigrationMoveState.Blocked;
                detail = "Plusieurs sections revendiquent cette destination.";
            }
            else if (milestone ? !File.Exists(from) : !Directory.Exists(from))
            {
                state = ComplexMigrationMoveState.MissingSource;
                detail = "Source absente : vérifier un déplacement préalable.";
            }
            else if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                state = ComplexMigrationMoveState.AlreadyPlaced;
                detail = "Section déjà dans le dossier prévu : aucun déplacement requis.";
            }
            else
            {
                string? blocker = FindBlocker(root, from, to);
                if (blocker is not null)
                {
                    state = ComplexMigrationMoveState.Blocked;
                    detail = blocker;
                }
            }

            moves.Add(new ComplexMigrationMove(
                section.Id, section.Name, source, destination, state, detail));
        }

        List<string> missing = Layout.Where(path =>
            !Directory.Exists(Resolve(root, path))).ToList();
        warnings.Add(
            "Le déplacement des anciens Jalons.md vers Management exige une validation " +
            "explicite des liens, manifestes et onglets avant tout changement.");
        warnings.Add(
            "Aucun déplacement permis par cet aperçu ; liens Markdown, pièces jointes et " +
            "manifestes exigent une migration approuvée et récupérable.");

        return new ComplexProjectMigrationPreview(project.Id, moves, missing, warnings);
    }

    /// <summary>Maps known built-in section roles only; never guesses custom roles.</summary>
    /// <param name="section">Existing manifest section.</param>
    /// <returns>v2 destination, or null when unchanged/unknown.</returns>
    private static string? GetDestination(SectionManifest section)
    {
        return section.TemplateKey?.ToLowerInvariant() switch
        {
            "milestones" => "Management/Jalons.md",
            "functional" => "Documentation/Fonctionnel",
            "technical-complex" => "Documentation/Technique",
            "risks" => "Analyse/Risques",
            "dependencies" => "Analyse/Dépendances",
            "deployment" => "Exploitation/Déploiement",
            null when !section.IsSingleton &&
                section.Name.Equals("Réunions", StringComparison.OrdinalIgnoreCase)
                    => "Management/Réunions",
            null when !section.IsSingleton &&
                section.Name.Equals("Décisions", StringComparison.OrdinalIgnoreCase)
                    => "Management/Décisions",
            _ => null
        };
    }

    /// <summary>Converts a fixed blueprint path to a full project path.</summary>
    /// <param name="root">Project root.</param>
    /// <param name="relative">Known blueprint path.</param>
    /// <returns>Full project path.</returns>
    private static string Resolve(string root, string relative)
    {
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Finds collisions or unsafe junctions without following them.</summary>
    /// <param name="root">Project boundary.</param>
    /// <param name="from">Existing source.</param>
    /// <param name="to">Intended destination.</param>
    /// <returns>Blocking diagnostic, or null.</returns>
    private static string? FindBlocker(string root, string from, string to)
    {
        if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0)
        {
            return "Source est une jonction ou un lien symbolique.";
        }

        string? current = to;
        while (current is not null &&
            !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(current))
            {
                return "Destination ou parent occupé par un fichier.";
            }

            if (Directory.Exists(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return "La destination utilise une jonction ou un lien symbolique.";
                }

                if (string.Equals(current, to, StringComparison.OrdinalIgnoreCase))
                {
                    return "Destination occupée : fusion de dossiers interdite.";
                }
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
