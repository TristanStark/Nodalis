using System.Security.Cryptography;
using Nodalis.Core.Links;
using Nodalis.Infrastructure.Links;
using Nodalis.Infrastructure.Persistence;

namespace Nodalis.Infrastructure.Projects;

/// <summary>
/// Immutable lineage for one indexed Markdown document relocated by a
/// transaction. Both hashes refer to original file bytes, not normalized text.
/// </summary>
public sealed record ComplexMigrationDocumentIdentity
{
    /// <summary>Stable document GUID from the index before migration.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>Original workspace-relative Markdown path.</summary>
    public required string OriginalRelativePath { get; init; }

    /// <summary>Published workspace-relative Markdown path.</summary>
    public required string PublishedRelativePath { get; init; }

    /// <summary>Original SHA-256 file bytes.</summary>
    public required string OriginalContentHash { get; init; }

    /// <summary>Published SHA-256 file bytes.</summary>
    public required string PublishedContentHash { get; init; }
}

/// <summary>
/// A directional, fingerprinted move from the last published/rolled-back journal.
/// </summary>
/// <param name="DocumentId">Canonical ID before and after the operation.</param>
/// <param name="PreviousPath">Location indexed before reconciliation.</param>
/// <param name="CurrentPath">Current physical location.</param>
/// <param name="CurrentHash">Expected bytes at the current location.</param>
internal sealed record ComplexMigrationIdentityRoute(
    Guid DocumentId, string PreviousPath, string CurrentPath, string CurrentHash);

/// <summary>
/// Bridges the durable Complex migration journal to the rebuildable link index,
/// without modifying any Markdown or creating a second identity registry.
/// </summary>
public sealed class ComplexMigrationDocumentIdentityService
{
    private const string TransactionDirectoryName = ".complex-v2-migrations";
    private readonly string _workspaceRoot;
    private readonly string _transactionsRoot;

