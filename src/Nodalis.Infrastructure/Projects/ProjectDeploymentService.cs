using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One validated canonical deployment operation and its byte revision.</summary>
/// <param name="Operation">Human event-sourced MEP ledger.</param>
/// <param name="RelativePath">Verified workspace-relative source.</param>
/// <param name="SourceSha256">SHA-256 of source bytes.</param>
public sealed record ProjectDeploymentEntry(
    ProjectDeploymentOperation Operation, string RelativePath, string SourceSha256);

/// <summary>Bounded project MEP listing; reading never creates files.</summary>
/// <param name="Operations">Unique safe project MEPs.</param>
/// <param name="Diagnostics">Unreadable or ambiguous sources.</param>
/// <param name="NextReference">Proposed unreserved reference.</param>
public sealed record ProjectDeploymentCatalog(
    IReadOnlyList<ProjectDeploymentEntry> Operations,
    IReadOnlyList<string> Diagnostics, string? NextReference);

/// <summary>
/// Project-local MEP Markdown ledger persistence, exact SHA-CAS and historical
/// immutability. No deployment is initiated by this service.
/// </summary>
public sealed class ProjectDeploymentService
{
    private const int MaxOperations = 500;
    private const long MaxBytes = 4 * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectGateRegistryService _gates;

