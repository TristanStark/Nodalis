using System.Security.Cryptography;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Projects;

/// <summary>One durable source/published snapshot of a backlink outside the project.</summary>
public sealed record ComplexMigrationExternalLinkFile
{
    /// <summary>Workspace-relative Markdown source path, unchanged by migration.</summary>
    public required string WorkspaceRelativePath { get; init; }

    /// <summary>Opaque numeric slot of the retained two copies.</summary>
    public required string Slot { get; init; }

    /// <summary>Original byte-level SHA-256.</summary>
    public required string OriginalHash { get; init; }

    /// <summary>Proposed byte-level SHA-256.</summary>
    public required string PublishedHash { get; init; }
}

/// <summary>
/// Independently stages every affected external Markdown document, and
/// replays either direction with hash-controlled, per-file atomic replacement.
/// The project journal owns authorization, phases, and pending recovery.
/// </summary>
internal sealed class ComplexMigrationExternalLinkTransactionService
{
    private const string OriginalDirectoryName = "external-original";
    private const string StagingDirectoryName = "external-staging";
    private readonly string _workspaceRoot;

    /// <summary>Constructs an executor restricted to one workspace.</summary>
    /// <param name="workspaceRoot">Workspace to protect.</param>
    internal ComplexMigrationExternalLinkTransactionService(string workspaceRoot)
    {
        _workspaceRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspaceRoot));
    }

    /// <summary>Creates exact immutable copies before the project journal is written.</summary>
    /// <param name="transactionDirectory">Private transaction folder.</param>
    /// <param name="rewrites">Frozen external link edits approved by the user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Expected original and published bytes for each affected file.</returns>
    internal async Task<IReadOnlyList<ComplexMigrationExternalLinkFile>> PrepareAsync(
        string transactionDirectory,
        IReadOnlyList<ComplexMigrationLinkRewrite> rewrites,
        CancellationToken cancellationToken)
    {
        if (rewrites.Count == 0)
        {
            return [];
        }

        string originalRoot = Path.Combine(transactionDirectory, OriginalDirectoryName);
        string stageRoot = Path.Combine(transactionDirectory, StagingDirectoryName);
        Directory.CreateDirectory(originalRoot);
        Directory.CreateDirectory(stageRoot);
        List<ComplexMigrationLinkRewrite> staged = [];
        List<ComplexMigrationExternalLinkFile> items = [];
        int index = 0;
        foreach (IGrouping<string, ComplexMigrationLinkRewrite> group in
                 rewrites.GroupBy(item => item.WorkspaceRelativePath,
                     StringComparer.OrdinalIgnoreCase)
                 .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveLivePath(group.Key);
            string hash = await HashAsync(source, cancellationToken);
            if (group.Any(item => !string.Equals(item.SourceHash, hash,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    "Backlink externe modifié depuis le plan : " + group.Key);
            }

            string slot = index.ToString("D6") + ".md";
            index++;
            string original = Path.Combine(originalRoot, slot);
            string stage = Path.Combine(stageRoot, slot);
            File.Copy(source, original, overwrite: false);
            File.Copy(source, stage, overwrite: false);
            staged.AddRange(group.Select(item => item with
            {
                ProjectRelativePath = slot
            }));
            items.Add(new ComplexMigrationExternalLinkFile
            {
                WorkspaceRelativePath = group.Key,
                Slot = slot,
                OriginalHash = hash,
                PublishedHash = string.Empty
            });
        }

        await ComplexProjectMigrationLinkRewriteApplier.ApplyAsync(
            stageRoot, staged, cancellationToken);

        List<ComplexMigrationExternalLinkFile> results = [];
        foreach (ComplexMigrationExternalLinkFile item in items)
        {
            results.Add(item with
            {
                PublishedHash = await HashAsync(Path.Combine(stageRoot, item.Slot),
                    cancellationToken)
            });
        }

        // Neither copy may be corrupted or mutated while preparing its sibling.
        await AssertBackupsAsync(transactionDirectory, results, cancellationToken);
        return results;
    }

    /// <summary>
    /// Verifies all live backlinks match the expected publication state before
    /// starting a rollback; never changes a file.
    /// </summary>
    /// <param name="directory">Journal transaction folder.</param>
    /// <param name="files">Frozen external document entries.</param>
    /// <param name="published">Expected final state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task AssertStateAsync(string directory,
        IReadOnlyList<ComplexMigrationExternalLinkFile> files,
        bool published, CancellationToken cancellationToken)
    {
        await AssertBackupsAsync(directory, files, cancellationToken);
        foreach (ComplexMigrationExternalLinkFile item in files)
        {
            string source = ResolveLivePath(item.WorkspaceRelativePath);
            string hash = await HashAsync(source, cancellationToken);
            if (!string.Equals(hash, published ? item.PublishedHash : item.OriginalHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Backlink externe modifié : refus pour protéger " +
                    item.WorkspaceRelativePath);
            }
        }
    }

    /// <summary>
    /// Prevalidates every external source before touching any of them; a human
    /// change to a later file must not cause an avoidable partial publication.
    /// Both old and new hashes are permitted for interrupted, idempotent replays.
    /// </summary>
    /// <param name="directory">Private transaction folder.</param>
    /// <param name="files">All reviewed backlinks.</param>
    /// <param name="publish">Requested publication or restoration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task AssertReplayableAsync(string directory,
        IReadOnlyList<ComplexMigrationExternalLinkFile> files,
        bool publish, CancellationToken cancellationToken)
    {
        await AssertBackupsAsync(directory, files, cancellationToken);
        foreach (ComplexMigrationExternalLinkFile item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveLivePath(item.WorkspaceRelativePath);
            string hash = await HashAsync(source, cancellationToken);
            string expected = publish ? item.PublishedHash : item.OriginalHash;
            string opposite = publish ? item.OriginalHash : item.PublishedHash;
            if (!string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(hash, opposite, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Backlink externe modifié : aucune réécriture partielle autorisée : " +
                    item.WorkspaceRelativePath);
            }
        }
    }

    /// <summary>
    /// Idempotently converges current external files to the approved version.
    /// First validates the full inventory, then checks each file again before
    /// its atomic replacement. A concurrent editor may still race this check;
    /// a changed later file leaves an explicit pending recovery journal.
    /// </summary>
    /// <param name="directory">Journal transaction folder.</param>
    /// <param name="files">Retained snapshot inventory.</param>
    /// <param name="publish">True for new backlinks, false for original bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="checkpoint">Optional injected interruption after each file.</param>
    internal async Task ReplayAsync(string directory,
        IReadOnlyList<ComplexMigrationExternalLinkFile> files, bool publish,
        CancellationToken cancellationToken, Action<string>? checkpoint = null)
    {
        await AssertReplayableAsync(directory, files, publish, cancellationToken);
        foreach (ComplexMigrationExternalLinkFile item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string source = ResolveLivePath(item.WorkspaceRelativePath);
            string hash = await HashAsync(source, cancellationToken);
            string expected = publish ? item.PublishedHash : item.OriginalHash;
            string opposite = publish ? item.OriginalHash : item.PublishedHash;
            if (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(hash, opposite, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Backlink externe modifié : récupération manuelle requise : " +
                    item.WorkspaceRelativePath);
            }

            string snapshot = Path.Combine(directory, publish
                ? StagingDirectoryName : OriginalDirectoryName, item.Slot);
            await AtomicFileWriter.WriteAsync(source,
                async (stream, token) =>
                {
                    await using FileStream input = new(snapshot, FileMode.Open,
                        FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                    await input.CopyToAsync(stream, token);
                }, cancellationToken);

            if (!string.Equals(await HashAsync(source, CancellationToken.None),
                expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Backlink externe publié avec empreinte inattendue : " +
                    item.WorkspaceRelativePath);
            }

            checkpoint?.Invoke(publish
                ? "external-published" : "external-rolled-back");
        }
    }

    /// <summary>Verifies all retained archives and slot identifiers.</summary>
    /// <param name="directory">Private transaction directory.</param>
    /// <param name="files">Journal entries.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task AssertBackupsAsync(string directory,
        IReadOnlyList<ComplexMigrationExternalLinkFile> files,
        CancellationToken cancellationToken)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> slots = new(StringComparer.OrdinalIgnoreCase);
        foreach (ComplexMigrationExternalLinkFile item in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!paths.Add(item.WorkspaceRelativePath) ||
                !slots.Add(item.Slot) || item.Slot.Length != 9 ||
                !item.Slot.EndsWith(".md", StringComparison.Ordinal) ||
                !item.Slot[..6].All(char.IsAsciiDigit) ||
                item.OriginalHash.Length != 64 || item.PublishedHash.Length != 64 ||
                !item.OriginalHash.All(Uri.IsHexDigit) ||
                !item.PublishedHash.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException(
                    "Journal des backlinks incohérent ou falsifié.");
            }

            string original = Path.Combine(directory, OriginalDirectoryName, item.Slot);
            string staged = Path.Combine(directory, StagingDirectoryName, item.Slot);
            if (!string.Equals(await HashAsync(original, cancellationToken),
                    item.OriginalHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(await HashAsync(staged, cancellationToken),
                    item.PublishedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Sauvegarde de backlink corrompue : " +
                    item.WorkspaceRelativePath);
            }
        }
    }

    /// <summary>Rejects traversal and junctions in live workspace paths.</summary>
    /// <param name="relative">Journal-relative Markdown path.</param>
    /// <returns>Validated full file path.</returns>
    private string ResolveLivePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) ||
            !relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith('/') || relative.Contains('\\') ||
            relative.Split('/').Any(part => part is "" or "." or ".." ||
                part.Contains(':')))
        {
            throw new InvalidDataException("Chemin de backlink externe non portable.");
        }

        string full = Path.GetFullPath(Path.Combine(_workspaceRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_workspaceRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            throw new InvalidDataException("Backlink externe absent ou hors workspace.");
        }

        string ancestor = full;
        while (!string.Equals(ancestor, _workspaceRoot,
                   StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Lien symbolique interdit dans un backlink externe.");
            }

            ancestor = Path.GetDirectoryName(ancestor)!;
        }

        return full;
    }

    /// <summary>Computes byte-level SHA-256 without changing the file.</summary>
    /// <param name="path">Path to fingerprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Uppercase hexadecimal hash.</returns>
    private static async Task<string> HashAsync(string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(
            input, cancellationToken));
    }
}
