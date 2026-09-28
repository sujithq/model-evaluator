using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Usage data an agent runner may write so efficiency can be reported.</summary>
public sealed record AdapterUsage
{
    public bool UsageIsPartial { get; init; }

    public int? ToolCalls { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public decimal? EstimatedCostUsd { get; init; }

    public decimal? AiCredits { get; init; }

    public decimal? PremiumRequests { get; init; }

    public long? CacheReadTokens { get; init; }

    public long? CacheWriteTokens { get; init; }

    public long? ReasoningTokens { get; init; }

    public long? ApiRequests { get; init; }

    public double? ApiDurationSeconds { get; init; }

    public IReadOnlyList<string> ReportedModels { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<string> UnavailableMetrics =>
        new (string Name, object? Value)[]
        {
            ("toolCalls", ToolCalls), ("inputTokens", InputTokens), ("outputTokens", OutputTokens),
            ("estimatedCostUsd", EstimatedCostUsd), ("aiCredits", AiCredits), ("premiumRequests", PremiumRequests),
            ("cacheReadTokens", CacheReadTokens), ("cacheWriteTokens", CacheWriteTokens),
            ("reasoningTokens", ReasoningTokens), ("apiRequests", ApiRequests), ("apiDurationSeconds", ApiDurationSeconds),
        }.Where(metric => metric.Value is null).Select(metric => metric.Name).ToList();
}

/// <summary>
/// Invokes an external agent runner (any CLI) to produce the solution. Keeps tools, context and
/// budgets identical across models by passing the same prompt file, workspace and timeout.
/// </summary>
/// <remarks>
/// Supported settings: <c>command</c>, <c>arguments</c> (space separated, quoted values supported),
/// <c>runnerName</c>, <c>runnerVersion</c>, <c>usageFile</c> (relative to the artifacts directory).
/// <c>usageFormat</c> selects <c>normalized</c> (default) or <c>copilot-cli</c>.
/// Placeholders replaced in arguments: <c>{workspace}</c>, <c>{promptFile}</c>, <c>{prompt}</c>,
/// <c>{artifacts}</c>, <c>{timeoutSeconds}</c>, <c>{scenario}</c>, <c>{model}</c>, <c>{usageFile}</c>.
/// </remarks>
public sealed class CommandLineAdapter(ProcessRunner processRunner) : IModelAdapter
{
    public const string AdapterKey = "command-line";

    private readonly ProcessRunner _processRunner = processRunner;

    public string Key => AdapterKey;

    public async Task<ModelAttemptOutput> GenerateAsync(ModelAttemptContext context, CancellationToken cancellationToken)
    {
        var settings = context.Model;
        var command = settings.GetSetting("command");
        var runnerName = settings.GetSetting("runnerName", command.Length == 0 ? AdapterKey : command);
        var runnerVersion = settings.GetSetting("runnerVersion", "unknown");
        var runner = new RunnerInfo { Name = runnerName, Version = runnerVersion };
        var usageFormat = settings.GetSetting("usageFormat", "normalized");

        if (string.IsNullOrWhiteSpace(command) || usageFormat is not ("normalized" or "copilot-cli"))
        {
            return new ModelAttemptOutput
            {
                Succeeded = false,
                InfrastructureFailure = true,
                FailureReason = string.IsNullOrWhiteSpace(command)
                    ? $"Model '{settings.Id}' uses the command-line adapter but defines no 'command' setting."
                    : $"Model '{settings.Id}' has unsupported usageFormat '{usageFormat}'.",
                Runner = runner,
                UnavailableMetrics = new AdapterUsage().UnavailableMetrics,
            };
        }

        var usageFile = Path.Combine(context.ArtifactsPath, settings.GetSetting("usageFile", "usage.json"));
        var arguments = ArgumentParser
            .Split(settings.GetSetting("arguments"))
            .Select(a => Substitute(a, context, usageFile))
            .ToList();
        var telemetryPath = Path.Combine(context.ArtifactsPath, "usage.telemetry.jsonl");
        string? sessionEventsPath = null;

        var environment = new Dictionary<string, string>(settings.Environment)
        {
            ["EVAL_WORKSPACE"] = context.WorkspacePath,
            ["EVAL_PROMPT_FILE"] = context.PromptFilePath,
            ["EVAL_ARTIFACTS"] = context.ArtifactsPath,
            ["EVAL_USAGE_FILE"] = usageFile,
            ["EVAL_SCENARIO"] = context.Scenario.Id,
            ["EVAL_MODEL"] = settings.Id,
            ["EVAL_TIMEOUT_SECONDS"] = ((int)context.Timeout.TotalSeconds).ToString(),
        };
        if (usageFormat == "copilot-cli")
        {
            environment["COPILOT_OTEL_ENABLED"] = "true";
            environment["COPILOT_OTEL_EXPORTER_TYPE"] = "file";
            environment["COPILOT_OTEL_FILE_EXPORTER_PATH"] = telemetryPath;
            environment["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"] = "false";
            // Only read state for a new session we own; never inspect an explicitly resumed session.
            if (!arguments.Any(a => a is "--continue" or "--resume" or "-r" or "--session-id" or "--connect"
                || a.StartsWith("--resume=", StringComparison.Ordinal)
                || a.StartsWith("--session-id=", StringComparison.Ordinal)
                || a.StartsWith("--connect=", StringComparison.Ordinal)
                || a.StartsWith("-r=", StringComparison.Ordinal)))
            {
                var sessionId = Guid.NewGuid().ToString();
                arguments.AddRange(["--session-id", sessionId]);
                var home = environment.TryGetValue("COPILOT_HOME", out var configuredHome)
                    ? configuredHome : Environment.GetEnvironmentVariable("COPILOT_HOME");
                home = string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot")
                    : Path.GetFullPath(home, context.WorkspacePath);
                sessionEventsPath = Path.Combine(home, "session-state", sessionId, "events.jsonl");
            }
        }

        var transcriptPath = Path.Combine(context.ArtifactsPath, "transcript.log");
        await using var transcript = new StreamWriter(transcriptPath, append: true);
        var outputLock = new object();

        // The agent runner keeps its provider credentials; generated code never does.
        var result = await _processRunner.RunAsync(
            command,
            arguments,
            context.WorkspacePath,
            context.Timeout,
            environment,
            stripCredentials: false,
            onOutput: line =>
            {
                lock (outputLock)
                {
                    transcript.WriteLine(line);
                    context.OnOutput?.Invoke(line);
                }
            },
            cancellationToken).ConfigureAwait(false);

        await transcript.FlushAsync(CancellationToken.None).ConfigureAwait(false);

        var usage = UsageReportReader.Read(usageFile, usageFormat);
        if (usageFormat == "copilot-cli"
            && usage.UnavailableMetrics.Count == new AdapterUsage().UnavailableMetrics.Count)
        {
            var partial = CopilotTelemetryReader.Read(telemetryPath);
            partial = sessionEventsPath is null
                ? partial with { Warnings = partial.Warnings.Append("Credit checkpoint recovery skipped for an explicitly selected/resumed session.").ToList() }
                : CopilotTelemetryReader.ReadCheckpoints(partial, sessionEventsPath,
                    Path.Combine(context.ArtifactsPath, "usage.checkpoints.jsonl"));
            usage = partial with { Warnings = usage.Warnings.Concat(partial.Warnings).ToList() };
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(context.ArtifactsPath, "usage.partial.json"),
                    System.Text.Json.JsonSerializer.Serialize(usage, JsonDefaults.Options),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                usage = usage with { Warnings = usage.Warnings.Append($"Could not persist partial usage: {ex.Message}").ToList() };
            }
        }

        return new ModelAttemptOutput
        {
            Succeeded = result.Succeeded,
            InfrastructureFailure = result.Cancelled,
            TimedOut = result.TimedOut,
            FailureReason = result.Succeeded
                ? null
                : result.Cancelled
                    ? "The evaluation run was cancelled."
                    : result.TimedOut
                    ? $"Agent runner exceeded the generation budget of {context.Timeout.TotalSeconds:0} s."
                    : $"Agent runner exited with code {result.ExitCode}.",
            DurationSeconds = result.DurationSeconds,
            UsageIsPartial = usage.UsageIsPartial,
            ToolCalls = usage.ToolCalls,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            EstimatedCostUsd = usage.EstimatedCostUsd,
            AiCredits = usage.AiCredits,
            PremiumRequests = usage.PremiumRequests,
            CacheReadTokens = usage.CacheReadTokens,
            CacheWriteTokens = usage.CacheWriteTokens,
            ReasoningTokens = usage.ReasoningTokens,
            ApiRequests = usage.ApiRequests,
            ApiDurationSeconds = usage.ApiDurationSeconds,
            ReportedModels = usage.ReportedModels,
            UsageWarnings = usage.Warnings,
            Runner = runner,
            UnavailableMetrics = usage.UnavailableMetrics,
        };
    }

    private static string Substitute(string value, ModelAttemptContext context, string usageFile) => value
        .Replace("{workspace}", context.WorkspacePath, StringComparison.Ordinal)
        .Replace("{promptFile}", context.PromptFilePath, StringComparison.Ordinal)
        .Replace("{prompt}", context.Prompt, StringComparison.Ordinal)
        .Replace("{artifacts}", context.ArtifactsPath, StringComparison.Ordinal)
        .Replace("{usageFile}", usageFile, StringComparison.Ordinal)
        .Replace("{scenario}", context.Scenario.Id, StringComparison.Ordinal)
        .Replace("{model}", context.Model.Id, StringComparison.Ordinal)
        .Replace("{timeoutSeconds}", ((int)context.Timeout.TotalSeconds).ToString(), StringComparison.Ordinal);

}
