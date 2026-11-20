using System.Globalization;
using System.Text;
using ModelEvaluator.ApiComparison.Core.Results;

namespace ModelEvaluator.ApiComparison.Core.Reporting;

public static class MarkdownReportWriter
{
    public static async Task<string> WriteAsync(
        EvaluationReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "report.md");
        await File.WriteAllTextAsync(
            path,
            Render(report),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
        return path;
    }

    public static string Render(EvaluationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI model and integration evaluation");
        builder.AppendLine();
        builder.AppendLine($"- Run: `{report.RunId}`");
        builder.AppendLine($"- Mode: **{report.ExecutionMode}**");
        builder.AppendLine($"- Started: {report.StartedAt:O}");
        builder.AppendLine($"- Completed: {report.CompletedAt:O}");
        builder.AppendLine(
            $"- Environment: {Escape(report.Environment.OperatingSystem)}, {report.Environment.Architecture}, " +
            $"{Escape(report.Environment.Framework)}, .NET SDK {Escape(report.Environment.DotnetSdk)}");
        builder.AppendLine($"- Git commit: {CodeOrUnknown(report.Environment.GitCommit)}");
        builder.AppendLine($"- Planned attempts: {report.PlannedAttempts}");
        builder.AppendLine($"- Completed attempts: {report.Attempts.Count}");
        builder.AppendLine();

        builder.AppendLine("> **Readiness gates are independent.** Missing EU Data Zone or capacity evidence is not a pass. " +
                           "Dry-run success is functional fixture validation only and is not live technical compatibility.");
        builder.AppendLine();

        if (report.Warnings.Count > 0)
        {
            builder.AppendLine("## Run warnings");
            builder.AppendLine();
            foreach (var warning in report.Warnings.Distinct(StringComparer.Ordinal))
            {
                builder.AppendLine($"- {Escape(warning)}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Side-by-side target summary");
        builder.AppendLine();
        builder.AppendLine(
            "| Target | Model/deployment | API kind/version | Requested / executed adapter | SDK package | Pass | Latency | TTFB | Input / output / reasoning tokens | Technical | Functional | EU Data Zone | Capacity |");
        builder.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | --- | --- | --- | --- | --- |");
        foreach (var summary in report.Summaries)
        {
            var package = report.Attempts
                .FirstOrDefault(attempt => attempt.Target.Id == summary.TargetId)
                ?.Target.Package;
            builder.AppendLine(Row(
                Code(summary.TargetId),
                Code(ModelLabel(summary.ModelId, summary.DeploymentId)),
                Code(
                    $"{summary.ApiKind}/{report.Attempts.FirstOrDefault(attempt => attempt.Target.Id == summary.TargetId)?.Target.ApiVersion ?? "unknown"}"),
                $"{Code(summary.Adapter)} / " +
                Code(report.Attempts.FirstOrDefault(attempt => attempt.Target.Id == summary.TargetId)?.Target.ExecutedAdapter ?? "unknown"),
                Code(package is null ? "unknown" : $"{package.Name}@{package.Version}"),
                $"{summary.Passed}/{summary.Planned}",
                Milliseconds(summary.MeanLatencyMilliseconds),
                OptionalMilliseconds(summary.MeanTimeToFirstByteMilliseconds),
                $"{Optional(summary.InputTokens)} / {Optional(summary.OutputTokens)} / {Optional(summary.ReasoningTokens)}",
                State(summary.Readiness.TechnicalCompatibility),
                State(summary.Readiness.FunctionalValidation),
                State(summary.Readiness.EuDataZone),
                State(summary.Readiness.Capacity)));
        }

        builder.AppendLine();
        builder.AppendLine("## Readiness evidence");
        builder.AppendLine();
        foreach (var summary in report.Summaries)
        {
            builder.AppendLine($"### {Escape(summary.TargetId)}");
            builder.AppendLine();
            builder.AppendLine($"- Technical compatibility: **{summary.Readiness.TechnicalCompatibility}**");
            builder.AppendLine($"- Functional validation: **{summary.Readiness.FunctionalValidation}**");
            builder.AppendLine(
                $"- Deployable in EU Data Zone: **{summary.Readiness.EuDataZone}**" +
                Evidence(summary.Readiness.EuDataZoneReference));
            builder.AppendLine(
                $"- Capacity confirmed: **{summary.Readiness.Capacity}**" +
                Evidence(summary.Readiness.CapacityReference));
            builder.AppendLine($"- Overall ready: **{YesNo(summary.Readiness.Ready)}**");
            builder.AppendLine();
        }

        builder.AppendLine("## Controlled comparisons");
        builder.AppendLine();
        if (report.Comparisons.Count == 0)
        {
            builder.AppendLine("No comparison groups were configured or selected.");
            builder.AppendLine();
        }
        else
        {
            builder.AppendLine(
                "| Group | Baseline | Candidate | Declared variable | Changed dimensions | Controlled | Outcome |");
            builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            foreach (var comparison in report.Comparisons)
            {
                builder.AppendLine(Row(
                    Code(comparison.Id),
                    Code(comparison.BaselineTargetId),
                    Code(comparison.CandidateTargetId),
                    Code(comparison.IntendedVariable ?? "unspecified"),
                    comparison.ChangedDimensions.Count == 0
                        ? "none"
                        : string.Join(", ", comparison.ChangedDimensions.Select(Code)),
                    YesNo(comparison.IsControlled),
                    State(comparison.Outcome)));
            }

            builder.AppendLine();
            foreach (var comparison in report.Comparisons)
            {
                builder.AppendLine(
                    $"- `{comparison.BaselineTargetId}` vs `{comparison.CandidateTargetId}`: " +
                    string.Join(" ", comparison.Notes.Select(Escape)));
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Scenario attempts");
        builder.AppendLine();
        builder.AppendLine(
            "| Attempt | Scenario | Target | Rep | Mode | Outcome | Calls | Failed checks |");
        builder.AppendLine("| --- | --- | --- | ---: | --- | --- | ---: | --- |");
        foreach (var attempt in report.Attempts.OrderBy(attempt => attempt.ScenarioId)
                     .ThenBy(attempt => attempt.Target.Id)
                     .ThenBy(attempt => attempt.Repetition))
        {
            var failed = attempt.Checks.Where(check => !check.Passed).Select(check => check.Id).ToList();
            builder.AppendLine(Row(
                Code(attempt.AttemptId),
                $"{Code(attempt.ScenarioId)} v{Escape(attempt.ScenarioVersion)}",
                Code(attempt.Target.Id),
                attempt.Repetition.ToString(CultureInfo.InvariantCulture),
                attempt.ExecutionMode,
                State(attempt.Outcome),
                attempt.Calls.Count.ToString(CultureInfo.InvariantCulture),
                failed.Count == 0 ? "none" : string.Join(", ", failed.Select(Code))));
        }

        builder.AppendLine();

        var errors = report.Attempts.SelectMany(attempt =>
            attempt.Calls.Where(call => call.Error is not null)
                .Select(call => (attempt.Target.Id, attempt.ScenarioId, call)))
            .ToList();
        builder.AppendLine("## Errors and unsupported parameters");
        builder.AppendLine();
        if (errors.Count == 0)
        {
            builder.AppendLine("No adapter/provider errors were returned.");
        }
        else
        {
            builder.AppendLine("| Target | Scenario | Category | Code | HTTP | Retries | Message |");
            builder.AppendLine("| --- | --- | --- | --- | ---: | ---: | --- |");
            foreach (var error in errors)
            {
                builder.AppendLine(Row(
                    Code(error.Id),
                    Code(error.ScenarioId),
                    Code(error.call.Error!.Category),
                    Code(error.call.Error.Code ?? "none"),
                    error.call.Error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a",
                    error.call.Retries.ToString(CultureInfo.InvariantCulture),
                    Escape(error.call.Error.Message)));
            }
        }

        builder.AppendLine();
        var tools = report.Attempts.Where(attempt => attempt.ToolTrace.Count > 0).ToList();
        builder.AppendLine("## Deterministic tool trace");
        builder.AppendLine();
        if (tools.Count == 0)
        {
            builder.AppendLine("No tool calls were made.");
        }
        else
        {
            foreach (var attempt in tools)
            {
                builder.AppendLine(
                    $"### `{attempt.ScenarioId}` / `{attempt.Target.Id}` / repetition {attempt.Repetition}");
                builder.AppendLine();
                foreach (var entry in attempt.ToolTrace)
                {
                    builder.AppendLine(
                        $"- `{entry.Name}` (`{entry.ToolCallId}`): arguments `{Escape(entry.Arguments.GetRawText())}`; " +
                        $"mock result `{Escape(entry.Result?.GetRawText() ?? "none")}`" +
                        (entry.Error is null ? "." : $"; **error:** {Escape(entry.Error)}."));
                }

                builder.AppendLine();
            }
        }

        builder.AppendLine("## Unavailable measurements");
        builder.AppendLine();
        var unavailable = report.Attempts.SelectMany(attempt =>
                attempt.Calls.SelectMany(call => call.UnavailableMetrics))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        builder.AppendLine(unavailable.Count == 0
            ? "All metrics supported by the selected adapters were captured."
            : string.Join(Environment.NewLine, unavailable.Select(metric => $"- `{metric}`")));
        builder.AppendLine();
        builder.AppendLine(
            "Token counts, finish reason, errors, retries and tool traces come from the service/adapter only when returned. " +
            "A missing measurement is `null`, never zero by implication.");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string Row(params string[] cells) => "| " + string.Join(" | ", cells) + " |";

    private static string Escape(string? value) =>
        (value ?? "n/a").Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private static string Code(string value) => $"`{Escape(value).Replace("`", "\\`", StringComparison.Ordinal)}`";

    private static string CodeOrUnknown(string? value) => string.IsNullOrWhiteSpace(value) ? "n/a" : Code(value);

    private static string ModelLabel(string modelId, string? deploymentId) =>
        string.IsNullOrWhiteSpace(deploymentId)
            ? modelId
            : $"{modelId} / deployment={deploymentId}";

    private static string State(string value) => $"**{Escape(value)}**";

    private static string Evidence(string? value) =>
        string.IsNullOrWhiteSpace(value) ? " (no evidence reference)" : $" ({Escape(value)})";

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string Milliseconds(double value) =>
        value.ToString("0.0", CultureInfo.InvariantCulture) + " ms";

    private static string OptionalMilliseconds(double? value) =>
        value is null ? "n/a" : Milliseconds(value.Value);

    private static string Optional(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";
}
