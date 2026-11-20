using System.Text.Json;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Tests;

public sealed class ScenarioRankingTests
{
    [Fact]
    public void Rank_UsesCostAndComparesModelsWithFirstModelBaseline()
    {
        var report = Report(
            Attempt("fast-failure", AttemptOutcome.ModelFailure, 0.01m, 1, passed: 10),
            Attempt("inaccurate", AttemptOutcome.ModelFailure, 0.01m, 1, passed: 1),
            Attempt("expensive", AttemptOutcome.Success, 5m, 5),
            Attempt("slow", AttemptOutcome.Success, 1m, 20),
            Attempt("winner", AttemptOutcome.Success, 1m, 10));

        var ranking = Assert.Single(ScenarioRanker.Rank(report));

        Assert.True(ranking.UsesAiCredits);
        Assert.Equal("fast-failure", ranking.BaselineModelId);
        Assert.Equal(["fast-failure", "inaccurate", "slow", "winner", "expensive"], ranking.Models.Select(m => m.ModelId));
        Assert.Equal([1, 1, 3, 3, 5], ranking.Models.Select(m => m.Rank!.Value));
        var baseline = ranking.Models[0];
        Assert.True(baseline.IsBaseline);
        Assert.Equal(0m, baseline.AiCreditsDeltaVsBaseline);
        Assert.Equal(0d, baseline.SuccessRateDeltaVsBaseline);
        var winner = ranking.Models.Single(model => model.ModelId == "winner");
        Assert.False(winner.IsBaseline);
        Assert.Equal(0.99m, winner.AiCreditsDeltaVsBaseline);
        Assert.Equal(1d, winner.SuccessRateDeltaVsBaseline);
        Assert.Equal(9d, winner.ElapsedSecondsDeltaVsBaseline);
        Assert.All(ranking.Models, m => Assert.True(m.Provisional));
    }

    [Fact]
    public void Rank_PartialUsageIsVisibleButNeverUsedAsCompleteCost()
    {
        var attempt = Attempt("partial", AttemptOutcome.BudgetExceeded, 0.01m, 300);
        attempt = attempt with
        {
            Efficiency = attempt.Efficiency with
            {
                UsageIsPartial = true,
                InputTokens = 100,
                OutputTokens = 20,
                ToolCalls = 2,
                PremiumRequests = 1,
                ApiRequests = 1,
                EstimatedCostUsd = 0.01m,
            },
        };
        var report = Report(attempt, Attempt("complete", AttemptOutcome.Success, 2m, 10));
        var ranking = Assert.Single(ScenarioRanker.Rank(report));
        Assert.True(ranking.UsesAiCredits);
        Assert.Equal(1, ranking.Models.Single(model => model.ModelId == "complete").Rank);
        var partial = ranking.Models.Single(m => m.ModelId == "partial");
        Assert.Null(partial.Rank);
        Assert.Null(partial.MeanAiCredits);
        Assert.Equal(0, partial.AiCreditsReportedAttempts);
        Assert.Equal("Complete AI-credit measurement unavailable.", partial.ExclusionReason);
        var summary = ReportAggregator.Summarise(report).Single(s => s.ModelId == "partial");
        Assert.Null(summary.TotalAiCredits);
        Assert.Null(summary.TotalTokens);
        Assert.Null(summary.TotalCostUsd);
        Assert.Null(summary.TotalToolCalls);
        Assert.Null(summary.TotalPremiumRequests);
        Assert.Null(summary.TotalApiRequests);
        Assert.Contains("## Partial usage measurements", MarkdownReportWriter.Render(report));
        Assert.Contains("0.01", MarkdownReportWriter.Render(report));
    }

    [Fact]
    public void Rank_MissingCreditDataDisablesCostTierForWholeGroup()
    {
        var ranking = Assert.Single(ScenarioRanker.Rank(Report(
            Attempt("complete", AttemptOutcome.Success, 0m, 50),
            Attempt("partial", AttemptOutcome.Success, 1m, 10),
            Attempt("partial", AttemptOutcome.Success, null, 10))));

        Assert.True(ranking.UsesAiCredits);
        Assert.Equal("complete", ranking.Models[0].ModelId);
        Assert.Equal(1, ranking.Models[0].Rank);
        var partial = ranking.Models[1];
        Assert.Equal("partial", partial.ModelId);
        Assert.Null(partial.Rank);
        Assert.Null(partial.MeanAiCredits);
        Assert.Equal(1, partial.AiCreditsReportedAttempts);
        Assert.Equal(2, partial.EvaluatedAttempts);
    }

