using Nodalis.Core.Domain;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One readable risk card with its canonical Markdown source path.</summary>
/// <param name="Card">Validated canonical card, never a computed risk.</param>
/// <param name="RelativePath">Portable workspace-relative source location.</param>
public sealed record ProjectRiskEntry(
    ProjectRiskCard Card, string RelativePath)
{
    /// <summary>Compact risk label and independent evaluation state.</summary>
    public string Label => Card.Reference + " · " + Card.Title;

    /// <summary>Risk state and human-assessed scores, without automatic approvals.</summary>
    public string Summary => "Qualification : " + Card.Qualification +
        " · Cycle : " + Card.Lifecycle +
        " · Initiale : " + ProjectRiskMatrix.Evaluate(Card.Initial) +
        " · Résiduelle : " + ProjectRiskMatrix.Evaluate(Card.Residual);
}

/// <summary>Read-only catalog; untrusted or old documents are diagnostics only.</summary>
/// <param name="Risks">Complete unique v2 risk records in this project.</param>
/// <param name="Diagnostics">Unparseable, duplicated or unsafe source warnings.</param>
/// <param name="NextReference">Proposed code, never reserved by a read.</param>
public sealed record ProjectRiskCatalog(
    IReadOnlyList<ProjectRiskEntry> Risks,
    IReadOnlyList<string> Diagnostics,
    string? NextReference);

