using System.Text;
using Nodalis.Core.Requirements;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Recoverable project-local batch presented to a human. A journal is not
/// a command: the UI must show every change and request confirmation.
/// </summary>
/// <param name="PlanSha256">Canonical journal identifier.</param>
/// <param name="ProjectId">Verified owner project.</param>
/// <param name="Files">Human-readable title changes, in stable order.</param>
/// <param name="Published">Files already at their approved SHA-256.</param>
/// <param name="Pending">Files still at their original SHA-256.</param>
public sealed record ProjectAiBatchRecoveryPreview(
    string PlanSha256, Guid ProjectId,
    IReadOnlyList<ProjectAiBatchTitleFile> Files,
    int Published, int Pending);

/// <summary>
/// Read-only discovery and explicit recovery of interrupted AI title batches.
/// Never trusts a journal without validating source, target, identities,
/// approved snapshots and the exact title-only transformation.
/// </summary>
public sealed partial class ProjectAiBatchTitleImportTransactionService
{
    /// <summary>
    /// Discovers up to 100 valid, INCOMPLETE batch journals for this project.
    /// Nothing is written. Invalid journals are not offered for automatic
    /// replay, and must be inspected manually.
    /// </summary>
    /// <param name="projectDirectory">Current active project.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Only safe-to-review, pending batch summaries.</returns>
    public async Task<IReadOnlyList<ProjectAiBatchRecoveryPreview>>
        ListPendingBatchesAsync(string projectDirectory,
            CancellationToken cancellationToken = default)
    {
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string root = Path.Combine(project, JournalFolder);
        if (!Directory.Exists(root))
        {
            return [];
        }

        RequireNormalDirectory(root);
        string[] candidates = Directory.GetDirectories(root, "batch-*",
            SearchOption.TopDirectoryOnly);
        if (candidates.Length > 100)
        {
            throw new InvalidDataException(
                "Trop de journaux IA : inspection manuelle requise.");
        }

        List<ProjectAiBatchRecoveryPreview> result = [];
        foreach (string directory in candidates.OrderBy(item => item,
            StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileName(directory);
            if (name.Length != 70 || !name.StartsWith("batch-",
                    StringComparison.Ordinal) ||
                !name[6..].All(Uri.IsHexDigit))
            {
                continue;
            }

            try
            {
                (ProjectAiBatchRecoveryPreview preview, _) =
                    await ReadRecoveryAsync(project, name[6..], cancellationToken);
                if (preview.Pending > 0)
                {
                    result.Add(preview);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or
                IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Corrupted or diverged batches must never become an action.
                // Their original journal remains untouched for human analysis.
            }
        }

        return result;
    }

    /// <summary>
    /// Verifies one retained batch and describes every file for the required
    /// human confirmation. This method does not modify any file or journal.
    /// </summary>
    /// <param name="projectDirectory">Current project.</param>
    /// <param name="planSha256">Exact 64-hex batch identifier.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Verifiable, still-pending publication information.</returns>
    public async Task<ProjectAiBatchRecoveryPreview> ReviewRecoveryAsync(
        string projectDirectory, string planSha256,
        CancellationToken cancellationToken = default)
    {
        (ProjectAiBatchRecoveryPreview preview, _) = await ReadRecoveryAsync(
            projectDirectory, planSha256, cancellationToken);
        return preview;
    }

    /// <summary>
    /// Replays an independently validated batch without the original WPF
    /// dialog or external AI response. Only call AFTER showing and confirming
    /// the matching recovery preview; every live hash is checked again.
    /// </summary>
    /// <param name="projectDirectory">Current project.</param>
    /// <param name="planSha256">Human-confirmed batch identifier.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Applied or already applied, without blind overwrite.</returns>
    public async Task<ProjectAiBatchTitleCommitStatus> ResumeAsync(
        string projectDirectory, string planSha256,
        CancellationToken cancellationToken = default)
    {
        (_, ProjectAiPreparedReviewPlan plan) = await ReadRecoveryAsync(
            projectDirectory, planSha256, cancellationToken);
        return await CommitAsync(projectDirectory, plan, cancellationToken);
    }

    /// <summary>
    /// Rebuilds authorization from an exact, durable journal only after proving
    /// that every approved snapshot differs from its original solely in the
    /// canonical metadata title and H1. No AI-supplied path or status is trusted.
    /// </summary>
    private async Task<(ProjectAiBatchRecoveryPreview Preview,
        ProjectAiPreparedReviewPlan Plan)> ReadRecoveryAsync(
        string projectDirectory, string planSha256, CancellationToken token)
    {
        if (planSha256 is not { Length: 64 } ||
            !planSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Identifiant de journal IA invalide.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string root = Path.Combine(project, JournalFolder);
        RequireNormalDirectory(root);
        string directory = Path.Combine(root, "batch-" + planSha256);
        RequireNormalDirectory(directory);
        ProjectAiBatchTitleJournal journal = await LoadManifestAsync(
            Path.Combine(directory, ManifestFile), token);

        if (journal.Files is null || journal.Files.Count is < 2 or > MaxFiles ||
            journal.Files.Any(item => item is null) ||
            journal.ProjectId == Guid.Empty ||
            journal.PlanSha256 != planSha256 ||
            journal.RawResponseSha256 is not { Length: 64 } ||
            !journal.RawResponseSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Journal de lot incomplet ou périmètre invalide.");
        }

        await VerifyProjectAsync(project, journal.ProjectId, token);
        ProjectAiPreparedReviewPlan plan = new(
            journal.RawResponseSha256,
            journal.Files.Select(file => new ProjectAiPreparedTitleChange(
                file.ProposalId, file.RequirementId, journal.ProjectId,
                file.Reference, file.RelativePath, file.OriginalSha256,
                file.OriginalTitle, file.ReviewedTitle, file.Reason)).ToArray(),
            journal.Files.Count, 0, 0);
        ProjectAiPreparedTitleChange[] changes = ValidatePlan(plan);
        if (PlanFingerprint(plan) != planSha256)
        {
            throw new InvalidDataException(
                "Le journal n'est pas l'autorisation humaine originale.");
        }

        ValidateManifest(journal, plan, changes, planSha256);
        string originals = Path.Combine(directory, OriginalFolder);
        string approved = Path.Combine(directory, ApprovedFolder);
        RequireNormalDirectory(originals);
        RequireNormalDirectory(approved);
        int published = 0;
        int pending = 0;
        foreach (ProjectAiBatchTitleFile item in journal.Files)
        {
            token.ThrowIfCancellationRequested();
            string originalPath = Path.Combine(originals, item.Slot);
            string approvedPath = Path.Combine(approved, item.Slot);
            await AssertSnapshotAsync(originalPath, item.OriginalSha256, token);
            await AssertSnapshotAsync(approvedPath, item.PublishedSha256, token);
            byte[] originalBytes = await ReadBoundedAsync(originalPath, token);
            byte[] approvedBytes = await ReadBoundedAsync(approvedPath, token);
            string original = new UTF8Encoding(false, true).GetString(originalBytes);
            ProjectRequirementCard originalCard =
                ProjectRequirementMarkdownCodec.Parse(original);
            if (originalCard.Id != item.RequirementId ||
                originalCard.ProjectId != journal.ProjectId ||
                originalCard.Reference != item.Reference ||
                originalCard.Title != item.OriginalTitle ||
                !approvedBytes.AsSpan().SequenceEqual(
                    new UTF8Encoding(false).GetBytes(
                        ProjectAiTitleImportTransactionService.RewriteTitle(
                            original, originalCard, item.ReviewedTitle))))
            {
                throw new InvalidDataException(
                    "Journal IA modifie des champs autres que le titre EXI.");
            }

            string target = ResolveCanonical(
                project, item.RelativePath, item.Reference);
            string liveHash = Hash(await ReadBoundedAsync(target, token));
            if (string.Equals(liveHash, item.PublishedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                published++;
            }
            else if (string.Equals(liveHash, item.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                pending++;
            }
            else
            {
                throw new InvalidDataException(
                    "Lot divergent : aucun fichier ne peut être rejoué " +
                    "sans résolution humaine : " + item.Reference);
            }
        }

        return (new ProjectAiBatchRecoveryPreview(planSha256,
            journal.ProjectId, journal.Files.ToArray(), published, pending),
            plan);
    }
}
