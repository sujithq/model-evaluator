using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Execution;

public sealed record EffectiveRequestParameters
{
    public int MaxOutputTokens { get; init; }

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public string? ReasoningEffort { get; init; }

    public static EffectiveRequestParameters Merge(
        RequestParametersConfiguration target,
        ScenarioRequestParameters scenario)
    {
        return new EffectiveRequestParameters
        {
            MaxOutputTokens = scenario.MaxOutputTokens ?? target.MaxOutputTokens ?? 800,
            Temperature = scenario.Temperature ?? target.Temperature,
            TopP = scenario.TopP ?? target.TopP,
            ReasoningEffort = scenario.ReasoningEffort ?? target.ReasoningEffort,
        };
    }
}
