using ModelEvaluator.ApiComparison.Core.Evaluation;
using ModelEvaluator.ApiComparison.Core.Results;
using ModelEvaluator.ApiComparison.Core.Scenarios;

namespace ModelEvaluator.ApiComparison.Core.Tests;

public class ScenarioValidatorTests
{
    [Fact]
    public void AcceptsEquivalentWordingForAlternativeRequirement()
    {
        var scenario = Scenario(
            new ScenarioAlternativeRequirement
            {
                Id = "verified-plan-information",
                Terms = ["verified", "written plan rules"],
            });

        var checks = ScenarioValidator.Validate(
            scenario,
            [],
            [Turn("The decision must follow the written plan rules.")],
            []);

        var check = Assert.Single(checks);
        Assert.True(check.Passed);
        Assert.Equal("contains-any:verified-plan-information", check.Id);
    }

    [Fact]
    public void RejectsWhenNoAlternativeWordingMatches()
    {
        var scenario = Scenario(
            new ScenarioAlternativeRequirement
            {
                Id = "human-escalation",
                Terms = ["human review", "human reviewer"],
            });

        var checks = ScenarioValidator.Validate(
            scenario,
            [],
            [Turn("The system completes the determination automatically.")],
            []);

        var check = Assert.Single(checks);
        Assert.False(check.Passed);
        Assert.Contains("human review", check.Detail, StringComparison.Ordinal);
    }

    private static ScenarioDefinition Scenario(ScenarioAlternativeRequirement requirement) => new()
    {
        Id = "alternative-wording",
        Version = "1.0.0",
        Title = "Alternative wording",
        Category = "test",
        Validation = new ScenarioValidation
        {
            ContainsAnyOf = [requirement],
        },
    };

    private static TurnResult Turn(string message) => new()
    {
        Index = 0,
        UserMessage = "Evaluate this response.",
        AssistantMessage = message,
    };
}
