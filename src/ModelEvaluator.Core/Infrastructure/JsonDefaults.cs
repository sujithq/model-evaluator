using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelEvaluator.Core.Infrastructure;

public static class JsonDefaults
{
    public static JsonSerializerOptions Read { get; } = Create();

    public static JsonSerializerOptions Write { get; } = Create(writeIndented: true);

    private static JsonSerializerOptions Create(bool writeIndented = false) => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = writeIndented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };
}
