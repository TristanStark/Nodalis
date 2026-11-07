using Nodalis.Core.Projects;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Crash replay of the two EXI A file moves. At every checkpoint the original
/// GUID has at most one active project owner; both staged snapshots survive.
/// </summary>
public sealed partial class ProjectRequirementMoveTransactionService
{
    /// <summary>
    /// Moves source into a private SHA-verified journal before publishing a
    /// different project-id and reference, preserving the original EXI GUID.
    /// </summary>
    /// <param name="directory">One durable transaction folder.</param>
    /// <param name="journal">Frozen, approved A journal.</param>
    /// <param name="token">Recovery cancellation.</param>
    /// <returns>Completed journal after exact replay.</returns>
    private async Task<ProjectRequirementMoveJournal> PublishAsync(
        string directory, ProjectRequirementMoveJournal journal,
        CancellationToken token)
    {
        if (journal.Phase != "prepared")
        {
            throw new InvalidDataException(
                "Transaction EXI A non préparée pour publication.");
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
            await VerifyHashAsync(
                _sources.ResolveExistingFile(journal.SourceRelativePath),
                journal.OriginalSha256, token);
            ProjectRequirementHandoverPreview fresh =
                await _previews.PrepareAsync(sourceProject, journal.IntentId, token);
            if (!fresh.CanPrepare ||
                fresh.Intent.Method != ProjectHandoverMethod.MoveOriginal ||
                fresh.Intent.SourceProjectId != journal.SourceProjectId ||
                fresh.Intent.TargetProjectId != journal.TargetProjectId ||
                fresh.ProposedTargetRelativePath != journal.TargetRelativePath ||
                fresh.SourceSha256 != journal.OriginalSha256 ||
                fresh.ProposedMarkdown is null ||
                Hash(fresh.ProposedMarkdown) != journal.DestinationSha256)
            {
                throw new InvalidDataException(
                    "Projet ou sources EXI modifiés avant parking de l'original.");
            }

            await _guard.AssertNoIncomingAsync(journal.RequirementId,
                journal.SourceReference, journal.SourceRelativePath, token);
            await _guard.AssertNoIncomingAsync(journal.RequirementId,
                journal.TargetReference, journal.SourceRelativePath, token);
            await new ProjectRequirementMoveCriteriaGuard(_root)
                .AssertNoIncomingAsync(journal.ReviewedCriteria,
                    journal.SourceRelativePath,
                    journal.TargetRelativePath, token);
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
                "État de transfert EXI A non récupérable sans revue manuelle.");
        }

        await VerifyHashAsync(parked, journal.OriginalSha256, token);
        if (!Path.Exists(target))
        {
            await new ProjectRequirementMoveCriteriaGuard(_root)
                .AssertNoIncomingAsync(journal.ReviewedCriteria,
                    journal.SourceRelativePath,
                    journal.TargetRelativePath, token);
            ProjectRequirementCatalog catalog =
                await new ProjectRequirementRegistryService(_root)
                    .ListAsync(targetProject, token);
            if (catalog.Diagnostics.Count != 0 ||
                catalog.NextReference != journal.TargetReference ||
                catalog.Requirements.Any(item =>
                    item.Card.Id == journal.RequirementId))
            {
                throw new InvalidDataException(
                    "Référence EXI cible désormais occupée : journal conservé " +
                    "pour reprise après correction humaine.");
            }

            string documentation = Path.Combine(targetProject, "Documentation");
            if (Path.Exists(documentation) &&
                !Directory.Exists(documentation))
            {
                throw new InvalidDataException(
                    "Documentation destinataire n'est pas un dossier.");
            }

            Directory.CreateDirectory(documentation);
            Plain(documentation);
            string requirements = Path.Combine(documentation, "Exigences");
            if (Path.Exists(requirements) &&
                !Directory.Exists(requirements))
            {
                throw new InvalidDataException(
                    "Exigences destinataire n'est pas un dossier.");
            }

            Directory.CreateDirectory(requirements);
            Plain(requirements);
            token.ThrowIfCancellationRequested();
            await AtomicFileWriter.WriteNewAllTextAsync(target,
                await File.ReadAllTextAsync(
                    Path.Combine(directory, Proposed), token), token);
            _checkpoint?.Invoke("target-created");
        }

