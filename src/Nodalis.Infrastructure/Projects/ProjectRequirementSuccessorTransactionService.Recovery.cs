using System.Security.Cryptography;
using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Completion and rollback for journaled EXI B successors. The journal phase
/// never substitutes for byte-level verification of the actual target file.
/// </summary>
public sealed partial class ProjectRequirementSuccessorTransactionService
{
    /// <summary>
    /// Finishes a previously reviewed CreateNew publication, or recognizes
    /// an exact committed file after an interrupted journal write.
    /// </summary>
    /// <param name="directory">Validated transaction folder.</param>
    /// <param name="journal">Frozen approved B transaction.</param>
    /// <param name="token">Recovery cancellation.</param>
    /// <returns>Verified completed transaction.</returns>
    private async Task<ProjectRequirementSuccessorJournal> PublishAsync(
        string directory, ProjectRequirementSuccessorJournal journal,
        CancellationToken token)
    {
        Validate(journal);
        RequirePlain(directory);
        if (journal.Phase is "rollback-prepared" or "rolled-back")
        {
            throw new InvalidOperationException(
                "Une restauration EXI ne peut pas relancer une création.");
        }

        ProjectRequirementSuccessorJournal stored =
            await ReadJournalAsync(directory, token);
        if (stored != journal)
        {
            throw new InvalidDataException("Journal EXI changé pendant la reprise.");
        }

        string staged = ResolveExisting(_sources.GetReferenceForExistingFile(
            Path.Combine(directory, StagedName)));
        await RequireHashAsync(staged, journal.SuccessorSha256, token);
        ProjectRequirementCard successor =
            ProjectRequirementMarkdownCodec.Parse(
                await File.ReadAllTextAsync(staged, token));
        if (successor.Id != journal.SuccessorRequirementId ||
            successor.ProjectId != journal.TargetProjectId ||
            successor.Reference != journal.TargetReference)
        {
            throw new InvalidDataException(
                "Le snapshot préparé ne correspond pas au successeur EXI approuvé.");
        }

        (string source, string target, string destination) =
            await ResolveOwnersAsync(journal, token);
        if (Path.Exists(destination))
        {
            string verified = ResolveExisting(journal.TargetRelativePath);
            await RequireHashAsync(verified, journal.SuccessorSha256, token);
        }
        else
        {
            string existingSource = ResolveExisting(journal.SourceRelativePath);
            await RequireHashAsync(existingSource, journal.SourceSha256, token);
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_root)
                    .ListAsync(target, token);
            if (catalog.Diagnostics.Count != 0 ||
                catalog.NextReference != journal.TargetReference ||
                catalog.Requirements.Any(entry =>
                    entry.Card.Id == journal.SuccessorRequirementId))
            {
                throw new InvalidDataException(
                    "Registre destinataire modifié depuis le journal : " +
                    "publication EXI refusée.");
            }

            // No recursive creation outside the verified active project.
            string documentation = Path.Combine(target, "Documentation");
            if (Path.Exists(documentation) &&
                !Directory.Exists(documentation))
            {
                throw new InvalidDataException(
                    "Le dossier Documentation est un fichier.");
            }

            Directory.CreateDirectory(documentation);
            RequirePlain(documentation);
            string requirements = Path.Combine(documentation, "Exigences");
            if (Path.Exists(requirements) && !Directory.Exists(requirements))
            {
                throw new InvalidDataException(
                    "Le dossier Exigences est un fichier.");
            }

            Directory.CreateDirectory(requirements);
            RequirePlain(requirements);
            token.ThrowIfCancellationRequested();
            await AtomicFileWriter.WriteNewAllTextAsync(destination,
                await File.ReadAllTextAsync(staged, token), token);
            _checkpoint?.Invoke("target-created");
            // A crash here is safe: the prepared journal and the exact
            // committed file can be recognized on a later explicit recovery.
            await RequireHashAsync(ResolveExisting(journal.TargetRelativePath),
                journal.SuccessorSha256, CancellationToken.None);
        }

