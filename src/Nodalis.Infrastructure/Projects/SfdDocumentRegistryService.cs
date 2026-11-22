using System.Globalization;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One canonical SFD card and its current physical Markdown location.</summary>
/// <param name="Card">Persisted stable business identity.</param>
/// <param name="AbsolutePath">Verified source file path, resolved without naming assumptions.</param>
public sealed record SfdDocumentEntry(SfdDocumentCard Card, string AbsolutePath);

/// <summary>Read-only SFD projection; diagnostics never cause implicit fixes.</summary>
/// <param name="ProjectId">Source project's stable identity.</param>
/// <param name="Entries">Recognized unambiguous documents.</param>
/// <param name="Diagnostics">Unsafe, duplicated or inconsistent sources.</param>
public sealed record SfdDocumentCatalog(
    Guid ProjectId,
    IReadOnlyList<SfdDocumentEntry> Entries,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Filesystem-backed SFD authoring, independent of the legacy 2.0 EXI registry.
/// Lists never write; authoring creates exactly one user-approved draft.
/// </summary>
public sealed class SfdDocumentRegistryService
{
    private const int MaxDocuments = 5000;
    private const int MaxFolders = 10000;
    private const int MaxDocumentBytes = 1024 * 1024;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Initializes the registry with an explicit Nodalis workspace boundary.</summary>
    /// <param name="workspaceRoot">Owner workspace.</param>
    public SfdDocumentRegistryService(string workspaceRoot)
    {
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>Lists only versioned SFD documents, without creating or migrating files.</summary>
    /// <param name="projectDirectory">Project SFD to inspect.</param>
    /// <param name="cancellationToken">Cancellation for filesystem scanning.</param>
    /// <returns>Valid entries and unresolved identity diagnostics.</returns>
    public async Task<SfdDocumentCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string root, ProjectManifest project) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        return await ListInProjectAsync(root, project.Id, cancellationToken);
    }

    /// <summary>
    /// Creates an explicit SFD draft. Parent identities are resolved from
    /// versioned sources, never inferred from a directory label.
    /// </summary>
    /// <param name="projectDirectory">Active SFD project root.</param>
    /// <param name="kind">Role to create; EMO remains deferred pending placement arbitration.</param>
    /// <param name="title">User-confirmed Markdown title.</param>
    /// <param name="parentId">Required process GUID for ACT, activity GUID for EXU/EXS/EXR.</param>
    /// <param name="functionalityMode">Mandatory with/sans IHM choice for FCT only.</param>
    /// <param name="cancellationToken">Creation cancellation.</param>
    /// <returns>Committed draft document.</returns>
    public async Task<SfdDocumentEntry> CreateAsync(
        string projectDirectory, SfdDocumentKind kind, string title,
        Guid? parentId = null, SfdFunctionalityMode? functionalityMode = null,
        CancellationToken cancellationToken = default)
    {
        if ((kind != SfdDocumentKind.Activity && string.IsNullOrWhiteSpace(title)) ||
            title.Trim().Length > 240 || title.Any(char.IsControl))
        {
            throw new ArgumentException("Un titre SFD sur une seule ligne est requis (240 caractères maximum).",
                nameof(title));
        }

        (string root, ProjectManifest project) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (project.ArchivedUtc.HasValue || project.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("Le projet archivé est en lecture seule.");
        }

        SfdDocumentCatalog catalog = await ListInProjectAsync(root, project.Id,
            cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Le catalogue SFD présente des identités ambiguës : " +
                string.Join("; ", catalog.Diagnostics.Take(3)));
        }

        SfdDocumentEntry? parent = parentId.HasValue
            ? catalog.Entries.SingleOrDefault(item => item.Card.Id == parentId)
            : null;
        SfdDocumentKind? expectedParent = kind switch
        {
            SfdDocumentKind.Activity => SfdDocumentKind.Process,
            SfdDocumentKind.UserRequirement or SfdDocumentKind.SystemRequirement or
                SfdDocumentKind.RegulatoryRequirement => SfdDocumentKind.Activity,
            _ => null
        };
        if (expectedParent.HasValue != parentId.HasValue ||
            (expectedParent.HasValue && parent?.Card.Kind != expectedParent))
        {
            throw new InvalidDataException(
                "Choisissez un parent SFD canonique du type requis.");
        }

        string prefix = SfdDocumentMarkdownCodec.Prefix(kind);
        string[] references = catalog.Entries
            .Where(item => item.Card.Kind == kind &&
                (kind != SfdDocumentKind.Activity || item.Card.ParentId == parentId))
            .Select(item => item.Card.Reference).ToArray();
        int highest = references.Select(reference =>
            int.Parse(reference[(reference.LastIndexOf('-') + 1)..],
                NumberStyles.None, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0).Max();
        if (highest == 9999)
        {
            throw new InvalidOperationException("Numérotation SFD épuisée pour " + prefix);
        }

        string reference = kind == SfdDocumentKind.Activity
            ? parent!.Card.Reference + "-ACT-" +
                (highest + 1).ToString("D3", CultureInfo.InvariantCulture)
            : prefix + "-" + (highest + 1).ToString("D4",
                CultureInfo.InvariantCulture);
        SfdDocumentCard card = new()
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Kind = kind,
            Title = string.IsNullOrWhiteSpace(title) ? reference : title.Trim(),
            Reference = reference,
            ParentId = parentId,
            FunctionalityMode = functionalityMode,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        string markdown = SfdDocumentMarkdownCodec.Format(card);
        string specs = Path.Combine(root, "SPECS FONCTIONNELLES");
        string parentDirectory = kind switch
        {
            SfdDocumentKind.Process => Path.Combine(specs, "PROCESSUS METIERS"),
            SfdDocumentKind.Functionality => Path.Combine(specs, "FONCTIONNALITES"),
            SfdDocumentKind.Activity => Path.Combine(
                Path.GetDirectoryName(parent!.AbsolutePath)!, "ACTIVITES"),
            SfdDocumentKind.UserRequirement => Path.Combine(
                Path.GetDirectoryName(parent!.AbsolutePath)!, "Exigences utilisateurs"),
            SfdDocumentKind.SystemRequirement => Path.Combine(
                Path.GetDirectoryName(parent!.AbsolutePath)!, "Exigences vis à vis du système"),
            SfdDocumentKind.RegulatoryRequirement => Path.Combine(
                Path.GetDirectoryName(parent!.AbsolutePath)!, "Exigences règlementaires"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        EnsureProjectFolder(root, parentDirectory);
        if (!Directory.Exists(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        EnsureProjectFolder(root, parentDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        string safeTitle = SafeTitleSegment(card.Title);
        bool isFolder = kind is SfdDocumentKind.Process or
            SfdDocumentKind.Activity or SfdDocumentKind.Functionality;
        string documentPath;
        if (isFolder)
        {
            string targetDirectory = Path.Combine(parentDirectory,
                card.Reference +
                (card.Title == card.Reference ? "" : " - " + safeTitle));
            string staging = Path.Combine(parentDirectory,
                ".nodalis-sfd-" + Guid.NewGuid().ToString("N") + ".tmp");
            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
            {
                throw new IOException("L'objet SFD existe déjà : " + card.Reference);
            }

            try
            {
                Directory.CreateDirectory(staging);
                string documentName = kind switch
                {
                    SfdDocumentKind.Process => "presentation processus.md",
                    SfdDocumentKind.Activity => "presentation activité.md",
                    _ => "presentation fonctionnalité.md"
                };
                await AtomicFileWriter.WriteNewAllTextAsync(
                    Path.Combine(staging, documentName), markdown, cancellationToken);
                if (kind == SfdDocumentKind.Process)
                {
                    Directory.CreateDirectory(Path.Combine(staging, "ACTIVITES"));
                }

                cancellationToken.ThrowIfCancellationRequested();
                EnsureProjectFolder(root, parentDirectory);
                Directory.Move(staging, targetDirectory);
                documentPath = Path.Combine(targetDirectory, documentName);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
        }
        else
        {
            documentPath = Path.Combine(parentDirectory,
                card.Reference + " - " + safeTitle + ".md");
            await AtomicFileWriter.WriteNewAllTextAsync(
                documentPath, markdown, cancellationToken);
        }

        _sources.GetReferenceForExistingFile(documentPath);
        return new SfdDocumentEntry(card, documentPath);
    }

    /// <summary>Validates a project SFD manifest and its workspace containment.</summary>
    /// <param name="projectDirectory">Candidate project root.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Canonical project root and manifest.</returns>
    private async Task<(string Directory, ProjectManifest Project)> ResolveProjectAsync(
        string projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string manifestPath = Path.Combine(root,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);

        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (project.Id == Guid.Empty || project.Kind != ProjectKind.WithSpecifications ||
            project.KindSchemaVersion != 1)
        {
            throw new InvalidDataException("Le projet sélectionné n'est pas un Projet avec spec compatible.");
        }

        return (root, project);
    }

    /// <summary>Reads the two SFD specification roots with bounded traversal.</summary>
    /// <param name="root">Canonical project root.</param>
    /// <param name="projectId">Persisted project identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Immutable projection and diagnostics.</returns>
    private async Task<SfdDocumentCatalog> ListInProjectAsync(
        string root, Guid projectId, CancellationToken cancellationToken)
    {
        string specs = Path.Combine(root, "SPECS FONCTIONNELLES");
        string[] folders =
        [
            Path.Combine(specs, "PROCESSUS METIERS"),
            Path.Combine(specs, "FONCTIONNALITES")
        ];
        List<SfdDocumentEntry> entries = [];
        List<string> diagnostics = [];
        int folderCount = 0;
        int documentCount = 0;
        foreach (string folder in folders)
        {
            EnsureProjectFolder(root, folder);
            if (!Directory.Exists(folder))
            {
                throw new InvalidDataException("Répertoire SFD canonique introuvable.");
            }

            Stack<string> pending = new();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string current = pending.Pop();
                EnsureProjectFolder(root, current);
                if (++folderCount > MaxFolders)
                {
                    throw new InvalidDataException("Nombre excessif de dossiers SFD.");
                }

                foreach (string file in Directory.EnumerateFiles(current, "*.md",
                    SearchOption.TopDirectoryOnly))
                {
                    if (!IsSfdFileName(Path.GetFileName(file)))
                    {
                        continue;
                    }

                    if (++documentCount > MaxDocuments)
                    {
                        throw new InvalidDataException("Nombre excessif de fiches SFD.");
                    }

                    try
                    {
                        _sources.GetReferenceForExistingFile(file);
                        if (new FileInfo(file).Length > MaxDocumentBytes)
                        {
                            throw new InvalidDataException("Fiche SFD trop volumineuse.");
                        }

                        string source = await File.ReadAllTextAsync(file,
                            cancellationToken);
                        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(source);
                        if (card.ProjectId != projectId)
                        {
                            throw new InvalidDataException("Projet propriétaire différent.");
                        }

                        entries.Add(new SfdDocumentEntry(card, file));
                    }
                    catch (Exception exception) when (exception is IOException or
                        UnauthorizedAccessException or InvalidDataException)
                    {
                        diagnostics.Add(Path.GetRelativePath(root, file) + " : " +
                            exception.Message);
                    }
                }

                foreach (string child in Directory.EnumerateDirectories(current))
                {
                    EnsureProjectFolder(root, child);
                    pending.Push(child);
                }
            }
        }

        foreach (IGrouping<Guid, SfdDocumentEntry> group in entries.GroupBy(
            item => item.Card.Id).Where(group => group.Count() > 1))
        {
            diagnostics.Add("Identifiant SFD dupliqué : " + group.Key.ToString("D"));
        }

        foreach (IGrouping<string, SfdDocumentEntry> group in entries.GroupBy(
            item => item.Card.Reference, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add("Référence SFD dupliquée : " + group.Key);
        }

        Dictionary<Guid, SfdDocumentEntry> byId = entries
            .GroupBy(item => item.Card.Id)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (SfdDocumentEntry entry in entries)
        {
            SfdDocumentKind? parentKind = entry.Card.Kind switch
            {
                SfdDocumentKind.Activity => SfdDocumentKind.Process,
                SfdDocumentKind.UserRequirement or SfdDocumentKind.SystemRequirement or
                    SfdDocumentKind.RegulatoryRequirement => SfdDocumentKind.Activity,
                _ => null
            };
            if (parentKind.HasValue &&
                (!byId.TryGetValue(entry.Card.ParentId!.Value, out SfdDocumentEntry? owner) ||
                 owner.Card.Kind != parentKind))
            {
                diagnostics.Add("Parent SFD manquant ou incohérent : " +
                    entry.Card.Reference);
            }
            else if (entry.Card.Kind == SfdDocumentKind.Activity &&
                entry.Card.Reference.StartsWith("PM-", StringComparison.Ordinal) &&
                byId.TryGetValue(entry.Card.ParentId!.Value, out SfdDocumentEntry? process) &&
                !entry.Card.Reference.StartsWith(
                    process.Card.Reference + "-ACT-", StringComparison.Ordinal))
            {
                diagnostics.Add("Code ACT incompatible avec le PM parent : " +
                    entry.Card.Reference);
            }
        }

        return new SfdDocumentCatalog(projectId, entries.OrderBy(entry =>
            entry.Card.Reference, StringComparer.Ordinal).ToArray(), diagnostics);
    }

    /// <summary>Restricts indexing to the explicit SFD Markdown role names.</summary>
    /// <param name="name">Markdown file name.</param>
    /// <returns>Whether this is a potentially canonical SFD document.</returns>
    private static bool IsSfdFileName(string name) =>
        name.Equals("presentation processus.md", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("presentation activité.md", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("presentation fonctionnalité.md", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("EXU-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("EXS-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("EXR-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Produces a short, Windows-safe human filename without altering business identity.</summary>
    /// <param name="title">User-authored title.</param>
    /// <returns>Portable folder/filename suffix.</returns>
    private static string SafeTitleSegment(string title)
    {
        string slug = new string(title.Trim().Take(70).Select(character =>
            char.IsLetterOrDigit(character) || character is ' ' or '-' or '_'
                ? character : '_').ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(slug) ? "Sans titre" : slug;
    }

    /// <summary>Rejects project traversal and reparse-point ancestry before mutations.</summary>
    /// <param name="root">Verified project root.</param>
    /// <param name="candidate">Candidate descendant folder.</param>
    private static void EnsureProjectFolder(string root, string candidate)
    {
        string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full = Path.GetFullPath(candidate);
        string relative = Path.GetRelativePath(canonicalRoot, full);
        if (Path.IsPathRooted(relative) ||
            relative is ".." or "." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Dossier SFD hors du projet.");
        }

        string? current = full;
        while (current is not null &&
            (Directory.Exists(current) || File.Exists(current)))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Liens symboliques ou jonctions interdits pour les objets SFD.");
            }

            current = Path.GetDirectoryName(current);
        }
    }
}
