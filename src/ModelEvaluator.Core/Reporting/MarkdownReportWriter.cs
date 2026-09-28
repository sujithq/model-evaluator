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
        builder.AppendLine($"- Maximum parallel attempts: {report.Environment.MaxParallel}");
        if (report.Environment.MaxParallel > 1)
        {
            builder.AppendLine("- Timing caveat: concurrent attempts share machine/provider resources; do not compare timing directly with sequential runs.");
        }
        if (report.Cancelled)
        {
            builder.AppendLine($"- **Cancelled run: partial results.** {report.Attempts.Count} started / {Optional(report.PlannedAttempts)} planned; {Optional(report.NotStartedAttempts)} not started.");
        }
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

        builder.AppendLine("## Per-task rankings");
        builder.AppendLine();
        builder.AppendLine("Order: success rate, mean acceptance accuracy, lower mean AI credits (only with complete coverage), then lower mean total wall time.");
        builder.AppendLine("Infrastructure failures are excluded from ranking metrics and shown separately. Missing acceptance execution scores 0; skipped tests count against accuracy.");
        builder.AppendLine("AI credits measure consumption, not invoice charges. Premium requests and reported USD are separate units, never substituted for credits.");
        builder.AppendLine("Ranks based on fewer than three evaluable attempts are provisional. Equal scores share a rank; reference samples are not ranked.");
        builder.AppendLine();
        foreach (var ranking in ScenarioRanker.Rank(report))
        {
            builder.AppendLine($"### `{ranking.ScenarioId}` ({ranking.BenchmarkVersion}, `{ranking.RunnerLabel}`)");
            builder.AppendLine();
            builder.AppendLine($"Prompt hash: `{ranking.PromptHash}`");
            builder.AppendLine($"Maximum parallel attempts: {ranking.MaxParallel}");
            builder.AppendLine(ranking.UsesAiCredits
                ? "AI-credit tie-breaker: enabled (complete measurements for every ranked model)."
                : "AI-credit tie-breaker: disabled for this group (missing measurements or no ranked models); time breaks accuracy ties.");
            builder.AppendLine();
            builder.AppendLine("| Rank | Model | Success | Acceptance accuracy | Mean AI credits | Credit coverage | Mean total / generation seconds | Infra failures | Notes |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var model in ranking.Models)
            {
                builder.AppendLine(Row(
                    Optional(model.Rank),
                    model.ModelId,
                    $"{model.SuccessfulAttempts}/{model.EvaluatedAttempts}",
                    model.AcceptanceAccuracy is null ? "n/a" : Percent(model.AcceptanceAccuracy.Value),
                    Optional(model.MeanAiCredits),
                    $"{model.AiCreditsReportedAttempts}/{model.EvaluatedAttempts}",
                    $"{Seconds(model.MeanElapsedSeconds)} / {Seconds(model.MeanGenerationSeconds)}",
                    model.InfrastructureFailures.ToString(CultureInfo.InvariantCulture),
                    model.ExclusionReason ?? (model.Provisional ? "provisional" : "-")));
            }

            builder.AppendLine();
        }

        foreach (var scenarioGroup in summaries.GroupBy(s => (s.ScenarioId, s.BenchmarkVersion, s.PromptHash, s.RunnerLabel, s.MaxParallel)))
        {
            var first = scenarioGroup.First();
            builder.AppendLine($"## Scenario `{scenarioGroup.Key.ScenarioId}`");
            builder.AppendLine();
            builder.AppendLine($"- Benchmark version: `{first.BenchmarkVersion}`");
            builder.AppendLine($"- Prompt hash: `{first.PromptHash}`");
            builder.AppendLine($"- Maximum parallel attempts: {first.MaxParallel}");
            builder.AppendLine();
            builder.AppendLine("| Model | Build & execution | Functional correctness | Instruction adherence | Code quality | Efficiency | Reliability |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

            foreach (var summary in scenarioGroup)
            {
                var efficiency =
                    $"{summary.MeanElapsedSeconds:0.0} s avg; tokens {Optional(summary.TotalTokens)}; " +
                    $"tool calls {Optional(summary.TotalToolCalls)}; reported USD {OptionalCost(summary.TotalCostUsd)}; " +
                    $"AI credits {Optional(summary.TotalAiCredits)} ({summary.AiCreditsReportedAttempts}/{summary.TotalAttempts} attempts); " +
                    $"premium requests {Optional(summary.TotalPremiumRequests)}; API requests {Optional(summary.TotalApiRequests)}";

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
            foreach (var summary in scenarioGroup.Where(s => s.ReportedModels.Count > 0))
            {
                builder.AppendLine($"- `{summary.ModelId}` reported contributing models: {string.Join(", ", summary.ReportedModels.Select(m => $"`{m}`"))}.");
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

        var partialUsage = report.Attempts.Where(a => a.Efficiency.UsageIsPartial).ToList();
        if (partialUsage.Count > 0)
        {
            builder.AppendLine("## Partial usage measurements");
            builder.AppendLine();
            builder.AppendLine("Observed usage only, not final totals. In-flight calls may be missing. Excluded from complete consumption totals and AI-credit ranking.");
            builder.AppendLine();
            builder.AppendLine("| Attempt | Observed AI credits | Input tokens | Output tokens | API requests |");
            builder.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var attempt in partialUsage)
            {
                builder.AppendLine(Row($"`{attempt.AttemptId}`", Optional(attempt.Efficiency.AiCredits),
                    Optional(attempt.Efficiency.InputTokens), Optional(attempt.Efficiency.OutputTokens),
                    Optional(attempt.Efficiency.ApiRequests)));
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

        var usageWarnings = report.Attempts.Where(a => a.Efficiency.UsageWarnings.Count > 0).ToList();
        if (usageWarnings.Count > 0)
        {
            builder.AppendLine("## Usage collection warnings");
            builder.AppendLine();
            foreach (var attempt in usageWarnings)
            {
                foreach (var warning in attempt.Efficiency.UsageWarnings)
                {
                    builder.AppendLine($"- `{attempt.AttemptId}`: {Escape(warning)}");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("Optional qualitative review is intentionally excluded from these automated results.");
        return builder.ToString();
    }

    private static string Row(params string[] cells) => "| " + string.Join(" | ", cells) + " |";

    private static string Percent(double value) => (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    private static string Seconds(double? value) => value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "n/a";

    private static string Optional<T>(T? value) where T : struct =>
        value is null ? "n/a" : Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "n/a";

    private static string OptionalCost(decimal? value) =>
        value is null ? "n/a" : "$" + value.Value.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "not recorded" : $"`{value}`";

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
