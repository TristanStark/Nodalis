using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Sfd;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One verified historical ACT directory eligible for explicit flattening.</summary>
/// <param name="Id">Stable ACT identity retained by the conversion.</param>
/// <param name="SourceFile">Existing ACT presentation file.</param>
/// <param name="SourceDirectory">Existing ACT directory.</param>
/// <param name="TargetFile">New flat Markdown location under ACTIVITES.</param>
/// <param name="OldReference">Current reference in Markdown metadata.</param>
/// <param name="NewReference">Compact reference that will replace the old code.</param>
/// <param name="Sha256">Exact source revision read during preview.</param>
public sealed record SfdFlatActivityCandidate(
    Guid Id, string SourceFile, string SourceDirectory, string TargetFile,
    string OldReference, string NewReference, string Sha256);

/// <summary>Read-only plan and any nonconvertible legacy contents.</summary>
/// <param name="Candidates">Leaf ACT directories that can safely be converted.</param>
/// <param name="Warnings">Other documents preserved without any modification.</param>
/// <param name="ArchiveDuplicateOverview">True only for the untouched redundant template.</param>
public sealed record SfdFlatActivityPreview(
    IReadOnlyList<SfdFlatActivityCandidate> Candidates,
    IReadOnlyList<string> Warnings, bool ArchiveDuplicateOverview);

/// <summary>Result of an explicitly approved non-destructive legacy conversion.</summary>
/// <param name="Converted">Number of ACT directories archived and replaced by files.</param>
/// <param name="OverviewArchived">Whether the exact unused presentation template was archived.</param>
public sealed record SfdFlatActivityResult(int Converted, bool OverviewArchived);

/// <summary>
/// Opt-in migration for old leaf ACT folders. Original directories are moved into
/// a hidden project-local backup; documents with children or unknown files are
/// never moved. The v2 project structure and GUID-based relations are untouched.
/// </summary>
public sealed class SfdFlatActivityMigrationService
{
    public const string BackupDirectoryName = ".nodalis-sfd-activity-legacy";
    private const string RedundantOverview =
        "# Présentation du projet\n\n" +
        "_Décrire ici le contexte, la cible, le périmètre et les décisions validées._\n";

    private readonly SfdDocumentRegistryService _documents;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Limits migration to the current verified workspace.</summary>
    /// <param name="workspaceRoot">Workspace root.</param>
    public SfdFlatActivityMigrationService(string workspaceRoot)
    {
        _documents = new SfdDocumentRegistryService(workspaceRoot);
        _sources = new ProjectJourneySourceService(workspaceRoot);
    }

    /// <summary>Plans eligible leaf conversions without touching source files.</summary>
    /// <param name="projectDirectory">SFD project root.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Conversion candidates, blockers and duplicate-overview detection.</returns>
    public async Task<SfdFlatActivityPreview> PreviewAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        SfdDocumentCatalog catalog = await RequireActiveCatalogAsync(
            projectDirectory, cancellationToken);
        string root = Path.GetFullPath(projectDirectory);
        string backupRoot = Path.Combine(root, BackupDirectoryName);
        List<SfdFlatActivityCandidate> candidates = [];
        List<string> warnings = [];