/// <summary>
/// Project-local risk service: read-only browsing and explicitly approved
/// no-overwrite creation in Analyse/Risques. Legacy risk documents stay intact.
/// </summary>
public sealed class ProjectRiskRegistryService
{
    private const string AnalysisFolder = "Analyse";
    private const string RisksFolder = "Risques";
    private const int MaxRiskBytes = 1024 * 1024;
    private const int MaxFiles = 5000;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Constructs a workspace-scoped risk service.</summary>
    /// <param name="workspaceRoot">Single existing workspace.</param>
    public ProjectRiskRegistryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Reads the known per-project risk directory without creating anything,
    /// editing legacy files or recursively traversing the workspace.
    /// </summary>
    /// <param name="projectDirectory">Existing project directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Risk cards, diagnostics and next unreserved reference.</returns>
    public async Task<ProjectRiskCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest manifest) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        return await ListInProjectAsync(directory, manifest.Id, cancellationToken);
    }

    /// <summary>
    /// Creates one approved risk document with its own persistent GUID.
    /// The folder is created only after the command is explicitly invoked.
    /// Existing files are never overwritten, including on reference races.
    /// </summary>
    /// <param name="projectDirectory">Existing, active project.</param>
    /// <param name="title">User-approved single-line title.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New risk with stable identity and actual source location.</returns>
    public async Task<ProjectRiskEntry> CreateAsync(
        string projectDirectory, string title,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 240 ||
            title.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Titre de risque requis, sans saut de ligne (240 caractères maximum).",
                nameof(title));
        }

        (string directory, ProjectManifest manifest) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Le projet archivé est consultable mais aucun risque ne peut y être créé.");
        }

        ProjectRiskCatalog catalog = await ListInProjectAsync(
            directory, manifest.Id, cancellationToken);
        string code = catalog.NextReference ??
            throw new InvalidOperationException(
                "Toutes les références RIS-0001..RIS-9999 sont utilisées.");
        ProjectRiskCard card = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = manifest.Id,
            Reference = code,
            Title = title.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        string markdown = ProjectRiskMarkdownCodec.Format(card);
        string analysis = Path.Combine(directory, AnalysisFolder);
        string riskDirectory = Path.Combine(analysis, RisksFolder);
        // This command alone may create directories, after the manifest check.
        Directory.CreateDirectory(analysis);
        RequireNormalDirectory(analysis);
        Directory.CreateDirectory(riskDirectory);
        RequireNormalDirectory(riskDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(riskDirectory, code + ".md");
        await AtomicFileWriter.WriteNewAllTextAsync(path, markdown,
            cancellationToken);
        string relative = _sources.GetReferenceForExistingFile(path);
        return new ProjectRiskEntry(card, relative);
    }

    /// <summary>Reads and validates the requested risk directory once.</summary>
    /// <param name="projectDirectory">Validated project location.</param>
    /// <param name="projectId">Persisted project identity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Canonical read-only projection and exclusion diagnostics.</returns>
    private async Task<ProjectRiskCatalog> ListInProjectAsync(
        string projectDirectory, Guid projectId,
        CancellationToken cancellationToken)
    {
        string analysis = Path.Combine(projectDirectory, AnalysisFolder);
        string riskDirectory = Path.Combine(analysis, RisksFolder);
        if (!Directory.Exists(riskDirectory))
        {
            if (Directory.Exists(analysis))
            {
                RequireNormalDirectory(analysis);
            }

            return new ProjectRiskCatalog([], [], "RIS-0001");
        }

        RequireNormalDirectory(analysis);
        RequireNormalDirectory(riskDirectory);
        string[] files = Directory.GetFiles(riskDirectory, "*.md",
            SearchOption.TopDirectoryOnly);
        if (files.Length > MaxFiles)
        {
            throw new InvalidDataException(
                "Registre trop volumineux (>5000 fiches) : inspection manuelle requise.");
        }

        List<ProjectRiskEntry> found = [];
        List<string> diagnostics = [];
        int highest = 0;
        foreach (string path in files.OrderBy(item => item,
            StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string baseName = Path.GetFileNameWithoutExtension(path);
            if (ProjectRiskMarkdownCodec.IsReference(baseName))
            {
                highest = Math.Max(highest,
                    int.Parse(baseName[4..],
                        System.Globalization.CultureInfo.InvariantCulture));
            }

            // Check the actual existing source and all ancestors. Broken or
            // linked sources are errors, never silently followed or repaired.
            string relative = _sources.GetReferenceForExistingFile(path);
            string actualPath = _sources.ResolveExistingFile(relative);
            FileInfo file = new(actualPath);
            if (file.Length > MaxRiskBytes)
            {
                diagnostics.Add(relative + " : fichier trop volumineux.");
                continue;
            }

            try
            {
                string markdown = await File.ReadAllTextAsync(
                    actualPath, cancellationToken);
                ProjectRiskCard card = ProjectRiskMarkdownCodec.Parse(markdown);
                if (card.ProjectId != projectId)
                {
                    diagnostics.Add(relative + " : risque d'un autre projet.");
                    continue;
                }

                found.Add(new ProjectRiskEntry(card, relative));
                highest = Math.Max(highest,
                    int.Parse(card.Reference[4..],
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (InvalidDataException exception)
            {
                diagnostics.Add(relative + " : " + exception.Message);
            }
        }

        HashSet<Guid> repeatedIds = found.GroupBy(item => item.Card.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet();
        HashSet<string> repeatedReferences = found
            .GroupBy(item => item.Card.Reference, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(
                StringComparer.OrdinalIgnoreCase);
        foreach (ProjectRiskEntry duplicate in found.Where(item =>
            repeatedIds.Contains(item.Card.Id) ||
            repeatedReferences.Contains(item.Card.Reference)))
        {
            diagnostics.Add(duplicate.RelativePath +
                " : GUID ou référence RIS dupliqué(e).");
        }

        ProjectRiskEntry[] valid = found.Where(item =>
            !repeatedIds.Contains(item.Card.Id) &&
            !repeatedReferences.Contains(item.Card.Reference))
            .OrderBy(item => item.Card.Reference, StringComparer.Ordinal)
            .ToArray();
        return new ProjectRiskCatalog(valid, diagnostics,
            highest >= 9999 ? null : "RIS-" + (highest + 1)
                .ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Confirms a directory is not a symlink or a junction.</summary>
    /// <param name="directory">Known in-workspace directory.</param>
    private static void RequireNormalDirectory(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier de risques sous lien symbolique ou jonction refusé.");
        }
    }

    /// <summary>Resolves a real project by its manifest and confined path.</summary>
    /// <param name="directory">Candidate project folder.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Resolved directory and canonical manifest.</returns>
    private async Task<(string Directory, ProjectManifest Manifest)> ResolveProjectAsync(
        string directory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        if (Path.IsPathRooted(relative) ||
            relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Projet hors workspace.");
        }

        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (segments.Length < 3 ||
            !(string.Equals(segments[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(segments[0],
                WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Dossier non reconnu comme projet.");
        }

        string manifestPath = Path.Combine(full,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(manifestPath));
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans identité stable.");
        }

        return (full, manifest);
    }
}
