using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Infrastructure;

namespace ModelEvaluator.Core.Adapters;

public sealed class DirectResponsesAdapter(
    HttpClient? httpClient = null,
    TokenCredential? tokenCredential = null) : IModelAdapter
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();
    private readonly TokenCredential? _tokenCredential = tokenCredential;

    public string Id => AdapterIds.DirectRestResponses;

    public string DisplayName => "Direct REST (Responses API)";

    public CompatibilityResult CheckCompatibility(TargetConfiguration target) =>
        AdapterUtilities.RequireLiveConfiguration(target);

    public AdapterPackage DescribePackage(TargetConfiguration target) => new()
    {
        Name = typeof(HttpClient).Namespace ?? "System.Net.Http",
        Version = AssemblyMetadata.VersionFor(typeof(HttpClient)),
    };

    public async Task<AdapterCompletion> CompleteAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        AdapterError? lastError = null;
        double? firstByteMilliseconds = null;
        var retries = 0;

        for (var retry = 0; retry <= invocation.MaxRetries; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);
            HttpRequestMessage? request = null;
            HttpResponseMessage? response = null;
            TimeSpan? retryAfter = null;
            try
            {
                request = await CreateRequestAsync(invocation, timeout.Token).ConfigureAwait(false);
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                firstByteMilliseconds ??= stopwatch.Elapsed.TotalMilliseconds;

                var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = AdapterUtilities.ParseProviderError(body, (int)response.StatusCode);
                    retryAfter = AdapterUtilities.GetRetryAfter(response.StatusCode, response.Headers);
                }
                else
                {
                    return ParseSuccess(
                        body,
                        startedAt,
                        stopwatch.Elapsed.TotalMilliseconds,
                        firstByteMilliseconds,
                        retries);
                }
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                lastError = new AdapterError
                {
                    Category = "timeout",
                    Code = "request-timeout",
                    Message = $"The Responses API request exceeded {invocation.Timeout.TotalSeconds:0.#} seconds.",
                };
            }
            catch (HttpRequestException exception)
            {
                lastError = AdapterUtilities.ClassifyError(
                    null,
                    exception.HttpRequestError.ToString(),
                    nameof(HttpRequestException),
                    exception.Message);
            }
            catch (AuthenticationFailedException exception)
            {
                lastError = new AdapterError
                {
                    Category = "authentication",
                    Code = "credential-failure",
                    Message = exception.Message,
                };
            }
            finally
            {
                request?.Dispose();
                response?.Dispose();
            }

            if (retry >= invocation.MaxRetries || !AdapterUtilities.ShouldRetry(lastError))
            {
                break;
            }

            retries++;
            await AdapterUtilities.DelayAsync(retries, retryAfter, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return new AdapterCompletion
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            LatencyMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            TimeToFirstByteMilliseconds = firstByteMilliseconds,
            Retries = retries,
            FinishReason = "error",
            Error = lastError ?? AdapterUtilities.ClassifyError(
                null,
                null,
                null,
                "The Responses API request did not complete."),
            UnavailableMetrics = ["tokenUsage"],
        };
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        var target = invocation.Target;
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildRequestUri(target))
        {
            Content = new StringContent(
                BuildBody(invocation),
                Encoding.UTF8,
                "application/json"),
        };

        if (target.Authentication == AuthenticationKinds.ApiKey)
        {
            var key = target.ResolveApiKey()
                      ?? throw new InvalidOperationException(
                          $"Target '{target.Id}' requires an API key but none is configured.");
            if (target.ApiKind == ApiKinds.AzureOpenAi)
            {
                request.Headers.Add("api-key", key);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }
        }
        else if (target.Authentication == AuthenticationKinds.DefaultCredential)
        {
            var credential = AdapterUtilities.CreateDefaultCredential(target, _tokenCredential);
            var token = await credential.GetTokenAsync(
                new TokenRequestContext([target.TokenScope]),
                cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        else
        {
            throw new InvalidOperationException(
                $"Target '{target.Id}' has unsupported authentication '{target.Authentication}'.");
        }

        return request;
    }

    private static string BuildBody(AdapterInvocation invocation)
    {
        var target = invocation.Target;
        var instructions = invocation.Messages
            .Where(message => message.Role is "system" or "developer")
            .Select(message => message.Content)
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToList();
        var root = new JsonObject
        {
            ["model"] = target.ModelOrDeployment,
            ["input"] = BuildInput(invocation.Messages),
            ["max_output_tokens"] = invocation.Parameters.MaxOutputTokens,
            ["store"] = false,
        };

        if (instructions.Count > 0)
        {
            root["instructions"] = string.Join(Environment.NewLine, instructions);
        }

        if (invocation.Parameters.Temperature is double temperature)
        {
            root["temperature"] = temperature;
        }

        if (invocation.Parameters.TopP is double topP)
        {
            root["top_p"] = topP;
        }

        if (!string.IsNullOrWhiteSpace(invocation.Parameters.ReasoningEffort))
        {
            root["reasoning"] = new JsonObject
            {
                ["effort"] = invocation.Parameters.ReasoningEffort,
            };
        }

        if (invocation.Scenario.ResponseFormat is not null)
        {
            var format = invocation.Scenario.ResponseFormat;
            root["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = format.Name,
                    ["description"] = format.Description,
                    ["strict"] = format.Strict,
                    ["schema"] = JsonNode.Parse(format.Schema.GetRawText()),
                },
            };
        }

        if (invocation.Scenario.Tools.Count > 0)
        {
            root["tools"] = new JsonArray(invocation.Scenario.Tools.Select(tool =>
                (JsonNode)new JsonObject
                {
                    ["type"] = "function",
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
                    ["strict"] = true,
                }).ToArray());
        }

        return root.ToJsonString(JsonDefaults.Read);
    }

    private static JsonArray BuildInput(IReadOnlyList<ConversationMessage> messages)
    {
        var input = new JsonArray();
        foreach (var message in messages)
        {
            if (message.Role is "system" or "developer")
            {
                continue;
            }

            if (message.Role == "assistant" && message.ToolCalls.Count > 0)
            {
                foreach (var call in message.ToolCalls)
                {
                    input.Add(new JsonObject
                    {
                        ["type"] = "function_call",
                        ["call_id"] = call.Id,
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments.GetRawText(),
                    });
                }

                continue;
            }

            if (message.Role == "tool")
            {
                input.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message.ToolCallId,
                    ["output"] = message.Content,
                });
                continue;
            }

            var textType = message.Role == "assistant" ? "output_text" : "input_text";
            input.Add(new JsonObject
            {
                ["type"] = "message",
                ["role"] = message.Role,
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = textType,
                    ["text"] = message.Content,
                }),
            });
        }

        return input;
    }

    private static Uri BuildRequestUri(TargetConfiguration target)
    {
        var endpoint = target.ResolveEndpoint()
                       ?? throw new InvalidOperationException($"Target '{target.Id}' has no endpoint.");
        var normalized = endpoint.TrimEnd('/');
        if (target.ApiKind == ApiKinds.AzureOpenAi)
        {
            if (!normalized.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            {
                normalized += "/openai/v1";
            }
        }
        else if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/v1";
        }

        return new Uri($"{normalized}/responses");
    }

    private static AdapterCompletion ParseSuccess(
        string body,
        DateTimeOffset startedAt,
        double latencyMilliseconds,
        double? firstByteMilliseconds,
        int retries)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var toolCalls = new List<ModelToolCall>();
            var outputText = "";
            if (root.TryGetProperty("output", out var output)
                && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    var type = item.TryGetProperty("type", out var typeElement)
                        ? typeElement.GetString()
                        : null;
                    if (type == "function_call")
                    {
                        var arguments = item.GetProperty("arguments").GetString() ?? "{}";
                        var parsed = AdapterUtilities.ParseToolArguments(arguments);
                        toolCalls.Add(new ModelToolCall
                        {
                            Id = item.TryGetProperty("call_id", out var callId)
                                ? callId.GetString() ?? Guid.NewGuid().ToString("N")
                                : item.TryGetProperty("id", out var id)
                                    ? id.GetString() ?? Guid.NewGuid().ToString("N")
                                    : Guid.NewGuid().ToString("N"),
                            Name = item.GetProperty("name").GetString() ?? "",
                            Arguments = parsed.Arguments,
                            ArgumentError = parsed.Error,
                        });
                    }
                    else if (type == "message"
                             && item.TryGetProperty("content", out var content)
                             && content.ValueKind == JsonValueKind.Array)
                    {
                        outputText += string.Concat(
                            content.EnumerateArray()
                                .Where(part => part.TryGetProperty("type", out var contentType)
                                               && contentType.GetString() == "output_text")
                                .Select(part => part.TryGetProperty("text", out var text)
                                    ? text.GetString()
                                    : "")
                                .Where(text => text is not null));
                    }
                }
            }

            if (root.TryGetProperty("output_text", out var directOutput)
                && directOutput.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(directOutput.GetString()))
            {
                outputText = directOutput.GetString() ?? outputText;
            }

            var usage = ParseUsage(root);
            var unavailable = new List<string>();
            if (firstByteMilliseconds is null)
            {
                unavailable.Add("timeToFirstByteMilliseconds");
            }

            if (!AdapterUtilities.HasTokenUsage(usage))
            {
                unavailable.Add("tokenUsage");
            }

            var status = root.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : toolCalls.Count > 0 ? "tool_calls" : "completed";
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return new AdapterCompletion
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    LatencyMilliseconds = latencyMilliseconds,
                    TimeToFirstByteMilliseconds = firstByteMilliseconds,
                    Retries = retries,
                    FinishReason = status,
                    Error = ParseResponseError(root),
                    Usage = usage,
                    UnavailableMetrics = unavailable,
                };
            }

            return new AdapterCompletion
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                LatencyMilliseconds = latencyMilliseconds,
                TimeToFirstByteMilliseconds = firstByteMilliseconds,
                Retries = retries,
                FinishReason = status,
                Text = string.IsNullOrEmpty(outputText) ? null : outputText,
                ToolCalls = toolCalls,
                Usage = usage,
                UnavailableMetrics = unavailable,
            };
        }
        catch (JsonException exception)
        {
            return new AdapterCompletion
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                LatencyMilliseconds = latencyMilliseconds,
                TimeToFirstByteMilliseconds = firstByteMilliseconds,
                Retries = retries,
                FinishReason = "error",
                Error = new AdapterError
                {
                    Category = "invalid-response",
                    Code = "malformed-json",
                    Message = $"The successful Responses API response was not valid JSON: {exception.Message}",
                },
                UnavailableMetrics = ["tokenUsage"],
            };
        }
        catch (KeyNotFoundException exception)
        {
            return new AdapterCompletion
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                LatencyMilliseconds = latencyMilliseconds,
                TimeToFirstByteMilliseconds = firstByteMilliseconds,
                Retries = retries,
                FinishReason = "error",
                Error = new AdapterError
                {
                    Category = "invalid-response",
                    Code = "missing-response-field",
                    Message = exception.Message,
                },
                UnavailableMetrics = ["tokenUsage"],
            };
        }
    }

    private static TokenUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
        {
            return new TokenUsage();
        }

        long? cached = null;
        if (usage.TryGetProperty("input_tokens_details", out var inputDetails)
            && inputDetails.TryGetProperty("cached_tokens", out var cachedElement)
            && cachedElement.TryGetInt64(out var parsedCached))
        {
            cached = parsedCached;
        }

        long? reasoning = null;
        if (usage.TryGetProperty("output_tokens_details", out var outputDetails)
            && outputDetails.TryGetProperty("reasoning_tokens", out var reasoningElement)
            && reasoningElement.TryGetInt64(out var parsedReasoning))
        {
            reasoning = parsedReasoning;
        }

        return new TokenUsage
        {
            InputTokens = ReadInt64(usage, "input_tokens"),
            OutputTokens = ReadInt64(usage, "output_tokens"),
            TotalTokens = ReadInt64(usage, "total_tokens"),
            CachedInputTokens = cached,
            ReasoningTokens = reasoning,
        };
    }

    private static AdapterError ParseResponseError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            return AdapterUtilities.ClassifyError(
                400,
                ReadString(error, "code"),
                ReadString(error, "type"),
                ReadString(error, "message"));
        }

        return AdapterUtilities.ClassifyError(
            400,
            "response-failed",
            null,
            "The Responses API returned a failed response.");
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
