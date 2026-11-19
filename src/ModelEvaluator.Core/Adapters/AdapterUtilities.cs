using System.Net;
using System.Text.Json;
using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Adapters;

internal static class AdapterUtilities
{
    public static ParsedToolArguments ParseToolArguments(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return new ParsedToolArguments(document.RootElement.Clone(), null);
        }
        catch (JsonException exception)
        {
            var sentinel = JsonSerializer.SerializeToElement(new
            {
                invalidJson = true,
                rawArguments = raw.Length > 8_000 ? raw[..8_000] : raw,
            });
            return new ParsedToolArguments(
                sentinel,
                $"The tool arguments were not valid JSON: {exception.Message}");
        }
    }

    public static AdapterError ClassifyError(
        int? statusCode,
        string? code,
        string? type,
        string? message)
    {
        var category = (statusCode, code, type, message?.ToLowerInvariant()) switch
        {
            (401 or 403, _, _, _) => "authentication",
            (404, _, _, _) => "model-or-deployment",
            (408, _, _, _) => "timeout",
            (429, _, _, _) => "rate-limit",
            ( >= 500, _, _, _) => "service",
            (_, _, _, var text) when text?.Contains("unsupported") == true
                                     || text?.Contains("not support") == true
                                     || text?.Contains("unknown parameter") == true
                                     || code?.Contains("parameter", StringComparison.OrdinalIgnoreCase) == true
                => "unsupported-parameter",
            (400, _, _, _) => "invalid-request",
            (null, _, _, _) => "transport",
            _ => "provider-error",
        };

        return new AdapterError
        {
            Category = category,
            Code = code,
            StatusCode = statusCode,
            Message = string.IsNullOrWhiteSpace(message)
                ? $"The service returned HTTP {statusCode?.ToString() ?? "unknown"}."
                : message[..Math.Min(message.Length, 800)],
        };
    }

    public static bool ShouldRetry(AdapterError? error) =>
        error?.Category is "transport" or "timeout" or "rate-limit" or "service";

    public static async Task DelayAsync(
        int retryIndex,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        var delay = retryAfter
                    ?? TimeSpan.FromMilliseconds(Math.Min(8_000, 250 * Math.Pow(2, retryIndex - 1)));
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    public static TokenUsage ParseTokenUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return new TokenUsage();
        }

        long? cached = null;
        if (usage.TryGetProperty("prompt_tokens_details", out var inputDetails)
            && inputDetails.TryGetProperty("cached_tokens", out var cachedElement)
            && cachedElement.TryGetInt64(out var parsedCached))
        {
            cached = parsedCached;
        }

        long? reasoning = null;
        if (usage.TryGetProperty("completion_tokens_details", out var outputDetails)
            && outputDetails.TryGetProperty("reasoning_tokens", out var reasoningElement)
            && reasoningElement.TryGetInt64(out var parsedReasoning))
        {
            reasoning = parsedReasoning;
        }

        return new TokenUsage
        {
            InputTokens = ReadInt64(usage, "prompt_tokens"),
            OutputTokens = ReadInt64(usage, "completion_tokens"),
            TotalTokens = ReadInt64(usage, "total_tokens"),
            CachedInputTokens = cached,
            ReasoningTokens = reasoning,
        };
    }

    public static bool HasTokenUsage(TokenUsage usage) =>
        usage.InputTokens is not null
        || usage.OutputTokens is not null
        || usage.TotalTokens is not null
        || usage.CachedInputTokens is not null
        || usage.ReasoningTokens is not null;

    public static AdapterError? ParseProviderError(string body, int statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var nested) ? nested : root;
            return ClassifyError(
                statusCode,
                ReadString(error, "code"),
                ReadString(error, "type"),
                ReadString(error, "message"));
        }
        catch (JsonException)
        {
            return ClassifyError(statusCode, null, null, body);
        }
    }

    public static CompatibilityResult RequireLiveConfiguration(
        TargetConfiguration target,
        bool requireEndpoint = true)
    {
        if (target.Adapter is not (AdapterIds.DirectRest or AdapterIds.OpenAiSdk))
        {
            return new CompatibilityResult(false, $"Adapter '{target.Adapter}' does not use live configuration.");
        }

        if (target.ApiKind is not (ApiKinds.OpenAi or ApiKinds.AzureOpenAi))
        {
            return new CompatibilityResult(
                false,
                $"apiKind '{target.ApiKind}' is not supported by adapter '{target.Adapter}'.");
        }

        if (requireEndpoint && string.IsNullOrWhiteSpace(target.ResolveEndpoint()))
        {
            return new CompatibilityResult(false, "No endpoint is configured.");
        }

        if (target.Authentication == AuthenticationKinds.ApiKey
            && string.IsNullOrWhiteSpace(target.ResolveApiKey()))
        {
            return new CompatibilityResult(false, "api-key authentication is selected but no key is configured.");
        }

        if (target.ApiKind == ApiKinds.OpenAi
            && target.Authentication != AuthenticationKinds.ApiKey)
        {
            return new CompatibilityResult(
                false,
                "OpenAI API targets support API-key authentication in this harness.");
        }

        if (target.ApiKind == ApiKinds.AzureOpenAi
            && target.Authentication is not (AuthenticationKinds.ApiKey or AuthenticationKinds.DefaultCredential))
        {
            return new CompatibilityResult(
                false,
                "Azure OpenAI targets require api-key or default-credential authentication.");
        }

        return new CompatibilityResult(true, "Supported live configuration.");
    }

    public static TimeSpan? GetRetryAfter(
        HttpStatusCode statusCode,
        System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        if (statusCode != HttpStatusCode.TooManyRequests
            && statusCode != HttpStatusCode.ServiceUnavailable)
        {
            return null;
        }

        var delta = headers.RetryAfter?.Delta;
        if (delta is not null)
        {
            return delta;
        }

        var date = headers.RetryAfter?.Date;
        return date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : null;
    }

    private static long? ReadInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

internal sealed record ParsedToolArguments(JsonElement Arguments, string? Error);
