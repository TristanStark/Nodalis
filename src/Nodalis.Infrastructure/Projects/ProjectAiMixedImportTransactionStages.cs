using System.Text;
using System.Text.Json;
using Nodalis.Core.Requirements;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Durable mixed batch staging. This module has no untrusted AI parsing;
/// every entry comes from explicitly reviewed canonical project data.
/// </summary>
public sealed partial class ProjectAiMixedImportTransactionService
{
    /// <summary>Prepares and journals every target before any business write.</summary>
    private async Task<ProjectAiMixedJournal> PrepareAsync(
        string project, ProjectAiPreparedReviewPlan plan,
        IReadOnlyList<ProjectAiPreparedTitleChange> updates,
        IReadOnlyList<ProjectAiPreparedRequirementCreation> creates,
        IReadOnlyList<string> codes, ProjectRequirementCatalog catalog,
        string transaction, CancellationToken token)
    {
        Dictionary<Guid, ProjectRequirementEntry> existing =
            catalog.Requirements.ToDictionary(item => item.Card.Id);
        List<(ProjectAiMixedEntry Entry, string? Original, string Approved,
            string? OriginalPath)> sources = [];
        Guid owner = updates[0].ProjectId;
        string docs = Path.Combine(project, "Documentation");
        string requirements = Path.Combine(docs, "Exigences");
        if (Directory.Exists(docs))
        {
            RequireNormalDirectory(docs);
        }

        if (Directory.Exists(requirements))
        {
            RequireNormalDirectory(requirements);
        }

        int slotIndex = 0;
        foreach (ProjectAiPreparedTitleChange change in updates)
        {
            token.ThrowIfCancellationRequested();
            if (!existing.TryGetValue(change.RequirementId,
                    out ProjectRequirementEntry? entry) ||
                entry.Card.ProjectId != owner ||
                entry.Card.Reference != change.Reference ||
                entry.Card.Title != change.CurrentTitle ||
                entry.RelativePath != change.RelativePath ||
                !string.Equals(entry.SourceSha256, change.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Exigence absente ou modifiée depuis la comparaison : " +
                    change.Reference);
            }

            string path = _sources.ResolveExistingFile(change.RelativePath);
            if (!string.Equals(path, TargetPath(project, change.Reference),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Cible EXI hors projet.");
            }

            byte[] originalBytes = await ReadBoundedAsync(path, token);
            string hash = Hash(originalBytes);
            if (!string.Equals(hash, change.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Exigence modifiée pendant la préparation : " +
                    change.Reference);
            }

            string original = new UTF8Encoding(false, true).GetString(originalBytes);
            ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(original);
            if (card.Id != change.RequirementId || card.ProjectId != owner ||
                card.Reference != change.Reference || card.Title != change.CurrentTitle)
            {
                throw new InvalidDataException(
                    "Métadonnées EXI incohérentes avec la décision humaine.");
            }

            string approved = ProjectAiTitleImportTransactionService.RewriteTitle(
                original, card, change.ReviewedTitle);
            string slot = slotIndex++.ToString("D4") + ".md";
            ProjectAiMixedEntry descriptor = new()
            {
                Operation = "update",
                ProposalId = change.ProposalId,
                RequirementId = change.RequirementId,
                Reference = change.Reference,
                RelativePath = change.RelativePath,
                Slot = slot,
                OriginalSha256 = hash,
                PublishedSha256 = Hash(new UTF8Encoding(false).GetBytes(approved)),
                OriginalTitle = change.CurrentTitle,
                ReviewedTitle = change.ReviewedTitle,
                Reason = change.Reason
            };
            sources.Add((descriptor, original, approved, path));
        }

        for (int i = 0; i < creates.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            ProjectAiPreparedRequirementCreation create = creates[i];
            string reference = codes[i];
            string target = TargetPath(project, reference);
            if (File.Exists(target) || Directory.Exists(target))
            {
                throw new InvalidDataException(
                    "Référence EXI déjà occupée : " + reference);
            }

            ProjectRequirementCard card = new()
            {
                Id = Guid.NewGuid(),
                ProjectId = owner,
                Reference = reference,
                Title = create.ReviewedTitle,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            string markdown = ProjectRequirementMarkdownCodec.Format(card);
            string slot = slotIndex++.ToString("D4") + ".md";
            string relative = Path.GetRelativePath(_workspaceRoot, target)
                .Replace(Path.DirectorySeparatorChar, '/');
            ProjectAiMixedEntry descriptor = new()
            {
                Operation = "create",
                ProposalId = create.ProposalId,
                RequirementId = card.Id,
                Reference = reference,
                RelativePath = relative,
                Slot = slot,
                OriginalSha256 = null,
                PublishedSha256 = Hash(new UTF8Encoding(false).GetBytes(markdown)),
                OriginalTitle = null,
                ReviewedTitle = create.ReviewedTitle,
                Reason = create.Reason
            };
            sources.Add((descriptor, null, markdown, null));
        }

        // A failed preparation with no intent.json is intentionally
        // quarantined for inspection: never silently regenerate GUIDs.
        Directory.CreateDirectory(transaction);
        RequireNormalDirectory(transaction);
        string originalRoot = Path.Combine(transaction, OriginalFolder);
        string approvedRoot = Path.Combine(transaction, ApprovedFolder);
        string receiptRoot = Path.Combine(transaction, ReceiptFolder);
        Directory.CreateDirectory(originalRoot);
        Directory.CreateDirectory(approvedRoot);
        Directory.CreateDirectory(receiptRoot);
        RequireNormalDirectory(originalRoot);
        RequireNormalDirectory(approvedRoot);
        RequireNormalDirectory(receiptRoot);

        foreach ((ProjectAiMixedEntry item, string? original, string approved,
                     string? originalPath) in sources)
        {
            token.ThrowIfCancellationRequested();
            if (originalPath is not null)
            {
                string snapshot = Path.Combine(originalRoot, item.Slot);
                File.Copy(originalPath, snapshot, overwrite: false);
                if (!string.Equals(Hash(await ReadBoundedAsync(snapshot, token)),
                        item.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Source modifiée pendant la préparation du journal.");
                }
            }

            await AtomicFileWriter.WriteNewAllTextAsync(
                Path.Combine(approvedRoot, item.Slot), approved, token);
        }

        ProjectAiMixedJournal journal = new()
        {
            Schema = "nodalis.ai-mixed-exi.v1",
            ProjectId = owner,
            RawResponseSha256 = plan.RawResponseSha256,
            PlanSha256 = PlanFingerprint(plan),
            Entries = sources.Select(source => source.Entry).ToList()
        };
        // After this durable intent, the transaction can be resumed safely.
        await AtomicFileWriter.WriteNewAllTextAsync(
            Path.Combine(transaction, ManifestName),
            JsonSerializer.Serialize(journal,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)), token);
        return journal;
    }

    /// <summary>
    /// Ensures a proposal cannot have a second independent journal and
    /// therefore generate a second official requirement identity.
    /// </summary>
    private static async Task CheckProposalReuseAsync(
        string root, IReadOnlyList<Guid> proposalIds, CancellationToken token)
    {
        string[] directories = Directory.GetDirectories(root, "*",
            SearchOption.TopDirectoryOnly);
        if (directories.Length > 1000)
        {
            throw new InvalidDataException(
                "Trop de journaux IA pour une déduplication sûre.");
        }

        foreach (Guid proposalId in proposalIds)
        {
            if (Directory.Exists(Path.Combine(root, proposalId.ToString("N"))))
            {
                throw new InvalidDataException(
                    "Proposition déjà journalisée individuellement : " +
                    proposalId.ToString("D"));
            }
        }

        foreach (string directory in directories)
        {
            token.ThrowIfCancellationRequested();
            RequireNormalDirectory(directory);
            string path = Path.Combine(directory, ManifestName);
            if (!File.Exists(path))
            {
                continue;
            }

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(path).Length > 65536)
            {
                throw new InvalidDataException(
                    "Journal concurrent non sûr : déduplication bloquée.");
            }

            string contents = await File.ReadAllTextAsync(path, token);
            foreach (Guid proposalId in proposalIds)
            {
                string needle = "\"proposalId\":\"" +
                    proposalId.ToString("D") + "\"";
                if (contents.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Proposition IA déjà incluse dans une autre transaction : " +
                        proposalId.ToString("D"));
                }
            }
        }
    }

