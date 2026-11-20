using ModelEvaluator.ApiComparison.Core.Scenarios;
using ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

namespace ModelEvaluator.ApiComparison.Core.Tests;

public class ScenarioCatalogTests
{
    [Fact]
    public void LoadsVersionedSyntheticScenarios()
    {
        var catalog = ScenarioCatalog.Load(Path.Combine(RepositoryLocator.Root, "scenarios", "v1"));

        Assert.Equal(5, catalog.Scenarios.Count);
        Assert.Contains(catalog.Scenarios, scenario => scenario.Id == "general-answer-quality");
        Assert.Contains(catalog.Scenarios, scenario => scenario.Id == "structured-output");
        Assert.Contains(catalog.Scenarios, scenario => scenario.Id == "deterministic-tool-calling");
        Assert.Contains(catalog.Scenarios, scenario => scenario.Id == "multi-turn-consistency");
        Assert.Contains(catalog.Scenarios, scenario => scenario.Id == "unsupported-parameter-handling");
        Assert.Equal(
            "1.4.0",
            catalog.Get("general-answer-quality").Version);
        Assert.All(
            catalog.Scenarios.Where(scenario => scenario.Id != "general-answer-quality"),
            scenario => Assert.Equal("1.0.0", scenario.Version));
        Assert.All(catalog.Scenarios, scenario =>
            Assert.Matches("^[0-9a-f]{64}$", ScenarioCatalog.Fingerprint(scenario)));
    }
}
