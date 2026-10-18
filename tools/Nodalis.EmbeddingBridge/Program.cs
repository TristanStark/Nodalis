using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Nodalis.EmbeddingBridge;

/// <summary>
/// Bundled optional bridge for Ollama embeddings. The only possible
/// destination is the numeric IPv4 loopback address at Ollama's local port.
/// </summary>
internal static class Program
{
    private const string OllamaEndpoint = "http://127.0.0.1:11434/api/embed";
    private const int MaxInputCharacters = 1_000_000;
    private const int MaxResponseBytes = 8_000_000;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed record BridgeRequest
    {
        public string ModelId { get; init; } = "";
        public List<string> Texts { get; init; } = [];
    }

    private sealed record OllamaRequest(
        string Model, IReadOnlyList<string> Input, bool Truncate = false);

    private sealed record OllamaResponse
    {
        public string Model { get; init; } = "";
        public List<float[]> Embeddings { get; init; } = [];
    }

    private sealed record BridgeResponse(
        string ModelId, int Dimensions, IReadOnlyList<float[]> Vectors);

    /// <summary>Processes one JSON embeddings batch, without any network call on startup.</summary>
    /// <param name="args">Only --self-test is recognized; all other flags are rejected.</param>
    /// <returns>Zero on success or an error status without source text.</returns>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(
                new BridgeResponse("bridge-self-test", 3, [[1f, 0f, 0f]]), Json));
            return 0;
        }

        if (args.Length != 0)
        {
            Console.Error.WriteLine("Unsupported embedding bridge argument.");
            return 2;
        }

        try
        {
            string body = await ReadBoundedTextAsync(Console.In, MaxInputCharacters);
            BridgeRequest? source = JsonSerializer.Deserialize<BridgeRequest>(body, Json);
            if (source is null || string.IsNullOrWhiteSpace(source.ModelId) ||
                source.ModelId.Length > 160 || source.Texts is null ||
                source.Texts.Count is < 1 or > 32 ||
                source.Texts.Any(value => string.IsNullOrWhiteSpace(value) ||
                    value.Length > 8192))
            {
                Console.Error.WriteLine("Invalid local embedding model or batch.");
                return 2;
            }

            using HttpClientHandler handler = new()
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                UseCookies = false
            };
            using HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(290)
            };
            using HttpRequestMessage request = new(
                HttpMethod.Post, OllamaEndpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(
                        new OllamaRequest(source.ModelId, source.Texts), Json),
                    Encoding.UTF8, "application/json")
            };
            using HttpResponseMessage response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    "Ollama local did not accept the embedding request.");
                return 3;
            }

            using Stream bodyStream = await response.Content.ReadAsStreamAsync();
            byte[] responseBytes = await ReadBoundedBytesAsync(
                bodyStream, MaxResponseBytes);
            OllamaResponse? embeddings =
                JsonSerializer.Deserialize<OllamaResponse>(responseBytes, Json);
            if (embeddings is null || embeddings.Embeddings is null ||
                embeddings.Embeddings.Count != source.Texts.Count ||
                !SameModel(embeddings.Model, source.ModelId))
            {
                Console.Error.WriteLine(
                    "Ollama local returned an incompatible embedding model or batch.");
                return 3;
            }

            int dimension = embeddings.Embeddings[0]?.Length ?? 0;
            if (dimension is < 1 or > 4096 ||
                embeddings.Embeddings.Any(vector => vector is null ||
                    vector.Length != dimension || vector.All(value => value == 0) ||
                    vector.Any(value => !float.IsFinite(value))))
            {
                Console.Error.WriteLine("Ollama local returned invalid vectors.");
                return 3;
            }

            Console.Out.WriteLine(JsonSerializer.Serialize(
                new BridgeResponse(source.ModelId, dimension,
                    embeddings.Embeddings), Json));
            return 0;
        }
        catch (Exception error) when (error is IOException or HttpRequestException or
            JsonException or OperationCanceledException or InvalidDataException)
        {
            // Never print the request, the document text, provider JSON or exception
            // details, which could include sensitive local source fragments.
            Console.Error.WriteLine(
                "Local embedding service unavailable, malformed or over its limits.");
            return 3;
        }
    }

    /// <summary>Allows Ollama to report the requested model with an optional latest tag.</summary>
    /// <param name="returned">Model declared by the local engine.</param>
    /// <param name="requested">Model the user explicitly configured.</param>
    /// <returns>True only for the same model.</returns>
    private static bool SameModel(string returned, string requested)
    {
        string canonical = requested.EndsWith(":latest", StringComparison.Ordinal)
            ? requested[..^7] : requested;
        return returned == requested || returned == canonical + ":latest" ||
            returned == canonical;
    }

    /// <summary>Limits the process's stdin input before JSON deserialization.</summary>
    /// <param name="reader">Standard input stream.</param>
    /// <param name="maximum">Hard character budget.</param>
    /// <returns>Bounded input text.</returns>
    private static async Task<string> ReadBoundedTextAsync(
        TextReader reader, int maximum)
    {
        StringBuilder content = new();
        char[] buffer = new char[4096];
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory());
            if (count == 0) return content.ToString();
            if (content.Length + count > maximum)
                throw new InvalidDataException("Local input limit exceeded.");
            content.Append(buffer, 0, count);
        }
    }

    /// <summary>Reads local server data without trusting Content-Length or allocating unbounded memory.</summary>
    /// <param name="stream">Ollama response body stream.</param>
    /// <param name="maximum">Maximum bytes allowed.</param>
    /// <returns>Bounded JSON body.</returns>
    private static async Task<byte[]> ReadBoundedBytesAsync(
        Stream stream, int maximum)
    {
        using MemoryStream buffer = new();
        byte[] block = new byte[8192];
        while (true)
        {
            int count = await stream.ReadAsync(block);
            if (count == 0) return buffer.ToArray();
            if (buffer.Length + count > maximum)
                throw new InvalidDataException("Local response limit exceeded.");
            buffer.Write(block, 0, count);
        }
    }
}
