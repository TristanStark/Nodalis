using Nodalis.Core.Projects;
using Nodalis.Core.Risks;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable replay of RIS A parking, publication and reverse restoration.
/// At most one active project owns the stable risk GUID at every checkpoint.
/// </summary>
public sealed partial class ProjectRiskMoveTransactionService
{
    /// <summary>
    /// Park original risk before publishing the new owner. An interrupted
    /// journal is replayed only if staged bytes and active owners are exact.
    /// </summary>
    /// <param name="directory">Approved transaction directory.</param>
    /// <param name="journal">Frozen prepared A journal.</param>
    /// <param name="token">Recovery cancellation.</param>
    /// <returns>Verified completed transaction.</returns>
    private async Task<ProjectRiskMoveJournal> PublishAsync(
        string directory, ProjectRiskMoveJournal journal,
        CancellationToken token)
    {
        if (journal.Phase != "prepared")
        {
            throw new InvalidDataException(
                "Journal RIS A non préparé pour publication.");
        }

        await CheckJournalAsync(directory, journal, token);
        (string sourceProject, string targetProject) =
            await ResolveOwnersAsync(journal, token);
        string source = CombineRelative(journal.SourceRelativePath);
        string target = CombineRelative(journal.TargetRelativePath);
        string parked = Path.Combine(directory, Parked);
        bool hasSource = Path.Exists(source);
        bool hasParked = Path.Exists(parked);
        if (hasSource && !hasParked && !Path.Exists(target))
        {
            await VerifyHashAsync(_sources.ResolveExistingFile(
                journal.SourceRelativePath), journal.OriginalSha256, token);
            ProjectRiskHandoverPreview fresh = await _previews.PrepareAsync(
                sourceProject, journal.IntentId, token);
            if (!fresh.CanPreview ||
                fresh.Intent.Method != ProjectHandoverMethod.MoveOriginal ||
                fresh.Intent.SourceProjectId != journal.SourceProjectId ||
                fresh.Intent.TargetProjectId != journal.TargetProjectId ||
                fresh.TargetId != journal.RiskId ||
                fresh.SourceSha256 != journal.OriginalSha256 ||
                fresh.TargetRelativePath != journal.TargetRelativePath ||
                fresh.MoveMarkdown is null ||
                Hash(fresh.MoveMarkdown) != journal.DestinationSha256)
            {
                throw new InvalidDataException(
                    "Risque ou projet modifié avant parking RIS A.");
            }

            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.SourceReference, journal.SourceRelativePath, token);
            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.TargetReference, journal.SourceRelativePath, token);
            token.ThrowIfCancellationRequested();
            File.Move(source, parked);
            try
            {
                await VerifyHashAsync(parked, journal.OriginalSha256,
                    CancellationToken.None);
            }
            catch
            {
                if (!Path.Exists(source))
                {
                    File.Move(parked, source);
                }

                throw;
            }

