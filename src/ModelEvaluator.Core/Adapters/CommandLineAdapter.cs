using System.Text.Json;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Usage data an agent runner may write so efficiency can be reported.</summary>
public sealed record AdapterUsage
{
    public int? ToolCalls { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public decimal? EstimatedCostUsd { get; init; }
}

/// <summary>
/// Invokes an external agent runner (any CLI) to produce the solution. Keeps tools, context and
/// budgets identical across models by passing the same prompt file, workspace and timeout.
/// </summary>
/// <remarks>
/// Supported settings: <c>command</c>, <c>arguments</c> (space separated, quoted values supported),
/// <c>runnerName</c>, <c>runnerVersion</c>, <c>usageFile</c> (relative to the artifacts directory).
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

        if (string.IsNullOrWhiteSpace(command))
        {
            return new ModelAttemptOutput
            {
                Succeeded = false,
                InfrastructureFailure = true,
                FailureReason = $"Model '{settings.Id}' uses the command-line adapter but defines no 'command' setting.",
                Runner = runner,
                UnavailableMetrics = ["toolCalls", "inputTokens", "outputTokens", "estimatedCostUsd"],
            };
        }

        var usageFile = Path.Combine(context.ArtifactsPath, settings.GetSetting("usageFile", "usage.json"));
        var arguments = ArgumentParser
            .Split(settings.GetSetting("arguments"))
            .Select(a => Substitute(a, context, usageFile))
            .ToList();

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

        var transcriptPath = Path.Combine(context.ArtifactsPath, "transcript.log");
        await using var transcript = new StreamWriter(transcriptPath, append: true);

        // The agent runner keeps its provider credentials; generated code never does.
        var result = await _processRunner.RunAsync(
            command,
            arguments,
            context.WorkspacePath,
            context.Timeout,
            environment,
            stripCredentials: false,
            onOutput: line => transcript.WriteLine(line),
            cancellationToken).ConfigureAwait(false);

        await transcript.FlushAsync(cancellationToken).ConfigureAwait(false);

        var usage = ReadUsage(usageFile);
        var unavailable = new List<string>();
        if (usage?.ToolCalls is null)
        {
            unavailable.Add("toolCalls");
        }

        if (usage?.InputTokens is null)
        {
            unavailable.Add("inputTokens");
        }

        if (usage?.OutputTokens is null)
        {
            unavailable.Add("outputTokens");
        }

        if (usage?.EstimatedCostUsd is null)
        {
            unavailable.Add("estimatedCostUsd");
        }

        return new ModelAttemptOutput
        {
            Succeeded = result.Succeeded,
            TimedOut = result.TimedOut,
            FailureReason = result.Succeeded
                ? null
                : result.TimedOut
                    ? $"Agent runner exceeded the generation budget of {context.Timeout.TotalSeconds:0} s."
                    : $"Agent runner exited with code {result.ExitCode}.",
            DurationSeconds = result.DurationSeconds,
            ToolCalls = usage?.ToolCalls,
            InputTokens = usage?.InputTokens,
            OutputTokens = usage?.OutputTokens,
            EstimatedCostUsd = usage?.EstimatedCostUsd,
            Runner = runner,
            UnavailableMetrics = unavailable,
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

    private static AdapterUsage? ReadUsage(string usageFile)
    {
        if (!File.Exists(usageFile))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AdapterUsage>(File.ReadAllText(usageFile), JsonDefaults.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
