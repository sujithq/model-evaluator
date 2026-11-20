using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Evaluation;
using ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

namespace ModelEvaluator.ApiComparison.Core.Tests;

public class FoundryResponsesConfigurationTests
{
    private static readonly string[] ActiveModels =
    [
        "gpt-5.1",
        "gpt-5.1-codex",
        "gpt-5.1-codex-mini",
        "gpt-5.1-codex-max",
        "gpt-5.2",
        "gpt-5.2-codex",
        "gpt-5.3-codex",
        "gpt-5.4-mini",
        "gpt-5.4-nano",
        "gpt-5.4",
        "gpt-5.4-pro",
        "gpt-5.5",
        "gpt-5.6-sol",
        "gpt-5.6-terra",
        "gpt-5.6-luna",
        "gpt-6-sol",
        "gpt-6-astra",
        "gpt-6-luna",
        "gpt-6.1-sol",
        "gpt-6.1-terra",
        "gpt-6.2-sol",
        "gpt-6.2-astra",
    ];

    private static readonly string[] DeployedModels =
    [
        "gpt-5.1",
        "gpt-5.4",
        "gpt-5.5",
        "gpt-5.6-sol",
        "gpt-5.6-terra",
        "gpt-5.6-luna",
        "gpt-6-sol",
        "gpt-6-astra",
        "gpt-6-luna",
        "gpt-6.1-sol",
        "gpt-6.1-terra",
        "gpt-6.2-sol",
        "gpt-6.2-astra",
    ];

    [Fact]
    public void AllCatalogConfigContainsEveryActiveModelAndControlledPairs()
    {
        var configuration = Load("evaluation.foundry.responses.example.json");

        AssertMatrixDefaults(configuration);
        AssertModelCatalog(configuration, ActiveModels);
        Assert.Equal(44, configuration.Targets.Count);
        Assert.Equal(22, configuration.Comparisons.Count(c => c.IntendedVariable == "adapter"));

        var deployedTargets = configuration.Targets
            .Where(target => DeployedModels.Contains(target.ModelId, StringComparer.Ordinal))
            .ToList();
        var catalogOnlyTargets = configuration.Targets.Except(deployedTargets).ToList();
        Assert.Equal(26, deployedTargets.Count);
        Assert.Equal(18, catalogOnlyTargets.Count);
        Assert.All(deployedTargets, target =>
        {
            Assert.Equal("confirmed", target.Evidence.EuDataZone);
            Assert.Equal("confirmed", target.Evidence.Capacity);
            Assert.Contains("verified 2026-11-20", target.Evidence.EuDataZoneReference);
            Assert.Contains("state Succeeded", target.Evidence.CapacityReference);
        });
        Assert.All(catalogOnlyTargets, target =>
        {
            Assert.Equal("not-confirmed", target.Evidence.EuDataZone);
            Assert.Equal("not-confirmed", target.Evidence.Capacity);
            Assert.Contains("Catalog-only target", target.Evidence.EuDataZoneReference);
            Assert.Contains("not confirmed as of 2026-11-20", target.Evidence.CapacityReference);
        });

        AssertControlledComparisons(configuration, expectedPairs: 64);
    }

    [Fact]
    public void DeployedConfigContainsOnlyConfirmedEuDataZoneDeployments()
    {
        var configuration = Load("evaluation.foundry.responses.deployed.example.json");

        AssertMatrixDefaults(configuration);
        AssertModelCatalog(configuration, DeployedModels);
        Assert.Equal(26, configuration.Targets.Count);
        Assert.Equal(13, configuration.Comparisons.Count(c => c.IntendedVariable == "adapter"));
        Assert.All(configuration.Targets, target =>
        {
            Assert.Equal("confirmed", target.Evidence.EuDataZone);
            Assert.Equal("confirmed", target.Evidence.Capacity);
            Assert.Contains("DataZoneStandard", target.Evidence.EuDataZoneReference);
            Assert.Contains("verified 2026-11-20", target.Evidence.CapacityReference);
        });

        AssertControlledComparisons(configuration, expectedPairs: 37);
    }

    [Fact]
    public void ExactDeploymentNamesDoNotTurnModelMigrationIntoTwoVariables()
    {
        var baseline = new TargetConfiguration
        {
            Id = "baseline",
            Adapter = AdapterIds.DirectRestResponses,
            ApiKind = ApiKinds.AzureOpenAi,
            ModelId = "gpt-5.1",
            DeploymentId = "gpt-5.1",
        };
        var candidate = new TargetConfiguration
        {
            Id = "candidate",
            Adapter = AdapterIds.DirectRestResponses,
            ApiKind = ApiKinds.AzureOpenAi,
            ModelId = "gpt-6.2-sol",
            DeploymentId = "gpt-6.2-sol",
        };
        var configuration = new EvaluationConfiguration
        {
            Targets = [baseline, candidate],
            Comparisons =
            [
                new ComparisonGroupConfiguration
                {
                    Id = "model",
                    BaselineTargetId = baseline.Id,
                    CandidateTargetIds = [candidate.Id],
                    IntendedVariable = "model",
                },
            ],
        };

        var comparison = Assert.Single(ComparisonAnalyzer.Analyze(
            configuration,
            configuration.Targets,
            []));

        Assert.True(comparison.IsControlled);
        Assert.Equal(["model"], comparison.ChangedDimensions);
    }