            _checkpoint?.Invoke("source-parked");
            hasSource = false;
            hasParked = true;
        }

        if (hasSource || !hasParked)
        {
            throw new InvalidDataException(
                "État RIS A ambigu : source, parking ou cible concurrents.");
        }

        await VerifyHashAsync(parked, journal.OriginalSha256, token);
        if (!Path.Exists(target))
        {
            // Incoming links created after parking still prevent publication.
            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.SourceReference, journal.SourceRelativePath, token);
            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.TargetReference, journal.SourceRelativePath, token);
            ProjectRiskCatalog catalog = await new ProjectRiskRegistryService(
                _root).ListAsync(targetProject, token);
            if (catalog.Diagnostics.Count > 0 ||
                catalog.NextReference != journal.TargetReference ||
                catalog.Risks.Any(item => item.Card.Id == journal.RiskId))
            {
                throw new InvalidDataException(
                    "Référence RIS A cible occupée ou registre ambigu : " +
                    "journal conservé pour reprise supervisée.");
            }

            string analysis = Path.Combine(targetProject, "Analyse");
            string risks = Path.Combine(analysis, "Risques");
            if (Path.Exists(analysis) && !Directory.Exists(analysis) ||
                Path.Exists(risks) && !Directory.Exists(risks))
            {
                throw new InvalidDataException(
                    "Répertoire RIS cible occupé par un fichier.");
            }

            Directory.CreateDirectory(analysis);
            Plain(analysis);
            Directory.CreateDirectory(risks);
            Plain(risks);
            token.ThrowIfCancellationRequested();
            await AtomicFileWriter.WriteNewAllTextAsync(target,
                await File.ReadAllTextAsync(
                    Path.Combine(directory, Proposed), token), token);
            _checkpoint?.Invoke("target-created");
        }

        await VerifyHashAsync(_sources.ResolveExistingFile(
            journal.TargetRelativePath), journal.DestinationSha256,
            CancellationToken.None);
        ProjectRiskCard moved = ProjectRiskMarkdownCodec.Parse(
            await File.ReadAllTextAsync(target, CancellationToken.None));
        if (moved.Id != journal.RiskId ||
            moved.ProjectId != journal.TargetProjectId ||
            moved.Reference != journal.TargetReference ||
            Path.Exists(source))
        {
            throw new InvalidDataException(
                "Risque RIS A publié avec un propriétaire ou GUID incorrect.");
        }

        ProjectRiskMoveJournal completed = journal with { Phase = "completed" };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), completed,
            CancellationToken.None);
        _checkpoint?.Invoke("completed");
        return completed;
    }

    /// <summary>
    /// Withdraw exact A recipient before restoring parked original bytes.
    /// Never overwrites either human-edited file, and retained snapshots
    /// permit recovery after interruption at either file boundary.
    /// </summary>
    /// <param name="directory">Known journal folder.</param>
    /// <param name="journal">Approved rollback-prepared journal.</param>
    /// <param name="token">Recovery cancellation.</param>
    /// <returns>Verified rolled-back transaction.</returns>
    private async Task<ProjectRiskMoveJournal> UndoAsync(
        string directory, ProjectRiskMoveJournal journal,
        CancellationToken token)
    {
        if (journal.Phase != "rollback-prepared")
        {
            throw new InvalidDataException(
                "Restauration RIS A non préparée par l'utilisateur.");
        }

        await CheckJournalAsync(directory, journal, token);
        await ResolveOwnersAsync(journal, token);
        string source = CombineRelative(journal.SourceRelativePath);
        string target = CombineRelative(journal.TargetRelativePath);
        string parked = Path.Combine(directory, Parked);
        string withdrawn = Path.Combine(directory, Withdrawn);
        if (Path.Exists(target) && !Path.Exists(withdrawn) &&
            !Path.Exists(source) && Path.Exists(parked))
        {
            await VerifyHashAsync(_sources.ResolveExistingFile(
                journal.TargetRelativePath), journal.DestinationSha256, token);
            await VerifyHashAsync(parked, journal.OriginalSha256, token);
            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.TargetReference, journal.TargetRelativePath, token);
            token.ThrowIfCancellationRequested();
            File.Move(target, withdrawn);
            try
            {
                await VerifyHashAsync(withdrawn, journal.DestinationSha256,
                    CancellationToken.None);
            }
            catch
            {
                if (!Path.Exists(target))
                {
                    File.Move(withdrawn, target);
                }

                throw;
            }

            _checkpoint?.Invoke("target-withdrawn");
        }

        if (Path.Exists(target) || !Path.Exists(withdrawn))
        {
            throw new InvalidDataException(
                "Cible RIS A occupée ou sauvegarde de retrait absente.");
        }

        await VerifyHashAsync(withdrawn, journal.DestinationSha256, token);
        if (Path.Exists(parked) && !Path.Exists(source))
        {
            await VerifyHashAsync(parked, journal.OriginalSha256, token);
            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.TargetReference, journal.TargetRelativePath, token);
            await _guard.AssertNoIncomingAsync(journal.RiskId,
                journal.SourceReference, journal.SourceRelativePath, token);
            token.ThrowIfCancellationRequested();
            File.Move(parked, source);
            _checkpoint?.Invoke("source-restored");
        }

        if (!Path.Exists(source) || Path.Exists(parked))
        {
            throw new InvalidDataException(
                "Restauration RIS A incomplète : intervention nécessaire.");
        }

        await VerifyHashAsync(_sources.ResolveExistingFile(
            journal.SourceRelativePath), journal.OriginalSha256,
            CancellationToken.None);
        ProjectRiskMoveJournal restored = journal with
        {
            Phase = "rolled-back"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), restored,
            CancellationToken.None);
        _checkpoint?.Invoke("rolled-back");
        return restored;
    }

    /// <summary>Rechecks active project manifests, exact paths and owners.</summary>
    /// <param name="journal">Frozen durable transaction.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Current physical project roots.</returns>
    private async Task<(string Source, string Target)> ResolveOwnersAsync(
        ProjectRiskMoveJournal journal, CancellationToken token)
    {
        Validate(journal);
        string source = ProjectRoot(journal.SourceRelativePath);
        string target = ProjectRoot(journal.TargetRelativePath);
        (string sourceRoot, string sourceRelative,
            Nodalis.Core.Domain.ProjectManifest owner) =
            await _preflight.ResolveActiveProjectAsync(source, token);
        (string targetRoot, string targetRelative,
            Nodalis.Core.Domain.ProjectManifest recipient) =
            await _preflight.ResolveActiveProjectAsync(target, token);
        if (owner.Id != journal.SourceProjectId ||
            recipient.Id != journal.TargetProjectId ||
            owner.Id == recipient.Id ||
            journal.SourceRelativePath != sourceRelative +
                "/Analyse/Risques/" + journal.SourceReference + ".md" ||
            journal.TargetRelativePath != targetRelative +
                "/Analyse/Risques/" + journal.TargetReference + ".md")
        {
            throw new InvalidDataException(
                "Projet ou chemin RIS A incompatible avec la passation.");
        }

        return (sourceRoot, targetRoot);
    }

    /// <summary>Validates journal data and exact original/proposed Markdown.</summary>
    /// <param name="directory">Existing journal directory.</param>
    /// <param name="journal">Expected approved immutable values.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Verified snapshots.</returns>
    private async Task CheckJournalAsync(string directory,
        ProjectRiskMoveJournal journal, CancellationToken token)
    {
        Plain(directory);
        ProjectRiskMoveJournal stored =
            await ReadJournalAsync(directory, token);
        if (stored != journal)
        {
            throw new InvalidDataException(
                "Journal de déplacement RIS A modifié pendant l'opération.");
        }

        string original = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, Original)));
        string proposed = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, Proposed)));
        await VerifyHashAsync(original, journal.OriginalSha256, token);
        await VerifyHashAsync(proposed, journal.DestinationSha256, token);
        string originalMarkdown = await File.ReadAllTextAsync(original, token);
        string proposedMarkdown = await File.ReadAllTextAsync(proposed, token);
        ProjectRiskCard source = ProjectRiskMarkdownCodec.Parse(originalMarkdown);
        ProjectRiskCard target = ProjectRiskMarkdownCodec.Parse(proposedMarkdown);
        if (source.Id != journal.RiskId ||
            target.Id != journal.RiskId ||
            source.ProjectId != journal.SourceProjectId ||
            target.ProjectId != journal.TargetProjectId ||
            source.Reference != journal.SourceReference ||
            target.Reference != journal.TargetReference ||
            ProjectRiskMoveDraft.Prepare(originalMarkdown,
                journal.TargetProjectId, journal.TargetReference) !=
            proposedMarkdown)
        {
            throw new InvalidDataException(
                "Snapshots RIS A modifiés ou historique non préservé.");
        }
    }

    /// <summary>Reads bounded, direct transaction directories and journals.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Verified stable journal sequence.</returns>
    private async Task<IReadOnlyList<(string Directory,
        ProjectRiskMoveJournal Journal)>> ReadJournalsAsync(
        CancellationToken token)
    {
        if (!Directory.Exists(_transactions))
        {
            return [];
        }

        Plain(_transactions);
        string[] directories = Directory.GetDirectories(_transactions);
        if (directories.Length > 2000)
        {
            throw new InvalidDataException(
                "Nombre de journaux RIS A non inspectable.");
        }

        List<(string Directory, ProjectRiskMoveJournal Journal)> all = [];
        foreach (string directory in directories.OrderBy(item => item,
            StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            Plain(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N",
                out Guid id))
            {
                throw new InvalidDataException(
                    "Dossier RIS A inconnu dans le journal.");
            }

            if (!File.Exists(Path.Combine(directory, JournalName)))
            {
                // A journal must exist before any source file is moved.
                continue;
            }

            ProjectRiskMoveJournal journal =
                await ReadJournalAsync(directory, token);
            if (journal.TransactionId != id)
            {
                throw new InvalidDataException(
                    "Identité journal RIS A et dossier incompatibles.");
            }

            all.Add((directory, journal));
        }

        return all;
    }

    /// <summary>Reads one size-limited, strongly validated JSON journal.</summary>
    /// <param name="directory">Known nonlinked journal directory.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Validated persistent A metadata.</returns>
    private async Task<ProjectRiskMoveJournal> ReadJournalAsync(
        string directory, CancellationToken token)
    {
        Plain(directory);
        string file = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, JournalName)));
        if (new FileInfo(file).Length > 16384)
        {
            throw new InvalidDataException(
                "Journal RIS A trop volumineux.");
        }

        ProjectRiskMoveJournal journal =
            await AtomicJsonFile.ReadAsync<ProjectRiskMoveJournal>(
                file, token);
        Validate(journal);
        return journal;
    }

    /// <summary>Rejects unknown journal states, paths and identities.</summary>
    /// <param name="journal">Untrusted disk journal.</param>
    private static void Validate(ProjectRiskMoveJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.TransactionId == Guid.Empty ||
            journal.IntentId == Guid.Empty ||
            journal.RiskId == Guid.Empty ||
            journal.SourceProjectId == Guid.Empty ||
            journal.TargetProjectId == Guid.Empty ||
            journal.SourceProjectId == journal.TargetProjectId ||
            !ProjectRiskMarkdownCodec.IsReference(journal.SourceReference) ||
            !ProjectRiskMarkdownCodec.IsReference(journal.TargetReference) ||
            !ProjectJourneySourcePath.IsSafe(journal.SourceRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(journal.TargetRelativePath) ||
            !journal.SourceRelativePath.EndsWith(
                "/Analyse/Risques/" + journal.SourceReference + ".md",
                StringComparison.Ordinal) ||
            !journal.TargetRelativePath.EndsWith(
                "/Analyse/Risques/" + journal.TargetReference + ".md",
                StringComparison.Ordinal) ||
            !ValidHash(journal.OriginalSha256) ||
            !ValidHash(journal.DestinationSha256) ||
            journal.CreatedUtc == default ||
            journal.Phase is not
                ("prepared" or "completed" or
                 "rollback-prepared" or "rolled-back"))
        {
            throw new InvalidDataException(
                "Journal RIS A invalide, reprise bloquée.");
        }
    }

    /// <summary>Checks persisted SHA-256 format.</summary>
    /// <param name="hash">Untrusted digest.</param>
    /// <returns>True for one 32-byte digest.</returns>
    private static bool ValidHash(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
