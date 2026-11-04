using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Persistence;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>Minimal, private reference to a reviewed recovery operation.</summary>
public sealed record ProjectAiRecoveryTarget
{
    /// <summary>Canonical Nodalis EXI reference, never an AI-supplied path.</summary>
    public required string Reference { get; init; }
    /// <summary>Only update or create, as proven by the source journal.</summary>
    public required string Operation { get; init; }
    /// <summary>Stable Nodalis requirement GUID from the original journal.</summary>
    public required Guid RequirementId { get; init; }
}

/// <summary>
/// An immutable user-confirmed attempt to resume a specific project-local
/// transaction. This is a record of INTENT, not a publication receipt.
/// </summary>
public sealed record ProjectAiRecoveryAttempt
{
    /// <summary>Independent, Nodalis-generated attempt identity.</summary>
    public required Guid AttemptId { get; init; }
    /// <summary>Verified canonical project owner.</summary>
    public required Guid ProjectId { get; init; }
    /// <summary>title-batch or mixed, never inferred from the model response.</summary>
    public required string Kind { get; init; }
    /// <summary>Exact local journal directory name, with no arbitrary path.</summary>
    public required string TransactionKey { get; init; }
    /// <summary>SHA-256 of the exact source intent.json at confirmation time.</summary>
    public required string JournalSha256 { get; init; }
    /// <summary>UTC moment after the user's explicit confirmation.</summary>
    public required DateTimeOffset StartedUtc { get; init; }
    /// <summary>Number already published when the human confirmed.</summary>
    public required int PublishedBefore { get; init; }
    /// <summary>Number remaining at that instant.</summary>
    public required int PendingBefore { get; init; }
    /// <summary>References and operations only: no titles or raw AI text.</summary>
    public required List<ProjectAiRecoveryTarget> Targets { get; init; }
}

/// <summary>
/// A separate immutable outcome. A failed attempt may have partly published
/// files; absence of this record means unknown/interrupted, NEVER success.
/// </summary>
public sealed record ProjectAiRecoveryOutcome
{
    /// <summary>Stable attempt identity.</summary>
    public required Guid AttemptId { get; init; }
    /// <summary>UTC completion timestamp.</summary>
    public required DateTimeOffset RecordedUtc { get; init; }
    /// <summary>Applied, AlreadyApplied, Failed or NeedsInspection.</summary>
    public required string Result { get; init; }
    /// <summary>Whether the remaining files were safely inspected.</summary>
    public required bool PostStateVerified { get; init; }
    /// <summary>Number published after attempt, or null if unverifiable.</summary>
    public int? PublishedAfter { get; init; }
    /// <summary>Number pending after attempt, or null if unverifiable.</summary>
    public int? PendingAfter { get; init; }
}

/// <summary>
/// Project-local audit for explicitly confirmed recovery attempts. It does not
/// read or archive raw AI responses and never initiates a business import.
/// Each start and outcome is a separate atomic no-overwrite JSON file.
/// </summary>
public sealed class ProjectAiRecoveryHistoryService
{
    private const string JournalRoot = ".nodalis-ai-import";
    private const string RecoveryFolder = "recoveries";
    private const string IntentFile = "attempt.json";
    private const string OutcomeFile = "outcome.json";
    private const int MaxEntries = 20;
    private const int MaxAuditBytes = 16384;
    private readonly string _workspaceRoot;
    private readonly ProjectJourneySourceService _sources;

