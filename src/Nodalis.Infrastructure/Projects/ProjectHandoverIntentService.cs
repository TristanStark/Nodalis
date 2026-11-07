using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Verified append-only handover intentions, not transfer history.</summary>
/// <param name="Ledger">Canonical source-owned event history.</param>
/// <param name="SourceSha256">Exact revision or null if no source exists.</param>
public sealed record ProjectHandoverIntentSnapshot(
    ProjectHandoverIntentLedger Ledger, string? SourceSha256);

/// <summary>
/// Safe opt-in journal for planned passations. No business object is copied,
/// moved, reassigned or rewritten by any method in this service.
/// </summary>
public sealed class ProjectHandoverIntentService
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private readonly string _root;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectHandoverPreflightService _preflight;

    /// <summary>Builds a project-scoped proposal journal service.</summary>
    /// <param name="workspaceRoot">Single active Nodalis workspace.</param>
    public ProjectHandoverIntentService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_root);
        _preflight = new ProjectHandoverPreflightService(_root);
    }

    /// <summary>
    /// Reads only an existing project-owned plan without any initialization.
    /// Archived source projects may inspect their existing history.
    /// </summary>
    /// <param name="sourceProjectDirectory">Source project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Valid canonical intention history and source hash.</returns>
    public async Task<ProjectHandoverIntentSnapshot> ReadAsync(
        string sourceProjectDirectory, CancellationToken cancellationToken = default)
    {
        string directory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(sourceProjectDirectory));
        string manifestPath = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(Path.Combine(
                directory, WorkspaceLayout.ProjectManifestFileName)));
        Nodalis.Core.Domain.ProjectManifest manifest =
            await Nodalis.Infrastructure.Persistence.AtomicJsonFile.ReadAsync<
                Nodalis.Core.Domain.ProjectManifest>(manifestPath, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException(
                "Projet propriétaire du journal sans identité.");
        }

        string parent = Path.Combine(directory, "Management");
        string path = Path.Combine(parent, "Passations.md");
        if (Directory.Exists(parent))
        {
            RequireNormal(parent);
        }

        if (!File.Exists(path))
        {
            if (Path.Exists(path))
            {
                throw new InvalidDataException(
                    "Chemin du journal occupé par une autre entrée.");
            }

            return new ProjectHandoverIntentSnapshot(
                new ProjectHandoverIntentLedger { ProjectId = manifest.Id }, null);
        }

        string verified = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(path));
        if (new FileInfo(verified).Length > MaxBytes)
        {
            throw new InvalidDataException("Journal de passation trop volumineux.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(verified, cancellationToken);
        string markdown = new UTF8Encoding(false, true).GetString(bytes);
        ProjectHandoverIntentLedger ledger =
            ProjectHandoverIntentCodec.Parse(markdown);
        if (ledger.ProjectId != manifest.Id)
        {
            throw new InvalidDataException(
                "Passations associées à un autre projet.");
        }

        return new ProjectHandoverIntentSnapshot(ledger,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Revalidates source, target, selected identity and both source revisions
    /// before appending one human intent with an atomic conditional write.
    /// </summary>
    /// <param name="reviewed">Exact preview approved by a person.</param>
    /// <param name="original">Read journal revision displayed in the UI.</param>
    /// <param name="actor">Named human reviewer.</param>
    /// <param name="rationale">Required human plan justification.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>New canonical ledger revision after appending the intention.</returns>
    public async Task<ProjectHandoverIntentSnapshot> RecordIntentAsync(
        ProjectHandoverPreview reviewed,
        ProjectHandoverIntentSnapshot original,
        string actor, string rationale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(original);
        if (!reviewed.CanRecord)
        {
            throw new InvalidOperationException(
                "La passation comporte encore des conditions bloquantes.");
        }

        string source = Path.Combine(_root,
            reviewed.SourceProjectRelativePath.Replace('/',
                Path.DirectorySeparatorChar));
        string target = Path.Combine(_root,
            reviewed.TargetProjectRelativePath.Replace('/',
                Path.DirectorySeparatorChar));
        ProjectHandoverPreview verified = await _preflight.PrepareAsync(
            source, target, reviewed.SourceObjectId, reviewed.Method,
            reviewed.ProposedTargetObjectId, cancellationToken);
        if (!verified.CanRecord ||
            !string.Equals(verified.SourceProjectRelativePath,
                reviewed.SourceProjectRelativePath, StringComparison.Ordinal) ||
            !string.Equals(verified.TargetProjectRelativePath,
                reviewed.TargetProjectRelativePath, StringComparison.Ordinal) ||
            verified.SourceProjectId != reviewed.SourceProjectId ||
            verified.TargetProjectId != reviewed.TargetProjectId ||
            verified.ProposedTargetObjectId != reviewed.ProposedTargetObjectId ||
            !string.Equals(verified.SourceObjectKind,
                reviewed.SourceObjectKind, StringComparison.Ordinal) ||
            !string.Equals(verified.SourceReference,
                reviewed.SourceReference, StringComparison.Ordinal) ||
            !string.Equals(verified.SourceRelativePath,
                reviewed.SourceRelativePath, StringComparison.Ordinal) ||
            !string.Equals(verified.SourceFingerprint,
                reviewed.SourceFingerprint, StringComparison.Ordinal) ||
            !string.Equals(verified.TargetFingerprint,
                reviewed.TargetFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Projet, objet, destinataire ou révisions modifiés : " +
                "préparez une nouvelle passation.");
        }

        ProjectHandoverIntentSnapshot current = await ReadAsync(source,
            cancellationToken);
        if (current.Ledger.ProjectId != verified.SourceProjectId ||
            current.Ledger.ProjectId != original.Ledger.ProjectId ||
            !string.Equals(current.SourceSha256, original.SourceSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Journal modifié depuis la lecture : rechargez les décisions.");
        }

        if (current.Ledger.Intents.Any(item =>
            item.SourceObjectId == verified.SourceObjectId &&
            item.TargetProjectId == verified.TargetProjectId &&
            item.Method == verified.Method))
        {
            throw new InvalidDataException(
                "Cette stratégie de passation est déjà préparée pour cet objet " +
                "et ce projet destinataire.");
        }

        ProjectHandoverIntent intention = new()
        {
            Id = Guid.NewGuid(),
            SourceProjectId = verified.SourceProjectId,
            TargetProjectId = verified.TargetProjectId,
            SourceObjectId = verified.SourceObjectId,
            SourceObjectKind = verified.SourceObjectKind,
            SourceReference = verified.SourceReference,
            SourceRelativePath = verified.SourceRelativePath,
            TargetProjectRelativePath = verified.TargetProjectRelativePath,
            Method = verified.Method,
            ProposedTargetObjectId = verified.ProposedTargetObjectId,
            SourceFingerprint = verified.SourceFingerprint,
            TargetFingerprint = verified.TargetFingerprint,
            Actor = actor.Trim(),
            Rationale = rationale.Trim(),
            RecordedUtc = DateTimeOffset.UtcNow
        };
        ProjectHandoverIntentLedger proposal = current.Ledger with
        {
            Intents = current.Ledger.Intents.Append(intention).ToArray()
        };
        string markdown = ProjectHandoverIntentCodec.Format(proposal);
        if (Encoding.UTF8.GetByteCount(markdown) > MaxBytes)
        {
            throw new InvalidDataException(
                "Journal de passation trop volumineux pour publication.");
        }

        // Recheck both identities before touching Management. If a project
        // was archived or physically moved, fail without recreating its path.
        (string activeSource, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest sourceManifest) =
            await _preflight.ResolveActiveProjectAsync(source, cancellationToken);
        (string activeTarget, string targetRelative,
            Nodalis.Core.Domain.ProjectManifest targetManifest) =
            await _preflight.ResolveActiveProjectAsync(target, cancellationToken);
        if (sourceManifest.Id != verified.SourceProjectId ||
            targetManifest.Id != verified.TargetProjectId ||
            !string.Equals(sourceRelative, reviewed.SourceProjectRelativePath,
                StringComparison.Ordinal) ||
            !string.Equals(targetRelative, reviewed.TargetProjectRelativePath,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Un des projets a changé depuis la préparation.");
        }

        string folder = Path.Combine(activeSource, "Management");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        RequireNormal(folder);
        string path = Path.Combine(folder, "Passations.md");
        if (current.SourceSha256 is null)
        {
            await AtomicFileWriter.WriteNewAllTextAsync(path, markdown,
                cancellationToken);
        }
        else
        {
            string live = _sources.ResolveExistingFile(
                _sources.GetReferenceForExistingFile(path));
            string stage = Path.Combine(folder,
                ".Passations." + Guid.NewGuid().ToString("N") + ".stage");
            try
            {
                await AtomicFileWriter.WriteNewAllTextAsync(stage,
                    markdown, cancellationToken);
                string hash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(markdown)));
                await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                    live, current.SourceSha256, stage, hash, cancellationToken);
            }
            finally
            {
                if (File.Exists(stage))
                {
                    File.Delete(stage);
                }
            }
        }

        return await ReadAsync(activeSource, cancellationToken);
    }

    /// <summary>Rejects symlinks and junctions in the intended journal folder.</summary>
    /// <param name="path">Existing folder.</param>
    private static void RequireNormal(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier de passation sous lien symbolique ou jonction.");
        }
    }
}
