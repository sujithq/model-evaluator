using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Tests;

/// <summary>Guards the shipped benchmark packages: they must stay loadable, complete and versioned.</summary>
public sealed class ScenarioCatalogTests
{
    private static readonly ScenarioCatalog Catalog = ScenarioCatalog.Load(RepositoryLocator.BenchmarkRoot);

    [Fact]
    public void Load_FindsEveryShippedScenario()
    {
        var ids = Catalog.Scenarios.Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();

        Assert.Equal(
            ["blazor-task-app", "class-library-pricing", "console-task-cli", "webapi-task-minimal", "worker-job-processor"],
            ids);
    }

    [Fact]
    public void EveryScenario_IsComplete()
    {
        foreach (var scenario in Catalog.Scenarios)
        {
            Assert.True(Directory.Exists(scenario.StarterPath), $"{scenario.Id}: missing starter directory.");
            Assert.True(File.Exists(scenario.AcceptanceProjectPath), $"{scenario.Id}: missing acceptance project.");
            Assert.True(
                File.Exists(Path.Combine(scenario.StarterPath, "global.json")),
                $"{scenario.Id}: the starter must pin the SDK.");
            Assert.True(
                File.Exists(Path.Combine(scenario.StarterPath, ".editorconfig")),
                $"{scenario.Id}: the starter must supply the formatting configuration.");
            Assert.False(string.IsNullOrWhiteSpace(scenario.Definition.BenchmarkVersion));
            Assert.NotEmpty(scenario.Definition.AllowedPackages);
            Assert.NotEmpty(scenario.Definition.RequiredGlobs);
        }
    }

    [Fact]
    public void EveryScenario_ShipsKnownGoodAndBrokenSamples()
    {
        foreach (var scenario in Catalog.Scenarios)
        {
            Assert.True(scenario.Definition.Samples.ContainsKey("good"), $"{scenario.Id}: no known-good sample.");
            Assert.True(scenario.Definition.Samples.ContainsKey("broken"), $"{scenario.Id}: no broken sample.");

            foreach (var (name, variant) in scenario.Definition.Samples)
            {
                Assert.True(
                    Directory.Exists(scenario.SamplePath(variant)),
                    $"{scenario.Id}: sample '{name}' directory is missing.");

                var overlay = scenario.SampleOverlayPath(variant);
                if (overlay is not null)
                {
                    Assert.True(Directory.Exists(overlay), $"{scenario.Id}: overlay for '{name}' is missing.");
                }
            }
        }
    }

    [Fact]
    public void ResolvedPrompt_CombinesSharedAndScenarioInstructions()
    {
        var scenario = Catalog.Get("console-task-cli");

        Assert.Contains("Shared instructions (benchmark v1)", scenario.ResolvedPrompt, StringComparison.Ordinal);
        Assert.Contains("Scenario: console task-management CLI", scenario.ResolvedPrompt, StringComparison.Ordinal);
        Assert.Contains("Contract: console task-management CLI", scenario.ResolvedPrompt, StringComparison.Ordinal);
        Assert.Equal(Hashing.Sha256(scenario.ResolvedPrompt), scenario.PromptHash);
    }

    [Fact]
    public void PromptHash_IsStableAcrossLoads()
    {
        var reloaded = ScenarioCatalog.Load(RepositoryLocator.BenchmarkRoot);

        foreach (var scenario in Catalog.Scenarios)
        {
            Assert.Equal(scenario.PromptHash, reloaded.Get(scenario.Id).PromptHash);
        }
    }

    [Fact]
    public void Get_ThrowsForUnknownScenario() =>
        Assert.Throws<KeyNotFoundException>(() => Catalog.Get("does-not-exist"));

    [Fact]
    public void Load_ThrowsWhenSharedInstructionsAreMissing() =>
        Assert.Throws<FileNotFoundException>(() =>
            ScenarioCatalog.Load(Path.Combine(RepositoryLocator.Root, "src")));
}