    /// <summary>Creates a read-only service until an explicit start or finish.</summary>
    /// <param name="workspaceRoot">The selected single workspace.</param>
    public ProjectAiRecoveryHistoryService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _sources = new ProjectJourneySourceService(_workspaceRoot);
    }

    /// <summary>
    /// Persists intent ONLY after a human has confirmed the exact displayed
    /// journal and its pending counts. Rechecks the project, the journal and
    /// current files; records no new AI-generated title or business write.
    /// </summary>
    /// <param name="projectDirectory">Canonical active project.</param>
    /// <param name="kind">Either title-batch or mixed.</param>
    /// <param name="key">Exact 64-character plan digest, no path components.</param>
    /// <param name="expectedPublished">Human-visible completed count.</param>
    /// <param name="expectedPending">Human-visible pending count.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Immutable recovered attempt identity.</returns>
    public async Task<ProjectAiRecoveryAttempt> StartAsync(
        string projectDirectory, string kind, string key,
        int expectedPublished, int expectedPending,
        CancellationToken token = default)
    {
        ValidateKey(kind, key);
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        Guid projectId = await VerifyProjectAsync(project, token);
        (Guid owner, IReadOnlyList<ProjectAiRecoveryTarget> targets,
            int published, int pending) = await InspectAsync(
                project, kind, key, token);
        if (owner != projectId || pending <= 0 ||
            expectedPublished != published || expectedPending != pending ||
            targets.Count is < 2 or > MaxEntries ||
            targets.Count != published + pending)
        {
            throw new InvalidDataException(
                "Lot modifié depuis la confirmation : renouveler la revue.");
        }

        string root = Path.Combine(project, JournalRoot);
        RequireDirectory(root);
        string transaction = Path.Combine(root, TransactionName(kind, key));
        RequireDirectory(transaction);
        string journalPath = Path.Combine(transaction, "intent.json");
        string fingerprint = await ReadJournalHashAsync(journalPath, token);

        string lockPath = Path.Combine(root, ".lock");
        await using FileStream gate = new(lockPath, FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous);
        if ((File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Verrou d'import redirigé.");
        }

        // Re-verify under the common project lock, then release it before
        // asking the publication engine to acquire the same lock.
        (Guid confirmedOwner, IReadOnlyList<ProjectAiRecoveryTarget> confirmed,
            int latestPublished, int latestPending) = await InspectAsync(
                project, kind, key, token);
        if (confirmedOwner != owner ||
            latestPublished != published || latestPending != pending ||
            !confirmed.SequenceEqual(targets) ||
            await ReadJournalHashAsync(journalPath, token) != fingerprint)
        {
            throw new InvalidDataException(
                "Journal ou progression changé durant la confirmation.");
        }

        string recoveryRoot = Path.Combine(root, RecoveryFolder);
        Directory.CreateDirectory(recoveryRoot);
        RequireDirectory(recoveryRoot);
        Guid attemptId = Guid.NewGuid();
        string directory = Path.Combine(recoveryRoot,
            attemptId.ToString("N"));
        Directory.CreateDirectory(directory);
        RequireDirectory(directory);
        ProjectAiRecoveryAttempt attempt = new()
        {
            AttemptId = attemptId,
            ProjectId = owner,
            Kind = kind,
            TransactionKey = TransactionName(kind, key),
            JournalSha256 = fingerprint,
            StartedUtc = DateTimeOffset.UtcNow,
            PublishedBefore = published,
            PendingBefore = pending,
            Targets = confirmed.ToList()
        };
        // Only this completed intent is discoverable. A crash before it
        // appears has never been recorded as a confirmed recovery attempt.
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, IntentFile), Serialize(attempt), token);
        return attempt;
    }

    /// <summary>
    /// Records a result AFTER the replay engine returns or throws. Never
    /// reports a verified success unless the post-recovery journal confirms
    /// every file is at its published SHA; failure may still be partial.
    /// </summary>
    /// <param name="projectDirectory">Owning project.</param>
    /// <param name="attemptId">Durable attempt.</param>
    /// <param name="engineResult">Applied, AlreadyApplied, or Failed.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Verified or explicitly uncertain outcome.</returns>
    public async Task<ProjectAiRecoveryOutcome> FinishAsync(
        string projectDirectory, Guid attemptId, string engineResult,
        CancellationToken token = default)
    {
        if (engineResult is not ("Applied" or "AlreadyApplied" or "Failed"))
        {
            throw new InvalidDataException("Résultat de récupération inconnu.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        ProjectAiRecoveryAttempt attempt = await ReadAsync(
            project, attemptId, token);
        string key = attempt.TransactionKey[6..];
        int? published = null;
        int? pending = null;
        bool verified = false;
        try
        {
            (Guid owner, IReadOnlyList<ProjectAiRecoveryTarget> targets,
                int completed, int remaining) = await InspectAsync(
                project, attempt.Kind, key, token);
            string journalPath = Path.Combine(project, JournalRoot,
                attempt.TransactionKey, "intent.json");
            if (owner != attempt.ProjectId ||
                !targets.SequenceEqual(attempt.Targets) ||
                await ReadJournalHashAsync(journalPath, token) !=
                    attempt.JournalSha256)
            {
                throw new InvalidDataException(
                    "Journal ou identités modifiés après la reprise.");
            }

            published = completed;
            pending = remaining;
            verified = true;
        }
        catch (Exception exception) when (exception is InvalidDataException or
            IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Preserve uncertainty without collecting sensitive exception text.
        }

        string result = engineResult;
        if ((engineResult is "Applied" or "AlreadyApplied") &&
            (!verified || pending != 0))
        {
            result = "NeedsInspection";
        }

        ProjectAiRecoveryOutcome outcome = new()
        {
            AttemptId = attemptId,
            RecordedUtc = DateTimeOffset.UtcNow,
            Result = result,
            PostStateVerified = verified,
            PublishedAfter = published,
            PendingAfter = pending
        };
        string directory = AttemptDirectory(project, attemptId);
        RequireDirectory(directory);
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(directory, OutcomeFile), Serialize(outcome), token);
        return outcome;
    }

    /// <summary>Returns the newest 100 complete attempts for this project.</summary>
    /// <param name="projectDirectory">Current active project.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Metadata only, no response content or Markdown body.</returns>
    public async Task<IReadOnlyList<ProjectAiRecoveryAttempt>> ListAsync(
        string projectDirectory, CancellationToken token = default)
    {
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        await VerifyProjectAsync(project, token);
        string root = Path.Combine(project, JournalRoot);
        if (!Directory.Exists(root))
        {
            return [];
        }

        RequireDirectory(root);
        string folder = Path.Combine(root, RecoveryFolder);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        RequireDirectory(folder);
        string[] entries = Directory.GetDirectories(folder, "*",
            SearchOption.TopDirectoryOnly);
        if (entries.Length > 1000)
        {
            throw new InvalidDataException(
                "Trop de tentatives de reprise pour une liste sûre.");
        }

        List<ProjectAiRecoveryAttempt> result = [];
        foreach (string entry in entries)
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(
                Path.GetFileName(entry), "N", out Guid id))
            {
                continue;
            }

            try
            {
                result.Add(await ReadAsync(project, id, token));
            }
            catch (Exception exception) when (exception is InvalidDataException or
                IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Never try to repair or execute from an incomplete audit.
            }
        }

        return result.OrderByDescending(item => item.StartedUtc)
            .Take(100).ToArray();
    }

    /// <summary>Reads one project-owned immutable recovery intent.</summary>
    /// <param name="projectDirectory">Current active project.</param>
    /// <param name="attemptId">Local attempt ID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Validated intent metadata.</returns>
    public async Task<ProjectAiRecoveryAttempt> ReadAsync(
        string projectDirectory, Guid attemptId,
        CancellationToken token = default)
    {
        if (attemptId == Guid.Empty)
        {
            throw new InvalidDataException("Tentative de reprise sans GUID.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        Guid owner = await VerifyProjectAsync(project, token);
        RequireDirectory(Path.Combine(project, JournalRoot));
        RequireDirectory(Path.Combine(project, JournalRoot, RecoveryFolder));
        string directory = AttemptDirectory(project, attemptId);
        RequireDirectory(directory);
        ProjectAiRecoveryAttempt? attempt =
            await ReadJsonAsync<ProjectAiRecoveryAttempt>(
                Path.Combine(directory, IntentFile), token);
        if (attempt is null || attempt.AttemptId != attemptId ||
            attempt.ProjectId != owner ||
            attempt.Targets is null ||
            attempt.Targets.Count is < 2 or > MaxEntries ||
            attempt.Targets.Any(item => item is null ||
                item.RequirementId == Guid.Empty ||
                item.Operation is not ("update" or "create") ||
                !Nodalis.Core.Requirements.ProjectRequirementMarkdownCodec
                    .IsReference(item.Reference)) ||
            attempt.PublishedBefore < 0 || attempt.PendingBefore <= 0 ||
            attempt.PublishedBefore + attempt.PendingBefore != attempt.Targets.Count ||
            attempt.JournalSha256 is not { Length: 64 } ||
            !attempt.JournalSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                "Audit de reprise incompatible avec ce projet.");
        }

        string prefix = attempt.Kind == "mixed" ? "mixed-" : "batch-";
        if (attempt.Kind is not ("mixed" or "title-batch") ||
            attempt.TransactionKey is null ||
            !attempt.TransactionKey.StartsWith(prefix, StringComparison.Ordinal) ||
            attempt.TransactionKey.Length != prefix.Length + 64 ||
            !attempt.TransactionKey[prefix.Length..].All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Référence de journal d'import non sûre.");
        }

        return attempt;
    }

    /// <summary>
    /// Reads a separate outcome, or null when interrupted before its write.
    /// A missing outcome is not inferred as success from the business files.
    /// </summary>
    /// <param name="projectDirectory">Current active project.</param>
    /// <param name="attemptId">Local attempt GUID.</param>
    /// <param name="token">Cancellation.</param>
    /// <returns>Immutable outcome, if one exists.</returns>
    public async Task<ProjectAiRecoveryOutcome?> ReadOutcomeAsync(
        string projectDirectory, Guid attemptId,
        CancellationToken token = default)
    {
        ProjectAiRecoveryAttempt attempt = await ReadAsync(
            projectDirectory, attemptId, token);
        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string path = Path.Combine(AttemptDirectory(project, attemptId),
            OutcomeFile);
        if (!File.Exists(path))
        {
            return null;
        }

        ProjectAiRecoveryOutcome? outcome =
            await ReadJsonAsync<ProjectAiRecoveryOutcome>(path, token);
        if (outcome is null || outcome.AttemptId != attempt.AttemptId ||
            outcome.Result is not (
                "Applied" or "AlreadyApplied" or "Failed" or "NeedsInspection") ||
            outcome.PostStateVerified !=
                (outcome.PublishedAfter.HasValue && outcome.PendingAfter.HasValue) ||
            outcome.PublishedAfter < 0 || outcome.PendingAfter < 0 ||
            outcome.PostStateVerified &&
                outcome.PublishedAfter + outcome.PendingAfter !=
                    attempt.Targets.Count ||
            (outcome.Result is "Applied" or "AlreadyApplied") &&
                (!outcome.PostStateVerified || outcome.PendingAfter != 0))
        {
            throw new InvalidDataException(
                "Preuve de reprise corrompue ou exagérant la publication.");
        }

        return outcome;
    }

    /// <summary>Revalidates the same two independent recovery engines.</summary>
    private async Task<(Guid Owner,
        IReadOnlyList<ProjectAiRecoveryTarget> Targets, int Published,
        int Pending)> InspectAsync(string project, string kind, string key,
        CancellationToken token)
    {
        if (kind == "title-batch")
        {
            ProjectAiBatchRecoveryPreview preview =
                await new ProjectAiBatchTitleImportTransactionService(
                    _workspaceRoot).ReviewRecoveryAsync(project, key, token);
            return (preview.ProjectId,
                preview.Files.Select(item => new ProjectAiRecoveryTarget
                {
                    Reference = item.Reference, Operation = "update",
                    RequirementId = item.RequirementId
                }).ToArray(), preview.Published, preview.Pending);
        }

        ProjectAiMixedRecoveryPreview mixed =
            await new ProjectAiMixedImportTransactionService(_workspaceRoot)
                .ReviewRecoveryAsync(project, key, token);
        return (mixed.ProjectId,
            mixed.Entries.Select(item => new ProjectAiRecoveryTarget
            {
                Reference = item.Reference, Operation = item.Operation,
                RequirementId = item.RequirementId
            }).ToArray(), mixed.Published, mixed.Pending);
    }

    /// <summary>Accepts only recognized journal types and 64 hex digits.</summary>
    private static void ValidateKey(string kind, string key)
    {
        if (kind is not ("title-batch" or "mixed") ||
            key is not { Length: 64 } || !key.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Identifiant de lot de reprise invalide.");
        }
    }

    /// <summary>Canonical journal name from a verified supported kind.</summary>
    private static string TransactionName(string kind, string key) =>
        (kind == "mixed" ? "mixed-" : "batch-") + key;

    /// <summary>Canonical, bounded metadata path; never model supplied.</summary>
    private static string AttemptDirectory(string project, Guid id) =>
        Path.Combine(project, JournalRoot, RecoveryFolder, id.ToString("N"));

    /// <summary>Verifies project path, active manifest and non-archival.</summary>
    private async Task<Guid> VerifyProjectAsync(string project, CancellationToken token)
    {
        string relative = Path.GetRelativePath(_workspaceRoot, project);
        string[] segments = relative.Split(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || segments.Length < 4 ||
            !string.Equals(segments[0], WorkspaceLayout.ApplicationsDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
            !segments.Skip(2).Any(segment => string.Equals(segment,
                WorkspaceLayout.ProjectsDirectoryName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Audit de reprise hors projet actif du workspace.");
        }

        string path = Path.Combine(project, WorkspaceLayout.ProjectManifestFileName);
        string safe = _sources.GetReferenceForExistingFile(path);
        _sources.ResolveExistingFile(safe);
        ProjectManifest manifest = await AtomicJsonFile.ReadAsync<ProjectManifest>(
            path, token);
        if (manifest.Id == Guid.Empty || manifest.ArchivedUtc.HasValue ||
            manifest.ArchiveOriginalRelativePath is not null)
        {
            throw new InvalidDataException(
                "Projet archivé ou sans identité : journal refusé.");
        }

        return manifest.Id;
    }

    /// <summary>Strictly reads a bounded, non-symlinked journal source.</summary>
    private static async Task<string> ReadJournalHashAsync(
        string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > MaxAuditBytes)
        {
            throw new InvalidDataException("Journal métier absent ou non sûr.");
        }

        byte[] bytes = await File.ReadAllBytesAsync(path, token);
        if (bytes.Length > MaxAuditBytes)
        {
            throw new InvalidDataException("Journal métier trop volumineux.");
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Reads immutable, bounded and non-symlinked JSON metadata.</summary>
    private static async Task<T?> ReadJsonAsync<T>(
        string path, CancellationToken token)
    {
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > MaxAuditBytes)
        {
            throw new InvalidDataException("Fichier d'audit absent ou non sûr.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                await File.ReadAllTextAsync(path, token),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Document d'audit de reprise malformé.", exception);
        }
    }

    /// <summary>Serializes short local-only JSON metadata.</summary>
    private static string Serialize<T>(T value)
    {
        string json = JsonSerializer.Serialize(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (Encoding.UTF8.GetByteCount(json) > MaxAuditBytes)
        {
            throw new InvalidDataException("Historique de reprise trop volumineux.");
        }

        return json;
    }

    /// <summary>Rejects redirected journal and audit directory ancestors.</summary>
    private static void RequireDirectory(string path)
    {
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "Dossier d'audit de reprise sous jonction ou manquant.");
        }
    }
}
