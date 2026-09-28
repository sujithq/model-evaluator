using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Reporting;

public sealed record ModelRanking
{
    public required string ModelId { get; init; }

    public int? Rank { get; init; }

    public int TotalAttempts { get; init; }

    public int EvaluatedAttempts { get; init; }

    public int InfrastructureFailures { get; init; }

    public int SuccessfulAttempts { get; init; }

    public double? SuccessRate { get; init; }

    public double? AcceptanceAccuracy { get; init; }

    public int AiCreditsReportedAttempts { get; init; }

    public decimal? MeanAiCredits { get; init; }

    public double? MeanElapsedSeconds { get; init; }

    public double? MeanGenerationSeconds { get; init; }

    public bool Provisional => EvaluatedAttempts < 3;

    public string? ExclusionReason { get; init; }
}

public sealed record ScenarioRanking
{
    public required string ScenarioId { get; init; }

    public required string BenchmarkVersion { get; init; }

    public required string PromptHash { get; init; }

    public required string RunnerLabel { get; init; }

    public bool UsesAiCredits { get; init; }

    public required IReadOnlyList<ModelRanking> Models { get; init; }
}

/// <summary>Ranks comparable attempts by correctness first, then measured consumption and wall time.</summary>
public static class ScenarioRanker
{
    public static IReadOnlyList<ScenarioRanking> Rank(EvaluationReport report) =>
        ReportAggregator.Summarise(report)
            .GroupBy(s => (s.ScenarioId, s.BenchmarkVersion, s.PromptHash, s.RunnerLabel))
            .Select(group =>
            {
                var models = group.Select(CreateModelRanking).ToList();
                var eligible = models.Where(m => m.ExclusionReason is null).ToList();
                var useCredits = eligible.Count > 0 && eligible.All(m => m.MeanAiCredits is not null);
                var ordered = eligible.OrderByDescending(m => m.SuccessRate)
                    .ThenByDescending(m => m.AcceptanceAccuracy)
                    .ThenBy(m => useCredits ? m.MeanAiCredits : 0m)
                    .ThenBy(m => m.MeanElapsedSeconds)
                    .ThenBy(m => m.ModelId, StringComparer.Ordinal)
                    .ToList();
                var ranked = new List<ModelRanking>();
                for (var index = 0; index < ordered.Count; index++)
                {
                    var model = ordered[index];
                    var tied = index > 0 && SameScore(model, ordered[index - 1], useCredits);
                    ranked.Add(model with { Rank = tied ? ranked[index - 1].Rank : index + 1 });
                }

                ranked.AddRange(models.Where(m => m.ExclusionReason is not null).OrderBy(m => m.ModelId, StringComparer.Ordinal));
                return new ScenarioRanking
                {
                    ScenarioId = group.Key.ScenarioId,
                    BenchmarkVersion = group.Key.BenchmarkVersion,
                    PromptHash = group.Key.PromptHash,
                    RunnerLabel = group.Key.RunnerLabel,
                    UsesAiCredits = useCredits,
                    Models = ranked,
                };
            }).ToList();

    private static bool SameScore(ModelRanking left, ModelRanking right, bool useCredits) =>
        left.SuccessRate == right.SuccessRate && left.AcceptanceAccuracy == right.AcceptanceAccuracy
        && (!useCredits || left.MeanAiCredits == right.MeanAiCredits)
        && left.MeanElapsedSeconds == right.MeanElapsedSeconds;

    private static ModelRanking CreateModelRanking(ModelScenarioSummary summary)
    {
        var evaluated = summary.Attempts.Where(a => a.Outcome != AttemptOutcome.InfrastructureFailure).ToList();
        var coverage = evaluated.Count(a => a.Efficiency.AiCredits is not null);
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
