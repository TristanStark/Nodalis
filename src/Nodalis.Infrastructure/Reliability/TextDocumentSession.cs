using System.Security.Cryptography;
using System.Text;

namespace Nodalis.Infrastructure.Reliability;

public sealed class TextDocumentSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);

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

    public static async Task<TextDocumentSession> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await ReadSnapshotAsync(path, cancellationToken);
        return new TextDocumentSession(path, snapshot);
    }

    public async Task<bool> HasExternalChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var current = await ReadRevisionAsync(Path, cancellationToken);
            return current != Revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var current = await ReadRevisionAsync(Path, cancellationToken);

            if (current != Revision)
            {
                throw new ExternalModificationException(Path);
            }

            await AtomicFileWriter.WriteAllTextAsync(
                Path,
                content,
                cancellationToken);

            var updated = await ReadSnapshotAsync(Path, cancellationToken);
            Content = updated.Content;
            Revision = updated.Revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReloadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var snapshot = await ReadSnapshotAsync(Path, cancellationToken);
            Content = snapshot.Content;
            Revision = snapshot.Revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<TextDocumentSnapshot> ReadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var fullPath = System.IO.Path.GetFullPath(path);

        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: true);

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();

        var content = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true)
            .GetString(bytes);

        var info = new FileInfo(fullPath);
        var revision = new FileRevision(
            bytes.LongLength,
            info.LastWriteTimeUtc,
            Convert.ToHexString(SHA256.HashData(bytes)));

        return new TextDocumentSnapshot(content, revision);
    }

    private static async Task<FileRevision> ReadRevisionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var snapshot = await ReadSnapshotAsync(path, cancellationToken);
        return snapshot.Revision;
    }
}
