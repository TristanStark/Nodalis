using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Validated requirement and its canonical, workspace-relative source.</summary>
/// <param name="Card">Explicit v2 metadata, not inferred status.</param>
/// <param name="RelativePath">Current portable Markdown location.</param>
/// <param name="SourceSha256">Exact SHA-256 of the last read source.</param>
public sealed record ProjectRequirementEntry(
    ProjectRequirementCard Card, string RelativePath, string SourceSha256)
{
    /// <summary>Read-only CA headings, never an inferred test outcome.</summary>
    public IReadOnlyList<ProjectRequirementCriterion> Criteria { get; init; } = [];

    /// <summary>Missing or ambiguous local CA identity diagnostics.</summary>
    public IReadOnlyList<string> CriterionDiagnostics { get; init; } = [];
}

/// <summary>Read-only computed view; invalid files are diagnostics, never migrated.</summary>
/// <param name="Requirements">Unambiguous project-owned requirement cards.</param>
/// <param name="Diagnostics">Ignored, unsafe or ambiguous sources.</param>
/// <param name="NextReference">Proposed unreserved reference; null at capacity.</param>
public sealed record ProjectRequirementCatalog(
    IReadOnlyList<ProjectRequirementEntry> Requirements,
    IReadOnlyList<string> Diagnostics,
    string? NextReference)
{
    /// <summary>Canonical owning project, set by real filesystem reads.</summary>
    public Guid ProjectId { get; init; }
}

