using System.Globalization;
using System.Text;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Reporting;

/// <summary>Writes the human-readable Markdown comparison report.</summary>
public static class MarkdownReportWriter
{
    public static string Write(EvaluationReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "report.md");
        File.WriteAllText(path, Render(report), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string Render(EvaluationReport report)
    {
        var summaries = ReportAggregator.Summarise(report);
        var builder = new StringBuilder();

        builder.AppendLine("# Model evaluation report");
        builder.AppendLine();
        builder.AppendLine($"- Run id: `{report.RunId}`");
        builder.AppendLine($"- Started: {report.StartedAt:u} / completed: {report.CompletedAt:u}");
        builder.AppendLine($"- Harness: `{report.Runner.Name}@{report.Runner.Version}`");
        builder.AppendLine($"- Environment: {report.Environment.OperatingSystem}, {report.Environment.Architecture}, " +
                           $".NET SDK {report.Environment.DotnetSdkVersion}");
        builder.AppendLine($"- Execution image: {Value(report.Environment.ExecutionImage)}");
        builder.AppendLine($"- Git commit: {Value(report.Environment.GitCommit)}");
        builder.AppendLine();

        if (summaries.Count == 0)
        {
            builder.AppendLine("No attempts were executed.");
            return builder.ToString();
        }

        builder.AppendLine("## Overall success rate");
        builder.AppendLine();
        builder.AppendLine("| Scenario | Benchmark | Model | Runner | Success | Rate | Model failures | Budget exceeded | Infra failures |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var summary in summaries)
        {
            builder.AppendLine(Row(
                summary.ScenarioId,
                summary.BenchmarkVersion,
                summary.ModelId,
                $"`{summary.RunnerLabel}`",
                $"{summary.SuccessfulAttempts}/{summary.TotalAttempts}",
                Percent(summary.SuccessRate),
                summary.ModelFailures.ToString(CultureInfo.InvariantCulture),
                summary.BudgetExceeded.ToString(CultureInfo.InvariantCulture),
                summary.InfrastructureFailures.ToString(CultureInfo.InvariantCulture)));
        }

        builder.AppendLine();

        foreach (var scenarioGroup in summaries.GroupBy(s => s.ScenarioId))
        {
            var first = scenarioGroup.First();
            builder.AppendLine($"## Scenario `{scenarioGroup.Key}`");
            builder.AppendLine();
            builder.AppendLine($"- Benchmark version: `{first.BenchmarkVersion}`");
            builder.AppendLine($"- Prompt hash: `{first.PromptHash}`");
            builder.AppendLine();
            builder.AppendLine("| Model | Build & execution | Functional correctness | Instruction adherence | Code quality | Efficiency | Reliability |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            foreach (var summary in scenarioGroup)
            {
                var efficiency =
                    $"{summary.MeanElapsedSeconds:0.0} s avg; tokens {Optional(summary.TotalTokens)}; " +
                    $"tool calls {Optional(summary.TotalToolCalls)}; cost {OptionalCost(summary.TotalCostUsd)}";

                builder.AppendLine(Row(
                    summary.ModelId,
                    $"{summary.BuildSuccesses}/{summary.TotalAttempts} attempts built and ran",
                    $"{summary.AcceptancePassed} passed / {summary.AcceptanceFailed} failed / {summary.AcceptanceSkipped} skipped",
                    $"{summary.InstructionCompliant}/{summary.TotalAttempts} compliant",
                    $"{summary.CodeQualityPassing}/{summary.TotalAttempts} clean",
                    efficiency,
                    $"{Percent(summary.SuccessRate)} over {summary.TotalAttempts} attempts (sd {summary.ElapsedStandardDeviation:0.0} s)"));
            }

            builder.AppendLine();
            builder.AppendLine("### Attempts");
            builder.AppendLine();
            builder.AppendLine("| Attempt | Model | Rep | Outcome | Failing checks | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- |");

            foreach (var attempt in scenarioGroup.SelectMany(s => s.Attempts).OrderBy(a => a.ModelId).ThenBy(a => a.Repetition))
            {
                var failing = attempt.Checks
                    .Where(c => c.Status is CheckStatus.Failed or CheckStatus.Error
                                || (c.Mandatory && c.Status == CheckStatus.Skipped))
                    .Select(c => c.Id)
                    .ToList();

                builder.AppendLine(Row(
                    $"`{attempt.AttemptId}`",
                    attempt.ModelId,
                    attempt.Repetition.ToString(CultureInfo.InvariantCulture),
                    attempt.Outcome.ToString(),
                    failing.Count == 0 ? "none" : string.Join(", ", failing),
                    Escape(attempt.FailureReason ?? "-")));
            }

            builder.AppendLine();
        }

        var unavailable = report.Attempts
            .SelectMany(a => a.Efficiency.UnavailableMetrics.Select(m => (a.ModelId, Metric: m)))
            .GroupBy(x => x.ModelId)
            .ToList();

        if (unavailable.Count > 0)
        {
            builder.AppendLine("## Unavailable measurements");
            builder.AppendLine();
            foreach (var group in unavailable)
            {
                builder.AppendLine($"- `{group.Key}`: {string.Join(", ", group.Select(g => g.Metric).Distinct())} not reported by the adapter.");
            }

            builder.AppendLine();
        }

        builder.AppendLine("Optional qualitative review is intentionally excluded from these automated results.");
        return builder.ToString();
    }

    private static string Row(params string[] cells) => "| " + string.Join(" | ", cells) + " |";

    private static string Percent(double value) => (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Optional<T>(T? value) where T : struct =>
        value is null ? "n/a" : Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "n/a";

    private static string OptionalCost(decimal? value) =>
        value is null ? "n/a" : "$" + value.Value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "not recorded" : $"`{value}`";

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