    /// <summary>Creates a workspace-scoped lineage reader and snapshot writer.</summary>
    /// <param name="workspaceRoot">Canonical workspace directory.</param>
    public ComplexMigrationDocumentIdentityService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
        _transactionsRoot = Path.Combine(_workspaceRoot,
            WorkspaceLayout.ImportsDirectoryName, TransactionDirectoryName);
    }

    /// <summary>
    /// Captures identities before publication, after the staging transformation.
    /// An index refresh may update only the reconstructible workspace cache.
    /// An incomplete test fixture without a workspace manifest has no index.
    /// </summary>
    /// <param name="projectRoot">Still-active original project.</param>
    /// <param name="stageRoot">Finished private staging project.</param>
    /// <param name="moves">Frozen candidate moves.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document GUIDs and byte hashes recorded in the journal.</returns>
    public async Task<IReadOnlyList<ComplexMigrationDocumentIdentity>> CaptureAsync(
        string projectRoot,
        string stageRoot,
        IReadOnlyList<ComplexMigrationMove> moves,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path.Combine(_workspaceRoot,
                WorkspaceLayout.WorkspaceManifestFileName)))
        {
            return [];
        }

        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        string stage = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(stageRoot));
        LinkIndexCatalog catalog = await new WorkspaceLinkIndexService(_workspaceRoot)
            .RefreshAsync(cancellationToken);

        if (catalog.Targets.GroupBy(target => target.Id)
            .Any(group => group.Key == Guid.Empty || group.Count() > 1))
        {
            throw new InvalidDataException(
                "Collision d'identités dans l'index : migration refusée.");
        }

        List<ComplexMigrationDocumentIdentity> entries = [];
        foreach (LinkTargetEntry document in catalog.Targets.Where(target =>
                     target.Kind == LinkTargetKind.Document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string originalPath = Path.GetFullPath(Path.Combine(_workspaceRoot,
                document.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(root, originalPath))
            {
                continue;
            }

            string projectRelative = Path.GetRelativePath(root, originalPath)
                .Replace('\\', '/');
            string mapped = MapRelativePath(projectRelative, moves);
            if (string.Equals(projectRelative, mapped,
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string publishedStagePath = Path.GetFullPath(Path.Combine(stage,
                mapped.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(stage, publishedStagePath) ||
                !File.Exists(originalPath) || !File.Exists(publishedStagePath))
            {
                throw new InvalidDataException(
                    "Document déplacé non résolu : " + document.RelativePath);
            }

            string originalHash = await FingerprintFileAsync(originalPath,
                cancellationToken);
            if (!string.Equals(originalHash, document.ContentHash,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Index périmé pour un document déplacé : " +
                    document.RelativePath);
            }

            string publishedRelativePath = Path.GetRelativePath(_workspaceRoot,
                Path.Combine(root, mapped.Replace('/',
                    Path.DirectorySeparatorChar))).Replace('\\', '/');
            entries.Add(new ComplexMigrationDocumentIdentity
            {
                DocumentId = document.Id,
                OriginalRelativePath = document.RelativePath,
                PublishedRelativePath = publishedRelativePath,
                OriginalContentHash = originalHash,
                PublishedContentHash = await FingerprintFileAsync(
                    publishedStagePath, cancellationToken)
            });
        }

        return entries.OrderBy(entry => entry.OriginalRelativePath,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Reads only the most recent final journal for each project; old journals
    /// cannot retarget an identity after a newer rollback or migration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current target path to expected historical mapping.</returns>
    internal async Task<IReadOnlyDictionary<string, ComplexMigrationIdentityRoute>>
        ReadActiveRoutesAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, ComplexProjectMigrationJournal> newest =
            new(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_transactionsRoot))
        {
            return new Dictionary<string, ComplexMigrationIdentityRoute>(
                StringComparer.OrdinalIgnoreCase);
        }

        foreach (string directory in Directory.EnumerateDirectories(
                     _transactionsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Jonction interdite dans les journaux de migration.");
            }

            string path = Path.Combine(directory, "journal.json");
            if (!File.Exists(path))
            {
                continue;
            }

            ComplexProjectMigrationJournal journal =
                await AtomicJsonFile.ReadAsync<ComplexProjectMigrationJournal>(
                    path, cancellationToken);
            if (!string.Equals(journal.TransactionId.ToString("N"),
                Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Journal d'identités de migration incohérent.");
            }

            if (journal.Phase is not ("completed" or "rolled-back"))
            {
                continue;
            }

            string projectRelative = NormalizeRelative(journal.ProjectRelativePath);
            if (!newest.TryGetValue(projectRelative, out
                    ComplexProjectMigrationJournal? current) ||
                journal.CreatedUtc > current.CreatedUtc ||
                (journal.CreatedUtc == current.CreatedUtc &&
                    journal.TransactionId.CompareTo(current.TransactionId) > 0))
            {
                newest[projectRelative] = journal;
            }
        }

        Dictionary<string, ComplexMigrationIdentityRoute> routes =
            new(StringComparer.OrdinalIgnoreCase);
        HashSet<Guid> ids = [];
        foreach (ComplexProjectMigrationJournal journal in newest.Values)
        {
            bool reversed = journal.Phase == "rolled-back";
            foreach (ComplexMigrationDocumentIdentity entry in
                     journal.DocumentIdentities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string previous = NormalizeRelative(reversed
                    ? entry.PublishedRelativePath : entry.OriginalRelativePath);
                string current = NormalizeRelative(reversed
                    ? entry.OriginalRelativePath : entry.PublishedRelativePath);
                string hash = reversed
                    ? entry.OriginalContentHash : entry.PublishedContentHash;
                if (entry.DocumentId == Guid.Empty || hash.Length != 64 ||
                    !hash.All(Uri.IsHexDigit) || !ids.Add(entry.DocumentId) ||
                    !routes.TryAdd(current, new ComplexMigrationIdentityRoute(
                        entry.DocumentId, previous, current, hash)))
                {
                    throw new InvalidDataException(
                        "Collision ou empreinte incorrecte dans le journal d'identités.");
                }
            }
        }

        return routes;
    }

    private static string MapRelativePath(string relative,
        IReadOnlyList<ComplexMigrationMove> moves)
    {
        foreach (ComplexMigrationMove move in moves.Where(item =>
                     item.State == ComplexMigrationMoveState.Candidate))
        {
            if (string.Equals(relative, move.From,
                StringComparison.OrdinalIgnoreCase))
            {
                return move.To;
            }

            if (relative.StartsWith(move.From + "/", StringComparison.OrdinalIgnoreCase))
            {
                return move.To + relative[move.From.Length..];
            }
        }

        return relative;
    }

    private static string NormalizeRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) ||
            relative.Contains('\\') || relative.StartsWith('/') ||
            relative.Split('/').Any(part => part.Length == 0 ||
                part is "." or ".." || part.Contains(':')))
        {
            throw new InvalidDataException("Chemin de journal non portable.");
        }

        return relative;
    }

    private static bool IsWithin(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static async Task<string> FingerprintFileAsync(
        string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(
            stream, cancellationToken));
    }
}
