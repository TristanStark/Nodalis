using System.Text.Json.Nodes;
using Nodalis.Core.Inbox;
using Nodalis.Infrastructure.Inbox;
using Nodalis.Infrastructure.Persistence;

internal static class InboxSafetySmokeTests
{
    /// <summary>
    /// Checks duplicate warnings (including legacy and trash), voluntary
    /// duplicate retention, attachment validation and recoverable failures.
    /// </summary>
    /// <param name="testRoot">Temporary test container.</param>
    /// <returns>Smoke test task.</returns>
    public static async Task RunAsync(string testRoot)
    {
        string root = Path.Combine(testRoot, "inbox-safety");
        FileSystemWorkspaceStore workspace = new FileSystemWorkspaceStore(root);
        await workspace.InitializeAsync("Safety smoke");
        WorkspaceInboxService inbox = new WorkspaceInboxService(root);
        InboxCaptureRequest text = new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.Text,
            ContentMarkdown = "Idée rapide\nÀ revoir.",
            Origin = "voluntary"
        };
        Assert((await inbox.FindProbableDuplicatesAsync(text)).Count == 0 &&
            !Directory.Exists(Path.Combine(root, "Inbox")),
            "A duplicate preview must not create Inbox storage.");

        InboxEntry first = await inbox.CaptureAsync(text);
        Assert(first.FingerprintSha256 is { Length: 64 },
            "New captures must persist their SHA-256 fingerprint.");
        InboxDuplicateCandidate match = (await inbox.FindProbableDuplicatesAsync(
            text)).Single();
        Assert(match.Id == first.Id && !match.InTrash,
            "Same text and kind must be recognized without blocking Save.");

        InboxEntry second = await inbox.CaptureAsync(text);
        Assert(second.Id != first.Id &&
            (await inbox.FindProbableDuplicatesAsync(text)).Count == 2,
            "A user must still be able to save an intentional duplicate.");

        InboxCaptureRequest differentKind = text with { Kind = InboxCaptureKind.Link };
        Assert((await inbox.FindProbableDuplicatesAsync(differentKind)).Count == 0,
            "Duplicate detection must distinguish content kinds.");
        InboxCaptureRequest changedText = text with
        {
            ContentMarkdown = "Idée rapide\nÀ revoir (mise à jour)."
        };
        Assert((await inbox.FindProbableDuplicatesAsync(changedText)).Count == 0,
            "Distinct content must not trigger a false duplicate warning.");
        Assert((await inbox.FindProbableDuplicatesAsync(text,
                TimeSpan.FromTicks(1))).Count == 0,
            "A controlled window may exclude previous captures.");

        // Backward compatibility: older metadata has no fingerprint.
        string firstItem = Path.Combine(root, "Inbox", "Items",
            first.Id.ToString("N"), "item.json");
        JsonNode metadata = JsonNode.Parse(await File.ReadAllTextAsync(firstItem))!;
        metadata.AsObject().Remove("fingerprintSha256");
        await File.WriteAllTextAsync(firstItem, metadata.ToJsonString());
        Assert((await inbox.FindProbableDuplicatesAsync(text))
                .Any(candidate => candidate.Id == first.Id),
            "Legacy captures must be compared on demand without rewriting metadata.");

        await inbox.MoveToTrashAsync(first.Id);
        Assert((await inbox.FindProbableDuplicatesAsync(text))
                .Any(candidate => candidate.Id == first.Id && candidate.InTrash),
            "Recoverable trash must be included in duplicate warnings.");
        InboxEntry restored = await inbox.RestoreAsync(first.Id);
        Assert(restored.Id == first.Id &&
            (await inbox.FindProbableDuplicatesAsync(text))
                .Any(candidate => candidate.Id == first.Id && !candidate.InTrash),
            "Trash restoration must preserve the original ID and fingerprint behavior.");