    /// <summary>Proves a retained journal matches the EXACT accepted plan.</summary>
    private static void ValidateJournal(
        ProjectAiMixedJournal journal, ProjectAiPreparedReviewPlan plan)
    {
        (ProjectAiPreparedTitleChange[] updates,
            ProjectAiPreparedRequirementCreation[] creates,
            Guid owner) = ValidatePlan(plan);
        string fingerprint = PlanFingerprint(plan);
        if (journal.Schema != "nodalis.ai-mixed-exi.v1" ||
            journal.ProjectId != owner ||
            journal.PlanSha256 != fingerprint ||
            journal.RawResponseSha256 != plan.RawResponseSha256 ||
            journal.Entries is null ||
            journal.Entries.Count != updates.Length + creates.Length ||
            journal.Entries.Any(item => item is null))
        {
            throw new InvalidDataException(
                "Journal mixte sans correspondance avec les choix humains.");
        }

        HashSet<Guid> assignedIds = [];
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> slots = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < journal.Entries.Count; index++)
        {
            ProjectAiMixedEntry item = journal.Entries[index];
            if (item.RequirementId == Guid.Empty ||
                !assignedIds.Add(item.RequirementId) ||
                !ProjectRequirementMarkdownCodec.IsReference(item.Reference) ||
                !codes.Add(item.Reference) ||
                !paths.Add(item.RelativePath) ||
                item.Slot != index.ToString("D4") + ".md" ||
                !slots.Add(item.Slot) ||
                item.PublishedSha256 is not { Length: 64 } ||
                !item.PublishedSha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    "Identité ou instantané ambigu dans le journal mixte.");
            }

