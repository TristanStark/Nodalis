using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Canonical project-local follow-up snapshot. Null SHA means no source exists
/// yet; reading never creates the register.
/// </summary>
/// <param name="Ledger">Validated append-only reservation register.</param>
/// <param name="RelativePath">Existing source or null before first creation.</param>
/// <param name="SourceSha256">Source byte fingerprint or null if absent.</param>
public sealed record ProjectDeploymentReservationSnapshot(
    ProjectDeploymentReservationLedger Ledger, string? RelativePath,
    string? SourceSha256);

/// <summary>
/// Saves separately tracked MEP reservations even after the original MEP is
/// closed, with exact SHA-CAS, strict project ownership and explicit humans.
/// </summary>
public sealed class ProjectDeploymentReservationService
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectDeploymentService _deployments;
    private readonly ProjectGateRegistryService _gates;

    /// <summary>Creates a project-scoped, no-background-work service.</summary>
    /// <param name="workspaceRoot">Active workspace.</param>
    public ProjectDeploymentReservationService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _deployments = new ProjectDeploymentService(_workspaceRoot);
        _gates = new ProjectGateRegistryService(_workspaceRoot);
    }

    /// <summary>
    /// Reads the existing canonical register without creating directories or
    /// migrating old MEP sources. Archived projects remain readable.
    /// </summary>
    /// <param name="projectDirectory">Exact owning project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Source-verified snapshot or empty not-yet-persisted register.</returns>
    public async Task<ProjectDeploymentReservationSnapshot> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest manifest, bool archived) =
            await ResolveAsync(projectDirectory, cancellationToken);
        string path = RegisterPath(directory);
        if (!File.Exists(path))
        {
            // A dangling or broken symlink is not a valid absent file.
            if (Path.Exists(path))
            {
                throw new InvalidDataException(
                    "Chemin de suivi des réserves non sûr.");
            }

            return new ProjectDeploymentReservationSnapshot(
                new ProjectDeploymentReservationLedger
                {
                    ProjectId = manifest.Id
                }, null, null);
        }

        string relative = _sources.GetReferenceForExistingFile(path);
        string safe = _sources.ResolveExistingFile(relative);
        FileInfo info = new(safe);
        if (info.Length > MaxBytes)
        {
            throw new InvalidDataException(
                "Registre de réserves MEP trop volumineux.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(safe, cancellationToken);
        string text = new UTF8Encoding(false, true).GetString(bytes);
        ProjectDeploymentReservationLedger ledger =
            ProjectDeploymentReservationCodec.Parse(text);
        if (ledger.ProjectId != manifest.Id)
        {
            throw new InvalidDataException(
                "Registre de réserves MEP d'un autre projet.");
        }

        return new ProjectDeploymentReservationSnapshot(ledger, relative,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Adds one individually described reservation ONLY after confirming the
    /// exact source MEP revision and its real human reservations.
    /// </summary>
    /// <param name="projectDirectory">Active owning project.</param>
    /// <param name="snapshot">Already reviewed source register.</param>
    /// <param name="mep">Selected, source-verified MEP.</param>
    /// <param name="item">Explicit human-owned follow-up.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New revision without touching the MEP or maintenance records.</returns>
    public async Task<ProjectDeploymentReservationSnapshot> AddAsync(
        string projectDirectory, ProjectDeploymentReservationSnapshot snapshot,
        ProjectDeploymentEntry mep, ProjectDeploymentReservation item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(mep);
        ArgumentNullException.ThrowIfNull(item);
        (string directory, ProjectManifest manifest, bool archived) =
            await ResolveAsync(projectDirectory, cancellationToken);
        RequireActive(archived);
        if (mep.Operation.ProjectId != manifest.Id ||
            item.OperationId != mep.Operation.Id ||
            !string.Equals(item.OriginSha256, mep.SourceSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(item.Reference, NextReference(snapshot.Ledger),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Origine MEP ou référence de réserve non qualifiée.");
        }

        ProjectDeploymentCatalog meps = await _deployments.ListAsync(
            directory, cancellationToken);
        ProjectDeploymentEntry currentMep = meps.Operations.SingleOrDefault(
            entry => entry.Operation.Id == mep.Operation.Id &&
                string.Equals(entry.RelativePath, mep.RelativePath,
                    StringComparison.OrdinalIgnoreCase)) ??
            throw new InvalidDataException(
                "MEP d'origine absente ou ambiguë dans ce projet.");
        if (!string.Equals(currentMep.SourceSha256, mep.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "MEP source modifiée : actualisez avant la reprise des réserves.");
        }

        ProjectDeploymentReservationSource origin =
            ProjectDeploymentReservationOrigins.List(currentMep.Operation)
                .SingleOrDefault(candidate =>
                    candidate.Kind == item.OriginKind &&
                    candidate.Id == item.OriginId) ??
            throw new InvalidDataException(
                "Aucune réserve explicite dans cette décision MEP.");
        if (!string.Equals(item.OriginSummary, origin.Description,
                StringComparison.Ordinal) ||
            item.CreatedUtc < origin.RecordedUtc)
        {
            throw new InvalidDataException(
                "Reprise non conforme à la décision MEP source.");
        }

        ProjectDeploymentReservationLedger next =
            ProjectDeploymentReservationCodec.Add(snapshot.Ledger, item);
        return await PublishAsync(
            directory, manifest.Id, snapshot, next, cancellationToken);
    }

    /// <summary>
    /// Appends a human acknowledgement, note, reassignment, resolution or
    /// reopening. This operation is allowed after the MEP has been closed.
    /// </summary>
    /// <param name="projectDirectory">Owner project.</param>
    /// <param name="snapshot">Previously inspected follow-up revision.</param>
    /// <param name="action">Explicit human event.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New append-only source revision.</returns>
    public async Task<ProjectDeploymentReservationSnapshot> RecordAsync(
        string projectDirectory, ProjectDeploymentReservationSnapshot snapshot,
        ProjectDeploymentReservationEvent action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(action);
        (string directory, ProjectManifest manifest, bool archived) =
            await ResolveAsync(projectDirectory, cancellationToken);
        RequireActive(archived);
        if (snapshot.Ledger.ProjectId != manifest.Id)
        {
            throw new InvalidDataException(
                "Suivi des réserves hors projet.");
        }

        ProjectDeploymentReservationLedger next =
            ProjectDeploymentReservationCodec.Append(snapshot.Ledger, action);
        if (action.Evidence is not null)
        {
            string path = _sources.ResolveExistingFile(
                action.Evidence.RelativePath);
            RequireWithinProject(directory, path);
            ProjectGateEvidenceSnapshot actual =
                await _gates.CaptureEvidenceAsync(
                    action.Evidence.RelativePath, cancellationToken);
            if (!string.Equals(actual.Sha256, action.Evidence.Sha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Preuve de réserve modifiée ou falsifiée.");
            }
        }

        return await PublishAsync(
            directory, manifest.Id, snapshot, next, cancellationToken);
    }

    /// <summary>Returns the next human-previewed RES number without mutation.</summary>
    /// <param name="ledger">Validated existing register.</param>
    /// <returns>Next unused reference, or null if exhausted.</returns>
    public static string? NextReference(
        ProjectDeploymentReservationLedger ledger)
    {
        ProjectDeploymentReservationCodec.Validate(ledger);
        int maximum = ledger.Items.Select(item => int.Parse(item.Reference[4..],
            System.Globalization.CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0).Max();
        return maximum >= 9999 ? null :
            "RES-" + (maximum + 1).ToString("D4",
                System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Atomically publishes one append-only human action.</summary>
    /// <param name="directory">Verified active project.</param>
    /// <param name="projectId">Manifest GUID.</param>
    /// <param name="snapshot">Previewed register source.</param>
    /// <param name="proposal">New validated ledger.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New source and byte SHA.</returns>
    private async Task<ProjectDeploymentReservationSnapshot> PublishAsync(
        string directory, Guid projectId,
        ProjectDeploymentReservationSnapshot snapshot,
        ProjectDeploymentReservationLedger proposal,
        CancellationToken cancellationToken)
    {
        ProjectDeploymentReservationCodec.Validate(proposal);
        ProjectDeploymentReservationSnapshot now = await ReadAsync(
            directory, cancellationToken);
        if (snapshot.Ledger.ProjectId != projectId ||
            proposal.ProjectId != projectId ||
            !string.Equals(snapshot.SourceSha256, now.SourceSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(JsonSerializer.Serialize(snapshot.Ledger),
                JsonSerializer.Serialize(now.Ledger), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Registre de réserves MEP modifié : actualisez.");
        }

        IReadOnlyList<ProjectDeploymentReservation> oldItems =
            now.Ledger.Items;
        IReadOnlyList<ProjectDeploymentReservationEvent> oldEvents =
            now.Ledger.Events;
        if (proposal.Items.Count < oldItems.Count ||
            proposal.Events.Count < oldEvents.Count ||
            proposal.Items.Count - oldItems.Count +
                proposal.Events.Count - oldEvents.Count != 1)
        {
            throw new InvalidDataException(
                "Enregistrez une seule action sans modifier l'historique.");
        }

        for (int index = 0; index < oldItems.Count; index++)
        {
            if (!string.Equals(JsonSerializer.Serialize(oldItems[index]),
                JsonSerializer.Serialize(proposal.Items[index]),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Réserve déjà historisée modifiée.");
            }
        }

        for (int index = 0; index < oldEvents.Count; index++)
        {
            if (!string.Equals(JsonSerializer.Serialize(oldEvents[index]),
                JsonSerializer.Serialize(proposal.Events[index]),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Action historique de réserve modifiée.");
            }
        }

        string markdown = ProjectDeploymentReservationCodec.Format(proposal);
        string folder = Path.GetDirectoryName(RegisterPath(directory))!;
        string[] ancestors =
        [
            Path.Combine(directory, "Exploitation"),
            Path.Combine(directory, "Exploitation", "Déploiement"),
            folder
        ];
        foreach (string parent in ancestors)
        {
            Directory.CreateDirectory(parent);
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Répertoire de réserves MEP lié ou non sûr.");
            }
        }

        string path = RegisterPath(directory);
        string hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(markdown)));
        if (now.SourceSha256 is null)
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                path, markdown, cancellationToken);
        }
        else
        {
            string verified = _sources.ResolveExistingFile(
                now.RelativePath!);
            TextDocumentSession session = await TextDocumentSession.OpenAsync(
                verified, cancellationToken);
            if (!string.Equals(session.Revision.Sha256, now.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Source de réserves modifiée au dernier instant.");
            }

            string stage = Path.Combine(folder,
                "." + Path.GetFileName(path) + "." +
                Guid.NewGuid().ToString("N") + ".stage");
            try
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    stage, markdown, cancellationToken);
                await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                    verified, now.SourceSha256, stage, hash, cancellationToken);
            }
            finally
            {
                if (File.Exists(stage))
                {
                    File.Delete(stage);
                }
            }
        }

        return new ProjectDeploymentReservationSnapshot(proposal,
            _sources.GetReferenceForExistingFile(path), hash);
    }

    /// <summary>Verifies a project manifest and archive status.</summary>
    /// <param name="projectDirectory">Project root.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Existing project and read-only indicator.</returns>
    private async Task<(string Directory, ProjectManifest Manifest, bool Archived)>
        ResolveAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string full = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string relative = Path.GetRelativePath(_workspaceRoot, full);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Projet de réserves MEP hors workspace.");
        }

        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 3 ||
            !(string.Equals(parts[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(parts[0], WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Dossier projet MEP non canonique.");
        }

        string path = Path.Combine(full, WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(path);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet MEP sans identifiant.");
        }

        bool archived = manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null ||
            string.Equals(parts[0], WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase);
        return (full, manifest, archived);
    }

    /// <summary>Refuses any write in archived projects.</summary>
    /// <param name="archived">Project status.</param>
    private static void RequireActive(bool archived)
    {
        if (archived)
        {
            throw new InvalidOperationException(
                "Suivi de réserves archivé : lecture seule.");
        }
    }

    /// <summary>Forbids evidence from another project or parent directory.</summary>
    /// <param name="projectDirectory">Expected project.</param>
    /// <param name="path">Source-verified existing proof.</param>
    private static void RequireWithinProject(string projectDirectory, string path)
    {
        string relative = Path.GetRelativePath(projectDirectory, path);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Preuve de suivi issue d'un autre projet.");
        }
    }

    /// <summary>Canonical source, independent from the MEP closure snapshot.</summary>
    /// <param name="directory">Canonical project root.</param>
    /// <returns>One register path.</returns>
    private static string RegisterPath(string directory) =>
        Path.Combine(directory, "Exploitation", "Déploiement", "Suivi",
            "Reservations-MEP.md");
}
