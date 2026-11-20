using System.Text.Json;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Evaluation;
using ModelEvaluator.ApiComparison.Core.Infrastructure;
using ModelEvaluator.ApiComparison.Core.Reporting;
using ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

namespace ModelEvaluator.ApiComparison.Core.Tests;

public class DryRunEvaluationTests
{
    [Fact]
    public async Task RunsEveryScenarioAndKeepsMissingDeploymentEvidenceAsUnknown()
    {
        var configuration = ConfigurationLoader.Load(
            Path.Combine(RepositoryLocator.Root, "config", "evaluation.offline.json"));
        var report = await new EvaluationRunner().RunAsync(
            configuration,
            new EvaluationRunOptions
            {
                DryRun = true,
            });

        Assert.Equal(10, report.PlannedAttempts);
        Assert.Equal(10, report.Attempts.Count);
        Assert.All(report.Attempts, attempt => Assert.True(attempt.Passed, attempt.Outcome));
        Assert.All(report.Attempts, attempt =>
            Assert.Equal("not-evaluated", attempt.Readiness.TechnicalCompatibility));
        Assert.All(report.Attempts, attempt =>
            Assert.Equal("unknown", attempt.Readiness.EuDataZone));
        Assert.All(report.Attempts, attempt =>
            Assert.Equal("unknown", attempt.Readiness.Capacity));
        Assert.All(report.Attempts, attempt => Assert.False(attempt.Readiness.Ready));

        var toolAttempt = Assert.Single(report.Attempts, attempt =>
            attempt.ScenarioId == "deterministic-tool-calling"
            && attempt.Target.Id == "fixture-gpt-5-1");
        var trace = Assert.Single(toolAttempt.ToolTrace);
        Assert.Equal("lookup_synthetic_plan", trace.Name);
        Assert.Equal("SYNTH-100", trace.Arguments.GetProperty("memberId").GetString());

        var multiTurn = Assert.Single(report.Attempts, attempt =>
            attempt.ScenarioId == "multi-turn-consistency"
            && attempt.Target.Id == "fixture-gpt-5-1");
        Assert.Equal(2, multiTurn.Turns.Count);
        Assert.Equal("BLUE-OWL-17", multiTurn.Turns[^1].AssistantMessage);

        var expectedError = Assert.Single(report.Attempts, attempt =>
            attempt.ScenarioId == "unsupported-parameter-handling"
            && attempt.Target.Id == "fixture-gpt-5-1");
        Assert.Equal("expected-error", expectedError.Outcome);
        Assert.Equal("unsupported-parameter", expectedError.Calls.Single().Error?.Category);
        Assert.Equal("controlled", Assert.Single(report.Comparisons).Outcome);
    }

    [Fact]
    public async Task WritesMachineAndHumanReportsWithoutConfigurationSecrets()
    {
        var target = ScenarioFactory.Target(
            AdapterIds.OpenAiSdk,
            "https://secret-endpoint.example/openai/v1");
        target = new TargetConfiguration
        {
            Id = target.Id,
            Adapter = target.Adapter,
            ApiKind = target.ApiKind,
            ModelId = target.ModelId,
            Endpoint = target.Endpoint,
            ApiKey = "DO-NOT-REPORT",
            Authentication = target.Authentication,
            Parameters = target.Parameters,
        };
        var configuration = new EvaluationConfiguration
        {
            ScenarioRoot = Path.Combine(RepositoryLocator.Root, "scenarios", "v1"),
            Targets = [target],
            Repetitions = 1,
        };

        var report = await new EvaluationRunner().RunAsync(
            configuration,
            new EvaluationRunOptions
            {
                DryRun = true,
            });
        var json = JsonSerializer.Serialize(report, JsonDefaults.Write);
        var markdown = MarkdownReportWriter.Render(report);

        Assert.DoesNotContain("DO-NOT-REPORT", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-endpoint", json, StringComparison.Ordinal);
        Assert.DoesNotContain("DO-NOT-REPORT", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-endpoint", markdown, StringComparison.Ordinal);
        Assert.Contains("Missing EU Data Zone or capacity evidence is not a pass", markdown);
        Assert.Contains("not-evaluated", markdown);
    }
}
