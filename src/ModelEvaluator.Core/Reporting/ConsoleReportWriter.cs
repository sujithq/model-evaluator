using ModelEvaluator.ApiComparison.Core.Results;

namespace ModelEvaluator.ApiComparison.Core.Reporting;

public static class ConsoleReportWriter
{
    public static void Write(EvaluationReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"Run {report.RunId} ({report.ExecutionMode})");
        foreach (var summary in report.Summaries)
        {
            Console.WriteLine(
                $"  {summary.TargetId,-28} {summary.Passed,3}/{summary.Planned,-3} passed | " +
                $"technical={summary.Readiness.TechnicalCompatibility}, " +
                $"functional={summary.Readiness.FunctionalValidation}, " +
                $"EU={summary.Readiness.EuDataZone}, capacity={summary.Readiness.Capacity}");
        }

        foreach (var comparison in report.Comparisons)
        {
            Console.WriteLine(
                $"  comparison {comparison.BaselineTargetId} -> {comparison.CandidateTargetId}: " +
                $"{comparison.Outcome} ({string.Join(", ", comparison.ChangedDimensions)})");
        }

        Console.WriteLine(
            report.Attempts.All(attempt => attempt.Passed)
                ? "All selected scenario attempts passed."
                : "One or more scenario attempts failed or were unsupported.");
    }
}