    [Fact]
    public void FoundryExamplesUseCognitiveServicesScopeWithoutInlineSecrets()
    {
        foreach (var fileName in new[]
                 {
                     "evaluation.foundry.example.json",
                     "evaluation.foundry.responses.example.json",
                     "evaluation.foundry.responses.deployed.example.json",
                     "evaluation.foundry.abstractions.example.json",
                 })
        {
            var configuration = Load(fileName);

            Assert.All(configuration.Targets, target =>
            {
                Assert.Equal(AuthenticationKinds.DefaultCredential, target.Authentication);
                Assert.Equal("FOUNDRY_DATAZONE_ENDPOINT", target.EndpointEnvironmentVariable);
                Assert.Equal("https://cognitiveservices.azure.com/.default", target.TokenScope);
                Assert.Null(target.Endpoint);
                Assert.Null(target.ApiKey);
                Assert.Null(target.ApiKeyEnvironmentVariable);
                Assert.Null(target.TenantId);
            });
        }
    }

    private static EvaluationConfiguration Load(string fileName)
    {
        var configuration = ConfigurationLoader.Load(
            Path.Combine(RepositoryLocator.Root, "config", fileName));
        var validation = ConfigurationValidator.Validate(configuration, requireLiveCredentials: false);
        Assert.True(
            validation.IsValid,
            string.Join(Environment.NewLine, validation.Errors));
        return configuration;
    }

    private static void AssertMatrixDefaults(EvaluationConfiguration configuration)
    {
        Assert.Equal(3, configuration.Repetitions);
        Assert.Equal(1, configuration.MaxParallel);
        Assert.Equal(0, configuration.MaxRetries);
        Assert.All(configuration.Targets, target =>
        {
            Assert.True(target.Enabled);
            Assert.Equal(ApiKinds.AzureOpenAi, target.ApiKind);
            Assert.Equal(AuthenticationKinds.DefaultCredential, target.Authentication);
            Assert.Equal("FOUNDRY_DATAZONE_ENDPOINT", target.EndpointEnvironmentVariable);
            Assert.Null(target.Endpoint);
            Assert.Null(target.ApiKey);
            Assert.Null(target.TenantId);
            Assert.Equal(target.ModelId, target.DeploymentId);
            Assert.Equal("https://cognitiveservices.azure.com/.default", target.TokenScope);
            Assert.Equal(800, target.Parameters.MaxOutputTokens);
            Assert.Null(target.Parameters.Temperature);
            Assert.Null(target.Parameters.TopP);
            Assert.Null(target.Parameters.ReasoningEffort);
        });
    }

    private static void AssertModelCatalog(
        EvaluationConfiguration configuration,
        IReadOnlyCollection<string> expectedModels)
    {
        var rest = configuration.Targets
            .Where(target => target.Adapter == AdapterIds.DirectRestResponses)
            .ToList();
        var sdk = configuration.Targets
            .Where(target => target.Adapter == AdapterIds.OpenAiSdkResponses)
            .ToList();
        Assert.Equal(expectedModels.Count, rest.Count);
        Assert.Equal(expectedModels.Count, sdk.Count);
        Assert.Equal(
            expectedModels.Order(StringComparer.Ordinal),
            rest.Select(target => target.ModelId).Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedModels.Order(StringComparer.Ordinal),
            sdk.Select(target => target.ModelId).Order(StringComparer.Ordinal));
        Assert.All(configuration.Targets, target =>
            Assert.True(
                target.Adapter is AdapterIds.DirectRestResponses or AdapterIds.OpenAiSdkResponses,
                target.Adapter));
    }

    private static void AssertControlledComparisons(
        EvaluationConfiguration configuration,
        int expectedPairs)
    {
        var comparisons = ComparisonAnalyzer.Analyze(
            configuration,
            configuration.Targets,
            []);

        Assert.Equal(expectedPairs, comparisons.Count);
        Assert.All(comparisons, comparison =>
        {
            Assert.True(comparison.IsControlled, comparison.Id + ": " + string.Join(", ", comparison.Notes));
            Assert.Equal("controlled", comparison.Outcome);
        });
    }
}
