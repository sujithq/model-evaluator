using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Infrastructure;
using OpenAI.Responses;

namespace ModelEvaluator.ApiComparison.Core.Adapters;

#pragma warning disable OPENAI001
public sealed class OpenAiResponsesAdapter(
    TokenCredential? tokenCredential = null,
    HttpClient? httpClient = null) : IModelAdapter
{
    private readonly TokenCredential? _tokenCredential = tokenCredential;
    private readonly HttpClient? _httpClient = httpClient;

    public string Id => AdapterIds.OpenAiSdkResponses;

    public string DisplayName => "Official OpenAI .NET SDK (Responses API)";

    public CompatibilityResult CheckCompatibility(TargetConfiguration target) =>
        AdapterUtilities.RequireLiveConfiguration(target);

    public AdapterPackage DescribePackage(TargetConfiguration target) => new()
    {
        Name = AssemblyMetadata.NameFor(typeof(ResponsesClient)),
        Version = AssemblyMetadata.VersionFor(typeof(ResponsesClient)),
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
        var options = BuildOptions(invocation);

        for (var retry = 0; retry <= invocation.MaxRetries; retry++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(invocation.Timeout);
            try
            {
                ClientResult<ResponseResult> result =
                    await client.CreateResponseAsync(options, timeout.Token).ConfigureAwait(false);
                var response = result.Value;
                var usage = ParseUsage(response.Usage);
                stopwatch.Stop();

                if (response.Status == ResponseStatus.Failed)
                {
                    var error = response.Error;
                    return new AdapterCompletion
                    {
                        StartedAt = startedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                        LatencyMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
                        Retries = retries,
                        FinishReason = "failed",
                        Error = AdapterUtilities.ClassifyError(
                            400,
                            error?.Code.ToString(),
                            error?.Kind,
                            error?.Message),
                        Usage = usage,
                        UnavailableMetrics = AdapterUtilities.HasTokenUsage(usage)
                            ? ["timeToFirstByteMilliseconds"]
                            : ["timeToFirstByteMilliseconds", "tokenUsage"],
                    };
                }

                var toolCalls = response.OutputItems
                    .OfType<FunctionCallResponseItem>()
                    .Select(call =>
                    {
                        var parsed = AdapterUtilities.ParseToolArguments(call.FunctionArguments.ToString());
                        return new ModelToolCall
                        {
                            Id = call.CallId,
                            Name = call.FunctionName,
                            Arguments = parsed.Arguments,
                            ArgumentError = parsed.Error,
                        };
                    })
                    .ToList();
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
                    FinishReason = response.Status?.ToString(),
                    Text = response.GetOutputText(),
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
                    Message = $"The Responses SDK request exceeded {invocation.Timeout.TotalSeconds:0.#} seconds.",
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
                "The Responses SDK call did not complete."),
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        };
    }

    private ResponsesClient CreateClient(TargetConfiguration target)
    {
        var endpoint = target.ResolveEndpoint()
                       ?? throw new InvalidOperationException($"Target '{target.Id}' has no endpoint.");
        var options = new ResponsesClientOptions
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
            var credential = AdapterUtilities.CreateDefaultCredential(target, _tokenCredential);
            var policy = new BearerTokenPolicy(credential, target.TokenScope);
            return new ResponsesClient(policy, options);
        }

        var key = target.ResolveApiKey()
                  ?? throw new InvalidOperationException(
                      $"Target '{target.Id}' requires an API key but none is configured.");
        return new ResponsesClient(new ApiKeyCredential(key), options);
    }

    private static CreateResponseOptions BuildOptions(AdapterInvocation invocation)
    {
        var options = new CreateResponseOptions
        {
            Model = invocation.Target.ModelOrDeployment,
            MaxOutputTokenCount = invocation.Parameters.MaxOutputTokens,
            StoredOutputEnabled = false,
            Temperature = invocation.Parameters.Temperature is double temperature
                ? (float)temperature
                : null,
            TopP = invocation.Parameters.TopP is double topP ? (float)topP : null,
        };

        foreach (var item in ConvertMessages(invocation.Messages))
        {
            options.InputItems.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(invocation.Parameters.ReasoningEffort))
        {
            options.ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = ParseReasoningEffort(
                    invocation.Parameters.ReasoningEffort),
            };
        }

        if (invocation.Scenario.ResponseFormat is not null)
        {
            var format = invocation.Scenario.ResponseFormat;
            options.TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    format.Name,
                    BinaryData.FromBytes(Encoding.UTF8.GetBytes(format.Schema.GetRawText())),
                    format.Description,
                    format.Strict),
            };
        }

        foreach (var tool in invocation.Scenario.Tools)
        {
            options.Tools.Add(ResponseTool.CreateFunctionTool(
                tool.Name,
                BinaryData.FromBytes(Encoding.UTF8.GetBytes(tool.Parameters.GetRawText())),
                strictModeEnabled: true,
                functionDescription: tool.Description));
        }

        return options;
    }

    private static List<ResponseItem> ConvertMessages(
        IEnumerable<ConversationMessage> messages)
    {
        var items = new List<ResponseItem>();
        foreach (var message in messages)
        {
            if (message.Role == "system")
            {
                items.Add(ResponseItem.CreateSystemMessageItem(message.Content ?? ""));
            }
            else if (message.Role == "developer")
            {
                items.Add(ResponseItem.CreateDeveloperMessageItem(message.Content ?? ""));
            }
            else if (message.Role == "user")
            {
                items.Add(ResponseItem.CreateUserMessageItem(message.Content ?? ""));
            }
            else if (message.Role == "assistant" && message.ToolCalls.Count > 0)
            {
                foreach (var call in message.ToolCalls)
                {
                    items.Add(ResponseItem.CreateFunctionCallItem(
                        call.Id,
                        call.Name,
                        BinaryData.FromBytes(
                            Encoding.UTF8.GetBytes(call.Arguments.GetRawText()))));
                }
            }
            else if (message.Role == "assistant")
            {
                items.Add(ResponseItem.CreateAssistantMessageItem(message.Content ?? ""));
            }
            else if (message.Role == "tool")
            {
                items.Add(ResponseItem.CreateFunctionCallOutputItem(
                    message.ToolCallId ?? "",
                    message.Content ?? ""));
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported conversation role '{message.Role}'.");
            }
        }

        return items;
    }

    private static TokenUsage ParseUsage(ResponseTokenUsage? usage) =>
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

    private static ResponseReasoningEffortLevel ParseReasoningEffort(string effort) =>
        effort.Trim().ToLowerInvariant() switch
        {
            "none" => ResponseReasoningEffortLevel.None,
            "minimal" => ResponseReasoningEffortLevel.Minimal,
            "low" => ResponseReasoningEffortLevel.Low,
            "medium" => ResponseReasoningEffortLevel.Medium,
            "high" => ResponseReasoningEffortLevel.High,
            _ => throw new InvalidOperationException(
                $"Unsupported reasoning effort '{effort}'. Expected none, minimal, low, medium, or high."),
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