        ProjectRequirementSuccessorJournal completed = journal with
        {
            Phase = "completed"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), completed,
            CancellationToken.None);
        _checkpoint?.Invoke("completed");
        return completed;
    }

    /// <summary>
    /// Human-approved undo: relocate the untouched successor into this
    /// private journal directory. No source EXI is changed or destroyed.
    /// A concurrent edit blocks removal, and a post-move mismatch is restored
    /// without overwriting a new destination entry.
    /// </summary>
    /// <param name="directory">Existing transaction folder.</param>
    /// <param name="journal">Rollback-prepared durable journal.</param>
    /// <param name="token">Cancellation before filesystem change.</param>
    /// <returns>Durable rolled-back journal.</returns>
    private async Task<ProjectRequirementSuccessorJournal> WithdrawAsync(
        string directory, ProjectRequirementSuccessorJournal journal,
        CancellationToken token)
    {
        if (journal.Phase != "rollback-prepared")
        {
            throw new InvalidDataException(
                "Restauration EXI non préparée par l'utilisateur.");
        }

        ProjectRequirementSuccessorJournal stored =
            await ReadJournalAsync(directory, token);
        if (stored != journal)
        {
            throw new InvalidDataException(
                "Journal EXI modifié pendant la restauration.");
        }

        (string source, string target, string destination) =
            await ResolveOwnersAsync(journal, token);
        string withdrawn = Path.Combine(directory, RestoredName);
        RequirePlain(directory);
        if (Path.Exists(withdrawn))
        {
            if (Path.Exists(destination))
            {
                throw new InvalidDataException(
                    "Successeur restauré mais référence à nouveau occupée : " +
                    "aucune suppression.");
            }

            await RequireHashAsync(
                ResolveExisting(_sources.GetReferenceForExistingFile(withdrawn)),
                journal.SuccessorSha256, token);
        }
        else
        {
            string current = ResolveExisting(journal.TargetRelativePath);
            await RequireHashAsync(current, journal.SuccessorSha256, token);
            await new ProjectRequirementSuccessorBacklinkGuard(_root)
                .AssertNoLinksAsync(journal, target, token);
            token.ThrowIfCancellationRequested();
            File.Move(current, withdrawn);
            _checkpoint?.Invoke("target-withdrawn");

            // A concurrent file replacement between hash and Move cannot
            // silently be treated as an archived approved successor.
            try
            {
                await RequireHashAsync(withdrawn, journal.SuccessorSha256,
                    CancellationToken.None);
            }
            catch
            {
                if (!Path.Exists(destination))
                {
                    File.Move(withdrawn, destination);
                }

                throw;
            }
        }

        ProjectRequirementSuccessorJournal restored = journal with
        {
            Phase = "rolled-back"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), restored,
            CancellationToken.None);
        _checkpoint?.Invoke("rolled-back");
        return restored;
    }

    /// <summary>
    /// Revalidates both active project manifests and the exact canonical
    /// EXI destination before any write, including transaction recovery.
    /// </summary>
    /// <param name="journal">Frozen transaction with expected owners.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified owner roots and destination file location.</returns>
    private async Task<(string Source, string Target, string Destination)>
        ResolveOwnersAsync(ProjectRequirementSuccessorJournal journal,
            CancellationToken token)
    {
        Validate(journal);
        string sourceRoot = GetProjectDirectory(journal.SourceRelativePath);
        string targetRoot = GetProjectDirectory(journal.TargetRelativePath);
        (string source, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest original) =
            await _preflight.ResolveActiveProjectAsync(sourceRoot, token);
        (string target, string targetRelative,
            Nodalis.Core.Domain.ProjectManifest recipient) =
            await _preflight.ResolveActiveProjectAsync(targetRoot, token);
        if (original.Id != journal.SourceProjectId ||
            recipient.Id != journal.TargetProjectId ||
            sourceRelative == targetRelative ||
            journal.TargetRelativePath != targetRelative +
                "/Documentation/Exigences/" + journal.TargetReference + ".md")
        {
            throw new InvalidDataException(
                "Projets ou destination EXI incohérents avec le journal.");
        }

        return (source, target, CombineRelative(journal.TargetRelativePath));
    }

    /// <summary>Extracts the project root from canonical EXI document path.</summary>
    /// <param name="relative">Workspace-relative source.</param>
    /// <returns>Path to the exact owning project root.</returns>
    private string GetProjectDirectory(string relative)
    {
        if (!ProjectJourneySourcePath.IsSafe(relative) ||
            !relative.Contains("/Documentation/Exigences/",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Chemin EXI en dehors d'un projet canonique.");
        }

        string projectRelative = relative[
            ..relative.IndexOf("/Documentation/Exigences/",
                StringComparison.Ordinal)];
        return CombineRelative(projectRelative);
    }

    /// <summary>Reads strict journals from direct, nonlinked GUID folders.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Oldest-first immutable journal collection.</returns>
    private async Task<IReadOnlyList<(string Directory,
        ProjectRequirementSuccessorJournal Journal)>> ReadJournalsAsync(
        CancellationToken token)
    {
        if (!Directory.Exists(_transactions))
        {
            return [];
        }

        RequirePlain(_transactions);
        string[] directories = Directory.GetDirectories(_transactions);
        if (directories.Length > 2000)
        {
            throw new InvalidDataException(
                "Trop de journaux EXI pour une inspection fiable.");
        }

        List<(string Directory, ProjectRequirementSuccessorJournal Journal)> all = [];
        foreach (string directory in directories.OrderBy(item => item,
            StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            RequirePlain(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N",
                out Guid id))
            {
                throw new InvalidDataException("Dossier de transaction EXI inconnu.");
            }

            if (!File.Exists(Path.Combine(directory, JournalName)))
            {
                // No journal means that no publication was authorized yet.
                continue;
            }

            ProjectRequirementSuccessorJournal journal =
                await ReadJournalAsync(directory, token);
            if (journal.TransactionId != id)
            {
                throw new InvalidDataException(
                    "Identité de transaction EXI et dossier incompatibles.");
            }

            all.Add((directory, journal));
        }

        return all;
    }

    /// <summary>Reads and validates one durable schema and its size.</summary>
    /// <param name="directory">Existing nonlinked transaction folder.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Strict journal.</returns>
    private async Task<ProjectRequirementSuccessorJournal> ReadJournalAsync(
        string directory, CancellationToken token)
    {
        RequirePlain(directory);
        string path = ResolveExisting(_sources.GetReferenceForExistingFile(
            Path.Combine(directory, JournalName)));
        if (new FileInfo(path).Length > 32768)
        {
            throw new InvalidDataException("Journal EXI trop volumineux.");
        }

        ProjectRequirementSuccessorJournal journal =
            await AtomicJsonFile.ReadAsync<ProjectRequirementSuccessorJournal>(
                path, token);
        Validate(journal);
        return journal;
    }

    /// <summary>Rejects malformed identities, paths, phases and SHA values.</summary>
    /// <param name="journal">Loaded persisted transaction.</param>
    private static void Validate(ProjectRequirementSuccessorJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.TransactionId == Guid.Empty ||
            journal.IntentId == Guid.Empty ||
            journal.SourceProjectId == Guid.Empty ||
            journal.TargetProjectId == Guid.Empty ||
            journal.SourceProjectId == journal.TargetProjectId ||
            journal.SourceRequirementId == Guid.Empty ||
            journal.SuccessorRequirementId == Guid.Empty ||
            journal.SourceRequirementId == journal.SuccessorRequirementId ||
            !ProjectRequirementMarkdownCodec.IsReference(
                journal.TargetReference) ||
            !ProjectJourneySourcePath.IsSafe(journal.SourceRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(journal.TargetRelativePath) ||
            !journal.SourceRelativePath.Contains(
                "/Documentation/Exigences/", StringComparison.Ordinal) ||
            !journal.SourceRelativePath.EndsWith(".md",
                StringComparison.Ordinal) ||
            !journal.TargetRelativePath.EndsWith(
                "/Documentation/Exigences/" +
                journal.TargetReference + ".md", StringComparison.Ordinal) ||
            !ValidHash(journal.SourceSha256) ||
            !ValidHash(journal.SuccessorSha256) ||
            journal.Phase is not
                ("prepared" or "completed" or
                 "rollback-prepared" or "rolled-back"))
        {
            throw new InvalidDataException(
                "Journal de succession EXI invalide.");
        }
    }

    /// <summary>Requires an exact canonical SHA-256 hex token.</summary>
    /// <param name="sha">Untrusted digest.</param>
    /// <returns>True for a single SHA-256.</returns>
    private static bool ValidHash(string? sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
}
