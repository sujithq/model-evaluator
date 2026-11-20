using System.Text.Json;
using ModelEvaluator.ApiComparison.Core.Adapters;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Execution;
using ModelEvaluator.ApiComparison.Core.Infrastructure;
using ModelEvaluator.ApiComparison.Core.Results;
using ModelEvaluator.ApiComparison.Core.Scenarios;

namespace ModelEvaluator.ApiComparison.Core.Evaluation;

public sealed class ScenarioRunner
{
    private readonly ApiComparisonModelAdapterFactory _adapterFactory;

    public ScenarioRunner(ApiComparisonModelAdapterFactory adapterFactory)
    {
        _adapterFactory = adapterFactory;
    }

    public async Task<AttemptResult> RunAsync(
        string runId,
        ScenarioDefinition scenario,
        TargetConfiguration target,
        int repetition,
        EvaluationConfiguration configuration,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var executedAdapter = dryRun
            ? _adapterFactory.Get(AdapterIds.LocalFixture)
            : _adapterFactory.Get(target.Adapter);
        var package = executedAdapter.DescribePackage(target);
        var compatibility = dryRun
            ? new CompatibilityResult(true, "Dry-run execution uses deterministic local fixtures.")
            : executedAdapter.CheckCompatibility(target);
        var calls = new List<CallResult>();
        var trace = new List<ToolTraceEntry>();
        var turns = new List<TurnResult>();
        var warnings = new List<string>();
        var parameters = EffectiveRequestParameters.Merge(target.Parameters, scenario.Parameters);
        var fingerprint = ScenarioCatalog.Fingerprint(scenario);
        var messages = new List<ConversationMessage>();

        if (!string.IsNullOrWhiteSpace(scenario.SystemPrompt))
        {
            messages.Add(new ConversationMessage
            {
                Role = "system",
                Content = scenario.SystemPrompt,
            });
        }

        if (!compatibility.Supported)
        {
            var unsupported = new CallResult
            {
                CallNumber = 1,
                StartedAt = startedAt,
                CompletedAt = startedAt,
                Error = new AdapterError
                {
                    Category = "unsupported-combination",
                    Code = "adapter-target-mismatch",
                    Message = compatibility.Summary,
                },
                FinishReason = "unsupported",
                Request = CreateRequestSnapshot(scenario, messages, parameters, fingerprint, target),
            };
            calls.Add(unsupported);
            warnings.Add(compatibility.Summary);
        }
        else
        {
            for (var turnIndex = 0; turnIndex < scenario.Turns.Count; turnIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var turn = scenario.Turns[turnIndex];
                messages.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = turn.User,
                });

                AdapterCompletion? finalCompletion = null;
                for (var toolRound = 0; toolRound <= scenario.MaxToolRounds; toolRound++)
                {
                    var invocation = new AdapterInvocation
                    {
                        Target = target,
                        Scenario = scenario,
                        Messages = messages.ToList(),
                        Parameters = parameters,
                        TurnIndex = turnIndex,
                        ToolRound = toolRound,
                        MaxRetries = configuration.MaxRetries,
                        Timeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds),
                    };
                    var completion = await executedAdapter.CompleteAsync(invocation, cancellationToken)
                        .ConfigureAwait(false);
                    calls.Add(ToCallResult(completion, invocation, scenario, fingerprint, calls.Count + 1));
                    if (completion.Error is not null)
                    {
                        finalCompletion = completion;
                        break;
                    }

                    if (completion.ToolCalls.Count == 0)
                    {
                        finalCompletion = completion;
                        var text = completion.Text ?? "";
                        messages.Add(new ConversationMessage
                        {
                            Role = "assistant",
                            Content = text,
                        });
                        turns.Add(new TurnResult
                        {
                            Index = turnIndex,
                            UserMessage = turn.User,
                            AssistantMessage = text,
                        });
                        break;
                    }

                    var assistant = new ConversationMessage
                    {
                        Role = "assistant",
                        ToolCalls = completion.ToolCalls,
                    };
                    messages.Add(assistant);
                    foreach (var call in completion.ToolCalls)
                    {
                        var tool = scenario.Tools.FirstOrDefault(candidate =>
                            string.Equals(candidate.Name, call.Name, StringComparison.Ordinal));
                        var error = call.ArgumentError
                                    ?? (tool is null
                                        ? $"Tool '{call.Name}' is not one of the scenario's deterministic mock tools."
                                        : null);
                        var result = error is null ? tool?.MockResult.Clone() : null;
                        trace.Add(new ToolTraceEntry
                        {
                            ToolCallId = call.Id,
                            Name = call.Name,
                            Arguments = call.Arguments,
                            Result = result,
                            Error = error,
                        });
                        messages.Add(new ConversationMessage
                        {
                            Role = "tool",
                            ToolCallId = call.Id,
                            Content = error is not null
                                ? JsonSerializer.Serialize(new
                                {
                                    error = tool is null ? "unknown-tool" : "invalid-tool-arguments",
                                    message = error,
                                })
                                : result?.GetRawText() ?? "{}",
                        });
                    }

                    if (toolRound == scenario.MaxToolRounds)
                    {
                        warnings.Add(
                            $"Scenario '{scenario.Id}' reached maxToolRounds={scenario.MaxToolRounds} without a final response.");
                        finalCompletion = completion;
                    }
                }

