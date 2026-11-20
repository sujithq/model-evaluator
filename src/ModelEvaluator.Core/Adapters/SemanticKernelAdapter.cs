using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Infrastructure;
using OpenAI;
using OpenAI.Chat;
using KernelChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

namespace ModelEvaluator.ApiComparison.Core.Adapters;

#pragma warning disable OPENAI001, SKEXP0010
public sealed class SemanticKernelAdapter(
    TokenCredential? tokenCredential = null,
    HttpClient? httpClient = null) : IModelAdapter
{
    private readonly TokenCredential? _tokenCredential = tokenCredential;
    private readonly HttpClient? _httpClient = httpClient;

    public string Id => AdapterIds.SemanticKernel;

    public string DisplayName => "Semantic Kernel OpenAI connector (Chat Completions)";

    public CompatibilityResult CheckCompatibility(TargetConfiguration target)
    {
        if (!string.Equals(target.Adapter, Id, StringComparison.Ordinal))
        {
            return new CompatibilityResult(false, $"Target adapter '{target.Adapter}' is not '{Id}'.");
        }

        return AdapterUtilities.RequireLiveConfiguration(target);
    }

    public AdapterPackage DescribePackage(TargetConfiguration target) => new()
    {
        Name = AssemblyMetadata.NameFor(typeof(OpenAIChatCompletionService)),
        Version = AssemblyMetadata.VersionFor(typeof(OpenAIChatCompletionService)),
    };

    public async Task<AdapterCompletion> CompleteAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        if (string.Equals(
                invocation.Parameters.ReasoningEffort?.Trim(),
                "none",
                StringComparison.OrdinalIgnoreCase))
        {
            stopwatch.Stop();
            return UnsupportedReasoningCompletion(
                startedAt,
                stopwatch.Elapsed.TotalMilliseconds,
                "none");
        }

        AdapterError? lastError = null;
        var retries = 0;
        var service = CreateService(invocation.Target);
        var history = ConvertMessages(invocation.Messages);
        var settings = BuildSettings(invocation);
        var kernel = Kernel.CreateBuilder().Build();

        for (var retry = 0; retry <= invocation.MaxRetries; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);
            try
            {
                var results = await service.GetChatMessageContentsAsync(
                    history,
                    settings,
                    kernel,
                    timeout.Token).ConfigureAwait(false);
                var result = results.LastOrDefault()
                             ?? throw new InvalidOperationException(
                                 "Semantic Kernel returned no chat message.");
                var raw = result.InnerContent as ChatCompletion;
                var usage = ParseUsage(raw?.Usage);
                var toolCalls = ParseToolCalls(result, raw);
                stopwatch.Stop();
                var unavailable = new List<string> { "timeToFirstByteMilliseconds" };
                if (!AdapterUtilities.HasTokenUsage(usage))
                {
                    unavailable.Add("tokenUsage");
                }

                return new AdapterCompletion
                {
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow,
                    LatencyMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                    Retries = retries,
                    FinishReason = raw?.FinishReason.ToString(),
                    Text = result.Content,
                    ToolCalls = toolCalls,
                    Usage = usage,
                    UnavailableMetrics = unavailable,
                };
            }
            catch (OperationCanceledException) when (
                timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                lastError = new AdapterError
                {
                    Category = "timeout",
                    Code = "request-timeout",
                    Message = $"The Semantic Kernel request exceeded " +
                              $"{invocation.Timeout.TotalSeconds:0.#} seconds.",
                };
            }
            catch (HttpOperationException exception)
            {
                lastError = exception.StatusCode is null
                    ? AdapterUtilities.ClassifyError(
                        null,
                        "semantic-kernel-http",
                        null,
                        exception.Message)
                    : ParseError(
                        exception.ResponseContent ?? "",
                        (int)exception.StatusCode.Value,
                        exception.Message);
            }
            catch (ClientResultException exception)
            {
                var body = exception.GetRawResponse()?.Content.ToString() ?? "";
                lastError = ParseError(body, exception.Status, exception.Message);
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

            if (retry >= invocation.MaxRetries || !AdapterUtilities.ShouldRetry(lastError))
            {
                break;
            }

            retries++;
            await AdapterUtilities.DelayAsync(retries, null, cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        return new AdapterCompletion
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            LatencyMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            Retries = retries,
            FinishReason = "error",
            Error = lastError ?? AdapterUtilities.ClassifyError(
                null,
                null,
                null,
                "The Semantic Kernel call did not complete."),
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        };
    }

    private OpenAIChatCompletionService CreateService(TargetConfiguration target)
    {
        var endpoint = target.ResolveEndpoint()
                       ?? throw new InvalidOperationException($"Target '{target.Id}' has no endpoint.");
        var options = new OpenAIClientOptions
        {
            Endpoint = BuildEndpoint(endpoint, target.ApiKind),
            RetryPolicy = new ClientRetryPolicy(0),
        };
        if (_httpClient is not null)
        {
            options.Transport = new HttpClientPipelineTransport(_httpClient);
        }

        OpenAIClient client;
        if (target.Authentication == AuthenticationKinds.DefaultCredential)
        {
            var credential = AdapterUtilities.CreateDefaultCredential(target, _tokenCredential);
            client = new OpenAIClient(
                new BearerTokenPolicy(credential, target.TokenScope),
                options);
        }
        else
        {
            var key = target.ResolveApiKey()
                      ?? throw new InvalidOperationException(
                          $"Target '{target.Id}' requires an API key but none is configured.");
            client = new OpenAIClient(new ApiKeyCredential(key), options);
        }

        return new OpenAIChatCompletionService(
            target.ModelOrDeployment,
            client);
    }

    private static OpenAIPromptExecutionSettings BuildSettings(
        AdapterInvocation invocation)
    {
        var settings = new OpenAIPromptExecutionSettings
        {
            MaxTokens = invocation.Parameters.MaxOutputTokens,
            Temperature = invocation.Parameters.Temperature,
            TopP = invocation.Parameters.TopP,
            ReasoningEffort = invocation.Parameters.ReasoningEffort,
        };

        if (invocation.Scenario.ResponseFormat is not null)
        {
            var format = invocation.Scenario.ResponseFormat;
            settings.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                format.Name,
                BinaryData.FromBytes(Encoding.UTF8.GetBytes(format.Schema.GetRawText())),
                format.Description,
                format.Strict);
        }

        if (invocation.Scenario.Tools.Count > 0)
        {
            settings.ExtraBody ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            settings.ExtraBody["tools"] = invocation.Scenario.Tools.Select(tool =>
                new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
                        ["strict"] = true,
                    },
                }).ToList();
            settings.ExtraBody["tool_choice"] = "auto";
        }

        return settings;
    }

    private static ChatHistory ConvertMessages(
        IReadOnlyList<ConversationMessage> messages)
    {
        var history = new ChatHistory();
        foreach (var message in messages)
        {
            if (message.Role == "assistant" && message.ToolCalls.Count > 0)
            {
                var assistant = new KernelChatMessageContent(
                    AuthorRole.Assistant,
                    message.Content ?? "");
                foreach (var call in message.ToolCalls)
                {
                    assistant.Items.Add(new FunctionCallContent(
                        functionName: call.Name,
                        pluginName: null,
                        id: call.Id,
                        arguments: ToKernelArguments(call.Arguments)));
                }

                history.Add(assistant);
            }
            else if (message.Role == "tool")
            {
                history.Add(new KernelChatMessageContent(
                    AuthorRole.Tool,
                    message.Content ?? "",
                    modelId: null,
                    innerContent: null,
                    encoding: null,
                    metadata: new Dictionary<string, object?>
                    {
                        [OpenAIChatMessageContent.ToolIdProperty] = message.ToolCallId ?? "",
                    }));
            }
            else
            {
                history.Add(new KernelChatMessageContent(
                    ParseRole(message.Role),
                    message.Content ?? ""));
            }
        }

        return history;
    }

    private static List<ModelToolCall> ParseToolCalls(
        KernelChatMessageContent result,
        ChatCompletion? raw)
    {
        if (raw is not null)
        {
            return raw.ToolCalls.Select(call =>
            {
                var parsed = AdapterUtilities.ParseToolArguments(
                    call.FunctionArguments.ToString());
                return new ModelToolCall
                {
                    Id = call.Id,
                    Name = call.FunctionName ?? "",
                    Arguments = parsed.Arguments,
                    ArgumentError = parsed.Error,
                };
            }).ToList();
        }

        return FunctionCallContent.GetFunctionCalls(result).Select(call =>
        {
            var rawArguments = JsonSerializer.SerializeToElement(
                call.Arguments,
                JsonDefaults.Read);
            var parsed = AdapterUtilities.ParseToolArguments(rawArguments.GetRawText());
            return new ModelToolCall
            {
                Id = call.Id ?? Guid.NewGuid().ToString("N"),
                Name = call.FunctionName ?? "",
                Arguments = parsed.Arguments,
                ArgumentError = call.Exception?.Message ?? parsed.Error,
            };
        }).ToList();
    }

    private static KernelArguments ToKernelArguments(JsonElement arguments)
    {
        var result = new KernelArguments();
        foreach (var property in arguments.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    private static AuthorRole ParseRole(string role) =>
        role.ToLowerInvariant() switch
        {
            "system" => AuthorRole.System,
            "developer" => AuthorRole.Developer,
            "user" => AuthorRole.User,
            "assistant" => AuthorRole.Assistant,
            "tool" => AuthorRole.Tool,
            _ => throw new InvalidOperationException($"Unsupported conversation role '{role}'."),
        };

    private static TokenUsage ParseUsage(ChatTokenUsage? usage) =>
        usage is null
            ? new TokenUsage()
            : new TokenUsage
            {
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
                CachedInputTokens = usage.InputTokenDetails?.CachedTokenCount,
                ReasoningTokens = usage.OutputTokenDetails?.ReasoningTokenCount,
            };

    private static AdapterError ParseError(string body, int statusCode, string fallback)
    {
        var error = string.IsNullOrWhiteSpace(body)
            ? null
            : AdapterUtilities.ParseProviderError(body, statusCode);
        return error ?? AdapterUtilities.ClassifyError(statusCode, null, null, fallback);
    }

    private static AdapterCompletion UnsupportedReasoningCompletion(
        DateTimeOffset startedAt,
        double latencyMilliseconds,
        string effort) =>
        new()
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            LatencyMilliseconds = latencyMilliseconds,
            FinishReason = "error",
            Error = new AdapterError
            {
                Category = "unsupported-parameter",
                Code = "semantic-kernel-unsupported-reasoning-effort",
                StatusCode = 400,
                Message = $"Semantic Kernel 1.82.0 does not expose reasoning effort '{effort}' " +
                          "through its Chat Completions connector settings.",
            },
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        };

    private static Uri BuildEndpoint(string endpoint, string apiKind)
    {
        var normalized = endpoint.TrimEnd('/');
        if (apiKind == ApiKinds.AzureOpenAi)
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

        return new Uri(normalized);
    }
}
#pragma warning restore OPENAI001, SKEXP0010
