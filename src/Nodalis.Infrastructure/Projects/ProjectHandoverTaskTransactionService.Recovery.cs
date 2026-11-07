using System.Security.Cryptography;
using Nodalis.Core.Projects;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Recovery half of the task-only handover engine. The real current file
/// hashes, not the last logged phase, determine all permitted replay steps.
/// </summary>
public sealed partial class ProjectHandoverTaskTransactionService
{
    /// <summary>
    /// Completes or restores an already snapshotted transaction with CAS.
    /// Only original or exactly published hashes are accepted, so a human
    /// edit is never overwritten during recovery or rollback.
    /// </summary>
    /// <param name="directory">Verified private transaction folder.</param>
    /// <param name="journal">Expected transaction checkpoint.</param>
    /// <param name="publish">True to complete, false to undo.</param>
    /// <param name="cancellationToken">Cancellation of replay.</param>
    /// <returns>Final durable journal snapshot.</returns>
    private async Task<ProjectHandoverTaskJournal> ReplayAsync(
        string directory, ProjectHandoverTaskJournal journal,
        bool publish, CancellationToken cancellationToken)
    {
        ValidateJournal(journal);
        EnsurePlain(directory);
        string journalFile = Path.Combine(directory, JournalName);
        ProjectHandoverTaskJournal persisted =
            await ReadJournalAsync(directory, cancellationToken);
        if (persisted.TransactionId != journal.TransactionId ||
            persisted.IntentId != journal.IntentId ||
            persisted.Method != journal.Method ||
            persisted.Phase != journal.Phase)
        {
            throw new InvalidDataException(
                "Journal de transaction modifié depuis la préparation.");
        }

        (string sourceDir, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest sourceOwner) =
            await _preflight.ResolveActiveProjectAsync(
                ResolveRelative(journal.SourceProjectId,
                    journal.SourceProjectRelativePath, parentDocument: false),
                cancellationToken);
        (string targetDir, string targetRelative,
            Nodalis.Core.Domain.ProjectManifest targetOwner) =
            await _preflight.ResolveActiveProjectAsync(
                ResolveRelative(journal.TargetProjectId,
                    journal.TargetProjectRelativePath, parentDocument: false),
                cancellationToken);
        if (sourceOwner.Id != journal.SourceProjectId ||
            targetOwner.Id != journal.TargetProjectId ||
            sourceRelative != journal.SourceProjectRelativePath ||
            targetRelative != journal.TargetProjectRelativePath ||
            !string.Equals(sourceDir, Path.GetDirectoryName(
                _sources.ResolveExistingFile(journal.SourceDocumentRelativePath)),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(targetDir, Path.GetDirectoryName(
                _sources.ResolveExistingFile(journal.TargetDocumentRelativePath)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Propriétaires ou documents déplacés depuis le journal.");
        }

        string sourcePath = _sources.ResolveExistingFile(
            journal.SourceDocumentRelativePath);
        string targetPath = _sources.ResolveExistingFile(
            journal.TargetDocumentRelativePath);
        string originalSource = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, OriginalSource)));
        string originalTarget = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, OriginalTarget)));
        string proposedSource = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, ProposedSource)));
        string proposedTarget = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, ProposedTarget)));

        await RequireHashAsync(originalSource, journal.OriginalSourceHash,
            cancellationToken);
        await RequireHashAsync(originalTarget, journal.OriginalTargetHash,
            cancellationToken);
        await RequireHashAsync(proposedSource, journal.PublishedSourceHash,
            cancellationToken);
        await RequireHashAsync(proposedTarget, journal.PublishedTargetHash,
            cancellationToken);

        string currentSource = await FileHashAsync(sourcePath, cancellationToken);
        string currentTarget = await FileHashAsync(targetPath, cancellationToken);
        bool sourceOriginal = currentSource == journal.OriginalSourceHash;
        bool sourcePublished = currentSource == journal.PublishedSourceHash;
        bool targetOriginal = currentTarget == journal.OriginalTargetHash;
        bool targetPublished = currentTarget == journal.PublishedTargetHash;
        if ((!sourceOriginal && !sourcePublished) ||
            (!targetOriginal && !targetPublished))
        {
            throw new InvalidDataException(
                "Édition extérieure depuis la passation : ni reprise ni " +
                "restauration automatique autorisée.");
        }

        // For A, publish source removal before target insertion so there is
        // never a window with the same GUID active in two projects. For B,
        // publish successor first and provenance in original second.
        bool firstIsSource = publish
            ? journal.Method == ProjectHandoverMethod.MoveOriginal
            : journal.Method == ProjectHandoverMethod.LinkedSuccessor;
        bool firstIsDone = firstIsSource
            ? (publish ? sourcePublished : sourceOriginal)
            : (publish ? targetPublished : targetOriginal);
        bool secondIsDone = firstIsSource
            ? (publish ? targetPublished : targetOriginal)
            : (publish ? sourcePublished : sourceOriginal);
        if (!firstIsDone && secondIsDone)
        {
            throw new InvalidDataException(
                "Ordre de publication incohérent : transfert potentiellement " +
                "dupliqué ou incomplet. Diagnostic manuel requis.");
        }

        if (!firstIsDone)
        {
            if (firstIsSource)
            {
                await ReplaceCheckedAsync(sourcePath,
                    publish ? journal.OriginalSourceHash :
                        journal.PublishedSourceHash,
                    publish ? proposedSource : originalSource,
                    publish ? journal.PublishedSourceHash :
                        journal.OriginalSourceHash, cancellationToken);
            }
            else
            {
                await ReplaceCheckedAsync(targetPath,
                    publish ? journal.OriginalTargetHash :
                        journal.PublishedTargetHash,
                    publish ? proposedTarget : originalTarget,
                    publish ? journal.PublishedTargetHash :
                        journal.OriginalTargetHash, cancellationToken);
            }

            _checkpoint?.Invoke("first-published");
            if (publish)
            {
                journal = journal with { Phase = "first-published" };
                await AtomicJsonFile.WriteAsync(journalFile, journal,
                    CancellationToken.None);
            }
        }

        if (!secondIsDone)
        {
            if (firstIsSource)
            {
                await ReplaceCheckedAsync(targetPath,
                    publish ? journal.OriginalTargetHash :
                        journal.PublishedTargetHash,
                    publish ? proposedTarget : originalTarget,
                    publish ? journal.PublishedTargetHash :
                        journal.OriginalTargetHash, cancellationToken);
            }
            else
            {
                await ReplaceCheckedAsync(sourcePath,
                    publish ? journal.OriginalSourceHash :
                        journal.PublishedSourceHash,
                    publish ? proposedSource : originalSource,
                    publish ? journal.PublishedSourceHash :
                        journal.OriginalSourceHash, cancellationToken);
            }

            _checkpoint?.Invoke("second-published");
        }

        await RequireHashAsync(sourcePath,
            publish ? journal.PublishedSourceHash : journal.OriginalSourceHash,
            CancellationToken.None);
        await RequireHashAsync(targetPath,
            publish ? journal.PublishedTargetHash : journal.OriginalTargetHash,
            CancellationToken.None);
        journal = journal with
        {
            Phase = publish ? "completed" : "rolled-back"
        };
        await AtomicJsonFile.WriteAsync(
            journalFile, journal, CancellationToken.None);
        _checkpoint?.Invoke(journal.Phase);
        return journal;
    }

    /// <summary>
    /// Applies one same-volume staged replacement only if the byte-exact
    /// approved live original remains present at its expected path.
    /// </summary>
    /// <param name="live">Existing source or target task file.</param>
    /// <param name="expected">Exact current SHA-256.</param>
    /// <param name="staged">Immutable validated replacement snapshot.</param>
    /// <param name="replacementHash">Exact staged SHA-256.</param>
    /// <param name="cancellationToken">Cancellation of atomic publication.</param>
    /// <returns>Completed same-volume CAS replacement.</returns>
    private static Task ReplaceCheckedAsync(
        string live, string expected, string staged,
        string replacementHash, CancellationToken cancellationToken) =>
        AtomicFileWriter.ReplaceSnapshotIfUnchangedAsync(
            live, expected, staged, replacementHash, cancellationToken);

    /// <summary>
    /// Lists only direct, plain transaction directories. An orphan without a
    /// journal means no publication could have begun; no source is touched.
    /// </summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Deterministically ordered known journal folders.</returns>
    private async Task<IReadOnlyList<(string Directory,
        ProjectHandoverTaskJournal Journal)>> ReadJournalsAsync(
            CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_transactions))
        {
            return [];
        }

        EnsurePlain(_transactions);
        string[] directories = Directory.GetDirectories(_transactions);
        if (directories.Length > 2000)
        {
            throw new InvalidDataException(
                "Trop de passations historisées : inspection requise.");
        }

        List<(string Directory, ProjectHandoverTaskJournal Journal)> list = [];
        foreach (string directory in directories.OrderBy(path => path,
            StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsurePlain(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory),
                "N", out Guid id))
            {
                throw new InvalidDataException(
                    "Dossier de transaction de passation non reconnu.");
            }

            if (!File.Exists(Path.Combine(directory, JournalName)))
            {
                // A crash during snapshot staging cannot have published
                // any live document because the journal comes first.
                continue;
            }

            ProjectHandoverTaskJournal journal =
                await ReadJournalAsync(directory, cancellationToken);
            if (journal.TransactionId != id)
            {
                throw new InvalidDataException(
                    "Journal de passation associé à un autre dossier.");
            }

            list.Add((directory, journal));
        }

        return list;
    }

    /// <summary>
    /// Reads one strictly validated, immutable-metadata journal. Dynamic
    /// checkpoints may change, but no path or identity is trusted unchecked.
    /// </summary>
    /// <param name="directory">Existing transaction directory.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Validated transaction journal.</returns>
    private async Task<ProjectHandoverTaskJournal> ReadJournalAsync(
        string directory, CancellationToken cancellationToken)
    {
        EnsurePlain(directory);
        string path = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, JournalName)));
        if (new FileInfo(path).Length > 32768)
        {
            throw new InvalidDataException(
                "Journal transactionnel anormalement volumineux.");
        }

        ProjectHandoverTaskJournal journal =
            await AtomicJsonFile.ReadAsync<ProjectHandoverTaskJournal>(
                path, cancellationToken);
        ValidateJournal(journal);
        return journal;
    }

    /// <summary>Rejects unrecognized paths, hashes, states and ownership.</summary>
    /// <param name="journal">Untrusted persisted journal.</param>
    private static void ValidateJournal(ProjectHandoverTaskJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.TransactionId == Guid.Empty ||
            journal.IntentId == Guid.Empty ||
            journal.SourceProjectId == Guid.Empty ||
            journal.TargetProjectId == Guid.Empty ||
            journal.SourceProjectId == journal.TargetProjectId ||
            journal.TaskId == Guid.Empty || journal.TargetTaskId == Guid.Empty ||
            !Enum.IsDefined(journal.Method) ||
            (journal.Method == ProjectHandoverMethod.MoveOriginal &&
             journal.TargetTaskId != journal.TaskId) ||
            (journal.Method == ProjectHandoverMethod.LinkedSuccessor &&
             journal.TargetTaskId == journal.TaskId) ||
            !ProjectJourneySourcePath.IsSafe(
                journal.SourceProjectRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(
                journal.TargetProjectRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(
                journal.SourceDocumentRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(
                journal.TargetDocumentRelativePath) ||
            journal.SourceDocumentRelativePath !=
                journal.SourceProjectRelativePath + "/Tâches.md" ||
            journal.TargetDocumentRelativePath !=
                journal.TargetProjectRelativePath + "/Tâches.md" ||
            !ValidHash(journal.OriginalSourceHash) ||
            !ValidHash(journal.OriginalTargetHash) ||
            !ValidHash(journal.PublishedSourceHash) ||
            !ValidHash(journal.PublishedTargetHash) ||
            journal.Phase is not
                ("prepared" or "first-published" or "completed" or
                 "rollback-prepared" or "rolled-back"))
        {
            throw new InvalidDataException(
                "Journal de transaction de passation invalide.");
        }
    }

    /// <summary>Checks that the original or snapshot has the expected hash.</summary>
    /// <param name="path">Existing file.</param>
    /// <param name="expected">Exact SHA-256.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified immutable staging content.</returns>
    private static async Task RequireHashAsync(
        string path, string expected, CancellationToken token)
    {
        if (!string.Equals(await FileHashAsync(path, token), expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Empreinte de sauvegarde ou document de tâche inattendue.");
        }
    }

    /// <summary>Computes the SHA-256 of an existing exact file.</summary>
    /// <param name="path">File on disk.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Exact SHA-256 uppercase.</returns>
    private static async Task<string> FileHashAsync(
        string path, CancellationToken token)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024)
        {
            throw new InvalidDataException(
                "Document transactionnel trop volumineux.");
        }

        await using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 65536, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    /// <summary>Tests a canonical uppercase SHA-256 value.</summary>
    /// <param name="hash">Hex string.</param>
    /// <returns>True only for expected SHA-256 hex.</returns>
    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
