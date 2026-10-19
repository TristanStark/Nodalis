using System.Text;
using System.Text.Json;
using Nodalis.Core.Domain;
using Nodalis.Infrastructure.Importing;
using Nodalis.Infrastructure.Persistence;

internal static class DocxReimportTransactionSmokeTests
{
    /// <summary>
    /// Exercises approved multi-file commits, idempotence, optimistic conflicts,
    /// forced write failures, crash recovery and byte-identical restoration.
    /// </summary>
    public static async Task RunAsync(string workspace)
    {
        string project = Path.Combine(workspace, "docx-transaction-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        Guid projectId = Guid.NewGuid();
        ProjectManifest manifest = new ProjectManifest
        {
            Id = projectId,
            Name = "DOCX transaction",
            ApplicationId = Guid.NewGuid(),
            InitialComplexity = ProjectComplexity.Medium
        };
        await File.WriteAllTextAsync(Path.Combine(project,
            WorkspaceLayout.ProjectManifestFileName), JsonSerializer.Serialize(
                manifest, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

        string tests = Path.Combine(project, "tests.md");
        string tech = Path.Combine(project, "tech.md");
        string originalOne = "# Tests\n\nV1\n";
        string originalTwo = "# Technique\n\nV1\n";
        string revisedOne = "# Tests\n\nV2\n";
        string revisedTwo = "# Technique\n\nV2\n";
        await File.WriteAllTextAsync(tests, originalOne);
        // Preserve UTF-8 BOM to check byte-for-byte recovery.
        byte[] originalWithBom = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(originalTwo)).ToArray();
        await File.WriteAllBytesAsync(tech, originalWithBom);

        DocxReimportTransactionService service =
            new DocxReimportTransactionService(workspace);
        Guid historyId = Guid.NewGuid();
        DocxReimportDecisionPlan plan = Plan(projectId, historyId,
        [
            Write(workspace, tests, originalOne, revisedOne),
            Write(workspace, tech, originalTwo, revisedTwo)
        ]);

        Guid operationId = Guid.NewGuid();
        string fingerprint = DocxReimportTransactionService.Fingerprint(plan);
        bool deniedUnapproved = false;
        try
        {
            await service.CommitAsync(project, plan, operationId,
                new string('0', 64));
        }
        catch (InvalidDataException)
        {
            deniedUnapproved = true;
        }

        Assert(deniedUnapproved &&
               await File.ReadAllTextAsync(tests) == originalOne,
            "A missing or invalid approval must never modify Markdown.");

        string relativeTech = Path.GetRelativePath(workspace, tech).Replace('\\', '/');
        DocxReimportDecisionPlan malformed = plan with
        {
            Writes =
            [
                plan.Writes[0],
                plan.Writes[1] with { DestinationRelativePath = "../outside.md" }
            ]
        };
        bool deniedTraversal = false;
        try
        {
            await service.CommitAsync(project, malformed, Guid.NewGuid(),
                DocxReimportTransactionService.Fingerprint(malformed));
        }
        catch (InvalidDataException)
        {
            deniedTraversal = true;
        }

        Assert(deniedTraversal && File.Exists(tech),
            "Paths escaping the workspace must be rejected before writing.");

        DocxReimportTransactionResult success =
            await service.CommitAsync(project, plan, operationId, fingerprint);
        Assert(success.WrittenFiles == 2 && !success.AlreadyApplied &&
               await File.ReadAllTextAsync(tests) == revisedOne &&
               await File.ReadAllTextAsync(tech) == revisedTwo,
            "An approved transaction must replace both Markdown destinations.");

        DocxReimportTransactionResult again =
            await service.CommitAsync(project, plan, operationId, fingerprint);
        Assert(again.AlreadyApplied && again.WrittenFiles == 2,
            "Repeating the same operation ID and approved plan must be idempotent.");

        bool deniedStale = false;
        try
        {
            await service.CommitAsync(project, plan, Guid.NewGuid(), fingerprint);
        }
        catch (InvalidDataException)
        {
            deniedStale = true;
        }

        Assert(deniedStale,
            "A different operation with stale original hashes must be rejected.");

        await File.WriteAllTextAsync(tests, originalOne);
        await File.WriteAllBytesAsync(tech, originalWithBom);
        DocxReimportTransactionService failsAfterFirst =
            new DocxReimportTransactionService(workspace,
                completed =>
                {
                    if (completed == 1)
                    {
                        throw new InvalidOperationException("Simulated interrupted commit");
                    }
                });
        Guid failedId = Guid.NewGuid();
        bool failureObserved = false;
        try
        {
            await failsAfterFirst.CommitAsync(project, plan, failedId, fingerprint);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("Simulated", StringComparison.Ordinal))
        {
            failureObserved = true;
        }

        Assert(failureObserved &&
               await File.ReadAllTextAsync(tests) == originalOne &&
               (await File.ReadAllBytesAsync(tech)).SequenceEqual(originalWithBom),
            "Failed second step must roll back the first and preserve byte-exact backups.");

        string transactions = Path.Combine(project,
            ".nodalis-reimport-transactions");
        string abortedJournal = Path.Combine(transactions,
            failedId.ToString("N"), "journal.json");
        DocxReimportTransactionJournal rolledBack =
            await ReadJournalAsync(abortedJournal);
        Assert(rolledBack.State == DocxReimportTransactionState.RolledBack,
            "A fully restored failed transaction must be recorded as rolled back.");

        // Mimic process termination between the first and second atomic
        // replacement by creating a durable Prepared journal manually.
        Guid pendingId = Guid.NewGuid();
        string pendingDir = Path.Combine(transactions, pendingId.ToString("N"));
        Directory.CreateDirectory(pendingDir);
        await File.WriteAllBytesAsync(Path.Combine(pendingDir, "00000.bak"),
            Encoding.UTF8.GetBytes(originalOne));
        await File.WriteAllBytesAsync(Path.Combine(pendingDir, "00001.bak"),
            originalWithBom);
        DocxReimportTransactionJournal pending = new DocxReimportTransactionJournal
        {
            OperationId = pendingId,
            ProjectId = projectId,
            PreviousImportId = historyId,
            Fingerprint = fingerprint,
            State = DocxReimportTransactionState.Prepared,
            Entries =
            [
                Entry(workspace, tests, "00000.bak", Encoding.UTF8.GetBytes(originalOne),
                    originalOne, revisedOne),
                Entry(workspace, tech, "00001.bak", originalWithBom,
                    originalTwo, revisedTwo)
            ]
        };
        string pendingPath = Path.Combine(pendingDir, "journal.json");
        await File.WriteAllTextAsync(pendingPath, JsonSerializer.Serialize(
            pending, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        await File.WriteAllTextAsync(tests, revisedOne);
        int recovered = await service.RecoverPendingAsync(project);
        Assert(recovered == 1 &&
               await File.ReadAllTextAsync(tests) == originalOne &&
               (await File.ReadAllBytesAsync(tech)).SequenceEqual(originalWithBom) &&
               (await ReadJournalAsync(pendingPath)).State ==
                   DocxReimportTransactionState.RolledBack &&
               await service.RecoverPendingAsync(project) == 0,
            "Recovery after simulated process termination must restore all sources once.");

        // A third-party edit after a crash must never be overwritten by recovery.
        Guid conflictId = Guid.NewGuid();
        string conflictDir = Path.Combine(transactions, conflictId.ToString("N"));
        Directory.CreateDirectory(conflictDir);
        await File.WriteAllBytesAsync(Path.Combine(conflictDir, "00000.bak"),
            Encoding.UTF8.GetBytes(originalOne));
        await File.WriteAllTextAsync(Path.Combine(conflictDir, "journal.json"),
            JsonSerializer.Serialize(pending with
            {
                OperationId = conflictId,
                Entries = [pending.Entries[0]]
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        await File.WriteAllTextAsync(tests, "User changed the file meanwhile");
        bool recoveryConflict = false;
        try
        {
            await service.RecoverPendingAsync(project);
        }
        catch (InvalidDataException)
        {
            recoveryConflict = true;
        }

        Assert(recoveryConflict &&
               await File.ReadAllTextAsync(tests) ==
                   "User changed the file meanwhile" &&
               File.Exists(Path.Combine(conflictDir, "00000.bak")),
            "Recovery must preserve a third-party edit and retain its backup.");

        // Ensure one expected destination comes from this project only.
        Assert(relativeTech.StartsWith(Path.GetFileName(project) + "/",
                StringComparison.Ordinal),
            "Fixtures must use portable project-relative workspace paths.");
    }

    /// <summary>Creates a fully reviewed dry-run with exactly the supplied writes.</summary>
    private static DocxReimportDecisionPlan Plan(
        Guid projectId, Guid historyId, List<DocxReimportProposedWrite> writes) =>
        new DocxReimportDecisionPlan
        {
            ProjectId = projectId,
            PreviousImportId = historyId,
            Lines = writes.Select(write => new DocxReimportDecisionLine
            {
                FragmentId = write.FragmentId,
                Action = DocxReimportResolutionAction.UseWord,
                Resolved = true,
                Diagnostic = "User confirmed"
            }).ToList(),
            Writes = writes
        };

    /// <summary>Builds a proposed write with the correct original destination SHA.</summary>
    private static DocxReimportProposedWrite Write(
        string workspace, string path, string before, string after) =>
        new DocxReimportProposedWrite
        {
            FragmentId = Guid.NewGuid(),
            DestinationRelativePath = Path.GetRelativePath(workspace, path)
                .Replace('\\', '/'),
            ExpectedCurrentSha256 = DocxImportManifestStore.ComputeSha256(before),
            ProjectedSha256 = DocxImportManifestStore.ComputeSha256(after),
            ProjectedMarkdown = after
        };

    /// <summary>Describes the original bytes for a simulated crash journal.</summary>
    private static DocxReimportTransactionEntry Entry(
        string workspace, string path, string backupName, byte[] backup,
        string before, string after) =>
        new DocxReimportTransactionEntry
        {
            DestinationRelativePath = Path.GetRelativePath(workspace, path)
                .Replace('\\', '/'),
            BackupFileName = backupName,
            BackupBytesSha256 = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(backup)).ToLowerInvariant(),
            BeforeSha256 = DocxImportManifestStore.ComputeSha256(before),
            AfterSha256 = DocxImportManifestStore.ComputeSha256(after)
        };

    /// <summary>Reads the transaction journal using standard enum deserialization.</summary>
    private static async Task<DocxReimportTransactionJournal> ReadJournalAsync(
        string path) =>
        System.Text.Json.JsonSerializer.Deserialize<DocxReimportTransactionJournal>(
            await File.ReadAllTextAsync(path),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters =
                {
                    new System.Text.Json.Serialization.JsonStringEnumConverter(
                        JsonNamingPolicy.CamelCase)
                }
            })
        ?? throw new InvalidDataException("Missing test journal.");

    /// <summary>Fails the smoke suite when any transactional safety invariant is broken.</summary>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