        await VerifyHashAsync(
            _sources.ResolveExistingFile(journal.TargetRelativePath),
            journal.DestinationSha256, CancellationToken.None);
        ProjectRequirementCard committed =
            ProjectRequirementMarkdownCodec.Parse(
                await File.ReadAllTextAsync(target, CancellationToken.None));
        if (committed.Id != journal.RequirementId ||
            committed.ProjectId != journal.TargetProjectId ||
            committed.Reference != journal.TargetReference ||
            Path.Exists(source))
        {
            throw new InvalidDataException(
                "La fiche EXI publiée ne conserve pas l'identité approuvée.");
        }

        ProjectRequirementMoveJournal completed = journal with
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
    /// First removes the exact newly owned EXI, then restores the original
    /// untouched bytes to the original project. Both documents are retained
    /// in the transaction folder for permanent historical verification.
    /// </summary>
    /// <param name="directory">Verified transaction directory.</param>
    /// <param name="journal">Previously approved rollback journal.</param>
    /// <param name="token">Recovery cancellation.</param>
    /// <returns>Durable rolled-back journal.</returns>
    private async Task<ProjectRequirementMoveJournal> UndoAsync(
        string directory, ProjectRequirementMoveJournal journal,
        CancellationToken token)
    {
        if (journal.Phase != "rollback-prepared")
        {
            throw new InvalidDataException(
                "Restauration EXI A non approuvée.");
        }

        await CheckJournalAsync(directory, journal, token);
        (string sourceProject, string targetProject) =
            await ResolveOwnersAsync(journal, token);
        string source = CombineRelative(journal.SourceRelativePath);
        string target = CombineRelative(journal.TargetRelativePath);
        string parked = Path.Combine(directory, Parked);
        string withdrawn = Path.Combine(directory, Withdrawn);
        if (Path.Exists(target) && !Path.Exists(withdrawn) &&
            !Path.Exists(source) && Path.Exists(parked))
        {
            await VerifyHashAsync(
                _sources.ResolveExistingFile(journal.TargetRelativePath),
                journal.DestinationSha256, token);
            await VerifyHashAsync(parked, journal.OriginalSha256, token);
            await _guard.AssertNoIncomingAsync(journal.RequirementId,
                journal.TargetReference, journal.TargetRelativePath, token);
            await new ProjectRequirementMoveCriteriaGuard(_root)
                .AssertNoIncomingAsync(journal.ReviewedCriteria,
                    journal.SourceRelativePath,
                    journal.TargetRelativePath, token);
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
                "Une autre fiche EXI occupe la destination après rollback.");
        }

        await VerifyHashAsync(withdrawn, journal.DestinationSha256, token);
        if (Path.Exists(parked) && !Path.Exists(source))
        {
            await VerifyHashAsync(parked, journal.OriginalSha256, token);
            await _guard.AssertNoIncomingAsync(journal.RequirementId,
                journal.TargetReference, journal.TargetRelativePath, token);
            await _guard.AssertNoIncomingAsync(journal.RequirementId,
                journal.SourceReference, journal.SourceRelativePath, token);
            await new ProjectRequirementMoveCriteriaGuard(_root)
                .AssertNoIncomingAsync(journal.ReviewedCriteria,
                    journal.SourceRelativePath,
                    journal.TargetRelativePath, token);
            token.ThrowIfCancellationRequested();
            File.Move(parked, source);
            _checkpoint?.Invoke("source-restored");
        }

        if (!Path.Exists(source) || Path.Exists(parked))
        {
            throw new InvalidDataException(
                "Restauration EXI A incomplète : revue humaine requise.");
        }

