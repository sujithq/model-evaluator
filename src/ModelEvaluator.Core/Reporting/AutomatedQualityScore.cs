using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Reporting;

/// <summary>Deterministic quality signal derived from mandatory evaluation checks.</summary>
public sealed record AutomatedQualityScore
{
    public double? Score { get; init; }

    public double? FunctionalCorrectness { get; init; }

    public double? InstructionAdherence { get; init; }

    public double? BuildAndAnalyzer { get; init; }

    public double? GeneratedTests { get; init; }

    public double? Reliability { get; init; }

    public bool Provisional { get; init; }

    public string? Status { get; init; }

    public static AutomatedQualityScore Calculate(ModelScenarioSummary summary)
    {
        var evaluated = summary.Attempts
            .Where(attempt => attempt.Outcome != AttemptOutcome.InfrastructureFailure)
            .ToList();
        if (evaluated.Count == 0)
        {
            return new AutomatedQualityScore { Status = "incomplete: infrastructure failures only" };
        }

        var functional = evaluated.Average(attempt =>
            attempt.Acceptance.Total == 0
                ? 0
                : (double)attempt.Acceptance.Passed / attempt.Acceptance.Total);
        var instruction = Rate(evaluated, attempt => MandatoryPassed(attempt, CheckCategory.InstructionAdherence));
        var build = Rate(evaluated, attempt => MandatoryPassed(attempt, CheckCategory.BuildAndExecution));
        var analyzer = Rate(evaluated, attempt => MandatoryPassed(attempt, CheckCategory.CodeQuality));
        var buildAndAnalyzer = (build + analyzer) / 2;
        var generatedTests = Rate(evaluated, attempt =>
        {
            var checks = attempt.ChecksIn(CheckCategory.CodeQuality)
                .Where(check => check.Mandatory && check.Id == "tests.generated")
                .ToList();
            return checks.Count > 0 && checks.All(check => check.Status == CheckStatus.Passed);
        });
        var reliability = (double)evaluated.Count(attempt => attempt.IsSuccess) / evaluated.Count;
        var score = (45 * functional) +
                    (20 * instruction) +
                    (15 * buildAndAnalyzer) +
                    (10 * generatedTests) +
                    (10 * reliability);

        var hasBuildFailure = evaluated.Any(attempt =>
            !MandatoryPassed(attempt, CheckCategory.BuildAndExecution));
        var hasAcceptanceFailure = evaluated.Any(attempt => attempt.Acceptance.Failed > 0);
        if (hasBuildFailure)
        {
            score = Math.Min(score, 20);
        }
        else if (hasAcceptanceFailure)
        {
            score = Math.Min(score, 60);
        }

        return new AutomatedQualityScore
        {
            Score = Math.Round(score, 2),
            FunctionalCorrectness = Math.Round(functional * 100, 2),
            InstructionAdherence = Math.Round(instruction * 100, 2),
            BuildAndAnalyzer = Math.Round(buildAndAnalyzer * 100, 2),
            GeneratedTests = Math.Round(generatedTests * 100, 2),
            Reliability = Math.Round(reliability * 100, 2),
            Provisional = evaluated.Count < 3,
            Status = evaluated.Count < 3 ? "provisional" : null,
        };
    }

    private static double Rate(
        IReadOnlyList<AttemptResult> attempts,
        Func<AttemptResult, bool> predicate) =>
        (double)attempts.Count(predicate) / attempts.Count;

    private static bool MandatoryPassed(AttemptResult attempt, CheckCategory category)
    {
        var checks = attempt.ChecksIn(category).Where(check => check.Mandatory).ToList();
        return checks.Count > 0 && checks.All(check => check.Status == CheckStatus.Passed);
    }
}
