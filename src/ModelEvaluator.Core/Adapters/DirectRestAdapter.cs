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

public sealed class DirectRestAdapter(
    HttpClient? httpClient = null,
    TokenCredential? tokenCredential = null) : IModelAdapter
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();
    private readonly TokenCredential? _tokenCredential = tokenCredential;

    public string Id => AdapterIds.DirectRest;

    public string DisplayName => "Direct REST (Chat Completions)";

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
                if (firstByteMilliseconds is null)
                {
                    firstByteMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                }

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
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                lastError = new AdapterError
                {
                    Category = "timeout",
                    Code = "request-timeout",
                    Message = $"The direct REST request exceeded {invocation.Timeout.TotalSeconds:0.#} seconds.",
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
            Error = lastError ?? AdapterUtilities.ClassifyError(null, null, null, "The request did not complete."),
            UnavailableMetrics = ["tokenUsage"],
        };
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        var target = invocation.Target;
        var body = BuildBody(invocation);
        var request = new HttpRequestMessage(HttpMethod.Post, BuildRequestUri(target))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
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
        var root = new JsonObject
        {
            ["model"] = target.ModelOrDeployment,
            ["messages"] = BuildMessages(invocation.Messages),
            ["max_completion_tokens"] = invocation.Parameters.MaxOutputTokens,
        };

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
            root["reasoning_effort"] = invocation.Parameters.ReasoningEffort;
        }

        if (invocation.Scenario.ResponseFormat is not null)
        {
            var format = invocation.Scenario.ResponseFormat;
            root["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = format.Name,
                    ["description"] = format.Description,
                    ["strict"] = format.Strict,
                    ["schema"] = JsonNode.Parse(format.Schema.GetRawText()),
                },
            };
        }

        if (invocation.Scenario.Tools.Count > 0)
        {
            root["tools"] = new JsonArray(invocation.Scenario.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
                },
            }).ToArray());
            root["tool_choice"] = "auto";
        }

        return root.ToJsonString(JsonDefaults.Read);
    }

    private static JsonArray BuildMessages(IReadOnlyList<ConversationMessage> messages)
    {
        var array = new JsonArray();
        foreach (var message in messages)
        {
            var node = new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            };

            if (message.ToolCallId is not null)
            {
                node["tool_call_id"] = message.ToolCallId;
            }

            if (message.ToolCalls.Count > 0)
            {
                node["tool_calls"] = new JsonArray(message.ToolCalls.Select(call => (JsonNode)new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments.GetRawText(),
                    },
                }).ToArray());
            }

            array.Add(node);
        }

        return array;
    }

    private static Uri BuildRequestUri(TargetConfiguration target)
    {
        var endpoint = target.ResolveEndpoint()
                       ?? throw new InvalidOperationException($"Target '{target.Id}' has no endpoint.");
        var normalized = endpoint.TrimEnd('/');

        if (target.ApiKind == ApiKinds.OpenAi)
        {
            if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                normalized += "/v1";
            }

            return new Uri($"{normalized}/chat/completions");
        }

        if (!string.IsNullOrWhiteSpace(target.ApiVersion)
            && !string.Equals(target.ApiVersion, "v1", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^"/openai/v1".Length];
            }

            return new Uri(
                $"{normalized}/openai/deployments/{Uri.EscapeDataString(target.ModelOrDeployment)}" +
                $"/chat/completions?api-version={Uri.EscapeDataString(target.ApiVersion)}");
        }

        if (!normalized.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/openai/v1";
        }

        return new Uri($"{normalized}/chat/completions");
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
            var choice = root.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            var toolCalls = new List<ModelToolCall>();
            if (message.TryGetProperty("tool_calls", out var toolCallsElement))
            {
                foreach (var call in toolCallsElement.EnumerateArray())
                {
                    var function = call.GetProperty("function");
                    var arguments = function.GetProperty("arguments").GetString() ?? "{}";
                    var parsedArguments = AdapterUtilities.ParseToolArguments(arguments);
                    toolCalls.Add(new ModelToolCall
                    {
                        Id = call.GetProperty("id").GetString() ?? Guid.NewGuid().ToString("N"),
                        Name = function.GetProperty("name").GetString() ?? "",
                        Arguments = parsedArguments.Arguments,
                        ArgumentError = parsedArguments.Error,
                    });
                }
            }

            var usage = AdapterUtilities.ParseTokenUsage(root);
            var unavailable = new List<string>();
            if (firstByteMilliseconds is null)
            {
                unavailable.Add("timeToFirstByteMilliseconds");
            }

            if (!AdapterUtilities.HasTokenUsage(usage))
            {
                unavailable.Add("tokenUsage");
            }

            return new AdapterCompletion
            {
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                LatencyMilliseconds = latencyMilliseconds,
                TimeToFirstByteMilliseconds = firstByteMilliseconds,
                Retries = retries,
                FinishReason = choice.GetProperty("finish_reason").GetString(),
                Text = message.GetProperty("content").ValueKind == JsonValueKind.String
                    ? message.GetProperty("content").GetString()
                    : null,
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
                    Message = $"The successful response was not valid chat-completions JSON: {exception.Message}",
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
                    Code = "missing-chat-field",
                    Message = exception.Message,
                },
                UnavailableMetrics = ["tokenUsage"],
            };
        }
    }
}
