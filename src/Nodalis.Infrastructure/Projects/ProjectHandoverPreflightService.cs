using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Read-only preflight of one potential A/B passation. Nothing in this record
/// is authority to move, copy or rewrite the source business object.
/// </summary>
public sealed record ProjectHandoverPreview
{
    /// <summary>Original owning project.</summary>
    public required Guid SourceProjectId { get; init; }

    /// <summary>Proposed recipient project.</summary>
    public required Guid TargetProjectId { get; init; }

    /// <summary>Workspace-relative original project directory.</summary>
    public required string SourceProjectRelativePath { get; init; }

    /// <summary>Workspace-relative recipient project directory.</summary>
    public required string TargetProjectRelativePath { get; init; }

    /// <summary>Stable selected item identity.</summary>
    public required Guid SourceObjectId { get; init; }

    /// <summary>Human register category.</summary>
    public required string SourceObjectKind { get; init; }

    /// <summary>Human reference at the time of preview.</summary>
    public required string SourceReference { get; init; }

    /// <summary>Original source, not a destination path.</summary>
    public required string SourceRelativePath { get; init; }

    /// <summary>Move original versus create a linked successor.</summary>
    public required ProjectHandoverMethod Method { get; init; }

    /// <summary>Original GUID for A or reserved proposed fresh GUID for B.</summary>
    public required Guid ProposedTargetObjectId { get; init; }

    /// <summary>Audited source content identity.</summary>
    public required string SourceFingerprint { get; init; }

    /// <summary>Audited recipient content identity.</summary>
    public required string TargetFingerprint { get; init; }

    /// <summary>Conditions that prevent even recording an intention.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = [];

    /// <summary>Known limits which a future executor must requalify.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True only when a human may record a pending intention.</summary>
    public bool CanRecord => Blockers.Count == 0;

    /// <summary>French bounded preview, never promises physical execution.</summary>
    /// <returns>Human-readable source, target, mode and limitations.</returns>
    public string FormatReview()
    {
        StringBuilder review = new();
        review.AppendLine("Passation envisagée — aucun objet déplacé")
            .AppendLine()
            .Append("Source : ").Append(SourceProjectRelativePath).AppendLine()
            .Append("Destination : ").Append(TargetProjectRelativePath).AppendLine()
            .Append("Objet : ").Append(SourceReference).Append(" · ")
            .AppendLine(SourceObjectKind)
            .Append("Mode : ").AppendLine(Method ==
                ProjectHandoverMethod.MoveOriginal
                    ? "A · déplacer l'original (GUID conservé)"
                    : "B · créer ultérieurement un successeur lié (GUID distinct)")
            .Append("Identité proposée : ")
            .AppendLine(ProposedTargetObjectId.ToString("D"));
        foreach (string blocker in Blockers.Take(5))
        {
            review.AppendLine("BLOCAGE : " + blocker);
        }

        foreach (string warning in Warnings.Take(5))
        {
            review.AppendLine("Vigilance : " + warning);
        }

        review.AppendLine()
            .AppendLine("Validation : seule une intention append-only sera " +
                "enregistrée dans le projet source. Aucun transfert, " +
                "successeur, modification de lien ou archive ne sera effectué.");
        return review.ToString();
    }
}

/// <summary>
/// Examines only two explicit project directories and their existing scoped
/// registries; never scans unrelated applications or changes a source.
/// </summary>
public sealed class ProjectHandoverPreflightService
{
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectClosureAuditService _audit;

