using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Tests;

public sealed class SmokeScenarioTests
{
    [Fact]
    public void Smoke_IsSeparateAndHasMinimalBoundedMatrix()
    {
        var config = EvaluationConfiguration.Load(Path.Combine(RepositoryLocator.Root, "config", "evaluation.smoke.example.json"));
        var scenario = Assert.Single(ScenarioCatalog.Load(config.BenchmarkRoot).Scenarios);
        Assert.Equal("smoke-add", scenario.Id);
        Assert.Equal(["smoke-add"], config.Scenarios);
        Assert.Equal(1, config.Repetitions);
        Assert.Equal(2, config.MaxParallel);
        Assert.Equal(["copilot-gpt-5-mini", "copilot-claude-haiku-4.5"], config.Models.Select(m => m.Id));
        Assert.All(config.Models, model => Assert.Equal("copilot-cli", model.GetSetting("usageFormat")));
        Assert.Equal(120, scenario.Definition.Budget.GenerationTimeoutSeconds);
        Assert.True(scenario.ResolvedPrompt.Length < 2000);
        Assert.DoesNotContain(ScenarioCatalog.Load(RepositoryLocator.BenchmarkRoot).Scenarios, s => s.Id == scenario.Id);
        Assert.True(File.Exists(scenario.AcceptanceProjectPath));
    }

    [Theory]
    [InlineData("good")]
    [InlineData("broken")]
    public void Samples_PreserveScaffoldingAndOnlyReplaceImplementation(string variantName)
    {
        var catalog = ScenarioCatalog.Load(Path.Combine(RepositoryLocator.Root, "benchmarks", "smoke-v1"));
        var scenario = catalog.Get("smoke-add");
        var root = Path.Combine(Path.GetTempPath(), "eval-smoke-tests", Guid.NewGuid().ToString("N"));
        try
        {
            FileSystemHelper.CopyDirectory(scenario.StarterPath, root);
            var sample = scenario.SamplePath(scenario.Definition.Samples[variantName]);
            Assert.Equal("Calculator.cs", Path.GetFileName(Assert.Single(FileSystemHelper.EnumerateSourceFiles(sample))));
            FileSystemHelper.CopyDirectory(sample, root);
            Assert.All(new InstructionAdherenceChecker().Check(root, scenario),
                check => Assert.Equal(CheckStatus.Passed, check.Status));
            Assert.DoesNotContain("NotImplementedException", File.ReadAllText(Path.Combine(root, "src", "Smoke", "Calculator.cs")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
