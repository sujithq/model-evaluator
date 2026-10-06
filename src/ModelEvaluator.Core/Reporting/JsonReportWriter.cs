using System.Text;
using System.Text.Json;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Reporting;

/// <summary>Writes the machine-readable JSON report.</summary>
public static class JsonReportWriter
{
    public static string Write(EvaluationReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "results.json");

        var payload = new
        {
            report.RunId,
            report.StartedAt,
            report.CompletedAt,
            report.Runner,
            report.Environment,
            report.Cancelled,
            report.PlannedAttempts,
            report.NotStartedAttempts,
            Summaries = ReportAggregator.Summarise(report).Select(s => new
            {
                s.ScenarioId,
                s.ModelId,
                s.BenchmarkVersion,
                s.PromptHash,
                s.RunnerLabel,
                s.MaxParallel,
                s.TotalAttempts,
                s.SuccessfulAttempts,
                s.SuccessRate,
                s.ModelFailures,
                s.BudgetExceeded,
                s.InfrastructureFailures,
                s.BuildSuccesses,
                s.InstructionCompliant,
                s.CodeQualityPassing,
                s.AcceptancePassed,
                s.AcceptanceFailed,
                s.AcceptanceSkipped,
                s.MeanElapsedSeconds,
                s.MeanGenerationSeconds,
                s.ElapsedStandardDeviation,
                s.TotalTokens,
                s.TotalInputTokens,
                s.TotalOutputTokens,
                s.TotalCacheReadTokens,
                s.TotalCacheWriteTokens,
                s.TotalReasoningTokens,
                s.TotalToolCalls,
                s.TotalCostUsd,
                s.TotalAiCredits,
                s.TotalEquivalentCostUsd,
                s.AiCreditsReportedAttempts,
                s.TotalPremiumRequests,
                s.TotalApiRequests,
                s.TotalApiDurationSeconds,
                s.ReportedModels,
            }),
            Rankings = ScenarioRanker.Rank(report),
            report.Attempts,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonDefaults.Options),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