    /// <summary>Creates a workspace-bound preflight planner.</summary>
    /// <param name="workspaceRoot">Existing Nodalis workspace.</param>
    public ProjectHandoverPreflightService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
        _audit = new ProjectClosureAuditService(_root);
    }

    /// <summary>
    /// Reads a true source item and an active destination project, including
    /// settled-object identities so collisions are not invisible.
    /// </summary>
    /// <param name="sourceProjectDirectory">Explicit original project.</param>
    /// <param name="targetProjectDirectory">Explicit recipient project.</param>
    /// <param name="objectId">Selected unresolved canonical item.</param>
    /// <param name="method">Requested non-executing handover strategy.</param>
    /// <param name="proposedSuccessorId">Previously reviewed B identity when rechecking.</param>
    /// <param name="cancellationToken">Cancellation of the preflight.</param>
    /// <returns>Immutable review with human-readable blockers and warnings.</returns>
    public async Task<ProjectHandoverPreview> PrepareAsync(
        string sourceProjectDirectory, string targetProjectDirectory,
        Guid objectId, ProjectHandoverMethod method,
        Guid? proposedSuccessorId = null,
        CancellationToken cancellationToken = default)
    {
        if (objectId == Guid.Empty || !Enum.IsDefined(method) ||
            (method == ProjectHandoverMethod.MoveOriginal &&
             proposedSuccessorId.HasValue &&
             proposedSuccessorId != objectId))
        {
            throw new InvalidDataException(
                "Objet ou stratégie de passation invalide.");
        }

        (string source, string sourceRelative, ProjectManifest sourceManifest) =
            await ResolveActiveProjectAsync(sourceProjectDirectory,
                cancellationToken);
        (string target, string targetRelative, ProjectManifest targetManifest) =
            await ResolveActiveProjectAsync(targetProjectDirectory,
                cancellationToken);
        if (sourceManifest.Id == targetManifest.Id ||
            string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le destinataire doit être un autre projet actif.");
        }

        ProjectClosureAuditSnapshot sourceAudit =
            await _audit.ReadAsync(source, cancellationToken);
        ProjectClosureAuditSnapshot targetAudit =
            await _audit.ReadAsync(target, cancellationToken);
        if (sourceAudit.ProjectId != sourceManifest.Id ||
            targetAudit.ProjectId != targetManifest.Id)
        {
            throw new InvalidDataException(
                "Le projet a changé de propriétaire pendant l'inspection.");
        }

        ProjectClosureAuditItem? item = sourceAudit.Items.SingleOrDefault(
            entry => entry.Id == objectId);
        if (item is null || item.RelativePath is null ||
            !ProjectJourneySourcePath.IsSafe(item.RelativePath))
        {
            throw new InvalidDataException(
                "L'objet source n'est plus identifié de façon unique " +
                "dans les éléments à reprendre.");
        }

        List<string> blockers = [];
        List<string> warnings = [];
        if (method == ProjectHandoverMethod.MoveOriginal &&
            targetAudit.AllKnownObjectIds.Contains(item.Id))
        {
            blockers.Add("Identifiant technique déjà présent dans le projet " +
                "destinataire, y compris parmi les objets soldés.");
        }

        Guid proposed = method == ProjectHandoverMethod.MoveOriginal
            ? item.Id : proposedSuccessorId ?? Guid.NewGuid();
        if (proposed == Guid.Empty ||
            (method == ProjectHandoverMethod.LinkedSuccessor &&
             proposed == item.Id))
        {
            blockers.Add("Le successeur doit recevoir un GUID nouveau et non vide.");
        }
        if (sourceAudit.AllKnownObjectIds.Contains(proposed) &&
            proposed != item.Id ||
            targetAudit.AllKnownObjectIds.Contains(proposed))
        {
            blockers.Add("Identifiant proposé déjà utilisé.");
        }

        if (sourceAudit.Diagnostics.Count != 0)
        {
            warnings.Add(sourceAudit.Diagnostics.Count +
                " diagnostic(s) sur le projet source : vérification " +
                "approfondie requise avant tout transfert.");
        }

        if (targetAudit.Diagnostics.Count != 0)
        {
            warnings.Add(targetAudit.Diagnostics.Count +
                " diagnostic(s) sur le destinataire : aucune garantie " +
                "de non-collision avec les objets illisibles.");
        }

        if (sourceManifest.ApplicationId != targetManifest.ApplicationId)
        {
            warnings.Add("Applications différentes : les liens et " +
                "références visibles devront être requalifiés.");
        }

        warnings.Add("Les liens entrants, doublons hors registres couverts, " +
            "références transversales et transferts interrompus exigent " +
            "encore un exécuteur transactionnel et un journal de reprise.");

        return new ProjectHandoverPreview
        {
            SourceProjectId = sourceManifest.Id,
            TargetProjectId = targetManifest.Id,
            SourceProjectRelativePath = sourceRelative,
            TargetProjectRelativePath = targetRelative,
            SourceObjectId = item.Id,
            SourceObjectKind = item.Kind,
            SourceReference = item.Reference,
            SourceRelativePath = item.RelativePath,
            Method = method,
            ProposedTargetObjectId = proposed,
            SourceFingerprint = sourceAudit.SourceFingerprint,
            TargetFingerprint = targetAudit.SourceFingerprint,
            Blockers = blockers.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>
    /// Verifies an existing active project manifest without accepting
    /// another workspace, a symlink or any archived directory.
    /// </summary>
    /// <param name="projectDirectory">Candidate project directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Physical path, portable path and exact manifest.</returns>
    internal async Task<(string Directory, string Relative, ProjectManifest Manifest)>
        ResolveActiveProjectAsync(string projectDirectory,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string full = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string manifestPath = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(Path.Combine(full,
                WorkspaceLayout.ProjectManifestFileName)));
        string relative = Path.GetRelativePath(_root, full)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!ProjectJourneySourcePath.IsSafe(relative) ||
            !relative.StartsWith(
                WorkspaceLayout.ApplicationsDirectoryName + "/",
                StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(full))
        {
            throw new InvalidDataException(
                "La passation exige deux projets actifs du workspace.");
        }

        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty || manifest.ApplicationId == Guid.Empty ||
            manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Manifeste de passation invalide ou projet archivé.");
        }

        string? name = Path.GetFileName(Path.GetDirectoryName(full));
        if (!string.Equals(name, WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, WorkspaceLayout.SubProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le dossier sélectionné n'est pas un projet canonique.");
        }

        return (full, relative, manifest);
    }
}
