using System.Security.Cryptography;
using System.Text;

namespace Nodalis.Infrastructure.Reliability;

public sealed class TextDocumentSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="TextDocumentSession"/>.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="snapshot">The <c>snapshot</c> value.</param>
    private TextDocumentSession(
            string path,
            TextDocumentSnapshot snapshot)
    {
        Path = System.IO.Path.GetFullPath(path);
        Content = snapshot.Content;
        Revision = snapshot.Revision;
    }

    public string Path { get; }

    public string Content { get; private set; }

    public FileRevision Revision { get; private set; }

    /// <summary>
    /// Performs the <c>OpenAsync</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static async Task<TextDocumentSession> OpenAsync(
            string path,
            CancellationToken cancellationToken = default)
    {
        global::Nodalis.Infrastructure.Reliability.TextDocumentSnapshot snapshot = await ReadSnapshotAsync(path, cancellationToken);
        return new TextDocumentSession(path, snapshot);
    }

    /// <summary>
    /// Performs the <c>HasExternalChangesAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task<bool> HasExternalChangesAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            global::Nodalis.Infrastructure.Reliability.FileRevision current = await ReadRevisionAsync(Path, cancellationToken);
            return current != Revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs the <c>SaveAsync</c> operation.
    /// </summary>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task SaveAsync(
            string content,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            global::Nodalis.Infrastructure.Reliability.FileRevision current = await ReadRevisionAsync(Path, cancellationToken);

            if (current != Revision)
            {
                throw new ExternalModificationException(Path);
            }

            await AtomicFileWriter.WriteAllTextAsync(
                Path,
                content,
                cancellationToken);

            global::Nodalis.Infrastructure.Reliability.TextDocumentSnapshot updated = await ReadSnapshotAsync(Path, cancellationToken);
            Content = updated.Content;
            Revision = updated.Revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs the <c>ReloadAsync</c> operation.
    /// </summary>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public async Task ReloadAsync(
            CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            global::Nodalis.Infrastructure.Reliability.TextDocumentSnapshot snapshot = await ReadSnapshotAsync(Path, cancellationToken);
            Content = snapshot.Content;
            Revision = snapshot.Revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Performs the <c>ReadSnapshotAsync</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<TextDocumentSnapshot> ReadSnapshotAsync(
            string path,
            CancellationToken cancellationToken)
    {
        string fullPath = System.IO.Path.GetFullPath(path);

        await using global::System.IO.FileStream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);

        using global::System.IO.MemoryStream memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        byte[] bytes = memory.ToArray();

        string content = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true)
            .GetString(bytes);

        global::System.IO.FileInfo info = new FileInfo(fullPath);
        global::Nodalis.Infrastructure.Reliability.FileRevision revision = new FileRevision(
            bytes.LongLength,
            info.LastWriteTimeUtc,
            Convert.ToHexString(SHA256.HashData(bytes)));

        return new TextDocumentSnapshot(content, revision);
    }

    /// <summary>
    /// Performs the <c>ReadRevisionAsync</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    private static async Task<FileRevision> ReadRevisionAsync(
            string path,
            CancellationToken cancellationToken)
    {
        global::Nodalis.Infrastructure.Reliability.TextDocumentSnapshot snapshot = await ReadSnapshotAsync(path, cancellationToken);
        return snapshot.Revision;
    }
}