        await VerifyHashAsync(
            _sources.ResolveExistingFile(journal.SourceRelativePath),
            journal.OriginalSha256, CancellationToken.None);
        ProjectRequirementMoveJournal restored = journal with
        {
            Phase = "rolled-back"
        };
        await AtomicJsonFile.WriteAsync(
            Path.Combine(directory, JournalName), restored,
            CancellationToken.None);
        _checkpoint?.Invoke("rolled-back");
        return restored;
    }

    /// <summary>Verifies active physical owners, paths and journal identity.</summary>
    /// <param name="journal">Frozen transaction identity.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Source and target project directory paths.</returns>
    private async Task<(string Source, string Target)> ResolveOwnersAsync(
        ProjectRequirementMoveJournal journal, CancellationToken token)
    {
        Validate(journal);
        string source = ProjectRootFromRelative(journal.SourceRelativePath);
        string target = ProjectRootFromRelative(journal.TargetRelativePath);
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
                "/Documentation/Exigences/" +
                journal.SourceReference + ".md" ||
            journal.TargetRelativePath != targetRelative +
                "/Documentation/Exigences/" +
                journal.TargetReference + ".md")
        {
            throw new InvalidDataException(
                "Propriétaires ou chemins EXI modifiés depuis l'approbation.");
        }

        return (sourceRoot, targetRoot);
    }

    /// <summary>Verifies journal and both staged files against durable hashes.</summary>
    /// <param name="directory">Nonlinked journal folder.</param>
    /// <param name="journal">Journal approved by the caller.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Verified immutable transaction snapshots.</returns>
    private async Task CheckJournalAsync(
        string directory, ProjectRequirementMoveJournal journal,
        CancellationToken token)
    {
        Plain(directory);
        ProjectRequirementMoveJournal saved =
            await ReadJournalAsync(directory, token);
        if (saved with { ReviewedCriteria = [] } !=
                journal with { ReviewedCriteria = [] } ||
            !saved.ReviewedCriteria.SequenceEqual(journal.ReviewedCriteria))
        {
            throw new InvalidDataException(
                "Journal ou revue CA EXI A modifié pendant la transaction.");
        }

        await VerifyHashAsync(
            Path.Combine(directory, Original), journal.OriginalSha256, token);
        await VerifyHashAsync(
            Path.Combine(directory, Proposed), journal.DestinationSha256, token);
        string originalMarkdown = await File.ReadAllTextAsync(
            Path.Combine(directory, Original), token);
        string proposedMarkdown = await File.ReadAllTextAsync(
            Path.Combine(directory, Proposed), token);
        ProjectRequirementCard before =
            ProjectRequirementMarkdownCodec.Parse(originalMarkdown);
        ProjectRequirementCard after =
            ProjectRequirementMarkdownCodec.Parse(proposedMarkdown);
        ProjectRequirementCriterionScan originalCriteria =
            ProjectRequirementCriterionCodec.Inspect(originalMarkdown);
        ProjectRequirementCriterionScan movedCriteria =
            ProjectRequirementCriterionCodec.Inspect(proposedMarkdown);
        ProjectRequirementReviewedCriterion[] originallyPresent =
            originalCriteria.Criteria.Select(item =>
                new ProjectRequirementReviewedCriterion(
                    item.Reference, item.Id ?? Guid.Empty)).ToArray();
        ProjectRequirementReviewedCriterion[] movedPresent =
            movedCriteria.Criteria.Select(item =>
                new ProjectRequirementReviewedCriterion(
                    item.Reference, item.Id ?? Guid.Empty)).ToArray();
        if (originalCriteria.Diagnostics.Count > 0 ||
            movedCriteria.Diagnostics.Count > 0 ||
            !originallyPresent.SequenceEqual(journal.ReviewedCriteria) ||
            !movedPresent.SequenceEqual(journal.ReviewedCriteria) ||
            !originalCriteria.Criteria.Select(item => item.Title).SequenceEqual(
                movedCriteria.Criteria.Select(item => item.Title)))
        {
            throw new InvalidDataException(
                "Critères d'acceptation modifiés ou non revus dans " +
                "les snapshots EXI A.");
        }
        if (before.Id != journal.RequirementId ||
            after.Id != journal.RequirementId ||
            before.ProjectId != journal.SourceProjectId ||
            after.ProjectId != journal.TargetProjectId ||
            before.Reference != journal.SourceReference ||
            after.Reference != journal.TargetReference)
        {
            throw new InvalidDataException(
                "Les snapshots EXI A ne respectent pas le GUID original.");
        }
    }

    /// <summary>Loads bounded, canonical, direct EXI A journal folders.</summary>
    /// <param name="token">Read cancellation.</param>
    /// <returns>Exactly validated journals and their directories.</returns>
    private async Task<IReadOnlyList<(string Directory,
        ProjectRequirementMoveJournal Journal)>> ReadJournalsAsync(
        CancellationToken token)
    {
        if (!Directory.Exists(_transactions))
        {
            return [];
        }

        Plain(_transactions);
        string[] folders = Directory.GetDirectories(_transactions);
        if (folders.Length > 2000)
        {
            throw new InvalidDataException(
                "Nombre de journaux EXI A trop élevé.");
        }

        List<(string Directory, ProjectRequirementMoveJournal Journal)> all = [];
        foreach (string folder in folders.OrderBy(item => item,
            StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            Plain(folder);
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out Guid id))
            {
                throw new InvalidDataException(
                    "Dossier de journal EXI A non canonique.");
            }

            if (!File.Exists(Path.Combine(folder, JournalName)))
            {
                continue;
            }

            ProjectRequirementMoveJournal journal =
                await ReadJournalAsync(folder, token);
            if (journal.TransactionId != id)
            {
                throw new InvalidDataException(
                    "Journal EXI A appartenant à une autre transaction.");
            }

            all.Add((folder, journal));
        }

        return all;
    }

    /// <summary>Reads a strict and size-limited durable EXI A journal.</summary>
    /// <param name="directory">Existing journal directory.</param>
    /// <param name="token">Read cancellation.</param>
    /// <returns>One validated journal.</returns>
    private async Task<ProjectRequirementMoveJournal> ReadJournalAsync(
        string directory, CancellationToken token)
    {
        Plain(directory);
        string file = _sources.ResolveExistingFile(
            _sources.GetReferenceForExistingFile(
                Path.Combine(directory, JournalName)));
        if (new FileInfo(file).Length > 32768)
        {
            throw new InvalidDataException(
                "Journal EXI A trop volumineux.");
        }

        ProjectRequirementMoveJournal journal =
            await AtomicJsonFile.ReadAsync<ProjectRequirementMoveJournal>(
                file, token);
        Validate(journal);
        return journal;
    }

    /// <summary>Rejects unknown phases, IDs, paths and cryptographic digests.</summary>
    /// <param name="journal">Untrusted stored journal.</param>
    private static void Validate(ProjectRequirementMoveJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (journal.TransactionId == Guid.Empty ||
            journal.IntentId == Guid.Empty ||
            journal.SourceProjectId == Guid.Empty ||
            journal.TargetProjectId == Guid.Empty ||
            journal.SourceProjectId == journal.TargetProjectId ||
            journal.RequirementId == Guid.Empty ||
            !ProjectJourneySourcePath.IsSafe(journal.SourceRelativePath) ||
            !ProjectJourneySourcePath.IsSafe(journal.TargetRelativePath) ||
            !ProjectRequirementMarkdownCodec.IsReference(
                journal.SourceReference) ||
            !ProjectRequirementMarkdownCodec.IsReference(
                journal.TargetReference) ||
            !journal.SourceRelativePath.EndsWith(
                "/Documentation/Exigences/" +
                journal.SourceReference + ".md", StringComparison.Ordinal) ||
            !journal.TargetRelativePath.EndsWith(
                "/Documentation/Exigences/" +
                journal.TargetReference + ".md", StringComparison.Ordinal) ||
            !ValidSha(journal.OriginalSha256) ||
            !ValidSha(journal.DestinationSha256) ||
            journal.ReviewedCriteria is null ||
            journal.ReviewedCriteria.Count > 99 ||
            journal.ReviewedCriteria.Any(item =>
                item.Id == Guid.Empty ||
                !ProjectRequirementCriterionCodec.IsReference(item.Reference)) ||
            journal.ReviewedCriteria.Select(item => item.Id)
                .Distinct().Count() != journal.ReviewedCriteria.Count ||
            journal.ReviewedCriteria.Select(item => item.Reference)
                .Distinct(StringComparer.Ordinal).Count() !=
                    journal.ReviewedCriteria.Count ||
            journal.Phase is not ("prepared" or "completed" or
                "rollback-prepared" or "rolled-back"))
        {
            throw new InvalidDataException(
                "Journal EXI A non canonique ou identité incohérente.");
        }
    }

    /// <summary>Checks exact SHA-256 hex contract.</summary>
    /// <param name="sha">Stored expected digest.</param>
    /// <returns>True for exactly 32-byte digest.</returns>
    private static bool ValidSha(string? sha) =>
        sha is { Length: 64 } && sha.All(Uri.IsHexDigit);
}
