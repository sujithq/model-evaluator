using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Execution;

namespace ModelEvaluator.Core.Results;

public sealed record EnvironmentSnapshot
{
    public required string OperatingSystem { get; init; }

    public required string Architecture { get; init; }

    public required string Framework { get; init; }

    public required string DotnetSdk { get; init; }

    public string? GitCommit { get; init; }
}

public sealed record TargetSnapshot
{
    public required string Id { get; init; }

    public required string ModelId { get; init; }

    public string? DeploymentId { get; init; }

    public required string ApiKind { get; init; }

    public required string ApiVersion { get; init; }

    public required string RequestedAdapter { get; init; }

    public required string ExecutedAdapter { get; init; }

    public required string Authentication { get; init; }

    public required AdapterPackage Package { get; init; }

    public required EffectiveRequestParameters Parameters { get; init; }
}

public sealed record RequestSnapshot
{
    public required string ScenarioFingerprint { get; init; }

    public required string ModelId { get; init; }

    public string? DeploymentId { get; init; }

    public required string ApiKind { get; init; }

    public required string ConversationHash { get; init; }

    public required string RequestHash { get; init; }

    public required EffectiveRequestParameters Parameters { get; init; }

    public IReadOnlyList<string> ToolNames { get; init; } = [];

    public string? ResponseFormatName { get; init; }

    public int MessageCount { get; init; }
}

public sealed record CallResult
{
    public required int CallNumber { get; init; }

    public int TurnIndex { get; init; }

    public int ToolRound { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public double LatencyMilliseconds { get; init; }

    public double? TimeToFirstByteMilliseconds { get; init; }

    public int Retries { get; init; }

    public string? FinishReason { get; init; }

    public TokenUsage Usage { get; init; } = new();

    public AdapterError? Error { get; init; }

    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];

    public required RequestSnapshot Request { get; init; }

    public IReadOnlyList<string> UnavailableMetrics { get; init; } = [];
}

public sealed record ToolTraceEntry
{
    public required string ToolCallId { get; init; }

    public required string Name { get; init; }

    public required JsonElement Arguments { get; init; }

    public JsonElement? Result { get; init; }

    public string? Error { get; init; }
}

public sealed record TurnResult
{
    public int Index { get; init; }

    public required string UserMessage { get; init; }

    public string? AssistantMessage { get; init; }
}

public sealed record ValidationCheck
{
    public required string Id { get; init; }

    public bool Passed { get; init; }

    public required string Detail { get; init; }
}

public sealed record ReadinessSummary
{
    public required string TechnicalCompatibility { get; init; }

    public required string FunctionalValidation { get; init; }

    public required string EuDataZone { get; init; }

    public string? EuDataZoneReference { get; init; }

    public required string Capacity { get; init; }

    public string? CapacityReference { get; init; }

    public bool Ready =>
        TechnicalCompatibility == "compatible"
        && FunctionalValidation == "passed"
        && EuDataZone == "confirmed"
        && Capacity == "confirmed";
}

public sealed record AttemptResult
{
    public required string AttemptId { get; init; }

    public required string RunId { get; init; }

    public int Repetition { get; init; }

    public required string ScenarioId { get; init; }

    public required string ScenarioVersion { get; init; }

    public required string ScenarioFingerprint { get; init; }

    public required TargetSnapshot Target { get; init; }

    public required string ExecutionMode { get; init; }

    public required string Outcome { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public IReadOnlyList<CallResult> Calls { get; init; } = [];

    public IReadOnlyList<ToolTraceEntry> ToolTrace { get; init; } = [];

    public IReadOnlyList<TurnResult> Turns { get; init; } = [];

    public IReadOnlyList<ValidationCheck> Checks { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public required ReadinessSummary Readiness { get; init; }

    public bool Passed => Outcome is "passed" or "expected-error";
}

public sealed record ComparisonResult
{
    public required string Id { get; init; }

    public required string BaselineTargetId { get; init; }

    public required string CandidateTargetId { get; init; }

    public string? IntendedVariable { get; init; }

    public IReadOnlyList<string> ChangedDimensions { get; init; } = [];

    public bool IsControlled => ChangedDimensions.Count == 1
                                && (IntendedVariable is null
                                    || string.Equals(
                                        ChangedDimensions[0],
                                        IntendedVariable,
                                        StringComparison.Ordinal));

    public required string Outcome { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record TargetSummary
{
    public required string TargetId { get; init; }

    public required string ModelId { get; init; }

    public string? DeploymentId { get; init; }

    public required string Adapter { get; init; }

    public required string ApiKind { get; init; }

    public int Planned { get; init; }

    public int Passed { get; init; }

    public double MeanLatencyMilliseconds { get; init; }

    public double? MeanTimeToFirstByteMilliseconds { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? ReasoningTokens { get; init; }

    public required ReadinessSummary Readiness { get; init; }
}

public sealed record EvaluationReport
{
    public required string SchemaVersion { get; init; }

    public required string RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required string ExecutionMode { get; init; }

    public required EnvironmentSnapshot Environment { get; init; }

    public int PlannedAttempts { get; init; }

    public IReadOnlyList<AttemptResult> Attempts { get; init; } = [];

    public IReadOnlyList<TargetSummary> Summaries { get; init; } = [];

    public IReadOnlyList<ComparisonResult> Comparisons { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}