    [Fact]
    public void Rank_ExcludesInfrastructureFailuresAndReferenceSamples()
    {
        var ranking = Assert.Single(ScenarioRanker.Rank(Report(
            Attempt("model", AttemptOutcome.Success, 2m, 10),
            Attempt("model", AttemptOutcome.InfrastructureFailure, null, 500),
            Attempt("unavailable", AttemptOutcome.InfrastructureFailure, null, 500),
            Attempt("reference", AttemptOutcome.Success, null, 0) with { Adapter = "local-sample" })));

        var model = ranking.Models.Single(m => m.ModelId == "model");
        Assert.Equal(1, model.Rank);
        Assert.Equal(1, model.SuccessRate);
        Assert.Equal(10, model.MeanElapsedSeconds);
        Assert.Equal(2m, model.MeanAiCredits);
        Assert.Equal(1, model.InfrastructureFailures);
        Assert.True(ranking.UsesAiCredits);
        Assert.All(ranking.Models.Where(m => m.ModelId != "model"), m =>
        {
            Assert.Null(m.Rank);
            Assert.NotNull(m.ExclusionReason);
        });
    }

    [Fact]
    public void Rank_WeightsEveryAttemptAndPenalizesUnexecutedOrSkippedAcceptance()
    {
        var ranking = Assert.Single(ScenarioRanker.Rank(Report(
            Attempt("model", AttemptOutcome.Success, 1m, 10),
            Attempt("model", AttemptOutcome.BudgetExceeded, 1m, 10) with { Acceptance = new AcceptanceSummary() },
            Attempt("model", AttemptOutcome.ModelFailure, 1m, 10) with
            {
                Acceptance = new AcceptanceSummary { Passed = 5, Skipped = 5 },
            })));

        var model = Assert.Single(ranking.Models);
        Assert.Equal(1.0 / 3, model.SuccessRate);
        Assert.Equal(0.5, model.AcceptanceAccuracy);
        Assert.False(model.Provisional);
    }

    [Fact]
    public void AutomatedQualityScore_UsesWeightedComponentsAndMarksProvisionalRuns()
    {
        var attempt = Attempt("model", AttemptOutcome.Success, 1m, 10, passed: 10) with
        {
            Checks =
            [
                CheckResult.Pass("build.compile", CheckCategory.BuildAndExecution),
                CheckResult.Pass("instructions.solution-file", CheckCategory.InstructionAdherence),
                CheckResult.Pass("quality.format", CheckCategory.CodeQuality),
                CheckResult.Pass("tests.generated", CheckCategory.CodeQuality),
            ],
        };

        var model = Assert.Single(Assert.Single(ScenarioRanker.Rank(Report(attempt))).Models);

        Assert.Equal(100, model.AutomatedQuality.Score);
        Assert.Equal(100, model.AutomatedQuality.FunctionalCorrectness);
        Assert.Equal(100, model.AutomatedQuality.InstructionAdherence);
        Assert.Equal(100, model.AutomatedQuality.BuildAndAnalyzer);
        Assert.Equal(100, model.AutomatedQuality.GeneratedTests);
        Assert.Equal(100, model.AutomatedQuality.Reliability);
        Assert.True(model.AutomatedQuality.Provisional);
        Assert.Equal("provisional", model.AutomatedQuality.Status);
    }

    [Fact]
    public void AutomatedQualityScore_CapsAcceptanceFailuresAndLeavesInfrastructureIncomplete()
    {
        var failed = Attempt("failed", AttemptOutcome.ModelFailure, 1m, 10, passed: 5) with
        {
            Checks =
            [
                CheckResult.Pass("build.compile", CheckCategory.BuildAndExecution),
                CheckResult.Pass("instructions.solution-file", CheckCategory.InstructionAdherence),
                CheckResult.Pass("quality.format", CheckCategory.CodeQuality),
                CheckResult.Pass("tests.generated", CheckCategory.CodeQuality),
            ],
        };
        var infrastructure = Attempt("infra", AttemptOutcome.InfrastructureFailure, null, 10);
        var ranking = ScenarioRanker.Rank(Report(failed, infrastructure)).Single();

        Assert.Equal(60, ranking.Models.Single(model => model.ModelId == "failed").AutomatedQuality.Score);
        Assert.Null(ranking.Models.Single(model => model.ModelId == "infra").AutomatedQuality.Score);
        Assert.Equal("incomplete: infrastructure failures only",
            ranking.Models.Single(model => model.ModelId == "infra").AutomatedQuality.Status);
    }

    [Fact]
    public void Rank_EqualScoresShareRankWithoutAlphabeticalBias()
    {
        var ranking = Assert.Single(ScenarioRanker.Rank(Report(
            Attempt("b", AttemptOutcome.Success, 1m, 10),
            Attempt("a", AttemptOutcome.Success, 1m, 10),
            Attempt("c", AttemptOutcome.Success, 2m, 10))));

        Assert.Equal(["a", "b", "c"], ranking.Models.Select(m => m.ModelId));
        Assert.Equal([1, 1, 3], ranking.Models.Select(m => m.Rank!.Value));
    }

    [Fact]
    public void Rank_DoesNotMixTasksVersionsPromptsRunnersOrConcurrency()
    {
        var attempt = Attempt("model", AttemptOutcome.Success, 1m, 10);
        var report = Report(
            attempt,
            attempt with { ScenarioId = "other-task" },
            attempt with { BenchmarkVersion = "2" },
            attempt with { PromptHash = "other-prompt" },
            attempt with { Runner = new RunnerInfo { Name = "other-runner", Version = "1" } },
            attempt with { Environment = attempt.Environment with { MaxParallel = 2 } });

        Assert.Equal(6, ReportAggregator.Summarise(report).Count);
        Assert.Equal(6, ScenarioRanker.Rank(report).Count);
        Assert.Contains(ScenarioRanker.Rank(report), ranking => ranking.MaxParallel == 2);
    }

