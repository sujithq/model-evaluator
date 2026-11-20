using System.Diagnostics;
using System.ComponentModel;
using ModelEvaluator.ApiComparison.Core.Adapters;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Infrastructure;
using ModelEvaluator.ApiComparison.Core.Results;
using ModelEvaluator.ApiComparison.Core.Scenarios;

namespace ModelEvaluator.ApiComparison.Core.Evaluation;

public sealed record EvaluationRunOptions
{
    public bool DryRun { get; init; }

    public IReadOnlyList<string> ScenarioIds { get; init; } = [];

    public IReadOnlyList<string> TargetIds { get; init; } = [];

    public int? Repetitions { get; init; }

    public Action<string>? OnProgress { get; init; }
}

public sealed class EvaluationRunner
{
    private readonly ApiComparisonModelAdapterFactory _adapters;

    public EvaluationRunner(ApiComparisonModelAdapterFactory? adapters = null)
    {
        _adapters = adapters ?? ApiComparisonModelAdapterFactory.CreateDefault();
    }

    public async Task<EvaluationReport> RunAsync(
        EvaluationConfiguration configuration,
        EvaluationRunOptions options,
        CancellationToken cancellationToken = default)
    {
        var validation = ConfigurationValidator.Validate(
            configuration,
            requireLiveCredentials: !options.DryRun);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Configuration is invalid:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", validation.Errors)}");
        }

        var catalog = ScenarioCatalog.Load(configuration.ScenarioRoot);
        var scenarios = SelectScenarios(catalog, options.ScenarioIds);
        var targets = SelectTargets(configuration.Targets, options.TargetIds);
        var repetitions = options.Repetitions ?? configuration.Repetitions;
        var startedAt = DateTimeOffset.UtcNow;
        var runId = $"run-{startedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var jobs = scenarios.SelectMany(scenario =>
            targets.SelectMany(target =>
                Enumerable.Range(1, repetitions).Select(repetition => (scenario, target, repetition))))
            .ToList();
        var attempts = new AttemptResult?[jobs.Count];

        var scenarioRunner = new ScenarioRunner(_adapters);
        await Parallel.ForEachAsync(
            Enumerable.Range(0, jobs.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = configuration.MaxParallel,
                CancellationToken = cancellationToken,
            },
            async (index, token) =>
            {
                var job = jobs[index];
                options.OnProgress?.Invoke(
                    $"[{index + 1}/{jobs.Count}] {job.scenario.Id} / {job.target.Id} " +
                    $"(rep {job.repetition}, {(options.DryRun ? "dry" : job.target.Adapter)})");
                attempts[index] = await scenarioRunner.RunAsync(
                    runId,
                    job.scenario,
                    job.target,
                    job.repetition,
                    configuration,
                    options.DryRun,
                    token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        var completedAttempts = attempts.OfType<AttemptResult>().ToList();
        var completedAt = DateTimeOffset.UtcNow;
        var warnings = new List<string>(validation.Warnings);
        if (configuration.MaxParallel > 1)
        {
            warnings.Add(
                "maxParallel is greater than 1; compare latency and TTFB only with matched runs because provider and host contention can change them.");
        }

        warnings.AddRange(completedAttempts.SelectMany(attempt => attempt.Warnings)
            .Where(warning => warning.StartsWith("Dry-run", StringComparison.Ordinal)
                              || warning.StartsWith("Scenario", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal));

        var environment = await EnvironmentProbe.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return new EvaluationReport
        {
            SchemaVersion = "1.0",
            RunId = runId,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ExecutionMode = options.DryRun
                ? "dry-run"
                : completedAttempts.All(attempt => attempt.ExecutionMode == "local-fixture")
                    ? "local-fixture"
                    : completedAttempts.Any(attempt => attempt.ExecutionMode == "local-fixture")
                        ? "mixed"
                        : "live",
            Environment = environment,
            PlannedAttempts = jobs.Count,
            Attempts = completedAttempts,
            Summaries = Summarize(completedAttempts, jobs.Select(job => job.target).DistinctBy(target => target.Id).ToList()),
            Comparisons = ComparisonAnalyzer.Analyze(configuration, targets, completedAttempts),
            Warnings = warnings,
        };
    }

    private static IReadOnlyList<ScenarioDefinition> SelectScenarios(
        ScenarioCatalog catalog,
        IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
        {
            return catalog.Scenarios;
        }

        return requested.Select(catalog.Get).ToList();
    }

    private static List<TargetConfiguration> SelectTargets(
        IReadOnlyList<TargetConfiguration> configured,
        IReadOnlyList<string> requested)
    {
        var targets = configured.Where(target => target.Enabled).ToList();
        if (requested.Count == 0)
        {
            return targets;
        }

        var selected = new List<TargetConfiguration>();
        foreach (var id in requested)
        {
            var target = targets.FirstOrDefault(candidate =>
                             string.Equals(candidate.Id, id, StringComparison.Ordinal))
                         ?? throw new KeyNotFoundException($"Enabled target '{id}' was not found.");
            selected.Add(target);
        }

        return selected;
    }

    private static IReadOnlyList<TargetSummary> Summarize(
        IReadOnlyList<AttemptResult> attempts,
        IReadOnlyList<TargetConfiguration> targets)
    {
        var summaries = new List<TargetSummary>();
        foreach (var target in targets)
        {
            var targetAttempts = attempts.Where(attempt => attempt.Target.Id == target.Id).ToList();
            var calls = targetAttempts.SelectMany(attempt => attempt.Calls).ToList();
            var first = targetAttempts.FirstOrDefault();
            var readiness = AggregateReadiness(targetAttempts);
            summaries.Add(new TargetSummary
            {
                TargetId = target.Id,
                ModelId = target.ModelId,
                DeploymentId = target.DeploymentId,
                Adapter = first?.Target.RequestedAdapter ?? target.Adapter,
                ApiKind = target.ApiKind,
                Planned = targetAttempts.Count,
                Passed = targetAttempts.Count(attempt => attempt.Passed),
                MeanLatencyMilliseconds = calls.Count == 0
                    ? 0
                    : calls.Average(call => call.LatencyMilliseconds),
                MeanTimeToFirstByteMilliseconds = AverageOrNull(
                    calls.Where(call => call.TimeToFirstByteMilliseconds.HasValue)
                        .Select(call => call.TimeToFirstByteMilliseconds!.Value)
                        .ToList()),
                InputTokens = SumWhenKnown(calls, call => call.Usage.InputTokens),
                OutputTokens = SumWhenKnown(calls, call => call.Usage.OutputTokens),
                ReasoningTokens = SumWhenKnown(calls, call => call.Usage.ReasoningTokens),
                Readiness = readiness,
            });
        }

        return summaries;
    }

    private static long? SumWhenKnown(
        IReadOnlyList<CallResult> calls,
        Func<CallResult, long?> selector)
    {
        var values = calls.Select(selector).ToList();
        return values.Count == 0 || values.Any(value => value is null)
            ? null
            : values.Sum(value => value!.Value);
    }

    private static double? AverageOrNull(IReadOnlyList<double> values) =>
        values.Count == 0 ? null : values.Average();

    private static ReadinessSummary AggregateReadiness(IReadOnlyList<AttemptResult> attempts)
    {
        var first = attempts.FirstOrDefault()?.Readiness ?? new ReadinessSummary
        {
            TechnicalCompatibility = "not-evaluated",
            FunctionalValidation = "not-evaluated",
            EuDataZone = "unknown",
            Capacity = "unknown",
        };
        return new ReadinessSummary
        {
            TechnicalCompatibility = AggregateState(
                attempts.Select(attempt => attempt.Readiness.TechnicalCompatibility),
                "compatible",
                "not-evaluated"),
            FunctionalValidation = attempts.Count > 0 && attempts.All(attempt =>
                attempt.Readiness.FunctionalValidation == "passed")
                ? "passed"
                : "failed",
            EuDataZone = first.EuDataZone,
            EuDataZoneReference = first.EuDataZoneReference,
            Capacity = first.Capacity,
            CapacityReference = first.CapacityReference,
        };
    }

    private static string AggregateState(
        IEnumerable<string> states,
        string success,
        string missing)
    {
        var values = states.ToList();
        if (values.Count == 0 || values.All(value => value == missing))
        {
            return missing;
        }

        return values.All(value => value == success) ? success : "not-compatible";
    }
}

internal static class EnvironmentProbe
{
    public static async Task<EnvironmentSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        return new EnvironmentSnapshot
        {
            OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim(),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            DotnetSdk = await RunAsync("dotnet", "--version", cancellationToken).ConfigureAwait(false)
                         ?? "unavailable",
            GitCommit = await RunAsync("git", "rev-parse HEAD", cancellationToken).ConfigureAwait(false),
        };
    }

    private static async Task<string?> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return null;
        }

        if (process is null)
        {
            return null;
        }

        using (process)
        {
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
    }
}
