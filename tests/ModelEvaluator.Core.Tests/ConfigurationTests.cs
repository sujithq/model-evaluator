using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Tests.TestSupport;

namespace ModelEvaluator.Core.Tests;

public class ConfigurationTests
{
    [Fact]
    public void ReportsUnsupportedAdapterProviderCombinationExplicitly()
    {
        var target = ScenarioFactory.Target();
        target = new TargetConfiguration
        {
            Id = target.Id,
            Adapter = target.Adapter,
            ApiKind = "anthropic",
            ModelId = target.ModelId,
            Authentication = target.Authentication,
        };
        var configuration = new EvaluationConfiguration
        {
            Targets = [target],
        };

        var result = ConfigurationValidator.Validate(configuration, requireLiveCredentials: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("unsupported apiKind", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticValidationAllowsDryRunWithoutLiveSecrets()
    {
        var target = new TargetConfiguration
        {
            Id = "live-target",
            Adapter = AdapterIds.OpenAiSdk,
            ApiKind = ApiKinds.OpenAi,
            ModelId = "model",
            Authentication = AuthenticationKinds.ApiKey,
        };
        var configuration = new EvaluationConfiguration
        {
            Targets = [target],
        };

        var dry = ConfigurationValidator.Validate(configuration, requireLiveCredentials: false);
        var live = ConfigurationValidator.Validate(configuration, requireLiveCredentials: true);

        Assert.True(dry.IsValid);
        Assert.False(live.IsValid);
        Assert.Contains(live.Errors, error => error.Contains("no API key", StringComparison.Ordinal));
        Assert.Contains(live.Errors, error => error.Contains("no endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public void ComparisonAnalyzerRejectsMultipleChangedDimensions()
    {
        var baseline = ScenarioFactory.Target();
        var candidate = new TargetConfiguration
        {
            Id = "candidate",
            Adapter = AdapterIds.OpenAiSdk,
            ApiKind = baseline.ApiKind,
            ModelId = "different-model",
            Endpoint = baseline.Endpoint,
            ApiKey = baseline.ApiKey,
            Authentication = baseline.Authentication,
            Parameters = baseline.Parameters,
        };
        var configuration = new EvaluationConfiguration
        {
            Targets = [baseline, candidate],
            Comparisons =
            [
                new ComparisonGroupConfiguration
                {
                    Id = "two-variables",
                    BaselineTargetId = baseline.Id,
                    CandidateTargetIds = [candidate.Id],
                    IntendedVariable = "model",
                },
            ],
        };

        var comparison = Assert.Single(ComparisonAnalyzer.Analyze(
            configuration,
            [baseline, candidate],
            []));

        Assert.Equal("not-controlled", comparison.Outcome);
        Assert.Contains("model", comparison.ChangedDimensions);
        Assert.Contains("adapter", comparison.ChangedDimensions);
        Assert.False(comparison.IsControlled);
    }

    [Fact]
    public async Task ConfirmedEvidenceWithoutReferenceDoesNotPassReadinessGate()
    {
        var target = new TargetConfiguration
        {
            Id = "fixture",
            Adapter = AdapterIds.LocalFixture,
            ApiKind = ApiKinds.Local,
            ModelId = "fixture",
            Authentication = AuthenticationKinds.None,
            Evidence = new ReadinessEvidenceConfiguration
            {
                EuDataZone = "confirmed",
                Capacity = "confirmed",
            },
        };
        var configuration = new EvaluationConfiguration
        {
            ScenarioRoot = Path.Combine(RepositoryLocator.Root, "scenarios", "v1"),
            Targets = [target],
        };

        var report = await new EvaluationRunner().RunAsync(
            configuration,
            new EvaluationRunOptions
            {
                DryRun = true,
                ScenarioIds = ["general-answer-quality"],
            });

        var readiness = Assert.Single(report.Attempts).Readiness;
        Assert.Equal("unverified", readiness.EuDataZone);
        Assert.Equal("unverified", readiness.Capacity);
        Assert.False(readiness.Ready);
    }
}
