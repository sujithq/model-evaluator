using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Tests;

public sealed class ReportingTests
{
    [Fact]
    public void Summarise_AggregatesAttemptsPerModelAndScenario()
    {
        var report = CreateReport();

        var summaries = ReportAggregator.Summarise(report);

        var good = summaries.Single(s => s.ModelId == "good-model");
        Assert.Equal(2, good.TotalAttempts);
        Assert.Equal(2, good.SuccessfulAttempts);
        Assert.Equal(1, good.SuccessRate);
        Assert.Equal(2, good.BuildSuccesses);
        Assert.Null(good.TotalTokens);

        var bad = summaries.Single(s => s.ModelId == "bad-model");
        Assert.Equal(0, bad.SuccessfulAttempts);
        Assert.Equal(1, bad.ModelFailures);
        Assert.Equal(1, bad.BudgetExceeded);
        Assert.Equal(1500, bad.TotalTokens);
        Assert.Equal(0.25m, bad.TotalCostUsd);
    }

    [Fact]
    public void Render_ContainsTraceabilityAndDimensions()
    {
        var markdown = MarkdownReportWriter.Render(CreateReport());

        Assert.Contains("| Scenario | Benchmark | Model |", markdown, StringComparison.Ordinal);
        Assert.Contains("prompt-hash-1", markdown, StringComparison.Ordinal);
        Assert.Contains("BudgetExceeded", markdown, StringComparison.Ordinal);
        Assert.Contains("tokens n/a", markdown, StringComparison.Ordinal);
        Assert.Contains("Unavailable measurements", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| | ", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithoutAttempts_ExplainsTheEmptyRun()
    {
        var markdown = MarkdownReportWriter.Render(CreateReport() with { Attempts = [] });

        Assert.Contains("No attempts were executed.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ProducesBothReportFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var json = JsonReportWriter.Write(CreateReport(), directory);
            var markdown = MarkdownReportWriter.Write(CreateReport(), directory);

            Assert.True(File.Exists(json));
            Assert.True(File.Exists(markdown));
            Assert.Contains("\"promptHash\": \"prompt-hash-1\"", File.ReadAllText(json), StringComparison.Ordinal);
            Assert.StartsWith("#", File.ReadAllText(markdown), StringComparison.Ordinal);
        }

        [Fact]
        public void ScenarioDetailsRender_ContainsMetadataEffectiveBudgetsAndResolvedPrompt()
        {
            var scenario = new ScenarioPackage(
                new ScenarioDefinition
                {
                    Id = "scenario-one",
                    Name = "Scenario one",
                    ProjectType = "Console",
                    BenchmarkVersion = "1.0.0",
                    AllowedPackages = ["Example.Package"],
                    RequiredGlobs = ["src/**/*.cs"],
                    PreservedPaths = ["global.json"],
                    Budget = new ScenarioBudget { GenerationTimeoutSeconds = 30 },
                    Samples = new Dictionary<string, SampleVariantDefinition>
                    {
                        ["good"] = new()
                        {
                            Path = "samples/good",
                            Description = "Known-good implementation.",
                        },
                    },
                },
                "/benchmarks/scenario-one",
                "# Prompt\n\nUse this contract.\n\n```csharp\nConsole.WriteLine();\n```");

            var markdown = ScenarioDetailsMarkdownWriter.Render(
                [scenario],
                new BudgetOverrides { GenerationTimeoutSeconds = 45 });

            Assert.Contains("## `scenario-one` - Scenario one", markdown, StringComparison.Ordinal);
            Assert.Contains("| Generation | 45 |", markdown, StringComparison.Ordinal);
            Assert.Contains("`Example.Package`", markdown, StringComparison.Ordinal);
            Assert.Contains("`samples/good`", markdown, StringComparison.Ordinal);
            Assert.Contains("### Resolved prompt", markdown, StringComparison.Ordinal);
            Assert.Contains("````markdown", markdown, StringComparison.Ordinal);
            Assert.Contains("Console.WriteLine();", markdown, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static EvaluationReport CreateReport()
    {
        var environment = new EnvironmentInfo
        {
            OperatingSystem = "linux",
            Architecture = "X64",
            DotnetSdkVersion = "10.0.100",
            EvaluatorVersion = "1.0.0",
            ExecutionImage = "ubuntu-24.04",
            GitCommit = "abc123",
        };

        var runner = new RunnerInfo { Name = "test-runner", Version = "1.0.0" };

        return new EvaluationReport
        {
            RunId = "run-1",
            StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddMinutes(5),
            Runner = runner,
            Environment = environment,
            Attempts =
            [
                Attempt("good-model", 1, AttemptOutcome.Success, environment, runner),
                Attempt("good-model", 2, AttemptOutcome.Success, environment, runner),
                Attempt("bad-model", 1, AttemptOutcome.ModelFailure, environment, runner, tokens: 1500, cost: 0.25m),
                Attempt("bad-model", 2, AttemptOutcome.BudgetExceeded, environment, runner),
            ],
        };
    }

    private static AttemptResult Attempt(
        string modelId,
        int repetition,
        AttemptOutcome outcome,
        EnvironmentInfo environment,
        RunnerInfo runner,
        long? tokens = null,
        decimal? cost = null)
    {
        var passed = outcome == AttemptOutcome.Success;
        return new AttemptResult
        {
            AttemptId = $"{modelId}-{repetition}",
            ScenarioId = "scenario-one",
            BenchmarkVersion = "1.0.0",
            PromptHash = "prompt-hash-1",
            ModelId = modelId,
            Adapter = "local-sample",
            Runner = runner,
            Environment = environment,
            Repetition = repetition,
            StartedAt = DateTimeOffset.UnixEpoch,
            CompletedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
            Outcome = outcome,
            FailureReason = passed ? null : "check failed",
            Checks =
            [
                passed
                    ? CheckResult.Pass("build.compile", CheckCategory.BuildAndExecution)
                    : CheckResult.Fail("build.compile", CheckCategory.BuildAndExecution, "compile error"),
                passed
                    ? CheckResult.Pass("instructions.readme", CheckCategory.InstructionAdherence)
                    : CheckResult.Fail("instructions.readme", CheckCategory.InstructionAdherence, "missing"),
                passed
                    ? CheckResult.Pass("quality.format", CheckCategory.CodeQuality)
                    : CheckResult.Skip("quality.format", CheckCategory.CodeQuality, "skipped"),
                passed
                    ? CheckResult.Pass("acceptance.all-passed", CheckCategory.FunctionalCorrectness)
                    : CheckResult.Fail("acceptance.all-passed", CheckCategory.FunctionalCorrectness, "failed"),
            ],
            Acceptance = passed ? new AcceptanceSummary { Passed = 10 } : new AcceptanceSummary { Passed = 4, Failed = 6 },
            GeneratedTests = new GeneratedTestSummary { Passed = passed ? 5 : 0, Failed = passed ? 0 : 5 },
            Efficiency = new EfficiencyMetrics
            {
                ElapsedSecondsTotal = 60 + repetition,
                InputTokens = tokens,
                EstimatedCostUsd = cost,
                UnavailableMetrics = tokens is null ? ["inputTokens", "outputTokens"] : ["toolCalls"],
            },
        };
    }
}
