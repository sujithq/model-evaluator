using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Tests;

public sealed class ModelCatalogTests
{
    [Theory]
    [InlineData("--model gpt-6-luna -p \"{prompt}\"", "gpt-6-luna")]
    [InlineData("--model=gpt-6-luna --allow-all-tools", "gpt-6-luna")]
    [InlineData("-p \"{prompt}\" --model   gpt-5.4-mini", "gpt-5.4-mini")]
    [InlineData("--model \"claude opus 5\"", "claude opus 5")]
    [InlineData("--allow-all-tools -p \"{prompt}\"", null)]
    [InlineData("--model", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FindProviderModel_ReadsTheModelOption(string? arguments, string? expected) =>
        Assert.Equal(expected, ModelCatalog.FindProviderModel(arguments));

    [Fact]
    public void Describe_CommandLineModel_ExposesRunnerDetails()
    {
        var entry = ModelCatalog.Describe(new ModelConfiguration
        {
            Id = "copilot-gpt-6-luna",
            Enabled = true,
            Adapter = "command-line",
            Description = " Copilot CLI ",
            Settings = new Dictionary<string, string>
            {
                ["command"] = "copilot",
                ["arguments"] = "--model gpt-6-luna -p \"{prompt}\"",
                ["runnerName"] = "github-copilot-cli",
                ["runnerVersion"] = "0.0.1",
            },
        });

        Assert.Equal("copilot-gpt-6-luna", entry.Id);
        Assert.True(entry.Enabled);
        Assert.True(entry.RequiresRunner);
        Assert.Equal("copilot", entry.Command);
        Assert.Equal("gpt-6-luna", entry.ProviderModel);
        Assert.Equal("github-copilot-cli", entry.RunnerName);
        Assert.Equal("0.0.1", entry.RunnerVersion);
        Assert.Equal("Copilot CLI", entry.Description);
        Assert.Null(entry.SampleVariant);
    }

    [Fact]
    public void Describe_LocalSampleModel_ReportsVariantAndNeedsNoRunner()
    {
        var entry = ModelCatalog.Describe(new ModelConfiguration
        {
            Id = "reference-good",
            Enabled = false,
            Adapter = "local-sample",
        });

        Assert.False(entry.Enabled);
        Assert.False(entry.RequiresRunner);
        Assert.Equal("good", entry.SampleVariant);
        Assert.Null(entry.Command);
        Assert.Null(entry.ProviderModel);
        Assert.Null(entry.RunnerName);
    }

    [Fact]
    public void Describe_Configuration_KeepsEveryConfiguredModelIncludingDisabled()
    {
        var configuration = new EvaluationConfiguration
        {
            Models =
            [
                new ModelConfiguration { Id = "enabled", Enabled = true, Adapter = "local-sample" },
                new ModelConfiguration { Id = "disabled", Enabled = false, Adapter = "local-sample" },
            ],
        };

        var entries = ModelCatalog.Describe(configuration);

        Assert.Equal(["enabled", "disabled"], entries.Select(entry => entry.Id));
    }
}
