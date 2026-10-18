using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Nodalis.Core.Semantic;
using Nodalis.Core.Settings;

namespace Nodalis.Infrastructure.AI;

/// <summary>
/// Optional local embedding process using a strict JSON stdin/stdout protocol.
/// This adapter never uses an HTTP client or starts without explicit opt-in.
/// </summary>
public sealed class ProcessEmbeddingProvider : IEmbeddingProvider
{
    private readonly SemanticSearchPreferences _options;

    private sealed record EmbeddingProcessRequest(string ModelId, IReadOnlyList<string> Texts);

    private sealed record EmbeddingProcessResponse
    {
        public string ModelId { get; init; } = string.Empty;
        public int Dimensions { get; init; }
        public List<float[]> Vectors { get; init; } = [];
    }

    /// <summary>Prepares a provider configuration without spawning a process.</summary>
    /// <param name="options">Local executable, expected model and explicit consent.</param>
    public ProcessEmbeddingProvider(SemanticSearchPreferences options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Advertised capabilities are read from configuration, never probed automatically.</summary>
    public EmbeddingCapabilities Capabilities =>
        new(_options.ModelId, _options.Dimensions, Math.Clamp(_options.MaxBatchSize, 1, 32));

    /// <summary>
    /// Runs one bounded embedding batch only for a locally configured and enabled provider.
    /// JSON input: modelId, texts. JSON output: modelId, dimensions, vectors.
    /// </summary>
    /// <param name="texts">User-approved input paragraphs.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Vectors of configured size and model, validated for finiteness.</returns>
    public async Task<EmbeddingBatch> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(texts);
        cancellationToken.ThrowIfCancellationRequested();

        using CancellationTokenSource timeout = new(
            TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(
                timeout.Token, cancellationToken);

        ProcessStartInfo start = new()
        {
            FileName = _options.ExecutablePath,
            Arguments = _options.Arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using Process process = new()
        {
            StartInfo = start,
            EnableRaisingEvents = true
        };
        try
        {
            if (!process.Start())
            {
                throw new EmbeddingProviderException(
                    EmbeddingFailureKind.Unavailable,
                    "Le fournisseur d'embeddings local n'a pas pu démarrer.");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(linked.Token);
            string payload = JsonSerializer.Serialize(
                new EmbeddingProcessRequest(_options.ModelId, texts),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await process.StandardInput.WriteAsync(
                payload.AsMemory(), linked.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(linked.Token);
            string responseText = await stdout;
            _ = await stderr;

            if (process.ExitCode != 0)
            {
                throw new EmbeddingProviderException(
                    EmbeddingFailureKind.Unavailable,
                    "Le fournisseur d'embeddings local a signalé une erreur.");
            }

            if (responseText.Length > 2_000_000)
            {
                throw new EmbeddingProviderException(
                    EmbeddingFailureKind.InvalidResponse,
                    "Réponse du fournisseur trop volumineuse.");
            }

            EmbeddingProcessResponse? response =
                JsonSerializer.Deserialize<EmbeddingProcessResponse>(
                    responseText,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (response is null ||
                !string.Equals(response.ModelId, _options.ModelId,
                    StringComparison.Ordinal) ||
                response.Dimensions != _options.Dimensions)
            {
                throw new EmbeddingProviderException(
                    EmbeddingFailureKind.IncompatibleModel,
                    "Le modèle ou la dimension des embeddings est incompatible.");
            }

            ValidateVectors(response.Vectors, texts.Count, response.Dimensions);
            return new EmbeddingBatch(response.ModelId, response.Dimensions,
                response.Vectors);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.Timeout,
                "Le fournisseur d'embeddings local a dépassé le délai autorisé.");
        }
        catch (Win32Exception)
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.Unavailable,
                "Le processus d'embeddings local est indisponible.");
        }
        catch (JsonException)
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.InvalidResponse,
                "Réponse du fournisseur d'embeddings illisible.");
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process never started, or it exited between the status read and kill.
            }
        }
    }

    /// <summary>Rejects remote executables, missing consent and unsafe batch/model settings.</summary>
    /// <param name="texts">Candidate batch.</param>
    private void ValidateConfiguration(IReadOnlyList<string> texts)
    {
        if (!_options.IsEnabled)
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.Disabled, "Recherche sémantique non activée.");
        }

        string path = _options.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal) ||
            _options.Arguments.Contains("://", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(path) ||
            !File.Exists(path))
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.InvalidConfiguration,
                "Configurer un exécutable d'embeddings installé localement.");
        }

        if (string.IsNullOrWhiteSpace(_options.ModelId) ||
            _options.Dimensions is < 1 or > 4096 ||
            _options.TimeoutSeconds is < 1 or > 300 ||
            _options.MaxBatchSize is < 1 or > 32 ||
            texts is null || texts.Count == 0 ||
            texts.Count > _options.MaxBatchSize ||
            texts.Any(text => string.IsNullOrWhiteSpace(text) || text.Length > 8192))
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.InvalidConfiguration,
                "Paramètres ou lot d'embeddings invalides.");
        }
    }

    /// <summary>Rejects missing, nonfinite, zero-norm or mismatched vectors.</summary>
    /// <param name="vectors">Returned vectors.</param>
    /// <param name="count">Expected passage count.</param>
    /// <param name="dimension">Expected dimension.</param>
    private static void ValidateVectors(
        IReadOnlyList<float[]>? vectors, int count, int dimension)
    {
        if (vectors is null || vectors.Count != count ||
            vectors.Any(vector => vector is null || vector.Length != dimension ||
                vector.Any(value => !float.IsFinite(value)) ||
                vector.All(value => value == 0)))
        {
            throw new EmbeddingProviderException(
                EmbeddingFailureKind.InvalidResponse,
                "Les vecteurs du modèle ne respectent pas son contrat.");
        }
    }
}
