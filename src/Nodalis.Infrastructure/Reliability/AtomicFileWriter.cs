using System.Security.Cryptography;
using System.Text;

namespace Nodalis.Infrastructure.Reliability;

public static class AtomicFileWriter
{
    /// <summary>
    /// Performs the <c>WriteAllTextAsync</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="content">The <c>content</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static Task WriteAllTextAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return WriteAsync(
            path,
            async (stream, token) =>
            {
                await using global::System.IO.StreamWriter writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 4096,
                    leaveOpen: true);

                await writer.WriteAsync(content.AsMemory(), token);
                await writer.FlushAsync(token);
            },
            cancellationToken);
    }

    /// <summary>
    /// Atomically creates a new UTF-8 file and never replaces a file that appeared
    /// after a preview was approved. The temporary file is cleaned on cancellation
    /// or collision; the caller must ensure the parent directory is allowed.
    /// </summary>
    public static async Task WriteNewAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(directory);
        }

        string temporaryPath = Path.Combine(directory,
            "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (FileStream stream = new FileStream(temporaryPath,
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await using (StreamWriter writer = new StreamWriter(stream,
                    new UTF8Encoding(false), 4096, leaveOpen: true))
                {
                    await writer.WriteAsync(content.AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                }

                stream.Flush(flushToDisk: true);
            }

            // No-overwrite File.Move is the atomic collision check, not File.Exists.
            File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// Replaces a file only while holding a read/share-delete handle on its
    /// exact approved bytes. The handle denies normal in-place writers between
    /// the SHA-256 comparison and the same-volume atomic File.Replace call.
    /// A third-party path rename/replacement can still race this operation.
    /// </summary>
    /// <param name="path">Existing destination, never created implicitly.</param>
    /// <param name="expectedCurrentHash">Approved current byte hash.</param>
    /// <param name="snapshotPath">Immutable, journaled replacement file.</param>
    /// <param name="expectedSnapshotHash">Expected replacement byte hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="beforeReplace">Optional test-only callback under the read lock.</param>
    /// <returns>Completed, verified conditional publication.</returns>
    internal static async Task ReplaceSnapshotIfUnchangedAsync(
        string path,
        string expectedCurrentHash,
        string snapshotPath,
        string expectedSnapshotHash,
        CancellationToken cancellationToken,
        Action? beforeReplace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotPath);
        string destination = Path.GetFullPath(path);
        string snapshot = Path.GetFullPath(snapshotPath);
        string directory = Path.GetDirectoryName(destination)!;
        if (!File.Exists(destination) || !Directory.Exists(directory))
        {
            throw new FileNotFoundException(
                "Le backlink à remplacer n'existe plus.", destination);
        }

        string temporary = Path.Combine(directory,
            "." + Path.GetFileName(destination) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // The journal snapshot is copied to the destination volume first.
            // Only after its SHA-256 is verified do we open the live file.
            await using (FileStream staged = new(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 65536,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await using FileStream originalSnapshot = new(snapshot, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536, useAsync: true);
                await originalSnapshot.CopyToAsync(staged, cancellationToken);
                await staged.FlushAsync(cancellationToken);
                staged.Flush(flushToDisk: true);
            }

            await using (FileStream verifyCopy = new(temporary, FileMode.Open,
                FileAccess.Read, FileShare.Read, 65536, useAsync: true))
            {
                string copyHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(verifyCopy, cancellationToken));
                if (!string.Equals(copyHash, expectedSnapshotHash,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Copie de backlink non conforme au journal.");
                }
            }

            // Do not allow existing in-place writers or new writers to race
            // the verified bytes. FileShare.Delete permits Windows ReplaceFile.
            await using FileStream current = new(destination, FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete,
                65536, useAsync: true);
            string currentHash = Convert.ToHexString(
                await SHA256.HashDataAsync(current, cancellationToken));
            if (!string.Equals(currentHash, expectedCurrentHash,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Backlink modifié au dernier instant : écrasement refusé : " +
                    destination);
            }

            cancellationToken.ThrowIfCancellationRequested();
            beforeReplace?.Invoke();
            File.Replace(temporary, destination,
                destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// Performs the <c>WriteAsync</c> operation.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="writeAsync">The <c>writeAsync</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
    public static async Task WriteAsync(
            string path,
            Func<Stream, CancellationToken, Task> writeAsync,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writeAsync);

        string fullPath = System.IO.Path.GetFullPath(path);
        string? directory = System.IO.Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                $"Cannot determine the directory for '{fullPath}'.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (global::System.IO.FileStream stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await writeAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                File.Replace(
                    temporaryPath,
                    fullPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
