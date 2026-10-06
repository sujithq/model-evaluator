using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Tests;

public sealed class PublishedCopilotModelCatalogTests
{
    private readonly PublishedCopilotModelCatalog _catalog = PublishedCopilotModelCatalog.Load();

    [Fact]
    public void SupportedCopilotModel_IsMatchedByProviderId()
    {
        var result = _catalog.Check(CopilotModel("gpt-6-astra"));

        Assert.Equal(PublishedCopilotModelStatus.Supported, result.Status);
        Assert.Equal("GPT-6 Astra", result.PublishedName);
        Assert.Equal("Yes", result.CopilotCli);
    }

    [Fact]
    public void RetiredCopilotModel_IsReportedWithAlternative()
    {
        var result = _catalog.Check(CopilotModel("claude-opus-4.7"));

        Assert.Equal(PublishedCopilotModelStatus.Retired, result.Status);
        Assert.Contains("Latest Claude Opus model", result.Detail);
    }

    [Fact]
    public void UnknownCopilotModel_IsNotListed()
    {
        var result = _catalog.Check(CopilotModel("future-model"));

        Assert.Equal(PublishedCopilotModelStatus.NotListed, result.Status);
    }

    [Fact]
    public void NonCopilotRunner_IsNotApplicable()
    {
        var model = CopilotModel("gpt-6-astra") with
        {
            Settings = new Dictionary<string, string>
            {
                ["command"] = "other-runner",
                ["arguments"] = "--model gpt-6-astra",
                ["runnerName"] = "other-runner",
            },
        };

        var result = _catalog.Check(model);

        Assert.Equal(PublishedCopilotModelStatus.NotApplicable, result.Status);
    }

    private static ModelConfiguration CopilotModel(string providerModel) => new()
    {
        Id = $"copilot-{providerModel}",
        Adapter = "command-line",
        Settings = new Dictionary<string, string>
        {
            ["command"] = "copilot",
            ["arguments"] = $"--model {providerModel} --prompt test",
            ["runnerName"] = "github-copilot-cli",
        },
    };
}
