using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Tests;

public sealed class CopilotExampleConfigurationTests
{
    [Fact]
    public void Matrix_ExportsNativeUsageAndContainsUniqueSelectableModels()
    {
        var directory = Path.Combine(RepositoryLocator.Root, "config");
        var matrix = EvaluationConfiguration.Load(Path.Combine(directory, "evaluation.copilot-matrix.example.json"));
        Assert.Equal(28, matrix.Models.Count);
        Assert.Equal(matrix.Models.Count, matrix.Models.Select(m => m.Id).Distinct().Count());
        Assert.DoesNotContain(matrix.Models, m => m.Id == "copilot-auto");
        Assert.Equal("copilot-gpt-6-luna", Assert.Single(matrix.Models, model => model.Enabled).Id);

        Assert.All(matrix.Models, model =>
        {
            Assert.Equal("command-line", model.Adapter);
            Assert.Equal("github-copilot-cli", model.GetSetting("runnerName"));
            Assert.Equal("copilot-cli", model.GetSetting("usageFormat"));
            Assert.Equal("usage.json", model.GetSetting("usageFile"));
            var arguments = ArgumentParser.Split(model.GetSetting("arguments")).ToList();
            var flag = arguments.IndexOf("--usage-output-file");
            Assert.True(flag >= 0 && flag < arguments.Count - 1);
            Assert.Equal("{usageFile}", arguments[flag + 1]);
        });

        Assert.Equal(Path.Combine(RepositoryLocator.Root, "benchmarks", "v1"), matrix.BenchmarkRoot);
        Assert.True(Directory.Exists(matrix.BenchmarkRoot));
        Assert.Equal(Path.Combine(RepositoryLocator.Root, "artifacts", "evaluations"), matrix.OutputDirectory);
        Assert.False(Assert.Single(matrix.Models, model => model.Id == "copilot-claude-opus-4.7").Enabled);
        Assert.False(Assert.Single(matrix.Models, model => model.Id == "copilot-gemini-3.5-flash").Enabled);
        Assert.False(Assert.Single(matrix.Models, model => model.Id == "copilot-gemini-3.6-flash").Enabled);
    }

    [Fact]
    public void SpecialCopilotExamples_StaySeparateFromTheNamedModelMatrix()
    {
        var directory = Path.Combine(RepositoryLocator.Root, "config");
        var auto = EvaluationConfiguration.Load(Path.Combine(directory, "evaluation.auto.example.json"));
        var smoke = EvaluationConfiguration.Load(Path.Combine(directory, "evaluation.smoke.example.json"));

        Assert.Equal("copilot-auto", Assert.Single(auto.Models).Id);
        Assert.Equal("copilot-cli", Assert.Single(auto.Models).GetSetting("usageFormat"));
        Assert.Equal(["smoke-add"], smoke.Scenarios);
        Assert.Equal(28, smoke.Models.Count);
    }
}
