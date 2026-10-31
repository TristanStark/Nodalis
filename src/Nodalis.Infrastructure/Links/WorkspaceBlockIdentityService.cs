using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Links;
using Nodalis.Core.Markdown;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Links;

/// <summary>A proposed, uncommitted change assigning an ID to one Markdown heading.</summary>
public sealed record BlockIdentityAssignmentPreview
{
    /// <summary>Full Markdown path belonging to the selected workspace.</summary>
    public required string DocumentPath { get; init; }
    /// <summary>One-based heading line in the approved source snapshot.</summary>
    public required int HeadingLine { get; init; }
    /// <summary>Stable identity chosen for the heading.</summary>
    public required Guid BlockId { get; init; }
    /// <summary>SHA-256 of the decoded, unchanged Markdown source.</summary>
    public required string OriginalSha256 { get; init; }
    /// <summary>Exact approved output for diff/preview, not written by preparation.</summary>
    public required string UpdatedMarkdown { get; init; }
}

/// <summary>
/// Prepares and applies explicitly approved block ID assignments using Nodalis'
/// existing optimistic file revisions, atomic writing and shared link index.
/// </summary>
public sealed class WorkspaceBlockIdentityService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _workspaceRoot;
    private readonly WorkspaceLinkIndexService _links;

    /// <summary>Creates a service for an existing workspace.</summary>
    /// <param name="workspaceRoot">Absolute or relative workspace directory.</param>
    public WorkspaceBlockIdentityService(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _workspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _links = new WorkspaceLinkIndexService(_workspaceRoot);
    }

    /// <summary>
    /// Prepares an in-memory, user-reviewable edit. Legacy Markdown is not changed.
    /// </summary>
    /// <param name="documentPath">Existing Markdown document in this workspace.</param>
    /// <param name="headingLine">One-based ATX heading line.</param>
    /// <param name="blockId">Proposed nonempty unique GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Source fingerprint and exact proposed Markdown.</returns>
    public async Task<BlockIdentityAssignmentPreview> PrepareAsync(
        string documentPath, int headingLine, Guid blockId,
        CancellationToken cancellationToken = default)
    {
        string path = ValidateDocumentPath(documentPath);
        TextDocumentSession session = await TextDocumentSession.OpenAsync(path, cancellationToken);
        string updated = MarkdownBlockIdentityCodec.InsertMarker(
            session.Content, headingLine, blockId);
        LinkIndexCatalog catalog = await _links.RefreshAsync(cancellationToken);
        if (updated != session.Content && catalog.Blocks.Any(block => block.Id == blockId))
        {
            throw new InvalidOperationException(
                "Un autre bloc porte déjà cet identifiant.");
        }

        return new BlockIdentityAssignmentPreview
        {
            DocumentPath = path,
            HeadingLine = headingLine,
            BlockId = blockId,
            OriginalSha256 = Hash(session.Content),
            UpdatedMarkdown = updated
        };
    }

    /// <summary>
    /// Commits a previously reviewed preview only if its source snapshot and
    /// workspace identity constraints remain valid. Does not auto-approve edits.
    /// </summary>
    /// <param name="approved">The caller's explicitly approved preview.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if a file was written; false for a repeated no-op.</returns>
    public async Task<bool> CommitApprovedAsync(
        BlockIdentityAssignmentPreview approved,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(approved);
        string path = ValidateDocumentPath(approved.DocumentPath);
        SemaphoreSlim gate = Gates.GetOrAdd(
            _workspaceRoot, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            TextDocumentSession session = await TextDocumentSession.OpenAsync(path, cancellationToken);
            if (!string.Equals(Hash(session.Content), approved.OriginalSha256,
                StringComparison.Ordinal))
            {
                throw new ExternalModificationException(path);
            }

            string proposed = MarkdownBlockIdentityCodec.InsertMarker(
                session.Content, approved.HeadingLine, approved.BlockId);
            if (!string.Equals(proposed, approved.UpdatedMarkdown, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "La proposition approuvée ne correspond plus au contenu attendu.");
            }

            if (string.Equals(proposed, session.Content, StringComparison.Ordinal))
            {
                return false;
            }

            LinkIndexCatalog catalog = await _links.RefreshAsync(cancellationToken);
            if (catalog.Blocks.Any(block => block.Id == approved.BlockId))
            {
                throw new InvalidOperationException(
                    "Cet identifiant a été utilisé depuis la préparation.");
            }

            await session.SaveAsync(proposed, cancellationToken);
            // The index is derived and can be reconstructed if a refresh fails.
            await _links.RefreshDocumentAsync(path, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Validates a Markdown path stays inside this workspace.</summary>
    /// <param name="documentPath">Candidate existing Markdown path.</param>
    /// <returns>Normalized absolute document path.</returns>
    private string ValidateDocumentPath(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        string fullPath = Path.GetFullPath(documentPath);
        string relative = Path.GetRelativePath(_workspaceRoot, fullPath);
        if (Path.IsPathRooted(relative) ||
            string.Equals(relative, "..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(fullPath), ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Le document à modifier doit appartenir au workspace.");
        }

        return fullPath;
    }

    /// <summary>Computes the decoded Markdown's SHA-256 fingerprint.</summary>
    /// <param name="markdown">Current exact text.</param>
    /// <returns>Uppercase hexadecimal digest.</returns>
    private static string Hash(string markdown)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
    }
}
