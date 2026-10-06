using System.ComponentModel;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Outcome of asking a runner whether the authenticated user can use a model.</summary>
public enum ModelAvailability
{
    /// <summary>The runner accepted the model and completed the probe request.</summary>
    Available,

    /// <summary>The runner explicitly rejected the model id, entitlement or policy.</summary>
    Unavailable,

    /// <summary>The probe could not determine availability; the detail explains why.</summary>
    Unknown,

    /// <summary>The model runs locally, so there is no provider account to query.</summary>
    NotApplicable,
}

/// <summary>Availability of one configured model for the currently authenticated runner user.</summary>
public sealed record ModelAvailabilityResult
{
    public required string ModelId { get; init; }

    public required ModelAvailability Availability { get; init; }

    /// <summary>Human readable explanation, taken from the runner output when it failed.</summary>
    public string? Detail { get; init; }

    public double DurationSeconds { get; init; }
}

/// <summary>
/// Checks whether a configured model is usable by the authenticated runner account.
/// The runner is the only component that knows the account's entitlements, so the probe
/// invokes it once per model with a minimal prompt.
/// </summary>
public sealed class ModelAvailabilityProbe(ProcessRunner? processRunner = null)
{
    /// <summary>Deliberately trivial prompt; a probe must not do real work.</summary>
    public const string ProbePrompt = "Reply with the single word: ok";

    private const string ProbeScenarioId = "availability-probe";

    /// <summary>Runner output fragments that mean the account cannot use the model.</summary>
    private static readonly string[] UnavailableMarkers =
    [
        "not available", "unknown model", "model not found", "model_not_found", "invalid model",
        "does not have access", "no access to", "not entitled", "not supported", "unsupported model",
    ];

    /// <summary>Runner output fragments that mean the runner itself is not signed in.</summary>
    private static readonly string[] AuthenticationMarkers =
    [
        "not logged in", "not authenticated", "please log in", "please login", "run `copilot login`",
        "authentication required", "unauthorized", "401",
    ];

    private readonly ProcessRunner _processRunner = processRunner ?? new ProcessRunner();

    public async Task<ModelAvailabilityResult> ProbeAsync(
        ModelConfiguration model,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(model.Adapter, CommandLineAdapter.AdapterKey, StringComparison.OrdinalIgnoreCase))
        {
            return new ModelAvailabilityResult
            {
                ModelId = model.Id,
                Availability = ModelAvailability.NotApplicable,
                Detail = $"Adapter '{model.Adapter}' runs locally and needs no provider account.",
            };
        }

        var command = model.GetSetting("command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return new ModelAvailabilityResult
            {
                ModelId = model.Id,
                Availability = ModelAvailability.Unknown,
                Detail = "The model defines no 'command' setting.",
            };
        }

        var workspace = Directory.CreateTempSubdirectory("model-evaluator-probe-");
        try
        {
            var promptFile = Path.Combine(workspace.FullName, "prompt.md");
            await File.WriteAllTextAsync(promptFile, ProbePrompt, cancellationToken).ConfigureAwait(false);

            var arguments = ArgumentParser
                .Split(model.GetSetting("arguments"))
                .Select(argument => Substitute(argument, model, workspace.FullName, promptFile, timeout))
                .ToList();

            var environment = new Dictionary<string, string>(model.Environment)
            {
                ["EVAL_WORKSPACE"] = workspace.FullName,
                ["EVAL_PROMPT_FILE"] = promptFile,
                ["EVAL_ARTIFACTS"] = workspace.FullName,
                ["EVAL_USAGE_FILE"] = Path.Combine(workspace.FullName, "usage.json"),
                ["EVAL_SCENARIO"] = ProbeScenarioId,
                ["EVAL_MODEL"] = model.Id,
                ["EVAL_TIMEOUT_SECONDS"] = ((int)timeout.TotalSeconds).ToString(),
            };

            // The runner keeps its own credentials; that is exactly what the probe is testing.
            var result = await _processRunner.RunAsync(
                command, arguments, workspace.FullName, timeout, environment,
                stripCredentials: false, onOutput: null, cancellationToken).ConfigureAwait(false);

            return Classify(model.Id, result);
        }
        catch (Win32Exception ex)
        {
            return new ModelAvailabilityResult
            {
                ModelId = model.Id,
                Availability = ModelAvailability.Unknown,
                Detail = $"Runner '{command}' could not be started: {ex.Message}",
            };
        }
        finally
        {
            FileSystemHelper.DeleteDirectoryIfExists(workspace.FullName);
        }
    }

    private static ModelAvailabilityResult Classify(string modelId, CommandResult result)
    {
        if (result.Cancelled)
        {
            return Result(modelId, ModelAvailability.Unknown, "The probe was cancelled.", result);
        }

        if (result.TimedOut)
        {
            return Result(modelId, ModelAvailability.Unknown,
                $"The runner did not answer within {result.DurationSeconds:0} s.", result);
        }

        if (result.Succeeded)
        {
            return Result(modelId, ModelAvailability.Available, null, result);
        }

        var output = result.CombinedOutput;
        var detail = FirstMeaningfulLine(output) ?? $"The runner exited with code {result.ExitCode}.";

        if (AuthenticationMarkers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return Result(modelId, ModelAvailability.Unknown, $"The runner is not authenticated: {detail}", result);
        }

        return UnavailableMarkers.Any(marker => output.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? Result(modelId, ModelAvailability.Unavailable, detail, result)
            : Result(modelId, ModelAvailability.Unknown, detail, result);
    }

    private static ModelAvailabilityResult Result(
        string modelId, ModelAvailability availability, string? detail, CommandResult result) => new()
        {
            ModelId = modelId,
            Availability = availability,
            Detail = detail,
            DurationSeconds = result.DurationSeconds,
        };

    private static string? FirstMeaningfulLine(string output) => output
        .Split('\n')
        .Select(line => line.Trim())
        .FirstOrDefault(line => line.Length > 0);

    private static string Substitute(
        string value, ModelConfiguration model, string workspace, string promptFile, TimeSpan timeout) => value
        .Replace("{workspace}", workspace, StringComparison.Ordinal)
        .Replace("{promptFile}", promptFile, StringComparison.Ordinal)
        .Replace("{prompt}", ProbePrompt, StringComparison.Ordinal)
        .Replace("{artifacts}", workspace, StringComparison.Ordinal)
        .Replace("{usageFile}", Path.Combine(workspace, "usage.json"), StringComparison.Ordinal)
        .Replace("{scenario}", ProbeScenarioId, StringComparison.Ordinal)
        .Replace("{model}", model.Id, StringComparison.Ordinal)
        .Replace("{timeoutSeconds}", ((int)timeout.TotalSeconds).ToString(), StringComparison.Ordinal);
}
