using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Infrastructure;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace ModelEvaluator.Core.Adapters;

#pragma warning disable OPENAI001
public sealed class MicrosoftExtensionsAiAdapter(
    bool useResponsesApi,
    TokenCredential? tokenCredential = null,
    HttpClient? httpClient = null) : IModelAdapter
{
    private readonly bool _useResponsesApi = useResponsesApi;
    private readonly TokenCredential? _tokenCredential = tokenCredential;
    private readonly HttpClient? _httpClient = httpClient;

    public string Id => _useResponsesApi
        ? AdapterIds.MicrosoftExtensionsAiResponses
        : AdapterIds.MicrosoftExtensionsAi;

    public string DisplayName => _useResponsesApi
        ? "Microsoft.Extensions.AI over Responses API"
        : "Microsoft.Extensions.AI over Chat Completions";

    public CompatibilityResult CheckCompatibility(TargetConfiguration target)
    {
        if (!string.Equals(target.Adapter, Id, StringComparison.Ordinal))
        {
            return new CompatibilityResult(false, $"Target adapter '{target.Adapter}' is not '{Id}'.");
        }

        return AdapterUtilities.RequireLiveConfiguration(target);
    }

    public AdapterPackage DescribePackage(TargetConfiguration target)
    {
        return new AdapterPackage
        {
            Name = "Microsoft.Extensions.AI.OpenAI",
            Version = AssemblyMetadata.VersionFor(typeof(OpenAIClientExtensions)),
        };
    }

    public async Task<AdapterCompletion> CompleteAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        if (string.Equals(
                invocation.Parameters.ReasoningEffort?.Trim(),
                "minimal",
                StringComparison.OrdinalIgnoreCase))
        {
            stopwatch.Stop();
            return UnsupportedReasoningCompletion(
                startedAt,
                stopwatch.Elapsed.TotalMilliseconds,
                "minimal");
        }

        AdapterError? lastError = null;
        var retries = 0;
        using var client = CreateClient(invocation.Target);
        var messages = ConvertMessages(invocation.Messages);
        var options = BuildOptions(invocation);

        for (var retry = 0; retry <= invocation.MaxRetries; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);
            try
            {
                var response = await client.GetResponseAsync(
                    messages,
                    options,
                    timeout.Token).ConfigureAwait(false);
                var toolCalls = response.Messages
                    .SelectMany(message => message.Contents)
                    .OfType<FunctionCallContent>()
                    .Select(ToModelToolCall)
                    .ToList();
                var usage = ParseUsage(response.Usage);
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
                    FinishReason = response.FinishReason?.ToString()
                                   ?? (toolCalls.Count > 0 ? "tool_calls" : "completed"),
                    Text = response.Text,
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
                    Message = $"The Microsoft.Extensions.AI request exceeded " +
                              $"{invocation.Timeout.TotalSeconds:0.#} seconds.",
                };
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
                "The Microsoft.Extensions.AI call did not complete."),
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        };
    }

    private IChatClient CreateClient(TargetConfiguration target)
    {
        var endpoint = target.ResolveEndpoint()
                       ?? throw new InvalidOperationException($"Target '{target.Id}' has no endpoint.");
        if (_useResponsesApi)
        {
            var options = new ResponsesClientOptions
            {
                Endpoint = BuildEndpoint(endpoint, target.ApiKind),
                RetryPolicy = new ClientRetryPolicy(0),
            };
            if (_httpClient is not null)
            {
                options.Transport = new HttpClientPipelineTransport(_httpClient);
            }

            ResponsesClient responsesClient;
            if (target.Authentication == AuthenticationKinds.DefaultCredential)
            {
                var credential = AdapterUtilities.CreateDefaultCredential(target, _tokenCredential);
                responsesClient = new ResponsesClient(
                    new BearerTokenPolicy(credential, target.TokenScope),
                    options);
            }
            else
            {
                var key = target.ResolveApiKey()
                          ?? throw new InvalidOperationException(
                              $"Target '{target.Id}' requires an API key but none is configured.");
                responsesClient = new ResponsesClient(new ApiKeyCredential(key), options);
            }

            return responsesClient.AsIChatClient(target.ModelOrDeployment);
        }

        var chatOptions = new OpenAIClientOptions
        {
            Endpoint = BuildEndpoint(endpoint, target.ApiKind),
            RetryPolicy = new ClientRetryPolicy(0),
        };
        if (_httpClient is not null)
        {
            chatOptions.Transport = new HttpClientPipelineTransport(_httpClient);
        }

        ChatClient chatClient;
        if (target.Authentication == AuthenticationKinds.DefaultCredential)
        {
            var credential = AdapterUtilities.CreateDefaultCredential(target, _tokenCredential);
            chatClient = new ChatClient(
                target.ModelOrDeployment,
                new BearerTokenPolicy(credential, target.TokenScope),
                chatOptions);
        }
        else
        {
            var key = target.ResolveApiKey()
                      ?? throw new InvalidOperationException(
                          $"Target '{target.Id}' requires an API key but none is configured.");
            chatClient = new ChatClient(
                target.ModelOrDeployment,
                new ApiKeyCredential(key),
                chatOptions);
        }

        return chatClient.AsIChatClient();
    }

    private static ChatOptions BuildOptions(AdapterInvocation invocation)
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = invocation.Parameters.MaxOutputTokens,
            Temperature = invocation.Parameters.Temperature is double temperature
                ? (float)temperature
                : null,
            TopP = invocation.Parameters.TopP is double topP ? (float)topP : null,
        };

        if (!string.IsNullOrWhiteSpace(invocation.Parameters.ReasoningEffort))
        {
            options.Reasoning = new ReasoningOptions
            {
                Effort = ParseReasoningEffort(invocation.Parameters.ReasoningEffort),
            };
        }

        if (invocation.Scenario.ResponseFormat is not null)
        {
            var format = invocation.Scenario.ResponseFormat;
            options.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(
                format.Schema,
                format.Name,
                format.Description);
        }

        foreach (var tool in invocation.Scenario.Tools)
        {
            options.Tools ??= [];
            options.Tools.Add(AIFunctionFactory.CreateDeclaration(
                tool.Name,
                tool.Description,
                tool.Parameters));
        }

        return options;
    }

    private static List<AiChatMessage> ConvertMessages(
        IEnumerable<ConversationMessage> messages)
    {
        var converted = new List<AiChatMessage>();
        foreach (var message in messages)
        {
            if (message.Role == "assistant" && message.ToolCalls.Count > 0)
            {
                converted.Add(new AiChatMessage(
                    ChatRole.Assistant,
                    message.ToolCalls.Select(call =>
                        (AIContent)new FunctionCallContent(
                            call.Id,
                            call.Name,
                            ParseArguments(call.Arguments))).ToList()));
            }
            else if (message.Role == "tool")
            {
                converted.Add(new AiChatMessage(
                    ChatRole.Tool,
                    [
                        new FunctionResultContent(
                            message.ToolCallId ?? "",
                            message.Content ?? ""),
                    ]));
            }
            else
            {
                converted.Add(new AiChatMessage(
                    ParseRole(message.Role),
                    message.Content ?? ""));
            }
        }

        return converted;
    }

    private static ModelToolCall ToModelToolCall(FunctionCallContent call)
    {
        var raw = JsonSerializer.SerializeToElement(call.Arguments, JsonDefaults.Read);
        var parsed = AdapterUtilities.ParseToolArguments(raw.GetRawText());
        return new ModelToolCall
        {
            Id = call.CallId,
            Name = call.Name,
            Arguments = parsed.Arguments,
            ArgumentError = call.Exception?.Message ?? parsed.Error,
        };
    }

    private static Dictionary<string, object?> ParseArguments(JsonElement arguments)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(
                   arguments.GetRawText(),
                   JsonDefaults.Read)
               ?? [];
    }

    private static ChatRole ParseRole(string role) =>
        role.ToLowerInvariant() switch
        {
            "system" => ChatRole.System,
            "developer" => new ChatRole("developer"),
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "tool" => ChatRole.Tool,
            _ => throw new InvalidOperationException($"Unsupported conversation role '{role}'."),
        };

    private static TokenUsage ParseUsage(UsageDetails? usage) =>
        usage is null
            ? new TokenUsage()
            : new TokenUsage
            {
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
                CachedInputTokens = usage.CachedInputTokenCount,
                ReasoningTokens = usage.ReasoningTokenCount,
            };

    private static ReasoningEffort ParseReasoningEffort(string effort) =>
        effort.Trim().ToLowerInvariant() switch
        {
            "none" => ReasoningEffort.None,
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            _ => throw new InvalidOperationException(
                $"Microsoft.Extensions.AI does not expose reasoning effort '{effort}'. " +
                "Expected none, low, medium, or high."),
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
                Code = "microsoft-extensions-ai-unsupported-reasoning-effort",
                StatusCode = 400,
                Message = $"Microsoft.Extensions.AI 11.0.0 does not expose reasoning effort '{effort}'.",
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
#pragma warning restore OPENAI001
