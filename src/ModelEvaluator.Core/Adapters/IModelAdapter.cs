using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Everything an adapter receives for one independent attempt.</summary>
public sealed record ModelAttemptContext
{
    public required ScenarioPackage Scenario { get; init; }

    public required ModelConfiguration Model { get; init; }

    /// <summary>Fresh workspace pre-populated with the scenario starter files.</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>Directory for prompts, transcripts and command output. Outside the model workspace.</summary>
    public required string ArtifactsPath { get; init; }

    /// <summary>Resolved instructions (shared + scenario + contracts).</summary>
    public required string Prompt { get; init; }

    /// <summary>File containing <see cref="Prompt"/>, written inside <see cref="ArtifactsPath"/>.</summary>
    public required string PromptFilePath { get; init; }

    public required TimeSpan Timeout { get; init; }

    public required int Repetition { get; init; }

    /// <summary>Optional observer for live runner output; transcripts are saved independently.</summary>
    public Action<string>? OnOutput { get; init; }
}

/// <summary>What an adapter reports back after producing a candidate solution.</summary>
public sealed record ModelAttemptOutput
{
    public required bool Succeeded { get; init; }

    public bool TimedOut { get; init; }

    /// <summary>True when the adapter itself failed (not the model's fault).</summary>
    public bool InfrastructureFailure { get; init; }

    public string? FailureReason { get; init; }

    public double DurationSeconds { get; init; }

    public int? ToolCalls { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public decimal? EstimatedCostUsd { get; init; }

    public required RunnerInfo Runner { get; init; }

    /// <summary>Metrics the adapter cannot supply, reported explicitly instead of as zero.</summary>
    public IReadOnlyList<string> UnavailableMetrics { get; init; } = [];
}

/// <summary>A pluggable model/provider integration.</summary>
public interface IModelAdapter
{
    /// <summary>Adapter key used in configuration.</summary>
    string Key { get; }

    Task<ModelAttemptOutput> GenerateAsync(ModelAttemptContext context, CancellationToken cancellationToken);
}
