using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Domain;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// One verified read of a canonical closure report; null SHA means the
/// project has not opted into closure reviews yet.
/// </summary>
/// <param name="Report">Current history, never a second editable business register.</param>
/// <param name="SourceSha256">Exact revision or null before first creation.</param>
public sealed record ProjectClosureReportSnapshot(
    ProjectClosureReport Report, string? SourceSha256);

/// <summary>
/// Append-only closure reviews backed by a project-local Markdown document.
/// All writes are explicit, revision checked and confined to one project.
/// </summary>
public sealed class ProjectClosureReportService
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;
    private readonly ProjectClosureAuditService _audit;

    /// <summary>Creates a scoped, no-background-work closure report service.</summary>
    /// <param name="workspaceRoot">Single active Nodalis workspace.</param>
    public ProjectClosureReportService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
        _audit = new ProjectClosureAuditService(_workspaceRoot);
    }

    /// <summary>
    /// Reads only the expected canonical Markdown path, never creates any
    /// folders or silently converts an existing human-authored report.
    /// </summary>
    /// <param name="projectDirectory">Canonical owning project.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Existing history and byte SHA, or an empty unsaved history.</returns>
    public async Task<ProjectClosureReportSnapshot> ReadAsync(
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        (string directory, ProjectManifest manifest, bool archived) =
            await ResolveAsync(projectDirectory, cancellationToken);
        string folder = Path.Combine(directory, "Management");
        string path = Path.Combine(folder, "Bilan de clôture.md");
        if (Directory.Exists(folder))
        {
            RequireNormal(folder);
        }

        if (!File.Exists(path))
        {
            if (Path.Exists(path))
            {
                throw new InvalidDataException(
                    "Chemin du bilan inaccessible ou non régulier.");
            }

            return new ProjectClosureReportSnapshot(
                new ProjectClosureReport { ProjectId = manifest.Id }, null);
        }

        string verified = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(path));
        FileInfo info = new(verified);
        if (info.Length > MaxBytes)
        {
            throw new InvalidDataException("Bilan de clôture trop volumineux.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(verified, cancellationToken);
        string markdown = new UTF8Encoding(false, true).GetString(bytes);
        ProjectClosureReport report = ProjectClosureReportCodec.Parse(markdown);
        if (report.ProjectId != manifest.Id)
        {
            throw new InvalidDataException(
                "Bilan rattaché à un autre projet.");
        }

        return new ProjectClosureReportSnapshot(report,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }

    /// <summary>
    /// Records a human review of a fresh, source-pinned pre-audit. The report
    /// remains a review, not the archive move or a transfer authorization.
    /// </summary>
    /// <param name="projectDirectory">Active project with canonical manifest.</param>
    /// <param name="snapshot">Revision read before opening the review UI.</param>
    /// <param name="audit">Exact reviewed audit before requesting approval.</param>
    /// <param name="outcome">Explicit human review outcome.</param>
    /// <param name="actor">Human reviewer.</param>
    /// <param name="rationale">Human rationale, required even with open items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New validated append-only revision.</returns>
    public async Task<ProjectClosureReportSnapshot> RecordAsync(
        string projectDirectory, ProjectClosureReportSnapshot snapshot,
        ProjectClosureAuditSnapshot audit, ProjectClosureReviewOutcome outcome,
        string actor, string rationale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(audit);
        (string directory, ProjectManifest manifest, bool archived) =
            await ResolveAsync(projectDirectory, cancellationToken);
        if (archived)
        {
            throw new InvalidOperationException(
                "Bilan de clôture archivé : lecture seule.");
        }

        if (manifest.Id != audit.ProjectId ||
            manifest.Id != snapshot.Report.ProjectId ||
            audit.SourceFingerprint is not { Length: 64 } ||
            !audit.SourceFingerprint.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Pré-audit ou bilan associé à un autre projet.");
        }

        ProjectClosureReportSnapshot latest =
            await ReadAsync(directory, cancellationToken);
        if (!string.Equals(latest.SourceSha256, snapshot.SourceSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Bilan modifié depuis la lecture : actualisez les revues.");
        }

        ProjectClosureAuditSnapshot current =
            await _audit.ReadAsync(directory, cancellationToken);
        if (current.ProjectId != audit.ProjectId ||
            !string.Equals(current.SourceFingerprint, audit.SourceFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Éléments du projet modifiés depuis le pré-audit : " +
                "une nouvelle revue est nécessaire.");
        }

        ProjectClosureReview review = new()
        {
            Id = Guid.NewGuid(),
            Number = latest.Report.Reviews.Count + 1,
            RecordedUtc = DateTimeOffset.UtcNow,
            Actor = actor,
            Rationale = rationale,
            Outcome = outcome,
            SourceFingerprint = current.SourceFingerprint,
            Items = current.Items.Select(item => new ProjectClosureReviewItem(
                item.Kind, item.Id, item.Reference, item.Title, item.Detail,
                item.RelativePath)).ToArray(),
            Diagnostics = current.Diagnostics.ToArray()
        };
        ProjectClosureReport proposed = latest.Report with
        {
            Reviews = latest.Report.Reviews.Append(review).ToArray()
        };
        string markdown = ProjectClosureReportCodec.Format(proposed);
        string folder = Path.Combine(directory, "Management");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        RequireNormal(folder);
        string path = Path.Combine(folder, "Bilan de clôture.md");
        if (latest.SourceSha256 is null)
        {
            await AtomicFileWriter.WriteNewAllTextAsync(
                path, markdown, cancellationToken);
        }
        else
        {
            string verified = _sources.ResolveExistingFile(
                _sources.GetReferenceForExistingFile(path));
            string stage = Path.Combine(folder,
                ".Bilan de clôture." + Guid.NewGuid().ToString("N") + ".stage");
            try
            {
                await AtomicFileWriter.WriteNewAllTextAsync(
                    stage, markdown, cancellationToken);
                string proposedHash = Convert.ToHexString(SHA256.HashData(
                    Encoding.UTF8.GetBytes(markdown)));
                await AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
                    verified, latest.SourceSha256, stage,
                    proposedHash, cancellationToken);
            }
            finally
            {
                if (File.Exists(stage))
                {
                    File.Delete(stage);
                }
            }
        }

        return await ReadAsync(directory, cancellationToken);
    }

    /// <summary>Verifies the canonical project identity and archive state.</summary>
    /// <param name="projectDirectory">Existing directory under this workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Safe physical project root, manifest and archived flag.</returns>
    private async Task<(string Directory, ProjectManifest Manifest, bool Archived)>
        ResolveAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        string directory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string relative = Path.GetRelativePath(_workspaceRoot, directory);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bilan hors workspace.");
        }

        string[] parts = relative.Split(Path.DirectorySeparatorChar);
        bool activeProjectPath = parts.Length >= 4 &&
            string.Equals(parts[0], WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase);
        bool archivedProjectPath = parts.Length >= 2 &&
            string.Equals(parts[0], WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase);
        if (!activeProjectPath && !archivedProjectPath)
        {
            throw new InvalidDataException("Projet de clôture non canonique.");
        }

        string path = Path.Combine(directory,
            WorkspaceLayout.ProjectManifestFileName);
        _sources.GetReferenceForExistingFile(path);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, cancellationToken);
        if (manifest.Id == Guid.Empty)
        {
            throw new InvalidDataException(
                "Projet de clôture sans identité persistante.");
        }

        bool archived = manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null ||
            string.Equals(parts[0], WorkspaceLayout.ArchivesDirectoryName,
                StringComparison.OrdinalIgnoreCase);
        return (directory, manifest, archived);
    }

    /// <summary>Rejects directory junctions and symlinks on the canonical path.</summary>
    /// <param name="path">Existing intended source folder.</param>
    private static void RequireNormal(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier Management sous lien symbolique ou jonction.");
        }
    }
}
