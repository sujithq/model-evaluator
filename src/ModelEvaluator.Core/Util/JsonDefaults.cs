using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelEvaluator.Core.Util;

/// <summary>Shared JSON settings so configuration and reports use one format.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);

    public static readonly JsonSerializerOptions CompactOptions = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new(JsonSerializerDefaults.Web)
    {
        WriteIndented = indented,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}
