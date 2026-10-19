using Nodalis.Core.Domain;
using Nodalis.Core.Importing;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Prepares a non-destructive three-way review of one explicitly selected
/// import history against the newly staged DOCX and the current workspace.
/// This operation never overwrites, deletes or creates a project document.
/// </summary>
public sealed class DocxReimportReviewService
{
    private const long MaxMarkdownBytes = 16L * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly DocxImportManifestStore _store;

    /// <summary>Initializes a local workspace review service.</summary>
    public DocxReimportReviewService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _store = new DocxImportManifestStore(_workspaceRoot);
    }

    /// <summary>
    /// Produces a full report without committing any file. The caller must
    /// choose a specific history manifest; an ambiguous filename is not a
    /// valid implicit identity. Corrupt manifests and cross-project references
    /// fail closed.
    /// </summary>
    public async Task<DocxReimportReview> BuildAsync(
        string projectDirectory,
        DocxImportManifest previous,
        DocxImportPreview latest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(latest);
        _store.Validate(previous);

        string projectRoot = Path.GetFullPath(projectDirectory);
        string projectRelative = _store.ToWorkspaceRelativePath(projectRoot);
        if (projectRelative == "." ||
            projectRelative.StartsWith("../", StringComparison.Ordinal) ||
            projectRelative == "..")
        {
            throw new InvalidDataException("Le projet sélectionné est hors du workspace.");
        }

        string projectManifestPath = Path.Combine(projectRoot,
            WorkspaceLayout.ProjectManifestFileName);
        ProjectManifest project = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            projectManifestPath, cancellationToken);
        if (project.Id != previous.ProjectId)
        {
            throw new InvalidDataException(
                "Ce manifeste DOCX n'appartient pas au projet Nodalis choisi.");
        }

        DocxReimportMatchReport matching =
            DocxReimportSectionMatcher.Match(previous, latest);
        List<DocxReimportReviewedFragment> reviewed = [];
        foreach (DocxReimportSectionAssociation association in matching.Associations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocxImportManifestFragment baseline = previous.Fragments.Single(fragment =>
                fragment.FragmentId == association.FragmentId);
            string destination = _store.ResolveWorkspaceRelativePath(
                baseline.DestinationRelativePath);
            string localRelative = Path.GetRelativePath(projectRoot, destination);
            if (Path.IsPathRooted(localRelative) ||
                localRelative == ".." ||
                localRelative.StartsWith(".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(destination), ".md",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Le manifeste pointe vers un Markdown étranger au projet.");
            }

            string? localMarkdown = null;
            if (File.Exists(destination))
            {
                FileInfo local = new FileInfo(destination);
                if (local.Length > MaxMarkdownBytes)
                {
                    throw new InvalidDataException(
                        "Fichier Markdown trop volumineux pour le comparateur de réimport.");
                }

                localMarkdown = await File.ReadAllTextAsync(destination, cancellationToken);
            }

            DocxImportSectionPreview? current = association.CurrentSectionIndex is int selected
                ? latest.Sections.Single(section => section.Index == selected)
                : null;
            string? wordMarkdown = current is null ? null :
                DocxImportSelectionRenderer.RenderAll(current).TrimEnd();

            // Ambiguous identity means we cannot assert that Word deleted the
            // section. Do not feed null into the WordDeleted classification.
            DocxReimportChange? change = association.Kind ==
                DocxReimportAssociationKind.Ambiguous
                ? null
                : DocxReimportDiffEngine.Compare(
                    baseline,
                    wordMarkdown,
                    localMarkdown,
                    current?.SourceHeading,
                    current?.HeadingBlockIndex);

            reviewed.Add(new DocxReimportReviewedFragment
            {
                Association = association,
                Change = change,
                BaseMarkdown = baseline.BaseMarkdown,
                LatestWordMarkdown = wordMarkdown,
                CurrentNodalisMarkdown = localMarkdown,
                RequiresExplicitResolution = association.RequiresConfirmation ||
                    change?.RequiresExplicitResolution == true
            });
        }

        string currentSourceHash = await DocxImportManifestStore.ComputeFileSha256Async(
            latest.StagedImport.StagedCopyPath, cancellationToken);

        List<string> warnings = [];
        string oldCopy = _store.ResolveWorkspaceRelativePath(
            previous.SourceCopyRelativePath);
        if (!File.Exists(oldCopy))
        {
            warnings.Add(
                "La copie locale du Word initial est absente ; snapshots conservés, vérification binaire impossible.");
        }
        else if (!string.Equals(
                     await DocxImportManifestStore.ComputeFileSha256Async(
                         oldCopy, cancellationToken),
                     previous.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "La copie locale du Word initial a changé : vérifier l'origine avant tout réimport.");
        }

        if (!string.Equals(previous.SourceFileName,
                Path.GetFileName(latest.StagedImport.OriginalSourcePath),
                StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Le fichier Word a changé de nom : vérifier qu'il s'agit bien du même document.");
        }

        return new DocxReimportReview
        {
            PreviousImportId = previous.ImportId,
            ProjectId = project.Id,
            Matching = matching,
            Fragments = reviewed,
            SourcePackageUnchanged = string.Equals(currentSourceHash,
                previous.SourceSha256, StringComparison.OrdinalIgnoreCase),
            Warnings = warnings
        };
    }
}

/// <summary>An inspection report with all three versions visible in memory.</summary>
public sealed record DocxReimportReview
{
    public required Guid PreviousImportId { get; init; }

    public required Guid ProjectId { get; init; }

    public required DocxReimportMatchReport Matching { get; init; }

    public List<DocxReimportReviewedFragment> Fragments { get; init; } = [];

    public bool SourcePackageUnchanged { get; init; }

    public List<string> Warnings { get; init; } = [];

    /// <summary>No document can be modified without an explicitly reviewed plan.</summary>
    public bool RequiresExplicitResolution =>
        Matching.RequiresConfirmation || Warnings.Count > 0 ||
        Fragments.Any(fragment => fragment.RequiresExplicitResolution);
}

/// <summary>One historical source fragment and its complete three-way text comparison.</summary>
public sealed record DocxReimportReviewedFragment
{
    public required DocxReimportSectionAssociation Association { get; init; }

    public DocxReimportChange? Change { get; init; }

    public required string BaseMarkdown { get; init; }

    public string? LatestWordMarkdown { get; init; }

    public string? CurrentNodalisMarkdown { get; init; }

    public bool RequiresExplicitResolution { get; init; }
}
