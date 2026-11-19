using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Infrastructure;
using OpenAI;
using OpenAI.Chat;

namespace ModelEvaluator.Core.Adapters;

public sealed class OpenAiSdkAdapter(
    TokenCredential? tokenCredential = null,
    HttpClient? httpClient = null) : IModelAdapter
{
    private readonly TokenCredential? _tokenCredential = tokenCredential;
    private readonly HttpClient? _httpClient = httpClient;

    public string Id => AdapterIds.OpenAiSdk;

    public string DisplayName => "Official OpenAI .NET SDK (OpenAI/Azure v1)";

    public CompatibilityResult CheckCompatibility(TargetConfiguration target) =>
        AdapterUtilities.RequireLiveConfiguration(target);

    public AdapterPackage DescribePackage(TargetConfiguration target) => new()
    {
        Name = AssemblyMetadata.NameFor(typeof(ChatClient)),
        Version = AssemblyMetadata.VersionFor(typeof(ChatClient)),
    };

    public async Task<AdapterCompletion> CompleteAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        AdapterError? lastError = null;
        var retries = 0;
        var client = CreateClient(invocation.Target);
        var messages = ConvertMessages(invocation.Messages);
        var options = BuildOptions(invocation);

        for (var retry = 0; retry <= invocation.MaxRetries; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);

            try
            {
                ClientResult<ChatCompletion> result =
                    await client.CompleteChatAsync(messages, options, timeout.Token).ConfigureAwait(false);
                var rawResponse = result.GetRawResponse();
                var rawBody = rawResponse.Content.ToString();
                var completion = result.Value;
                var usage = ParseUsage(rawBody);
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
                    FinishReason = completion.FinishReason.ToString(),
                    Text = string.Concat(completion.Content.Select(part => part.Text)),
                    ToolCalls = completion.ToolCalls.Select(call =>
                    {
                        var parsedArguments = AdapterUtilities.ParseToolArguments(
                            call.FunctionArguments.ToString());
                        return new ModelToolCall
                        {
                            Id = call.Id,
                            Name = call.FunctionName,
                            Arguments = parsedArguments.Arguments,
                            ArgumentError = parsedArguments.Error,
                        };
                    }).ToList(),
                    Usage = usage,
                    UnavailableMetrics = unavailable,
                };
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested
                                                     && !cancellationToken.IsCancellationRequested)
            {
                lastError = new AdapterError
                {
                    Category = "timeout",
                    Code = "request-timeout",
                    Message = $"The SDK request exceeded {invocation.Timeout.TotalSeconds:0.#} seconds.",
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
            Error = lastError ?? AdapterUtilities.ClassifyError(null, null, null, "The SDK call did not complete."),
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        };
    }

    private ChatClient CreateClient(TargetConfiguration target)
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

        if (target.Authentication == AuthenticationKinds.DefaultCredential)
        {
#pragma warning disable OPENAI001
            TokenCredential credential = _tokenCredential ?? new DefaultAzureCredential();
            var policy = new BearerTokenPolicy(
                credential,
                target.TokenScope);
            return new ChatClient(target.ModelOrDeployment, policy, options);
#pragma warning restore OPENAI001
        }

        var key = target.ResolveApiKey()
                  ?? throw new InvalidOperationException(
                      $"Target '{target.Id}' requires an API key but none is configured.");
        return new ChatClient(target.ModelOrDeployment, new ApiKeyCredential(key), options);
    }

    private static ChatCompletionOptions BuildOptions(AdapterInvocation invocation)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = invocation.Parameters.MaxOutputTokens,
        };

        if (invocation.Parameters.Temperature is double temperature)
        {
            options.Temperature = (float)temperature;
        }

        if (invocation.Parameters.TopP is double topP)
        {
            options.TopP = (float)topP;
        }

#pragma warning disable OPENAI001
        if (!string.IsNullOrWhiteSpace(invocation.Parameters.ReasoningEffort))
        {
            options.ReasoningEffortLevel = ParseReasoningEffort(invocation.Parameters.ReasoningEffort);
        }
#pragma warning restore OPENAI001

        if (invocation.Scenario.ResponseFormat is not null)
        {
            var format = invocation.Scenario.ResponseFormat;
            options.ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                format.Name,
                BinaryData.FromBytes(Encoding.UTF8.GetBytes(format.Schema.GetRawText())),
                format.Description,
                format.Strict);
        }

        foreach (var tool in invocation.Scenario.Tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                BinaryData.FromBytes(Encoding.UTF8.GetBytes(tool.Parameters.GetRawText())),
                functionSchemaIsStrict: true));
        }

        return options;
    }

    private static List<ChatMessage> ConvertMessages(IEnumerable<ConversationMessage> messages)
    {
        return messages.Select<ConversationMessage, ChatMessage>(message => message.Role.ToLowerInvariant() switch
        {
            "system" => new SystemChatMessage(message.Content ?? ""),
            "developer" => new SystemChatMessage(message.Content ?? ""),
            "user" => new UserChatMessage(message.Content ?? ""),
            "assistant" when message.ToolCalls.Count > 0 =>
                new AssistantChatMessage(message.ToolCalls.Select(call =>
                    ChatToolCall.CreateFunctionToolCall(
                        call.Id,
                        call.Name,
                        BinaryData.FromBytes(Encoding.UTF8.GetBytes(call.Arguments.GetRawText()))))),
            "assistant" => new AssistantChatMessage(message.Content ?? ""),
            "tool" => new ToolChatMessage(message.ToolCallId ?? "", message.Content ?? ""),
            _ => throw new InvalidOperationException($"Unsupported conversation role '{message.Role}'."),
        }).ToList();
    }

    private static TokenUsage ParseUsage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new TokenUsage();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return AdapterUtilities.ParseTokenUsage(document.RootElement);
        }
        catch (JsonException)
        {
            return new TokenUsage();
        }
    }

    private static AdapterError ParseError(string body, int statusCode, string fallback)
    {
        var error = string.IsNullOrWhiteSpace(body)
            ? null
            : AdapterUtilities.ParseProviderError(body, statusCode);
        return error ?? AdapterUtilities.ClassifyError(statusCode, null, null, fallback);
    }

#pragma warning disable OPENAI001
    private static ChatReasoningEffortLevel ParseReasoningEffort(string effort) =>
        effort.Trim().ToLowerInvariant() switch
        {
            "none" => ChatReasoningEffortLevel.None,
            "minimal" => ChatReasoningEffortLevel.Minimal,
            "low" => ChatReasoningEffortLevel.Low,
            "medium" => ChatReasoningEffortLevel.Medium,
            "high" => ChatReasoningEffortLevel.High,
            _ => throw new InvalidOperationException(
                $"Unsupported reasoning effort '{effort}'. Expected none, minimal, low, medium, or high."),
        };
#pragma warning restore OPENAI001

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
