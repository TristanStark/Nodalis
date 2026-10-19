using System.Text.Json;

/// <summary>
/// Test-only local executable protocol fixture. The surrounding smoke-test
/// process hosts it through a dedicated command switch instead of running tests.
/// This class does not send network requests or reveal received source text.
/// </summary>
internal static class EmbeddingProtocolFixture
{
    private sealed record Request
    {
        public string ModelId { get; init; } = "";
        public List<string> Texts { get; init; } = [];
    }

    /// <summary>Serves exactly one synthetic embedding batch for a chosen failure scenario.</summary>
    /// <param name="scenario">valid, bad-json, bad-model, short-batch, stderr-exit or wait.</param>
    public static async Task ServeAsync(string scenario)
    {
        if (scenario == "wait")
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            return;
        }

        // Read from the same redirected standard input as an actual provider.
        // Do not echo this input to stdout, stderr, exceptions or test logs.
        string raw = await Console.In.ReadToEndAsync();
        Request? input = JsonSerializer.Deserialize<Request>(raw,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (input is null || string.IsNullOrEmpty(input.ModelId) ||
            input.Texts.Count == 0)
        {
            Environment.ExitCode = 2;
            return;
        }

        if (scenario == "stderr-exit")
        {
            Console.Error.WriteLine("PRIVATE_SOURCE should never reach UI diagnostics");
            Environment.ExitCode = 7;
            return;
        }

        if (scenario == "bad-json")
        {
            Console.Out.WriteLine("{malformed response");
            return;
        }

        float[][] vectors = Enumerable.Range(0, input.Texts.Count)
            .Select(_ => new[] { 1f, 0.5f, 0.25f }).ToArray();
        if (scenario == "short-batch")
            vectors = vectors.Take(vectors.Length - 1).ToArray();
        string model = scenario == "bad-model"
            ? "unexpected-other-model" : input.ModelId;
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            modelId = model,
            dimensions = 3,
            vectors
        }));
    }
}