    /// <summary>Creates a workspace-isolated deployment service.</summary>
    /// <param name="workspaceRoot">Active workspace.</param>
    public ProjectDeploymentService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _gates = new ProjectGateRegistryService(_workspaceRoot);
    }

    /// <summary>Lists existing MEPs, without creating a deployment folder.</summary>
    /// <param name="projectDirectory">Canonical project root.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Validated source catalog.</returns>
    public async Task<ProjectDeploymentCatalog> ListAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest manifest) =
            await ResolveAsync(projectDirectory, cancellationToken);
        return await ListInProjectAsync(directory, manifest.Id, cancellationToken);
    }

    /// <summary>
    /// Creates a user-approved MEP, rechecking the proposed reference and
    /// never overwriting an already occupied file.
    /// </summary>
    /// <param name="projectDirectory">Owner project.</param>
    /// <param name="title">Human title.</param>
    /// <param name="expectedReference">Reviewed MEP-xxxx reference.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted operation and SHA.</returns>
    public async Task<ProjectDeploymentEntry> CreateAsync(
        string projectDirectory, string title, string expectedReference,
        CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest project) =
            await ResolveAsync(projectDirectory, cancellationToken);
        RequireActive(project);
        ProjectDeploymentCatalog catalog = await ListInProjectAsync(
            directory, project.Id, cancellationToken);
        if (!string.Equals(expectedReference, catalog.NextReference,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException("Référence MEP obsolète ou réservée.");
        }

        ProjectDeploymentOperation operation = ProjectDeploymentEditor.Create(
            project.Id, expectedReference, title);
        string markdown = ProjectDeploymentMarkdownCodec.Format(operation);
        string root = Path.Combine(directory, "Exploitation");
        string deployments = Path.Combine(root, "Déploiement");
        string folder = Path.Combine(deployments, "Opérations");
        foreach (string item in new[] { root, deployments, folder })
        {
            Directory.CreateDirectory(item);
            RequireNormalDirectory(item);
        }

        string path = Path.Combine(folder, expectedReference + ".md");
        await AtomicFileWriter.WriteNewAllTextAsync(path, markdown,
            cancellationToken);
        return new ProjectDeploymentEntry(
            operation, _sources.GetReferenceForExistingFile(path),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))));
    }

    /// <summary>
    /// Publishes user-approved changes only when the original file is
    /// unchanged, all prior attempts/events are immutable and each new
    /// attempt refers to a compatible, reviewed gate from this project.
    /// </summary>
    /// <param name="projectDirectory">Owner project.</param>
    /// <param name="snapshot">Previously read exact source.</param>
    /// <param name="proposed">Append-only reviewed ledger.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Persisted new source revision.</returns>
    public async Task<ProjectDeploymentEntry> SaveAsync(
        string projectDirectory, ProjectDeploymentEntry snapshot,
        ProjectDeploymentOperation proposed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        (string directory, ProjectManifest project) =
            await ResolveAsync(projectDirectory, cancellationToken);
        RequireActive(project);
        ProjectDeploymentMarkdownCodec.Validate(proposed);
        if (proposed.ProjectId != project.Id ||
            snapshot.Operation.ProjectId != project.Id ||
            proposed.Id != snapshot.Operation.Id || proposed.Id == Guid.Empty ||
            !ValidSha(snapshot.SourceSha256))
        {
            throw new InvalidDataException("MEP hors projet ou sans empreinte.");
        }

        ProjectDeploymentCatalog catalog = await ListInProjectAsync(
            directory, project.Id, cancellationToken);
        ProjectDeploymentEntry? current = catalog.Operations.SingleOrDefault(
            item => item.Operation.Id == snapshot.Operation.Id &&
                string.Equals(item.RelativePath, snapshot.RelativePath,
                    StringComparison.OrdinalIgnoreCase));
        if (current is null || !string.Equals(current.SourceSha256,
            snapshot.SourceSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "MEP source modifiée, déplacée ou ambiguë : actualisez.");
        }

        string path = _sources.ResolveExistingFile(current.RelativePath);
        string folder = OperationsFolder(directory);
        if (!string.Equals(Path.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(path) ?? string.Empty),
            Path.TrimEndingDirectorySeparator(folder),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Dossier source de MEP non canonique.");
        }

        TextDocumentSession session = await TextDocumentSession.OpenAsync(
            path, cancellationToken);
        if (!string.Equals(session.Revision.Sha256, snapshot.SourceSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Révision MEP périmée.");
        }

        ProjectDeploymentOperation old =
            ProjectDeploymentMarkdownCodec.Parse(session.Content);
        if (old.Id != proposed.Id || old.ProjectId != proposed.ProjectId ||
            old.CreatedUtc != proposed.CreatedUtc ||
            !string.Equals(old.Reference, proposed.Reference,
                StringComparison.Ordinal) ||
            !string.Equals(old.Title, proposed.Title, StringComparison.Ordinal) ||
            proposed.Attempts.Count < old.Attempts.Count ||
            proposed.Events.Count < old.Events.Count)
        {
            throw new InvalidDataException("Historique ou identité MEP modifiés.");
        }

        for (int index = 0; index < old.Attempts.Count; index++)
        {
            if (!string.Equals(JsonSerializer.Serialize(old.Attempts[index]),
                JsonSerializer.Serialize(proposed.Attempts[index]),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Tentative historique modifiée : écriture interdite.");
            }
        }

        for (int index = 0; index < old.Events.Count; index++)
        {
            if (!string.Equals(JsonSerializer.Serialize(old.Events[index]),
                JsonSerializer.Serialize(proposed.Events[index]),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Événement historique modifié : écriture interdite.");
            }
        }

        // A recorded closure freezes the full operation, even at the same SHA.
        if (old.Closure is not null)
        {
            throw new InvalidDataException(
                "Cette MEP est déjà clôturée : aucune modification permise.");
        }

        List<ProjectDeploymentHandoverReview> oldReviews =
            old.HandoverReviews ?? [];
        List<ProjectDeploymentHandoverReview> newReviews =
            proposed.HandoverReviews ?? [];
        if (newReviews.Count < oldReviews.Count)
        {
            throw new InvalidDataException(
                "Ancienne décision de passation supprimée.");
        }

        for (int index = 0; index < oldReviews.Count; index++)
        {
            if (!string.Equals(JsonSerializer.Serialize(oldReviews[index]),
                JsonSerializer.Serialize(newReviews[index]),
                StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Revue de passation historique modifiée.");
            }
        }

        if (newReviews.Count > oldReviews.Count &&
            (proposed.Attempts.Count != old.Attempts.Count ||
             proposed.Events.Count != old.Events.Count ||
             proposed.Closure is not null))
        {
            throw new InvalidDataException(
                "Enregistrez la passation séparément des tentatives et du bilan.");
        }

        // User-linked evidence must still exist in THIS project and match
        // its observed bytes at save time. A valid SHA-shaped string alone
        // cannot certify another project's documents or missing sources.
        foreach (ProjectDeploymentHandoverReview review in newReviews.Skip(
            oldReviews.Count))
        {
            foreach (ProjectDeploymentHandoverCheck check in review.Checks)
            {
                if (check.Evidence is not ProjectGateEvidenceSnapshot proof)
                {
                    continue;
                }

                string fullPath = _sources.ResolveExistingFile(
                    proof.RelativePath);
                string relative = Path.GetRelativePath(directory, fullPath);
                if (Path.IsPathRooted(relative) || relative is "." or ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal) ||
                    relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Preuve de passation hors du projet propriétaire.");
                }

                ProjectGateEvidenceSnapshot currentProof =
                    await _gates.CaptureEvidenceAsync(
                        proof.RelativePath, cancellationToken);
                if (!string.Equals(currentProof.Sha256, proof.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Preuve de passation modifiée depuis la revue.");
                }
            }
        }

        if (proposed.Closure is not null &&
            (proposed.Attempts.Count != old.Attempts.Count ||
             proposed.Events.Count != old.Events.Count))
        {
            throw new InvalidDataException(
                "Enregistrez le bilan séparément des tentatives et événements.");
        }

        foreach (ProjectDeploymentAttempt attempt in proposed.Attempts.Skip(
            old.Attempts.Count))
        {
            await ValidateNewAttemptAsync(directory, attempt, cancellationToken);
        }

        string markdown = ProjectDeploymentMarkdownCodec.Format(proposed);
        string stage = Path.Combine(folder, "." + Path.GetFileName(path) + "." +
            Guid.NewGuid().ToString("N") + ".stage");
        try
        {
            await AtomicFileWriter.WriteNewAllTextAsync(stage,
                markdown, cancellationToken);
            string hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(markdown)));
            await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                path, snapshot.SourceSha256, stage, hash, cancellationToken);
            return new ProjectDeploymentEntry(proposed, current.RelativePath, hash);
        }
        finally
        {
            if (File.Exists(stage))
            {
                File.Delete(stage);
            }
        }
    }

    /// <summary>
    /// Requires an actual, latest approving gate and rehashes the gate evidence,
    /// deployment procedure and rollback procedure before adding an attempt.
    /// </summary>
    /// <param name="directory">Verified project root.</param>
    /// <param name="attempt">Human-declared frozen attempt.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Completed non-authorizing verification.</returns>
    private async Task ValidateNewAttemptAsync(string directory,
        ProjectDeploymentAttempt attempt, CancellationToken cancellationToken)
    {
        ProjectGateRegistrySnapshot register =
            await _gates.ReadAsync(directory, cancellationToken);
        ProjectGateDecision decision = register.Registry.Decisions.SingleOrDefault(
            item => item.Id == attempt.GateDecisionId) ??
            throw new InvalidDataException(
                "Décision GO absente du projet courant.");
        ProjectGateDecision? latest = register.Registry.Decisions.LastOrDefault(
            item => item.GateId == decision.GateId);
        if (latest?.Id != decision.Id ||
            decision.Verdict is not (ProjectGateVerdict.Go or
                ProjectGateVerdict.GoWithReservations))
        {
            throw new InvalidDataException(
                "GO remplacé, NO-GO ou report : nouvelle revue requise.");
        }

        List<ProjectGateEvidenceSnapshot> proofs = [];
        foreach (ProjectGateEvidenceSnapshot original in decision.Evidence)
        {
            proofs.Add(await _gates.CaptureEvidenceAsync(
                original.RelativePath, cancellationToken));
        }

        ProjectGateReuseAssessment review = ProjectGateReuseReview.Assess(
            register.Registry, decision.Id, attempt.TargetVersion,
            attempt.Environment, attempt.Scope, proofs);
        if (!review.WithinHistoricalConditions)
        {
            throw new InvalidDataException(
                "Périmètre, version, environnement ou preuves hors GO : " +
                "nouvelle décision requise.");
        }

        ProjectGateEvidenceSnapshot deployment = await _gates.CaptureEvidenceAsync(
            attempt.Procedure.RelativePath, cancellationToken);
        ProjectGateEvidenceSnapshot rollback = await _gates.CaptureEvidenceAsync(
            attempt.RollbackProcedure.RelativePath, cancellationToken);
        if (!string.Equals(deployment.Sha256, attempt.Procedure.Sha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(rollback.Sha256, attempt.RollbackProcedure.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Empreinte de procédure ou rollback modifiée.");
        }

        // A matching source remains an attestation, not permission to execute.
        // The human must separately review incidents, risks and actual state.
    }

    /// <summary>Boundedly scans only the exact project MEP folder.</summary>
    /// <param name="directory">Verified project folder.</param>
    /// <param name="projectId">Expected canonical project identity.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Unambiguous safe entries plus diagnostics.</returns>
    private async Task<ProjectDeploymentCatalog> ListInProjectAsync(
        string directory, Guid projectId, CancellationToken cancellationToken)
    {
        string root = Path.Combine(directory, "Exploitation");
        string deployments = Path.Combine(root, "Déploiement");
        string folder = OperationsFolder(directory);
        foreach (string item in new[] { root, deployments, folder })
        {
            if (!Directory.Exists(item))
            {
                return new ProjectDeploymentCatalog([], [], "MEP-0001");
            }

            RequireNormalDirectory(item);
        }

        string[] files = Directory.EnumerateFiles(folder, "*.md",
            SearchOption.TopDirectoryOnly).Take(MaxOperations + 1).ToArray();
        if (files.Length > MaxOperations)
        {
            throw new InvalidDataException("Plus de 500 fiches MEP.");
        }

        List<ProjectDeploymentEntry> entries = [];
        List<string> diagnostics = [];
        int highest = 0;
        foreach (string path in files.OrderBy(
            item => item, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(path);
            if (ProjectDeploymentMarkdownCodec.IsReference(name))
            {
                highest = Math.Max(highest, int.Parse(
                    name[4..], CultureInfo.InvariantCulture));
            }

            try
            {
                string relative = _sources.GetReferenceForExistingFile(path);
                string full = _sources.ResolveExistingFile(relative);
                if (new FileInfo(full).Length > MaxBytes)
                {
                    diagnostics.Add(relative + " : source trop volumineuse.");
                    continue;
                }

                byte[] bytes = await File.ReadAllBytesAsync(full,
                    cancellationToken);
                string markdown = new UTF8Encoding(false, true).GetString(bytes);
                ProjectDeploymentOperation operation =
                    ProjectDeploymentMarkdownCodec.Parse(markdown);
                if (operation.ProjectId != projectId ||
                    !string.Equals(operation.Reference, name,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(relative + " : projet ou nom incompatible.");
                    continue;
                }

                entries.Add(new ProjectDeploymentEntry(operation, relative,
                    Convert.ToHexString(SHA256.HashData(bytes))));
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or DecoderFallbackException)
            {
                diagnostics.Add(name + ".md : " + exception.Message);
            }
        }

        HashSet<Guid> duplicatedIds = entries.GroupBy(item => item.Operation.Id)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        HashSet<string> duplicatedRefs = entries.GroupBy(item =>
            item.Operation.Reference, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ProjectDeploymentEntry duplicate in entries.Where(item =>
            duplicatedIds.Contains(item.Operation.Id) ||
            duplicatedRefs.Contains(item.Operation.Reference)))
        {
            diagnostics.Add(duplicate.RelativePath +
                " : GUID ou référence MEP ambiguë.");
        }

        ProjectDeploymentEntry[] unique = entries.Where(item =>
            !duplicatedIds.Contains(item.Operation.Id) &&
            !duplicatedRefs.Contains(item.Operation.Reference))
            .OrderBy(item => item.Operation.Reference,
                StringComparer.Ordinal).ToArray();
        string? next = highest >= 9999 ? null : "MEP-" +
            (highest + 1).ToString("D4", CultureInfo.InvariantCulture);
        return new ProjectDeploymentCatalog(unique, diagnostics, next);
    }

    /// <summary>Resolves one project manifest strictly within the workspace.</summary>
    /// <param name="projectDirectory">Candidate project path.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verified directory and project identity.</returns>
    private async Task<(string Directory, ProjectManifest Manifest)>
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
            throw new InvalidDataException("Projet MEP hors workspace.");
        }

        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 3 ||
            !(string.Equals(parts[0],
                WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
              string.Equals(parts[0], WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Projet MEP non reconnu.");
        }

        string manifestPath = Path.Combine(full,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(manifestPath);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException("Projet sans GUID.");
        }

        return (full, manifest);
    }

    /// <summary>Archived project MEP files are read-only.</summary>
    /// <param name="project">Owner manifest.</param>
    private static void RequireActive(ProjectManifest project)
    {
        if (project.ArchivedUtc.HasValue ||
            project.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidOperationException("MEP archivée : lecture seule.");
        }
    }

    /// <summary>Refuses symlink and reparse-point directory components.</summary>
    /// <param name="directory">Existing deployment folder.</param>
    private static void RequireNormalDirectory(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Dossier MEP lié ou non sûr.");
        }
    }

    /// <summary>Checks an expected source byte fingerprint.</summary>
    /// <param name="sha">Hex digest.</param>
    /// <returns>Whether syntactically a SHA-256.</returns>
    private static bool ValidSha(string? sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);

    /// <summary>Returns the only canonical folder for this feature.</summary>
    /// <param name="directory">Verified project root.</param>
    /// <returns>Absolute MEP file directory.</returns>
    private static string OperationsFolder(string directory) =>
        Path.Combine(directory, "Exploitation", "Déploiement", "Opérations");
}
