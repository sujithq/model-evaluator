namespace ModelEvaluator.Core.Results;

/// <summary>Classification of the final state of an attempt.</summary>
public enum AttemptOutcome
{
    /// <summary>All mandatory checks passed.</summary>
    Success,

    /// <summary>The model produced output that failed one or more mandatory checks.</summary>
    ModelFailure,

    /// <summary>The model exceeded its assigned time budget. Counts as an unsuccessful attempt.</summary>
    BudgetExceeded,

    /// <summary>The harness itself failed (adapter crash, missing tooling, workspace error).</summary>
    InfrastructureFailure,
}

/// <summary>Aggregated results of evaluator-owned acceptance checks.</summary>
public sealed record AcceptanceSummary
{
    public int Passed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public int Total => Passed + Failed + Skipped;
}

/// <summary>Results of the tests authored by the evaluated model.</summary>
public sealed record GeneratedTestSummary
{
    public int Passed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public int Total => Passed + Failed + Skipped;
}

/// <summary>Efficiency measurements. Null values mean "not reported by the adapter".</summary>
public sealed record EfficiencyMetrics
{
    public double ElapsedSecondsTotal { get; init; }

    public double ElapsedSecondsGeneration { get; init; }

    public double ElapsedSecondsEvaluation { get; init; }

    public int? ToolCalls { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public decimal? EstimatedCostUsd { get; init; }

    /// <summary>Metric names the adapter could not supply, reported explicitly.</summary>
    public IReadOnlyList<string> UnavailableMetrics { get; init; } = [];
}

/// <summary>Identity of the agent runner used to drive the model.</summary>
public sealed record RunnerInfo
{
    public required string Name { get; init; }

    public required string Version { get; init; }
}

/// <summary>Execution environment captured for traceability.</summary>
public sealed record EnvironmentInfo
{
    public required string OperatingSystem { get; init; }

    public required string Architecture { get; init; }

    public required string DotnetSdkVersion { get; init; }

    public required string EvaluatorVersion { get; init; }

    public string? ExecutionImage { get; init; }

    public string? GitCommit { get; init; }
}

/// <summary>Everything recorded for one independent attempt.</summary>
public sealed record AttemptResult
{
    public required string AttemptId { get; init; }

    public required string ScenarioId { get; init; }

    public required string BenchmarkVersion { get; init; }

    public required string PromptHash { get; init; }

    public required string ModelId { get; init; }

    public required string Adapter { get; init; }

    public IReadOnlyDictionary<string, string> ModelSettings { get; init; } =
        new Dictionary<string, string>();

    public required RunnerInfo Runner { get; init; }

    public required EnvironmentInfo Environment { get; init; }

    public required int Repetition { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required AttemptOutcome Outcome { get; init; }

    public string? FailureReason { get; init; }

    public IReadOnlyList<CheckResult> Checks { get; init; } = [];

    public AcceptanceSummary Acceptance { get; init; } = new();

    public GeneratedTestSummary GeneratedTests { get; init; } = new();

    public EfficiencyMetrics Efficiency { get; init; } = new();

    public string? ArtifactsPath { get; init; }

    public bool IsSuccess => Outcome == AttemptOutcome.Success;

    public IEnumerable<CheckResult> ChecksIn(CheckCategory category) =>
        Checks.Where(c => c.Category == category);
}

/// <summary>Full report for one evaluation run (matrix of models x scenarios x repetitions).</summary>
public sealed record EvaluationReport
{
    public required string RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required RunnerInfo Runner { get; init; }

    public required EnvironmentInfo Environment { get; init; }

    public IReadOnlyList<AttemptResult> Attempts { get; init; } = [];
}