    [Fact]
    public void Summarise_MissingMeasurementsDoNotBecomeZeroOrPartialTotals()
    {
        var complete = Attempt("model", AttemptOutcome.Success, 1m, 10) with
        {
            Efficiency = new EfficiencyMetrics
            {
                AiCredits = 1m,
                InputTokens = 100,
                OutputTokens = 10,
                ToolCalls = 5,
                EstimatedCostUsd = 1m,
            },
        };
        var incomplete = complete with { Efficiency = new EfficiencyMetrics { InputTokens = 50 } };
        var summary = Assert.Single(ReportAggregator.Summarise(Report(complete, incomplete)));

        Assert.Null(summary.TotalAiCredits);
        Assert.Equal(1, summary.AiCreditsReportedAttempts);
        Assert.Null(summary.TotalCostUsd);
        Assert.Null(summary.TotalTokens);
        Assert.Null(summary.TotalToolCalls);
    }

    [Fact]
    public void Summarise_CompleteMeasurementsAreAddedWithoutSubtotals()
    {
        var attempt = Attempt("model", AttemptOutcome.Success, 1m, 10) with
        {
            Efficiency = new EfficiencyMetrics
            {
                AiCredits = 1m,
                InputTokens = 100,
                OutputTokens = 10,
                CacheReadTokens = 60,
                CacheWriteTokens = 20,
                ReasoningTokens = 5,
                ApiRequests = 3,
                PremiumRequests = 1m,
                EstimatedCostUsd = 0.25m,
                ToolCalls = 4,
            },
        };
        var summary = Assert.Single(ReportAggregator.Summarise(Report(attempt, attempt)));

        Assert.Equal(2m, summary.TotalAiCredits);
        Assert.Equal(220, summary.TotalTokens);
        Assert.Equal(6, summary.TotalApiRequests);
        Assert.Equal(2m, summary.TotalPremiumRequests);
        Assert.Equal(0.5m, summary.TotalCostUsd);
        Assert.Equal(8, summary.TotalToolCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writers_IncludeSameRankingsAndUsageWarnings(bool listUnavailableMetrics)
    {
        var root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var report = Report(Attempt("model", AttemptOutcome.Success, 1m, 10) with
            {
                Efficiency = new EfficiencyMetrics
                {
                    UsageWarnings = ["Usage file missing."],
                    UnavailableMetrics = listUnavailableMetrics ? ["aiCredits"] : [],
                },
            });
            var path = JsonReportWriter.Write(report, root);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var ranking = json.RootElement.GetProperty("rankings")[0];
            Assert.False(ranking.GetProperty("usesAiCredits").GetBoolean());
            Assert.Equal("model", json.RootElement.GetProperty("baselineModelId").GetString());
            Assert.False(ranking.GetProperty("models")[0].TryGetProperty("rank", out _));
            var markdown = MarkdownReportWriter.Render(report);
            Assert.Contains("## Per-task rankings", markdown);
            Assert.Contains("Cost ranking: unavailable", markdown);
            Assert.Contains("| n/a | model | baseline |", markdown);
            Assert.Contains("## Usage collection warnings", markdown);
            Assert.Contains("Usage file missing.", markdown);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AttemptResult Attempt(string model, AttemptOutcome outcome, decimal? credits, double seconds, int passed = 10) => new()
    {
        AttemptId = Guid.NewGuid().ToString("N"),
        ScenarioId = "task",
        BenchmarkVersion = "1",
        PromptHash = "hash",
        ModelId = model,
        Adapter = "command-line",
        Runner = new RunnerInfo { Name = "copilot", Version = "1" },
        Environment = Environment(),
        Repetition = 1,
        StartedAt = DateTimeOffset.UnixEpoch,
        CompletedAt = DateTimeOffset.UnixEpoch.AddSeconds(seconds),
        Outcome = outcome,
        Acceptance = new AcceptanceSummary { Passed = passed, Failed = 10 - passed },
        Efficiency = new EfficiencyMetrics { AiCredits = credits, ElapsedSecondsTotal = seconds, ElapsedSecondsGeneration = seconds / 2 },
    };

    private static EvaluationReport Report(params AttemptResult[] attempts) => new()
    {
        BaselineModelId = attempts.FirstOrDefault()?.ModelId,
        RunId = "run",
        StartedAt = DateTimeOffset.UnixEpoch,
        CompletedAt = DateTimeOffset.UnixEpoch,
        Runner = new RunnerInfo { Name = "evaluator", Version = "1" },
        Environment = Environment(),
        Attempts = attempts,
    };

    private static EnvironmentInfo Environment() => new()
    {
        OperatingSystem = "test",
        Architecture = "test",
        DotnetSdkVersion = "10",
        EvaluatorVersion = "1",
    };
}
