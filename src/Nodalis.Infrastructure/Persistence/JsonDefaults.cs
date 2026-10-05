using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Infrastructure.Persistence;

internal static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>
    /// Performs the <c>CreateOptions</c> operation.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    private static JsonSerializerOptions CreateOptions()
    {
        global::System.Text.Json.JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