        string attachmentA = Path.Combine(root, "specification.txt");
        string attachmentB = Path.Combine(root, "workflow.json");
        await File.WriteAllTextAsync(attachmentA, "same bytes A");
        await File.WriteAllTextAsync(attachmentB, "{\"ok\":true}");
        InboxCaptureRequest files = new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.File,
            ContentMarkdown = "Fichiers sélectionnés.",
            AttachmentSourcePaths = [attachmentA, attachmentB]
        };
        InboxEntry withFiles = await inbox.CaptureAsync(files);
        InboxCaptureRequest reordered = files with
        {
            AttachmentSourcePaths = [attachmentB, attachmentA]
        };
        Assert((await inbox.FindProbableDuplicatesAsync(reordered))
                .Any(candidate => candidate.Id == withFiles.Id),
            "Attachment selection order and random stored names must not affect hash.");
        await File.WriteAllTextAsync(attachmentB, "{\"ok\":false}");
        Assert(!(await inbox.FindProbableDuplicatesAsync(reordered))
                .Any(candidate => candidate.Id == withFiles.Id),
            "Content hashes must detect changed bytes even for the same filenames.");

        string unsafeFile = Path.Combine(root, "run.cmd");
        await File.WriteAllTextAsync(unsafeFile, "echo unsafe");
        bool rejectedExtension = false;
        try
        {
            InboxCaptureClassifier.ValidateFiles([unsafeFile]);
        }
        catch (InvalidDataException)
        {
            rejectedExtension = true;
        }

        Assert(rejectedExtension, "Executable/shortcut extensions must be rejected.");
        string oversize = Path.Combine(root, "oversize.bin");
        await using (FileStream stream = new FileStream(
            oversize, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(InboxAttachmentRules.MaximumFileBytes + 1);
        }

        bool rejectedSize = false;
        try
        {
            InboxCaptureClassifier.ValidateFiles([oversize]);
        }
        catch (IOException)
        {
            rejectedSize = true;
        }

        Assert(rejectedSize, "Files over 25 MiB must be rejected before copying.");
        File.Delete(oversize);

        InboxCaptureRequest missing = new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.File,
            ContentMarkdown = "Import interrompu.",
            AttachmentSourcePaths = [Path.Combine(root, "disparu.png")]
        };
        bool importFailed = false;
        try
        {
            await inbox.CaptureAsync(missing);
        }
        catch (IOException)
        {
            importFailed = true;
        }

        Assert(importFailed && (await inbox.ReadAllAsync()).Entries.Count == 3,
            "Failed import must not publish an incomplete capture.");
        Assert(Directory.GetDirectories(Path.Combine(root, "Inbox", "Quarantaine")).Length > 0,
            "Interrupted staging must be recoverable in quarantine.");

        using CancellationTokenSource canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool cancellationCaught = false;
        try
        {
            await inbox.CaptureAsync(text, canceled.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationCaught = true;
        }

        Assert(cancellationCaught &&
            (await inbox.ReadAllAsync()).Entries.Count == 3,
            "Canceled capture cannot publish a duplicate or lose existing content.");

        // A malicious attachment path must never be followed outside its item.
        InboxEntry malicious = await inbox.CaptureAsync(new InboxCaptureRequest
        {
            Kind = InboxCaptureKind.File,
            ContentMarkdown = "Chemin non fiable.",
            AttachmentSourcePaths = [attachmentA]
        });
        string maliciousDirectory = Path.Combine(root, "Inbox", "Items",
            malicious.Id.ToString("N"));
        string maliciousMetadata = Path.Combine(maliciousDirectory, "item.json");
        JsonNode untrusted = JsonNode.Parse(
            await File.ReadAllTextAsync(maliciousMetadata))!;
        untrusted["attachments"]![0]!["relativePath"] =
            "attachments/../outside.txt";
        await File.WriteAllTextAsync(maliciousMetadata, untrusted.ToJsonString());
        InboxReadResult unsafeScan = await inbox.ReadAllAsync();
        string quarantine = Path.Combine(root, "Inbox", "Quarantaine");
        Assert(!unsafeScan.Entries.Any(entry => entry.Id == malicious.Id) &&
            unsafeScan.Warnings.Any(warning =>
                warning.Item == malicious.Id.ToString("N") &&
                warning.Message.Contains("Quarantaine", StringComparison.Ordinal)) &&
            Directory.EnumerateFiles(quarantine, "capture.md",
                SearchOption.AllDirectories).Any(path =>
                File.ReadAllText(path).Contains(
                    "Chemin non fiable.", StringComparison.Ordinal)),
            "Malicious attachment traversal must quarantine the item and preserve the source.");

        string failureRoot = Path.Combine(testRoot, "inbox-safety-iofailure");
        FileSystemWorkspaceStore failureWorkspace = new FileSystemWorkspaceStore(failureRoot);
        await failureWorkspace.InitializeAsync("Disk error demo");
        string fakeStaging = Path.Combine(failureRoot, "Inbox", ".staging");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeStaging)!);
        await File.WriteAllTextAsync(fakeStaging, "simulate a conflicting disk object");
        WorkspaceInboxService broken = new WorkspaceInboxService(failureRoot);
        bool diskErrorCaught = false;
        try
        {
            await broken.CaptureAsync(text);
        }
        catch (IOException)
        {
            diskErrorCaught = true;
        }

        Assert(diskErrorCaught &&
            File.Exists(fakeStaging) &&
            !Directory.Exists(Path.Combine(failureRoot, "Inbox", "Items",
                first.Id.ToString("N"))),
            "Disk collision must be surfaced without overwriting or dropping data.");
    }

    /// <summary>Throws for a smoke-test invariant violation.</summary>
    /// <param name="condition">Expected condition.</param>
    /// <param name="message">Failure detail.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
