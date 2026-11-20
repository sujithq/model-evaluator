using System.Text.Json;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Scenarios;

namespace ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

internal static class ScenarioFactory
{
    public static ScenarioDefinition Simple() => new()
    {
        Id = "test-scenario",
        Version = "1.0.0",
        Title = "Test scenario",
        Category = "test",
        SystemPrompt = "Be exact.",
        Turns =
        [
            new ScenarioTurn { User = "Reply with exactly: hello" },
        ],
        Parameters = new ScenarioRequestParameters
        {
            MaxOutputTokens = 100,
        },
        DryRun = new DryRunDefinition
        {
            Calls =
            [
                new DryRunCall
                {
                    Turn = 0,
                    ToolRound = 0,
                    Text = "hello",
                },
            ],
        },
        Validation = new ScenarioValidation
        {
            ContainsAll = ["hello"],
        },
    };

    public static TargetConfiguration Target(
        string adapter = AdapterIds.DirectRest,
        string? endpoint = "https://example.test/v1",
        string authentication = AuthenticationKinds.ApiKey) => new()
        {
            Id = "test-target",
            Adapter = adapter,
            ApiKind = adapter == AdapterIds.LocalFixture ? ApiKinds.Local : ApiKinds.OpenAi,
            ModelId = "test-model",
            Endpoint = endpoint,
            ApiKey = "test-key",
            Authentication = authentication,
            Parameters = new RequestParametersConfiguration
            {
                MaxOutputTokens = 512,
            },
        };

    public static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
