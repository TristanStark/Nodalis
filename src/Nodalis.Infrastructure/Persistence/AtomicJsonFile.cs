using System.Text.Json;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Persistence;

internal static class AtomicJsonFile
{
    /// <summary>
    /// Performs the <c>ReadAsync</c> operation.
    /// </summary>
    /// <typeparam name="T">The <c>T</c> type.</typeparam>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static async Task<T> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using global::System.IO.FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        T? value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonDefaults.Options,
            cancellationToken);

        return value ?? throw new InvalidDataException(
            $"JSON file '{path}' contained no value.");
    }

    /// <summary>
    /// Performs the <c>WriteAsync</c> operation.
    /// </summary>
    /// <typeparam name="T">The <c>T</c> type.</typeparam>
    /// <param name="path">The <c>path</c> value.</param>
    /// <param name="value">The <c>value</c> value.</param>
    /// <param name="cancellationToken">The <c>cancellationToken</c> value.</param>
    /// <returns>The result of the operation.</returns>
public static Task WriteAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        return AtomicFileWriter.WriteAsync(
            path,
            async (stream, token) =>
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    JsonDefaults.Options,
                    token);
            },
            cancellationToken);
    }
}
