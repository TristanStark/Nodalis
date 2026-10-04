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
