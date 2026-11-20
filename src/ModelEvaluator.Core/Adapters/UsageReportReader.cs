using System.Text.Json;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Reads runner usage without guessing missing values or combining duplicate breakdowns.</summary>
public static class UsageReportReader
{
    public static AdapterUsage Read(string path, string format)
    {
        try
        {
            return Parse(File.ReadAllText(path), format);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AdapterUsage { Warnings = [$"Usage file could not be read: {path} ({ex.GetType().Name})."] };
        }
    }

    public static AdapterUsage Parse(string json, string format)
    {
        try
        {
            if (format == "normalized")
            {
                var usage = JsonSerializer.Deserialize<AdapterUsage>(json, JsonDefaults.Options)
                            ?? throw new JsonException("Usage must be an object.");
                if (usage.ToolCalls < 0 || usage.InputTokens < 0 || usage.OutputTokens < 0
                    || usage.EstimatedCostUsd < 0 || usage.AiCredits < 0 || usage.PremiumRequests < 0
                    || usage.CacheReadTokens < 0 || usage.CacheWriteTokens < 0 || usage.ReasoningTokens < 0
                    || usage.ApiRequests < 0 || usage.ApiDurationSeconds < 0
                    || (usage.ApiDurationSeconds is double seconds && !double.IsFinite(seconds)))
                {
                    throw new JsonException("Usage metrics must not be negative.");
                }

                usage = usage with { Warnings = [], ReportedModels = usage.ReportedModels ?? [] };
                return usage.UnavailableMetrics.Count == new AdapterUsage().UnavailableMetrics.Count
                    ? usage with { Warnings = ["Usage file contains no recognized measurements. Check usageFormat."] }
                    : usage;
            }

            if (format != "copilot-cli")
            {
                throw new ArgumentException($"Unsupported usage format '{format}'.", nameof(format));
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Usage must be an object.");
            }

            return ParseCopilot(document.RootElement);
        }
        catch (JsonException)
        {
            return new AdapterUsage { Warnings = ["Usage JSON is malformed or contains invalid metrics; measurements are unavailable."] };
        }
    }

    private static AdapterUsage ParseCopilot(JsonElement root)
    {
        var warnings = new List<string>();
        var models = new List<JsonProperty>();
        if (root.TryGetProperty("modelMetrics", out var modelMetrics))
        {
            if (modelMetrics.ValueKind == JsonValueKind.Object)
            {
                models.AddRange(modelMetrics.EnumerateObject());
            }
            else
            {
                warnings.Add("Usage field 'modelMetrics' must be an object.");
            }
        }

        decimal? Number(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)
                || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number >= 0)
            {
                return number;
            }

            warnings.Add($"Usage field '{name}' must be a nonnegative number.");
            return null;
        }

        long? SumModels(string section, string field)
        {
            if (models.Count == 0)
            {
                return null;
            }

            long sum = 0;
            foreach (var model in models)
            {
                if (model.Value.ValueKind != JsonValueKind.Object || !model.Value.TryGetProperty(section, out var values))
                {
                    return null;
                }

                var number = Number(values, field);
                if (number is null)
                {
                    return null;
                }

                if (number != decimal.Truncate(number.Value) || number > long.MaxValue - sum)
                {
                    warnings.Add($"Usage field '{field}' must contain integer counts within the supported range.");
                    return null;
                }

                sum += (long)number.Value;
            }

            return sum;
        }

        // Session totals and modelMetrics already include agents. agentMetrics is a breakdown, not extra usage.
        var nanoAiu = Number(root, "totalNanoAiu");
        var premiumRequests = Number(root, "totalPremiumRequestCost");
        var durationMs = Number(root, "totalApiDurationMs");
        var usage = new AdapterUsage
        {
            AiCredits = nanoAiu / 1_000_000_000m,
            PremiumRequests = premiumRequests,
            InputTokens = SumModels("usage", "inputTokens"),
            OutputTokens = SumModels("usage", "outputTokens"),
            CacheReadTokens = SumModels("usage", "cacheReadTokens"),
            CacheWriteTokens = SumModels("usage", "cacheWriteTokens"),
            ReasoningTokens = SumModels("usage", "reasoningTokens"),
            ApiRequests = SumModels("requests", "count"),
            ApiDurationSeconds = durationMs is null ? null : (double)(durationMs.Value / 1000m),
            ReportedModels = models.Select(m => m.Name).Order(StringComparer.Ordinal).ToList(),
        };
        if (usage.UnavailableMetrics.Count == new AdapterUsage().UnavailableMetrics.Count)
        {
            warnings.Add("Copilot usage file contains no recognized measurements.");
        }

        return usage with { Warnings = warnings };
    }
}
