using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Adapters;

/// <summary>
/// Copies a reference implementation that ships with the scenario into the workspace.
/// Used to validate the harness: known-good samples must pass, broken variants must fail.
/// </summary>
public sealed class LocalSampleAdapter : IModelAdapter
{
    public const string AdapterKey = "local-sample";

    public string Key => AdapterKey;

    public Task<ModelAttemptOutput> GenerateAsync(ModelAttemptContext context, CancellationToken cancellationToken)
    {
        var runner = new RunnerInfo { Name = AdapterKey, Version = EvaluatorVersion.Value };
        var variantName = context.Model.GetSetting("variant", "good");

        if (!context.Scenario.Definition.Samples.TryGetValue(variantName, out var variant))
        {
            return Task.FromResult(new ModelAttemptOutput
            {
                Succeeded = false,
                InfrastructureFailure = true,
                FailureReason =
                    $"Scenario '{context.Scenario.Id}' does not define sample variant '{variantName}'.",
                Runner = runner,
                UnavailableMetrics = AllMetrics,
            });
        }

        var start = DateTimeOffset.UtcNow;
        try
        {
            FileSystemHelper.CopyDirectory(context.Scenario.SamplePath(variant), context.WorkspacePath);

            var overlay = context.Scenario.SampleOverlayPath(variant);
            if (overlay is not null)
            {
                FileSystemHelper.CopyDirectory(overlay, context.WorkspacePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return Task.FromResult(new ModelAttemptOutput
            {
                Succeeded = false,
                InfrastructureFailure = true,
                FailureReason = $"Failed to copy sample '{variantName}': {ex.Message}",
                Runner = runner,
                UnavailableMetrics = AllMetrics,
            });
        }

        File.WriteAllText(
            Path.Combine(context.ArtifactsPath, "transcript.log"),
            $"local-sample adapter copied variant '{variantName}' ({variant.Description}).{Environment.NewLine}");

        return Task.FromResult(new ModelAttemptOutput
        {
            Succeeded = true,
            DurationSeconds = (DateTimeOffset.UtcNow - start).TotalSeconds,
            Runner = runner,
            UnavailableMetrics = AllMetrics,
        });
    }

    private static readonly IReadOnlyList<string> AllMetrics = new AdapterUsage().UnavailableMetrics;
}
