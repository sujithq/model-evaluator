using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Reporting;

public sealed record ModelRanking
{
    public required string ModelId { get; init; }

    public bool IsBaseline { get; init; }

    public int? Rank { get; init; }

    public int TotalAttempts { get; init; }

    public int EvaluatedAttempts { get; init; }

    public int InfrastructureFailures { get; init; }

    public int SuccessfulAttempts { get; init; }

    public double? SuccessRate { get; init; }

    public double? AcceptanceAccuracy { get; init; }

    public int AiCreditsReportedAttempts { get; init; }

    public decimal? MeanAiCredits { get; init; }

    public decimal? AiCreditsDeltaVsBaseline { get; init; }

    public double? SuccessRateDeltaVsBaseline { get; init; }

    public double? AcceptanceAccuracyDeltaVsBaseline { get; init; }

    public double? MeanElapsedSeconds { get; init; }

    public double? ElapsedSecondsDeltaVsBaseline { get; init; }

    public double? MeanGenerationSeconds { get; init; }

    public bool Provisional => EvaluatedAttempts < 3;

    public string? ExclusionReason { get; init; }
}

public sealed record ScenarioRanking
{
    public int MaxParallel { get; init; } = 1;

    public required string ScenarioId { get; init; }

    public required string BenchmarkVersion { get; init; }

    public required string PromptHash { get; init; }

    public required string RunnerLabel { get; init; }

    public bool UsesAiCredits { get; init; }

    public string? BaselineModelId { get; init; }

    public required IReadOnlyList<ModelRanking> Models { get; init; }
}

/// <summary>Ranks comparable attempts by complete AI-credit consumption and compares them with the configured baseline.</summary>
public static class ScenarioRanker
{
    public static IReadOnlyList<ScenarioRanking> Rank(EvaluationReport report) =>
        ReportAggregator.Summarise(report)
            .GroupBy(s => (s.ScenarioId, s.BenchmarkVersion, s.PromptHash, s.RunnerLabel, s.MaxParallel))
            .Select(group =>
            {
                var models = group.Select(CreateModelRanking).ToList();
                var baseline = models.FirstOrDefault(model =>
                    model.ModelId.Equals(report.BaselineModelId, StringComparison.Ordinal));
                models = models.Select(model => CompareWithBaseline(model, baseline)).ToList();

                var costRankable = models
                    .Where(model => model.ExclusionReason is null && model.MeanAiCredits is not null)
                    .ToList();
                var useCredits = costRankable.Count > 0;
                var ordered = costRankable
                    .OrderBy(model => model.MeanAiCredits)
                    .ThenBy(m => m.ModelId, StringComparer.Ordinal)
                    .ToList();
                var ranked = new List<ModelRanking>();
                for (var index = 0; index < ordered.Count; index++)
                {
                    var model = ordered[index];
                    var tied = index > 0 && model.MeanAiCredits == ordered[index - 1].MeanAiCredits;
                    ranked.Add(model with { Rank = tied ? ranked[index - 1].Rank : index + 1 });
                }

                ranked.AddRange(models
                    .Where(model => model.ExclusionReason is not null || model.MeanAiCredits is null)
                    .Select(model => model.ExclusionReason is null
                        ? model with { ExclusionReason = "Complete AI-credit measurement unavailable." }
                        : model)
                    .OrderBy(model => model.ModelId, StringComparer.Ordinal));
                return new ScenarioRanking
                {
                    ScenarioId = group.Key.ScenarioId,
                    BenchmarkVersion = group.Key.BenchmarkVersion,
                    PromptHash = group.Key.PromptHash,
                    RunnerLabel = group.Key.RunnerLabel,
                    MaxParallel = group.Key.MaxParallel,
                    UsesAiCredits = useCredits,
                    BaselineModelId = report.BaselineModelId,
                    Models = ranked,
                };
            }).ToList();

    private static ModelRanking CompareWithBaseline(ModelRanking model, ModelRanking? baseline) =>
        model with
        {
            IsBaseline = baseline is not null && model.ModelId.Equals(baseline.ModelId, StringComparison.Ordinal),
            AiCreditsDeltaVsBaseline = Difference(model.MeanAiCredits, baseline?.MeanAiCredits),
            SuccessRateDeltaVsBaseline = Difference(model.SuccessRate, baseline?.SuccessRate),
            AcceptanceAccuracyDeltaVsBaseline = Difference(model.AcceptanceAccuracy, baseline?.AcceptanceAccuracy),
            ElapsedSecondsDeltaVsBaseline = Difference(model.MeanElapsedSeconds, baseline?.MeanElapsedSeconds),
        };

    private static decimal? Difference(decimal? value, decimal? baseline) =>
        value is null || baseline is null ? null : value.Value - baseline.Value;

    private static double? Difference(double? value, double? baseline) =>
        value is null || baseline is null ? null : value.Value - baseline.Value;

    private static ModelRanking CreateModelRanking(ModelScenarioSummary summary)
    {
        var evaluated = summary.Attempts.Where(a => a.Outcome != AttemptOutcome.InfrastructureFailure).ToList();
        var coverage = evaluated.Count(a => !a.Efficiency.UsageIsPartial && a.Efficiency.AiCredits is not null);
        var exclusion = summary.Attempts.Any(a => a.Adapter.Equals(LocalSampleAdapter.AdapterKey, StringComparison.OrdinalIgnoreCase))
            ? "Reference samples are not model generations."
            : evaluated.Count == 0 ? "No evaluable attempts (infrastructure failures only)." : null;
        return new ModelRanking
        {
            ModelId = summary.ModelId,
            TotalAttempts = summary.TotalAttempts,
            EvaluatedAttempts = evaluated.Count,
            InfrastructureFailures = summary.InfrastructureFailures,
            SuccessfulAttempts = evaluated.Count(a => a.IsSuccess),
            SuccessRate = evaluated.Count == 0 ? null : (double)evaluated.Count(a => a.IsSuccess) / evaluated.Count,
            AcceptanceAccuracy = evaluated.Count == 0 ? null : evaluated.Average(a =>
                a.Acceptance.Total == 0 ? 0 : (double)a.Acceptance.Passed / a.Acceptance.Total),
            AiCreditsReportedAttempts = coverage,
            MeanAiCredits = evaluated.Count > 0 && coverage == evaluated.Count
                ? evaluated.Average(a => a.Efficiency.AiCredits!.Value) : null,
            MeanElapsedSeconds = evaluated.Count == 0 ? null : evaluated.Average(a => a.Efficiency.ElapsedSecondsTotal),
            MeanGenerationSeconds = evaluated.Count == 0 ? null : evaluated.Average(a => a.Efficiency.ElapsedSecondsGeneration),
            ExclusionReason = exclusion,
        };
    }
}
