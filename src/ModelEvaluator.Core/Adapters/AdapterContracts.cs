using System.Text.Json;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Execution;
using ModelEvaluator.ApiComparison.Core.Scenarios;

namespace ModelEvaluator.ApiComparison.Core.Adapters;

public sealed record ConversationMessage
{
    public required string Role { get; init; }

    public string? Content { get; init; }

    public string? ToolCallId { get; init; }

    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];
}

public sealed record ModelToolCall
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required JsonElement Arguments { get; init; }

    public string? ArgumentError { get; init; }
}

public sealed record AdapterInvocation
{
    public required TargetConfiguration Target { get; init; }

    public required ScenarioDefinition Scenario { get; init; }

    public required IReadOnlyList<ConversationMessage> Messages { get; init; }

    public required EffectiveRequestParameters Parameters { get; init; }

    public int TurnIndex { get; init; }

    public int ToolRound { get; init; }

    public int MaxRetries { get; init; }

    public TimeSpan Timeout { get; init; }
}

public sealed record AdapterPackage
{
    public required string Name { get; init; }

    public required string Version { get; init; }
}

public sealed record TokenUsage
{
    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? TotalTokens { get; init; }

    public long? CachedInputTokens { get; init; }

    public long? ReasoningTokens { get; init; }
}

public sealed record AdapterError
{
    public required string Category { get; init; }

    public string? Code { get; init; }

    public int? StatusCode { get; init; }

    public required string Message { get; init; }
}

public sealed record AdapterCompletion
{
    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required double LatencyMilliseconds { get; init; }

    public double? TimeToFirstByteMilliseconds { get; init; }

    public int Retries { get; init; }

    public string? FinishReason { get; init; }

    public string? Text { get; init; }

    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];

    public TokenUsage Usage { get; init; } = new();

    public AdapterError? Error { get; init; }

    public IReadOnlyList<string> UnavailableMetrics { get; init; } = [];
}

public sealed record CompatibilityResult(bool Supported, string Summary);

public interface IModelAdapter
{
    string Id { get; }

    string DisplayName { get; }

    CompatibilityResult CheckCompatibility(TargetConfiguration target);

    AdapterPackage DescribePackage(TargetConfiguration target);

    Task<AdapterCompletion> CompleteAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken);
}
