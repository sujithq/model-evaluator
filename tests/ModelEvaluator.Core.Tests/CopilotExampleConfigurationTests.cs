using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Tests;

public sealed class CopilotExampleConfigurationTests
{
    [Fact]
    public void ExamplesAndMatrix_ExportNativeUsageAndSelectTheExpectedModels()
    {
        var directory = Path.Combine(RepositoryLocator.Root, "config");
        var matrix = EvaluationConfiguration.Load(Path.Combine(directory, "evaluation.copilot-matrix.example.json"));
        Assert.Equal(28, matrix.Models.Count);
        Assert.Equal(matrix.Models.Count, matrix.Models.Select(m => m.Id).Distinct().Count());
        Assert.DoesNotContain(matrix.Models, m => m.Id == "copilot-auto");

        var exampleCount = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "evaluation.*.example.json"))
        {
            var config = EvaluationConfiguration.Load(path);
            if (config.Models.Count != 1 || config.Models[0].GetSetting("runnerName") != "github-copilot-cli")
            {
                continue;
            }

            exampleCount++;
            var model = config.Models[0];
            Assert.Equal("copilot-cli", model.GetSetting("usageFormat"));
            Assert.Equal("usage.json", model.GetSetting("usageFile"));
            var arguments = ArgumentParser.Split(model.GetSetting("arguments")).ToList();
            var flag = arguments.IndexOf("--usage-output-file");
            Assert.True(flag >= 0 && flag < arguments.Count - 1);
            Assert.Equal("{usageFile}", arguments[flag + 1]);
            if (model.Id != "copilot-auto")
            {
                var inMatrix = Assert.Single(matrix.Models, m => m.Id == model.Id);
                Assert.Equal(model.GetSetting("arguments"), inMatrix.GetSetting("arguments"));
                Assert.Equal(model.GetSetting("usageFormat"), inMatrix.GetSetting("usageFormat"));
                Assert.Equal(model.GetSetting("usageFile"), inMatrix.GetSetting("usageFile"));
            }
        }

        Assert.Equal(29, exampleCount);
    }
}
