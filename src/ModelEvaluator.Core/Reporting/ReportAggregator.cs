using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Reporting;

/// <summary>Aggregated statistics for one model on one scenario.</summary>
public sealed record ModelScenarioSummary
{
    public required string ScenarioId { get; init; }

    public required string ModelId { get; init; }

    public required string BenchmarkVersion { get; init; }

    public required string PromptHash { get; init; }

    public required string RunnerLabel { get; init; }

    public int MaxParallel { get; init; } = 1;

    public required IReadOnlyList<AttemptResult> Attempts { get; init; }

    public int TotalAttempts => Attempts.Count;

    public int SuccessfulAttempts => Attempts.Count(a => a.IsSuccess);

    public double SuccessRate => TotalAttempts == 0 ? 0 : (double)SuccessfulAttempts / TotalAttempts;

    public int BudgetExceeded => Attempts.Count(a => a.Outcome == AttemptOutcome.BudgetExceeded);

    public int InfrastructureFailures => Attempts.Count(a => a.Outcome == AttemptOutcome.InfrastructureFailure);

    public int ModelFailures => Attempts.Count(a => a.Outcome == AttemptOutcome.ModelFailure);

    /// <summary>Attempts where every mandatory build and execution check passed.</summary>
    public int BuildSuccesses => Attempts.Count(a => AllMandatoryPassed(a, CheckCategory.BuildAndExecution));

    public int InstructionCompliant => Attempts.Count(a => AllMandatoryPassed(a, CheckCategory.InstructionAdherence));

    public int CodeQualityPassing => Attempts.Count(a => AllMandatoryPassed(a, CheckCategory.CodeQuality));

    public int AcceptancePassed => Attempts.Sum(a => a.Acceptance.Passed);

    public int AcceptanceFailed => Attempts.Sum(a => a.Acceptance.Failed);

    public int AcceptanceSkipped => Attempts.Sum(a => a.Acceptance.Skipped);

    public double MeanElapsedSeconds => Attempts.Count == 0
        ? 0
        : Attempts.Average(a => a.Efficiency.ElapsedSecondsTotal);

    public double MeanGenerationSeconds => Attempts.Count == 0
        ? 0
        : Attempts.Average(a => a.Efficiency.ElapsedSecondsGeneration);

    public double ElapsedStandardDeviation
    {
        get
        {
            if (Attempts.Count < 2)
            {
                return 0;
            }

            var mean = MeanElapsedSeconds;
            var variance = Attempts.Sum(a => Math.Pow(a.Efficiency.ElapsedSecondsTotal - mean, 2)) / (Attempts.Count - 1);
            return Math.Sqrt(variance);
        }
    }

    public long? TotalTokens
    {
        get
        {
            return HasCompleteUsage && Attempts.All(a => a.Efficiency.InputTokens is not null && a.Efficiency.OutputTokens is not null)
                ? Attempts.Sum(a => a.Efficiency.InputTokens!.Value + a.Efficiency.OutputTokens!.Value)
                : null;
        }
    }

    private bool HasCompleteUsage => Attempts.Count > 0 && Attempts.All(a => !a.Efficiency.UsageIsPartial);

    public decimal? TotalCostUsd => HasCompleteUsage && Attempts.All(a => a.Efficiency.EstimatedCostUsd is not null)
        ? Attempts.Sum(a => a.Efficiency.EstimatedCostUsd!.Value)
        : null;

    public int? TotalToolCalls => HasCompleteUsage && Attempts.All(a => a.Efficiency.ToolCalls is not null)
        ? Attempts.Sum(a => a.Efficiency.ToolCalls!.Value)
        : null;

    public int AiCreditsReportedAttempts => Attempts.Count(a => !a.Efficiency.UsageIsPartial && a.Efficiency.AiCredits is not null);

    public decimal? TotalAiCredits => Attempts.Count > 0 && AiCreditsReportedAttempts == Attempts.Count
        ? Attempts.Sum(a => a.Efficiency.AiCredits!.Value)
        : null;

    /// <summary>Published USD value of consumed credits, not an additional invoice charge.</summary>
    public decimal? TotalEquivalentCostUsd => TotalAiCredits * 0.01m;

    public decimal? TotalPremiumRequests => HasCompleteUsage && Attempts.All(a => a.Efficiency.PremiumRequests is not null)
        ? Attempts.Sum(a => a.Efficiency.PremiumRequests!.Value)
        : null;

    public long? TotalApiRequests => HasCompleteUsage && Attempts.All(a => a.Efficiency.ApiRequests is not null)
        ? Attempts.Sum(a => a.Efficiency.ApiRequests!.Value)
        : null;

    public IReadOnlyList<string> ReportedModels =>
        Attempts.SelectMany(a => a.Efficiency.ReportedModels).Distinct().Order(StringComparer.Ordinal).ToList();

    private static bool AllMandatoryPassed(AttemptResult attempt, CheckCategory category)
    {
        var checks = attempt.ChecksIn(category).Where(c => c.Mandatory).ToList();
        return checks.Count > 0 && checks.All(c => c.Status == CheckStatus.Passed);
    }
}

/// <summary>Builds the comparison view used by both report formats.</summary>
public static class ReportAggregator
{
    public static IReadOnlyList<ModelScenarioSummary> Summarise(EvaluationReport report) => report.Attempts
        .GroupBy(a => (a.ScenarioId, a.ModelId, a.BenchmarkVersion, a.PromptHash, a.Runner.Name, a.Runner.Version, a.Environment.MaxParallel))
        .Select(g => new ModelScenarioSummary
        {
            ScenarioId = g.Key.ScenarioId,
            ModelId = g.Key.ModelId,
            BenchmarkVersion = g.First().BenchmarkVersion,
            PromptHash = g.First().PromptHash,
            RunnerLabel = $"{g.First().Runner.Name}@{g.First().Runner.Version}",
            MaxParallel = g.Key.MaxParallel,
            Attempts = g.OrderBy(a => a.Repetition).ToList(),
        })
        .OrderBy(s => s.ScenarioId, StringComparer.Ordinal)
        .ThenBy(s => s.ModelId, StringComparer.Ordinal)
        .ToList();
}