                if (finalCompletion?.Error is not null
                    || finalCompletion is null
                    || finalCompletion.ToolCalls.Count > 0)
                {
                    break;
                }
            }
        }

        var checks = ScenarioValidator.Validate(scenario, calls, turns, trace);
        var passed = checks.Count > 0 && checks.All(check => check.Passed);
        var expectedError = calls.LastOrDefault(call => call.Error is not null)?.Error is not null
                            && scenario.Validation.AllowedErrorCategories.Count > 0
                            && passed;
        var outcome = !compatibility.Supported
            ? "unsupported"
            : passed
                ? expectedError ? "expected-error" : "passed"
                : "failed";
        var completedAt = DateTimeOffset.UtcNow;
        var readiness = BuildReadiness(target, dryRun, executedAdapter.Id, passed, calls);

        foreach (var metric in calls.SelectMany(call => call.UnavailableMetrics).Distinct(StringComparer.Ordinal))
        {
            warnings.Add($"Metric '{metric}' was not reported by the executed adapter.");
        }

        if (dryRun)
        {
            warnings.Add(
                "Dry-run results validate the offline harness only; they do not establish live technical compatibility, deployment, or capacity.");
        }
        else if (executedAdapter.Id == AdapterIds.LocalFixture)
        {
            warnings.Add(
                "Local fixture results validate the offline harness only; they do not establish live technical compatibility.");
        }
        else if (executedAdapter.Id == AdapterIds.CopilotCli)
        {
            warnings.Add(
                "GitHub Copilot CLI uses an agent runtime. Tool schemas, tool results, structured output, " +
                "and max-output-token limits are prompt-mediated and are not evidence of native provider API behavior.");
        }
        else if (executedAdapter.Id is AdapterIds.MicrosoftExtensionsAi
                 or AdapterIds.MicrosoftExtensionsAiResponses
                 or AdapterIds.SemanticKernel)
        {
            warnings.Add(
                "This adapter uses an abstraction/framework over an OpenAI-compatible client. " +
                "Compare its result with the corresponding direct/SDK path before attributing differences to the model.");
        }

        return new AttemptResult
        {
            AttemptId = $"{runId}:{scenario.Id}:{target.Id}:{repetition}",
            RunId = runId,
            Repetition = repetition,
            ScenarioId = scenario.Id,
            ScenarioVersion = scenario.Version,
            ScenarioFingerprint = fingerprint,
            Target = new TargetSnapshot
            {
                Id = target.Id,
                ModelId = target.ModelId,
                DeploymentId = target.DeploymentId,
                ApiKind = target.ApiKind,
                ApiVersion = EffectiveApiVersion(target, executedAdapter.Id, dryRun),
                RequestedAdapter = target.Adapter,
                ExecutedAdapter = executedAdapter.Id,
                Authentication = target.Authentication,
                Package = package,
                Parameters = parameters,
            },
            ExecutionMode = dryRun
                ? "dry-run"
                : executedAdapter.Id == AdapterIds.LocalFixture
                    ? "local-fixture"
                    : "live",
            Outcome = outcome,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Calls = calls,
            ToolTrace = trace,
            Turns = turns,
            Checks = checks,
            Warnings = warnings,
            Readiness = readiness,
        };
    }

    private static CallResult ToCallResult(
        AdapterCompletion completion,
        AdapterInvocation invocation,
        ScenarioDefinition scenario,
        string fingerprint,
        int callNumber) =>
        new()
        {
            CallNumber = callNumber,
            TurnIndex = invocation.TurnIndex,
            ToolRound = invocation.ToolRound,
            StartedAt = completion.StartedAt,
            CompletedAt = completion.CompletedAt,
            LatencyMilliseconds = completion.LatencyMilliseconds,
            TimeToFirstByteMilliseconds = completion.TimeToFirstByteMilliseconds,
            Retries = completion.Retries,
            FinishReason = completion.FinishReason,
            Usage = completion.Usage,
            Error = completion.Error,
            ToolCalls = completion.ToolCalls,
            Request = CreateRequestSnapshot(
                scenario,
                invocation.Messages,
                invocation.Parameters,
                fingerprint,
                invocation.Target),
            UnavailableMetrics = completion.UnavailableMetrics,
        };

    private static RequestSnapshot CreateRequestSnapshot(
        ScenarioDefinition scenario,
        IReadOnlyList<ConversationMessage> messages,
        EffectiveRequestParameters parameters,
        string fingerprint,
        TargetConfiguration target)
    {
        var conversationHash = Hashing.Sha256(JsonSerializer.Serialize(messages, JsonDefaults.Read));
        var requestHash = Hashing.Sha256(new
        {
            fingerprint,
            conversationHash,
            modelId = target.ModelId,
            deploymentId = target.DeploymentId,
            apiKind = target.ApiKind,
            parameters,
            tools = scenario.Tools.Select(tool => new
            {
                tool.Name,
                tool.Description,
                tool.Parameters,
            }),
            responseFormat = scenario.ResponseFormat,
        });
        return new RequestSnapshot
        {
            ScenarioFingerprint = fingerprint,
            ConversationHash = conversationHash,
            ModelId = target.ModelId,
            DeploymentId = target.DeploymentId,
            ApiKind = target.ApiKind,
            RequestHash = requestHash,
            Parameters = parameters,
            ToolNames = scenario.Tools.Select(tool => tool.Name).ToList(),
            ResponseFormatName = scenario.ResponseFormat?.Name,
            MessageCount = messages.Count,
        };
    }

    private static ReadinessSummary BuildReadiness(
        TargetConfiguration target,
        bool dryRun,
        string executedAdapter,
        bool functionallyPassed,
        IReadOnlyList<CallResult> calls)
    {
        var liveTechnical = calls.Any(call =>
            call.Error?.Category is "model-or-deployment"
                or "authentication"
                or "invalid-request"
                or "provider-error"
                or "transport"
                or "timeout"
                or "service"
                or "rate-limit"
                or "invalid-response"
                or "unsupported-combination")
            ? "not-compatible"
            : "compatible";
        var technical = dryRun || executedAdapter == AdapterIds.LocalFixture
            ? "not-evaluated"
            : liveTechnical;

        return new ReadinessSummary
        {
            TechnicalCompatibility = technical,
            FunctionalValidation = functionallyPassed ? "passed" : "failed",
            EuDataZone = NormalizeEvidence(
                target.Evidence.EuDataZone,
                target.Evidence.EuDataZoneReference),
            EuDataZoneReference = target.Evidence.EuDataZoneReference,
            Capacity = NormalizeEvidence(
                target.Evidence.Capacity,
                target.Evidence.CapacityReference),
            CapacityReference = target.Evidence.CapacityReference,
        };
    }

    private static string NormalizeEvidence(string? evidence, string? reference)
    {
        var state = string.IsNullOrWhiteSpace(evidence) ? "unknown" : evidence.Trim().ToLowerInvariant();
        return state == "confirmed" && string.IsNullOrWhiteSpace(reference)
            ? "unverified"
            : state;
    }

    private static string EffectiveApiVersion(
        TargetConfiguration target,
        string executedAdapter,
        bool dryRun)
    {
        if (dryRun)
        {
            return "fixture-no-api";
        }

        if (executedAdapter == AdapterIds.DirectRest)
        {
            return target.ApiKind == ApiKinds.AzureOpenAi
                ? target.ApiVersion ?? "v1"
                : "v1";
        }

        if (executedAdapter == AdapterIds.CopilotCli)
        {
            return "programmatic-cli";
        }

        return "v1";
    }
}