        foreach (SfdDocumentEntry entry in catalog.Entries.Where(item =>
            item.Card.Kind == SfdDocumentKind.Activity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.GetFileName(entry.AbsolutePath).Equals(
                    "presentation activité.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string folder = Path.GetDirectoryName(entry.AbsolutePath)!;
            string activitiesFolder = Path.GetDirectoryName(folder)!;
            if (!Path.GetFileName(activitiesFolder).Equals("ACTIVITES",
                    StringComparison.OrdinalIgnoreCase) ||
                Directory.EnumerateFileSystemEntries(folder)
                    .Any(path => !string.Equals(path, entry.AbsolutePath,
                        StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add(entry.Card.Reference +
                    " : dossier avec fichiers/enfants supplémentaires ou emplacement atypique ; conservé.");
                continue;
            }

            string newReference = SfdDocumentMarkdownCodec.ActivityReference(
                FindParent(catalog, entry.Card.ParentId).Card.Reference,
                SfdDocumentMarkdownCodec.ActivitySequence(entry.Card.Reference));
            string target = Path.Combine(activitiesFolder, newReference + ".md");
            string backup = Path.Combine(backupRoot, entry.Card.Id.ToString("N"));
            if (File.Exists(target) || Directory.Exists(target) ||
                Directory.Exists(backup) || File.Exists(backup))
            {
                warnings.Add(entry.Card.Reference +
                    " : destination ou sauvegarde déjà présente ; conversion suspendue.");
                continue;
            }

            _sources.GetReferenceForExistingFile(entry.AbsolutePath);
            SfdTextRevision revision = await SfdMarkdownRevisionWriter.ReadAsync(
                entry.AbsolutePath, cancellationToken);
            candidates.Add(new SfdFlatActivityCandidate(entry.Card.Id,
                entry.AbsolutePath, folder, target, entry.Card.Reference,
                newReference, revision.Sha256));
        }

        HashSet<string> collisions = candidates
            .GroupBy(candidate => candidate.TargetFile, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (collisions.Count > 0)
        {
            warnings.Add("Plusieurs ACT historiques visent le même fichier : " +
                "vérifiez leurs références avant migration.");
            candidates.RemoveAll(candidate => collisions.Contains(candidate.TargetFile));
        }

        string redundantPath = Path.Combine(root, "SPECS FONCTIONNELLES",
            "presentation projet.md");
        bool redundantIsUnchanged = false;
        if (File.Exists(redundantPath))
        {
            _sources.GetReferenceForExistingFile(redundantPath);
            string content = await File.ReadAllTextAsync(
                redundantPath, cancellationToken);
            redundantIsUnchanged = content.Replace("\r\n", "\n",
                StringComparison.Ordinal) == RedundantOverview;
            if (!redundantIsUnchanged)
            {
                warnings.Add("La présentation projet secondaire contient des modifications : " +
                    "elle est conservée ; reportez son contenu utile dans Présentation.md.");
            }
            else if (File.Exists(Path.Combine(backupRoot, "presentation-projet.md")))
            {
                redundantIsUnchanged = false;
                warnings.Add("Sauvegarde de présentation projet déjà présente : " +
                    "l'ancien fichier est conservé.");
            }
        }

        return new SfdFlatActivityPreview(candidates, warnings,
            redundantIsUnchanged);
    }

    /// <summary>
    /// Applies only the previously reviewed candidate set. Every original ACT
    /// directory remains available in a hidden backup after the conversion.
    /// </summary>
    /// <param name="projectDirectory">Project root.</param>
    /// <param name="expected">Exact user-reviewed preview.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Count of completed, recoverable conversions.</returns>
    public async Task<SfdFlatActivityResult> ConvertAsync(
        string projectDirectory, SfdFlatActivityPreview expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        SfdFlatActivityPreview current = await PreviewAsync(
            projectDirectory, cancellationToken);
        if (current.ArchiveDuplicateOverview != expected.ArchiveDuplicateOverview ||
            !current.Candidates.SequenceEqual(expected.Candidates))
        {
            throw new InvalidDataException(
                "Les activités ou documents ont changé depuis la prévisualisation.");
        }

        string root = Path.GetFullPath(projectDirectory);
        string backupRoot = Path.Combine(root, BackupDirectoryName);
        Directory.CreateDirectory(backupRoot);
        if ((File.GetAttributes(backupRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Dossier de sauvegarde non sûr.");
        }

        int converted = 0;
        foreach (SfdFlatActivityCandidate candidate in current.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _sources.GetReferenceForExistingFile(candidate.SourceFile);
            SfdTextRevision source = await SfdMarkdownRevisionWriter.ReadAsync(
                candidate.SourceFile, cancellationToken);
            if (source.Sha256 != candidate.Sha256 ||
                Directory.EnumerateFileSystemEntries(candidate.SourceDirectory).Count() != 1)
            {
                throw new InvalidDataException(
                    "Activité modifiée pendant la conversion : " +
                    candidate.OldReference);
            }

            string revised = RewriteActivitySource(source.Content,
                candidate.OldReference, candidate.NewReference);
            SfdDocumentCard convertedCard = SfdDocumentMarkdownCodec.Parse(revised);
            if (convertedCard.Id != candidate.Id)
            {
                throw new InvalidDataException("Identité ACT modifiée : conversion refusée.");
            }

            string archived = Path.Combine(
                backupRoot, candidate.Id.ToString("N"));
            if (File.Exists(candidate.TargetFile) ||
                Directory.Exists(archived) || File.Exists(archived))
            {
                throw new IOException("Collision pendant la conversion ACT.");
            }

            Directory.Move(candidate.SourceDirectory, archived);
            try
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    candidate.TargetFile, revised, cancellationToken);
                converted++;
            }
            catch
            {
                // The original source still exists in the hidden backup.
                // Restore its directory only if the new file was not published.
                if (!File.Exists(candidate.TargetFile) &&
                    !Directory.Exists(candidate.SourceDirectory))
                {
                    Directory.Move(archived, candidate.SourceDirectory);
                }

                throw;
            }
        }

        bool overviewArchived = false;
        if (current.ArchiveDuplicateOverview)
        {
            string oldOverview = Path.Combine(root, "SPECS FONCTIONNELLES",
                "presentation projet.md");
            string backupOverview = Path.Combine(backupRoot, "presentation-projet.md");
            if (File.Exists(oldOverview))
            {
                _sources.GetReferenceForExistingFile(oldOverview);
                string content = await File.ReadAllTextAsync(oldOverview,
                    cancellationToken);
                if (content.Replace("\r\n", "\n", StringComparison.Ordinal) ==
                        RedundantOverview && !File.Exists(backupOverview))
                {
                    File.Move(oldOverview, backupOverview);
                    overviewArchived = true;
                }
            }
        }

        return new SfdFlatActivityResult(converted, overviewArchived);
    }

    /// <summary>Returns the project-owned PM metadata for one historical ACT.</summary>
    /// <param name="catalog">Verified catalog.</param>
    /// <param name="parentId">Parent business process ID.</param>
    /// <returns>Exact process entry.</returns>
    private static SfdDocumentEntry FindParent(
        SfdDocumentCatalog catalog, Guid? parentId) =>
        catalog.Entries.SingleOrDefault(item => item.Card.Id == parentId &&
            item.Card.Kind == SfdDocumentKind.Process) ??
            throw new InvalidDataException("PM parent absent pour l'ACT.");

    /// <summary>Changes only the canonical reference/title/heading fields.</summary>
    /// <param name="source">Complete historical Markdown.</param>
    /// <param name="oldReference">Existing reference from the parsed card.</param>
    /// <param name="newReference">Target compact code.</param>
    /// <returns>Markdown with every user-authored section preserved.</returns>
    private static string RewriteActivitySource(
        string source, string oldReference, string newReference)
    {
        SfdDocumentCard previous = SfdDocumentMarkdownCodec.Parse(source);
        if (previous.Reference != oldReference ||
            previous.Kind != SfdDocumentKind.Activity)
        {
            throw new InvalidDataException("Identité de la source ACT inattendue.");
        }

        string from = "reference: " + oldReference;
        string to = "reference: " + newReference;
        int metadataStart = source.IndexOf(from, StringComparison.Ordinal);
        if (metadataStart < 0 || metadataStart > 2048)
        {
            throw new InvalidDataException("En-tête ACT non canonique.");
        }

        string revised = source[..metadataStart] + to +
            source[(metadataStart + from.Length)..];
        if (previous.Title == oldReference)
        {
            string titleOld = "title: " + JsonSerializer.Serialize(oldReference);
            string titleNew = "title: " + JsonSerializer.Serialize(newReference);
            int titlePosition = revised.IndexOf(titleOld, StringComparison.Ordinal);
            if (titlePosition < 0 || titlePosition > 2048)
            {
                throw new InvalidDataException("Titre ACT non canonique.");
            }

            revised = revised[..titlePosition] + titleNew +
                revised[(titlePosition + titleOld.Length)..];
        }

        string oldHeading = "# " + oldReference + " — " + previous.Title;
        string updatedTitle = previous.Title == oldReference
            ? newReference : previous.Title;
        string newHeading = "# " + newReference + " — " + updatedTitle;
        int headingPosition = revised.IndexOf(oldHeading, StringComparison.Ordinal);
        if (headingPosition >= 0 && headingPosition < 4096)
        {
            revised = revised[..headingPosition] + newHeading +
                revised[(headingPosition + oldHeading.Length)..];
        }

        SfdDocumentCard card = SfdDocumentMarkdownCodec.Parse(revised);
        if (card.Id != previous.Id || card.ProjectId != previous.ProjectId ||
            card.ParentId != previous.ParentId || card.Reference != newReference ||
            card.Title != updatedTitle)
        {
            throw new InvalidDataException("Transformation ACT non conforme.");
        }

        return revised;
    }

    /// <summary>Checks family, active status and catalog integrity before mutation.</summary>
    /// <param name="projectDirectory">Candidate active SFD project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Validated current catalog.</returns>
    private async Task<SfdDocumentCatalog> RequireActiveCatalogAsync(
        string projectDirectory, CancellationToken cancellationToken)
    {
        SfdDocumentCatalog catalog = await _documents.ListAsync(
            projectDirectory, cancellationToken);
        if (catalog.Diagnostics.Count > 0)
        {
            throw new InvalidDataException(
                "Le projet contient des identités SFD ambiguës.");
        }

        string manifestPath = Path.Combine(
            Path.GetFullPath(projectDirectory), ".project.json");
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id != catalog.ProjectId ||
            manifest.Kind != ProjectKind.WithSpecifications ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("Projet SFD archivé ou identité modifiée.");
        }

        return catalog;
    }
}