            if (index < updates.Length)
            {
                ProjectAiPreparedTitleChange expected = updates[index];
                if (item.Operation != "update" ||
                    item.ProposalId != expected.ProposalId ||
                    item.RequirementId != expected.RequirementId ||
                    item.Reference != expected.Reference ||
                    item.RelativePath != expected.RelativePath ||
                    !string.Equals(item.OriginalSha256,
                        expected.ExpectedSha256, StringComparison.OrdinalIgnoreCase) ||
                    item.OriginalTitle != expected.CurrentTitle ||
                    item.ReviewedTitle != expected.ReviewedTitle ||
                    item.Reason != expected.Reason)
                {
                    throw new InvalidDataException(
                        "Révision EXI non conforme au plan humain.");
                }
            }
            else
            {
                ProjectAiPreparedRequirementCreation expected =
                    creates[index - updates.Length];
                if (item.Operation != "create" ||
                    item.ProposalId != expected.ProposalId ||
                    item.OriginalSha256 is not null ||
                    item.OriginalTitle is not null ||
                    item.ReviewedTitle != expected.ReviewedTitle ||
                    item.Reason != expected.Reason)
                {
                    throw new InvalidDataException(
                        "Création EXI différente de la décision humaine.");
                }
            }
        }
    }

    /// <summary>
    /// Validates all snapshots and all LIVE states before publishing any
    /// individual file. Never auto-restores a deleted card with a receipt.
    /// </summary>
    private async Task<(int Already, List<ProjectAiMixedEntry> Pending)>
        VerifyPendingAsync(string project, string transaction,
            ProjectAiMixedJournal journal, CancellationToken token)
    {
        RequireNormalDirectory(transaction);
        string originalRoot = Path.Combine(transaction, OriginalFolder);
        string approvedRoot = Path.Combine(transaction, ApprovedFolder);
        string receipts = Path.Combine(transaction, ReceiptFolder);
        RequireNormalDirectory(originalRoot);
        RequireNormalDirectory(approvedRoot);
        RequireNormalDirectory(receipts);
        List<ProjectAiMixedEntry> pending = [];
        int already = 0;

        foreach (ProjectAiMixedEntry item in journal.Entries)
        {
            token.ThrowIfCancellationRequested();
            await VerifySnapshotAsync(project, originalRoot, approvedRoot,
                journal.ProjectId, item, token);
            string target = TargetPath(project, item.Reference);
            bool published;
            if (item.Operation == "update")
            {
                string existing = ResolveExistingTarget(project, item);
                string actual = Hash(await ReadBoundedAsync(existing, token));
                published = string.Equals(actual, item.PublishedSha256,
                    StringComparison.OrdinalIgnoreCase);
                if (!published && !string.Equals(actual,
                    item.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Exigence " + item.Reference +
                        " modifiée depuis le journal : aucune suite du lot.");
                }
            }
            else
            {
                string docs = Path.Combine(project, "Documentation");
                string exis = Path.Combine(docs, "Exigences");
                if (Directory.Exists(docs))
                {
                    RequireNormalDirectory(docs);
                }

                if (Directory.Exists(exis))
                {
                    RequireNormalDirectory(exis);
                }

                bool receipt = await VerifyReceiptAsync(
                    receipts, item, token);
                published = File.Exists(target);
                if (published)
                {
                    string existing = ResolveExistingTarget(project, item);
                    if (!string.Equals(Hash(await ReadBoundedAsync(existing, token)),
                        item.PublishedSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "Référence EXI occupée ou modifiée : " + item.Reference);
                    }
                }
                else if (receipt)
                {
                    throw new InvalidDataException(
                        "Exigence précédemment créée puis supprimée : " +
                        item.Reference + " ; aucune recréation automatique.");
                }
            }

            if (published)
            {
                already++;
            }
            else
            {
                pending.Add(item);
            }
        }

        return (already, pending);
    }

    /// <summary>
    /// Proof of exact bytes and strictly title-only updates; a forged SHA in
    /// a retained manifest cannot authorize arbitrary Markdown modifications.
    /// </summary>
    private static async Task VerifySnapshotAsync(
        string project, string originals, string approvedRoot, Guid projectId,
        ProjectAiMixedEntry item, CancellationToken token)
    {
        string approvedPath = Path.Combine(approvedRoot, item.Slot);
        if (!File.Exists(approvedPath) ||
            (File.GetAttributes(approvedPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Instantané EXI manquant ou non sûr.");
        }

        byte[] approvedBytes = await ReadBoundedAsync(approvedPath, token);
        if (!string.Equals(Hash(approvedBytes), item.PublishedSha256,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Instantané publié corrompu dans le journal mixte.");
        }

        string approved = new UTF8Encoding(false, true).GetString(approvedBytes);
        if (item.Operation == "update")
        {
            string originalPath = Path.Combine(originals, item.Slot);
            if (!File.Exists(originalPath) ||
                (File.GetAttributes(originalPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Original EXI manquant.");
            }

            byte[] beforeBytes = await ReadBoundedAsync(originalPath, token);
            if (!string.Equals(Hash(beforeBytes), item.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Original EXI altéré.");
            }

            string before = new UTF8Encoding(false, true).GetString(beforeBytes);
            ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(before);
            if (card.Id != item.RequirementId ||
                card.ProjectId != projectId ||
                card.Reference != item.Reference ||
                card.Title != item.OriginalTitle ||
                approved != ProjectAiTitleImportTransactionService
                    .RewriteTitle(before, card, item.ReviewedTitle))
            {
                throw new InvalidDataException(
                    "L'instantané IA modifie d'autres données que le titre EXI.");
            }
        }
        else if (item.Operation == "create")
        {
            ProjectRequirementCard card = ProjectRequirementMarkdownCodec.Parse(approved);
            if (card.Id != item.RequirementId ||
                card.ProjectId != projectId ||
                card.Reference != item.Reference ||
                card.Title != item.ReviewedTitle ||
                card.Validation != ProjectRequirementValidation.Draft ||
                card.Implementation != ProjectRequirementImplementation.NotStarted ||
                card.Verification != ProjectRequirementVerification.NotVerified ||
                card.Kind != ProjectRequirementKind.Unclassified ||
                approved != ProjectRequirementMarkdownCodec.Format(card))
            {
                throw new InvalidDataException(
                    "L'instantané de création contient des données non autorisées.");
            }
        }
        else
        {
            throw new InvalidDataException("Opération IA inconnue.");
        }
    }

    /// <summary>Validates a persistent proof of a previously created EXI.</summary>
    private static async Task<bool> VerifyReceiptAsync(
        string receipts, ProjectAiMixedEntry item, CancellationToken token)
    {
        string path = Path.Combine(receipts, item.Slot + ".json");
        if (!File.Exists(path))
        {
            return false;
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            new FileInfo(path).Length > 1024)
        {
            throw new InvalidDataException("Preuve de création illisible.");
        }

        string json = await File.ReadAllTextAsync(path, token);
        string? fingerprint;
        try
        {
            fingerprint = JsonSerializer.Deserialize<string>(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Preuve de création corrompue.", exception);
        }

        if (fingerprint != item.PublishedSha256)
        {
            throw new InvalidDataException(
                "Preuve de création incohérente avec le journal.");
        }

        return true;
    }

    /// <summary>Stores a no-overwrite creation receipt after publication.</summary>
    private static async Task WriteReceiptAsync(
        string transaction, ProjectAiMixedEntry item, CancellationToken token)
    {
        string folder = Path.Combine(transaction, ReceiptFolder);
        RequireNormalDirectory(folder);
        string path = Path.Combine(folder, item.Slot + ".json");
        if (await VerifyReceiptAsync(folder, item, token))
        {
            return;
        }

        await AtomicFileWriter.WriteNewAllTextAsync(
            path, JsonSerializer.Serialize(item.PublishedSha256), token);
    }

    /// <summary>
    /// Rechecks complete post-state before announcing success, and records
    /// delayed receipts for creations committed just before an interruption.
    /// </summary>
    private async Task VerifyAllPublishedAsync(
        string project, ProjectAiMixedJournal journal, CancellationToken token)
    {
        string transaction = TransactionDirectory(project, journal.PlanSha256);
        foreach (ProjectAiMixedEntry item in journal.Entries)
        {
            string target = item.Operation == "update"
                ? ResolveExistingTarget(project, item)
                : _sources.ResolveExistingFile(item.RelativePath);
            if (!string.Equals(Hash(await ReadBoundedAsync(target, token)),
                    item.PublishedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Lot mixte non stabilisé : " + item.Reference);
            }

            if (item.Operation == "create")
            {
                await WriteReceiptAsync(transaction, item, token);
            }
        }
    }

    /// <summary>
    /// Rebuilds original human authorization from a retained journal only
    /// after verifying schema, plan fingerprint, owner and all snapshots.
    /// </summary>
    private async Task<(string Project, ProjectAiMixedJournal Journal,
        ProjectAiPreparedReviewPlan Plan)> ReadRecoveryAsync(
        string projectDirectory, string key, CancellationToken token)
    {
        if (key is not { Length: 64 } || !key.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Identifiant de lot mixte invalide.");
        }

        string project = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectDirectory));
        string transaction = TransactionDirectory(project, key);
        RequireNormalDirectory(transaction);
        ProjectAiMixedJournal journal = await ReadJournalAsync(
            Path.Combine(transaction, ManifestName), token);
        if (journal.Schema != "nodalis.ai-mixed-exi.v1" ||
            journal.ProjectId == Guid.Empty ||
            journal.PlanSha256 != key ||
            journal.RawResponseSha256 is not { Length: 64 } ||
            !journal.RawResponseSha256.All(Uri.IsHexDigit) ||
            journal.Entries is null ||
            journal.Entries.Any(item => item is null))
        {
            throw new InvalidDataException("Journal mixte incomplet.");
        }

        ProjectAiPreparedTitleChange[] updates =
            journal.Entries.Where(item => item.Operation == "update")
                .Select(item => new ProjectAiPreparedTitleChange(
                    item.ProposalId, item.RequirementId, journal.ProjectId,
                    item.Reference, item.RelativePath, item.OriginalSha256!,
                    item.OriginalTitle!, item.ReviewedTitle, item.Reason))
                .ToArray();
        ProjectAiPreparedRequirementCreation[] creates =
            journal.Entries.Where(item => item.Operation == "create")
                .Select(item => new ProjectAiPreparedRequirementCreation(
                    item.ProposalId, journal.ProjectId, item.ReviewedTitle,
                    item.Reason)).ToArray();
        ProjectAiPreparedReviewPlan plan = new(
            journal.RawResponseSha256, updates, updates.Length + creates.Length,
            0, 0)
        {
            Creations = creates
        };
        ValidateJournal(journal, plan);
        await VerifyProjectAsync(project, journal.ProjectId, token);
        return (project, journal, plan);
    }
}
