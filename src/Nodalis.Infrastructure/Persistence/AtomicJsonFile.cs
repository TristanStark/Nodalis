using System.Text.Json;
using Nodalis.Infrastructure.Reliability;

namespace Nodalis.Infrastructure.Persistence;

internal static class AtomicJsonFile
{
    public static async Task<T> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            JsonDefaults.Options,
            cancellationToken);

        return value ?? throw new InvalidDataException(
            $"JSON file '{path}' contained no value.");
    }

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
