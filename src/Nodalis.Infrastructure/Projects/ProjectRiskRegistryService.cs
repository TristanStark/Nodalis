using System.Security.Cryptography;
using System.Text;
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
    /// <summary>Exact source revision returned on the last verified listing.</summary>
    public string SourceSha256 { get; init; } = string.Empty;

    /// <summary>Chronological, human-approved rating events from this Markdown.</summary>
    public IReadOnlyList<ProjectRiskReviewEvent> Reviews { get; init; } = [];

    /// <summary>Human business-qualification decisions, distinct from scoring.</summary>
    public IReadOnlyList<ProjectRiskQualificationEvent> Qualifications
    { get; init; } = [];

    /// <summary>Explicit treatment history, independent from human scores.</summary>
    public IReadOnlyList<ProjectRiskTreatmentEvent> Treatments
    { get; init; } = [];

    /// <summary>The current human-approved treatment plan, or null.</summary>
    public ProjectRiskTreatmentPlan? CurrentTreatment =>
        Treatments.Count == 0 ? null : Treatments[^1].Plan;

    /// <summary>Human progress events across all recorded plan versions.</summary>
    public IReadOnlyList<ProjectRiskMeasureEvent> MeasureEvents
    { get; init; } = [];

    /// <summary>Gets explicit progress for a slot of the CURRENT plan only.</summary>
    /// <param name="kind">Named prevention, mitigation or contingency slot.</param>
    /// <returns>Human status, or Pending when no event exists for this version.</returns>
    public ProjectRiskMeasureStatus MeasureStatus(ProjectRiskMeasureKind kind)
    {
        if (Treatments.Count == 0)
        {
            return ProjectRiskMeasureStatus.Pending;
        }

        Guid planId = Treatments[^1].Id;
        return MeasureEvents.LastOrDefault(item =>
            item.TreatmentId == planId && item.Kind == kind)?.Status ??
            ProjectRiskMeasureStatus.Pending;
    }

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
        return new ProjectRiskEntry(card, relative)
        {
            SourceSha256 = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(markdown)))
        };
    }

    /// <summary>
    /// Commits one human-approved rating event to the selected Markdown risk.
    /// Requires the exact source hash previously displayed, project-local
    /// unique identity, active project, and atomic source revision.
    /// A changed document is rejected, not reinterpreted or overwritten.
    /// </summary>
    /// <param name="projectDirectory">Current project location.</param>
    /// <param name="snapshot">Previously read and displayed risk.</param>
    /// <param name="kind">Initial or residual human review.</param>
    /// <param name="rating">Explicit probability and impact, both 1..3.</param>
    /// <param name="actor">Named human reviewer.</param>
    /// <param name="rationale">Human review justification.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified saved revision and complete review history.</returns>
    public async Task<ProjectRiskEntry> ReviewAsync(
        string projectDirectory, ProjectRiskEntry snapshot,
        ProjectRiskReviewKind kind, ProjectRiskRating rating,
        string actor, string rationale,
        CancellationToken cancellationToken = default)
    {
        (ProjectRiskEntry current, TextDocumentSession session) =
            await OpenVerifiedRiskAsync(
                projectDirectory, snapshot, cancellationToken);
        ProjectRiskReviewHistory existing =
            ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        ProjectRiskTreatmentMarkdownCodec.Inspect(session.Content);
        ProjectRiskMeasureMarkdownCodec.Inspect(session.Content);
        if (existing.Card.Id != snapshot.Card.Id ||
            existing.Card.ProjectId != snapshot.Card.ProjectId)
        {
            throw new InvalidDataException(
                "Identité de risque modifiée depuis la sélection.");
        }

        string updated = ProjectRiskReviewMarkdownCodec.Append(
            session.Content, kind, rating, actor, rationale);
        if (Encoding.UTF8.GetByteCount(updated) > MaxRiskBytes)
        {
            throw new InvalidDataException(
                "Cette qualification dépasse la taille maximale de la fiche.");
        }

        await session.SaveAsync(updated, cancellationToken);
        ProjectRiskReviewHistory saved =
            ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        return current with
        {
            Card = saved.Card,
            Reviews = saved.Events,
            SourceSha256 = session.Revision.Sha256
        };
    }

    /// <summary>
    /// Commits one human business-state change without touching any risk
    /// rating, lifecycle, incident, task, or gate. All past decisions remain
    /// in the source and a stale source is never silently overwritten.
    /// </summary>
    /// <param name="projectDirectory">Canonical project folder.</param>
    /// <param name="snapshot">Previously displayed stable risk and SHA.</param>
    /// <param name="qualification">Human-selected ToReview/Validated/Rejected.</param>
    /// <param name="actor">Human decision maker.</param>
    /// <param name="rationale">Explicit justification of the decision.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Saved source projection and the full qualification history.</returns>
    public async Task<ProjectRiskEntry> QualifyAsync(
        string projectDirectory, ProjectRiskEntry snapshot,
        ProjectRiskQualification qualification, string actor,
        string rationale, CancellationToken cancellationToken = default)
    {
        (ProjectRiskEntry current, TextDocumentSession session) =
            await OpenVerifiedRiskAsync(
                projectDirectory, snapshot, cancellationToken);
        ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        ProjectRiskTreatmentMarkdownCodec.Inspect(session.Content);
        ProjectRiskMeasureMarkdownCodec.Inspect(session.Content);
        ProjectRiskQualificationHistory old =
            ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        if (old.Card.Id != snapshot.Card.Id ||
            old.Card.ProjectId != snapshot.Card.ProjectId)
        {
            throw new InvalidDataException(
                "Identité du risque modifiée avant décision métier.");
        }

        string updated = ProjectRiskQualificationMarkdownCodec.Append(
            session.Content, qualification, actor, rationale);
        if (Encoding.UTF8.GetByteCount(updated) > MaxRiskBytes)
        {
            throw new InvalidDataException(
                "Le journal de qualification dépasse la taille de la fiche.");
        }

        await session.SaveAsync(updated, cancellationToken);
        ProjectRiskQualificationHistory saved =
            ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        return current with
        {
            Card = saved.Card,
            Qualifications = saved.Events,
            SourceSha256 = session.Revision.Sha256
        };
    }

    /// <summary>
    /// Commits one explicitly confirmed risk-treatment plan or revision, with
    /// source SHA and project isolation identical to scoring/qualification.
    /// Does not change the score, business decision or risk lifecycle.
    /// </summary>
    /// <param name="projectDirectory">Project holding this risk.</param>
    /// <param name="snapshot">Previously displayed risk and exact source SHA.</param>
    /// <param name="plan">New human-owned treatment strategy and measures.</param>
    /// <param name="actor">Person reviewing the plan.</param>
    /// <param name="rationale">Reason for the treatment choice or revision.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Saved read-only risk projection, including full treatment history.</returns>
    public async Task<ProjectRiskEntry> PlanTreatmentAsync(
        string projectDirectory, ProjectRiskEntry snapshot,
        ProjectRiskTreatmentPlan plan, string actor,
        string rationale, CancellationToken cancellationToken = default)
    {
        (ProjectRiskEntry current, TextDocumentSession session) =
            await OpenVerifiedRiskAsync(
                projectDirectory, snapshot, cancellationToken);
        ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        ProjectRiskMeasureMarkdownCodec.Inspect(session.Content);
        ProjectRiskTreatmentHistory previous =
            ProjectRiskTreatmentMarkdownCodec.Inspect(session.Content);
        if (previous.Card.Id != snapshot.Card.Id ||
            previous.Card.ProjectId != snapshot.Card.ProjectId)
        {
            throw new InvalidDataException(
                "Identité du risque modifiée avant validation du plan.");
        }

        string updated = ProjectRiskTreatmentMarkdownCodec.Append(
            session.Content, plan, actor, rationale);
        if (Encoding.UTF8.GetByteCount(updated) > MaxRiskBytes)
        {
            throw new InvalidDataException(
                "Le nouveau plan dépasse la taille maximale de la fiche.");
        }

        await session.SaveAsync(updated, cancellationToken);
        ProjectRiskTreatmentHistory saved =
            ProjectRiskTreatmentMarkdownCodec.Inspect(session.Content);
        ProjectRiskMeasureMarkdownCodec.Inspect(session.Content);
        ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        return current with
        {
            Card = saved.Card,
            Treatments = saved.Events,
            SourceSha256 = session.Revision.Sha256
        };
    }

    /// <summary>
    /// Records exactly one human progress statement on a named treatment
    /// measure, with project/SHA confinement and atomic source revisions.
    /// No Markdown task is inferred or marked completed.
    /// </summary>
    /// <param name="projectDirectory">Owning project directory.</param>
    /// <param name="snapshot">Previously displayed risk and source revision.</param>
    /// <param name="kind">Named slot in the latest treatment plan.</param>
    /// <param name="status">Explicit human-chosen progress status.</param>
    /// <param name="actor">Person declaring the status.</param>
    /// <param name="rationale">Required reason for the transition.</param>
    /// <param name="evidence">Description of proof when marking completed.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Revalidated record and complete measure history.</returns>
    public async Task<ProjectRiskEntry> UpdateMeasureAsync(
        string projectDirectory, ProjectRiskEntry snapshot,
        ProjectRiskMeasureKind kind, ProjectRiskMeasureStatus status,
        string actor, string rationale, string? evidence,
        CancellationToken cancellationToken = default)
    {
        (ProjectRiskEntry current, TextDocumentSession session) =
            await OpenVerifiedRiskAsync(
                projectDirectory, snapshot, cancellationToken);
        ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        ProjectRiskTreatmentMarkdownCodec.Inspect(session.Content);
        ProjectRiskMeasureHistory old =
            ProjectRiskMeasureMarkdownCodec.Inspect(session.Content);
        if (old.Card.Id != snapshot.Card.Id ||
            old.Card.ProjectId != snapshot.Card.ProjectId)
        {
            throw new InvalidDataException(
                "Identité de risque modifiée avant le suivi d'exécution.");
        }

        string updated = ProjectRiskMeasureMarkdownCodec.Append(
            session.Content, kind, status, actor, rationale, evidence);
        if (Encoding.UTF8.GetByteCount(updated) > MaxRiskBytes)
        {
            throw new InvalidDataException(
                "Le journal des mesures dépasse la taille maximale de la fiche.");
        }

        await session.SaveAsync(updated, cancellationToken);
        ProjectRiskMeasureHistory saved =
            ProjectRiskMeasureMarkdownCodec.Inspect(session.Content);
        ProjectRiskQualificationMarkdownCodec.Inspect(session.Content);
        ProjectRiskReviewMarkdownCodec.Inspect(session.Content);
        return current with
        {
            Card = saved.Card,
            MeasureEvents = saved.Events,
            SourceSha256 = session.Revision.Sha256
        };
    }

    /// <summary>
    /// Revalidates the previously displayed GUID, project ownership, canonical
    /// local source path, unique identity, SHA and source bytes before editing.
    /// </summary>
    /// <param name="projectDirectory">Existing active project.</param>
    /// <param name="snapshot">Risk selection and exact verified SHA.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Latest equivalent catalog entry and guarded document session.</returns>
    private async Task<(ProjectRiskEntry Current, TextDocumentSession Session)>
        OpenVerifiedRiskAsync(
            string projectDirectory, ProjectRiskEntry snapshot,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (string directory, ProjectManifest manifest) = await ResolveProjectAsync(
            projectDirectory, cancellationToken);
        if (manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException(
                "Un risque archivé est consultable mais aucune décision n'y est modifiable.");
        }

        if (snapshot.Card.ProjectId != manifest.Id ||
            snapshot.Card.Id == Guid.Empty ||
            snapshot.SourceSha256.Length != 64 ||
            !snapshot.SourceSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Sélection de risque non vérifiée pour ce projet.");
        }

        ProjectRiskCatalog latest = await ListInProjectAsync(
            directory, manifest.Id, cancellationToken);
        ProjectRiskEntry? current = latest.Risks.SingleOrDefault(item =>
            item.Card.Id == snapshot.Card.Id &&
            string.Equals(item.RelativePath, snapshot.RelativePath,
                StringComparison.OrdinalIgnoreCase));
        if (current is null ||
            !string.Equals(current.SourceSha256, snapshot.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Fiche de risque modifiée, déplacée ou devenue ambiguë : " +
                "rechargez le registre avant de qualifier.");
        }

        string full = _sources.ResolveExistingFile(current.RelativePath);
        string knownFolder = Path.GetFullPath(Path.Combine(
            directory, AnalysisFolder, RisksFolder));
        if (!string.Equals(
            Path.TrimEndingDirectorySeparator(
                Path.GetDirectoryName(full) ?? string.Empty),
            Path.TrimEndingDirectorySeparator(knownFolder),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Seules les fiches du dossier canonique sont qualifiables.");
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            full, cancellationToken);
        if (!string.Equals(session.Revision.Sha256, snapshot.SourceSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le document a changé pendant la préparation de l'avis.");
        }

        return (current, session);
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
                byte[] bytes = await File.ReadAllBytesAsync(
                    actualPath, cancellationToken);
                string markdown = new UTF8Encoding(false, true).GetString(bytes);
                ProjectRiskReviewHistory history =
                    ProjectRiskReviewMarkdownCodec.Inspect(markdown);
                ProjectRiskQualificationHistory qualifications =
                    ProjectRiskQualificationMarkdownCodec.Inspect(markdown);
                ProjectRiskTreatmentHistory treatment =
                    ProjectRiskTreatmentMarkdownCodec.Inspect(markdown);
                ProjectRiskMeasureHistory measures =
                    ProjectRiskMeasureMarkdownCodec.Inspect(markdown);
                ProjectRiskCard card = history.Card;
                if (card.ProjectId != projectId)
                {
                    diagnostics.Add(relative + " : risque d'un autre projet.");
                    continue;
                }

                found.Add(new ProjectRiskEntry(card, relative)
                {
                    Reviews = history.Events,
                    Qualifications = qualifications.Events,
                    Treatments = treatment.Events,
                    MeasureEvents = measures.Events,
                    SourceSha256 = Convert.ToHexString(SHA256.HashData(bytes))
                });
                highest = Math.Max(highest,
                    int.Parse(card.Reference[4..],
                        System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception exception) when (exception is
                InvalidDataException or DecoderFallbackException)
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