/// <summary>
/// Opt-in project requirement storage using canonical Markdown in
/// Documentation/Exigences. Listing never creates folders or changes files.
/// </summary>
public sealed class ProjectRequirementRegistryService
{
    private const string DocumentationFolder = "Documentation";
    private const string RequirementsFolder = "Exigences";
    private const int MaxRequirementBytes = 1024 * 1024;
    private const int MaxFiles = 5000;

    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a scoped service without scanning the workspace.</summary>
    /// <param name="workspaceRoot">Single active workspace.</param>
    public ProjectRequirementRegistryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Computes the project-local catalog from its existing files alone.
    /// No implicit import, approval or mutation is ever performed.
    /// </summary>
    /// <param name="projectDirectory">Project with a valid manifest.</param>
    /// <param name="cancellationToken">Aborts the current enumeration.</param>
    /// <returns>Unique requirements and informative diagnostics.</returns>
    public async Task<ProjectRequirementCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest project) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        ProjectRequirementCatalog catalog = await ListInProjectAsync(
            directory, project.Id, cancellationToken);
        return catalog with { ProjectId = project.Id };
    }

    /// <summary>
    /// Creates one explicitly approved, unclassified draft. The expected
    /// reference prevents writing under an unreviewed code if a second
    /// process reserved the earlier reference after the preview.
    /// </summary>
    /// <param name="projectDirectory">Verified active project.</param>
    /// <param name="title">Human-entered one-line title.</param>
    /// <param name="expectedReference">Optional reviewed EXI code.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Saved identity and relative Markdown path.</returns>
    public async Task<ProjectRequirementEntry> CreateAsync(
        string projectDirectory, string title, string? expectedReference = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 240 ||
            title.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Titre d'exigence requis, sans saut de ligne (240 caractères maximum).",
                nameof(title));
        }

        (string directory, ProjectManifest project) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (project.ArchivedUtc.HasValue ||
            project.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Les projets archivés sont consultables mais non modifiables.");
        }

        ProjectRequirementCatalog catalog = await ListInProjectAsync(
            directory, project.Id, cancellationToken);
        string code = catalog.NextReference ??
            throw new InvalidOperationException(
                "Toutes les références EXI-0001..EXI-9999 sont utilisées.");
        if (expectedReference is not null &&
            !string.Equals(code, expectedReference, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "La référence d'exigence a changé depuis l'aperçu : réessayez.");
        }

        ProjectRequirementCard card = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Reference = code,
            Title = title.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        string markdown = ProjectRequirementMarkdownCodec.Format(card);
        string documentation = Path.Combine(directory, DocumentationFolder);
        string requirements = Path.Combine(documentation, RequirementsFolder);
        // Directories are created only after explicit human action.
        Directory.CreateDirectory(documentation);
        RequireNormalDirectory(documentation);
        Directory.CreateDirectory(requirements);
        RequireNormalDirectory(requirements);
        cancellationToken.ThrowIfCancellationRequested();

        string targetPath = Path.Combine(requirements, code + ".md");
        await AtomicFileWriter.WriteNewAllTextAsync(
            targetPath, markdown, cancellationToken);
        string relative = _sources.GetReferenceForExistingFile(targetPath);
        return new ProjectRequirementEntry(card, relative,
            Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(markdown))));
    }

    /// <summary>Lists only the immediate, canonical requirement folder.</summary>
    /// <param name="projectDirectory">Validated project directory.</param>
    /// <param name="projectId">Canonical manifest GUID.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Pure projection, never a second persisted registry.</returns>
    private async Task<ProjectRequirementCatalog> ListInProjectAsync(
        string projectDirectory, Guid projectId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string documentation = Path.Combine(
            projectDirectory, DocumentationFolder);
        string requirements = Path.Combine(
            documentation, RequirementsFolder);
        if (!Directory.Exists(requirements))
        {
            if (Directory.Exists(documentation))
            {
                RequireNormalDirectory(documentation);
            }

            return new ProjectRequirementCatalog([], [], "EXI-0001");
        }

        RequireNormalDirectory(documentation);
        RequireNormalDirectory(requirements);
        string[] files = Directory.GetFiles(
            requirements, "*.md", SearchOption.TopDirectoryOnly);
        if (files.Length > MaxFiles)
        {
            throw new InvalidDataException(
                "Registre d'exigences trop volumineux (>5000 fiches).");
        }

        List<ProjectRequirementEntry> found = [];
        List<string> diagnostics = [];
        int highestReference = 0;
        foreach (string path in files.OrderBy(item => item,
            StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length == 8 &&
                name.StartsWith("EXI-", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name[4..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out int occupiedNumber) &&
                occupiedNumber is >= 1 and <= 9999)
            {
                highestReference = Math.Max(highestReference, occupiedNumber);
            }

            try
            {
                // This also checks the file itself and each existing ancestor
                // for symbolic links, junctions and workspace escapes.
                string relative = _sources.GetReferenceForExistingFile(path);
                string verified = _sources.ResolveExistingFile(relative);
                FileInfo info = new(verified);
                if (info.Length > MaxRequirementBytes)
                {
                    diagnostics.Add(relative + " : fichier trop volumineux.");
                    continue;
                }

                byte[] bytes = await File.ReadAllBytesAsync(
                    verified, cancellationToken);
                string markdown = new UTF8Encoding(false, true).GetString(bytes);
                ProjectRequirementCard card =
                    ProjectRequirementMarkdownCodec.Parse(markdown);
                if (card.ProjectId != projectId)
                {
                    diagnostics.Add(relative + " : exigence d'un autre projet.");
                    continue;
                }

                highestReference = Math.Max(highestReference,
                    int.Parse(card.Reference[4..], CultureInfo.InvariantCulture));
                ProjectRequirementCriterionScan criteria =
                    ProjectRequirementCriterionCodec.Inspect(markdown);
                found.Add(new ProjectRequirementEntry(card, relative,
                    Convert.ToHexString(SHA256.HashData(bytes)))
                {
                    Criteria = criteria.Criteria,
                    CriterionDiagnostics = criteria.Diagnostics
                });
            }
            catch (Exception exception) when (
                exception is InvalidDataException or DecoderFallbackException or
                IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(name + ".md : " + exception.Message);
            }
        }

        HashSet<Guid> duplicatedIds = found.GroupBy(item => item.Card.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        HashSet<string> duplicatedReferences = found
            .GroupBy(item => item.Card.Reference,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(
                StringComparer.OrdinalIgnoreCase);

        foreach (ProjectRequirementEntry duplicate in found.Where(item =>
            duplicatedIds.Contains(item.Card.Id) ||
            duplicatedReferences.Contains(item.Card.Reference)))
        {
            diagnostics.Add(duplicate.RelativePath +
                " : GUID ou référence EXI dupliqué(e).");
        }

        ProjectRequirementEntry[] unambiguous = found.Where(item =>
            !duplicatedIds.Contains(item.Card.Id) &&
            !duplicatedReferences.Contains(item.Card.Reference))
            .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
            .ToArray();
        string? next = highestReference == 9999
            ? null : "EXI-" + (highestReference + 1).ToString(
                "D4", CultureInfo.InvariantCulture);
        return new ProjectRequirementCatalog(unambiguous, diagnostics, next);
    }

    /// <summary>Rejects a junction or symlink in the canonical folder.</summary>
    /// <param name="directory">Existing workspace directory.</param>
    private static void RequireNormalDirectory(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier d'exigences sous lien symbolique ou jonction refusé.");
        }
    }

    /// <summary>Confirms membership and stable identity before any read/write.</summary>
    /// <param name="directory">Candidate project directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Resolved directory and manifest.</returns>
    private async Task<(string Directory, ProjectManifest Manifest)>
        ResolveProjectAsync(string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Projet hors workspace.");
        }

        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 3 ||
            !(string.Equals(parts[0], WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(parts[0], WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Dossier non reconnu comme projet.");
        }

        string path = Path.Combine(full, WorkspaceLayout.ProjectManifestFileName);
        _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(path));
        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, cancellationToken);
        if (project.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans identité stable.");
        }

        return (full, project);
    }
}
