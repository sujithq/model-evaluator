using System.Diagnostics;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Evaluation;

/// <summary>Executes the full evaluation matrix of models x scenarios x repetitions.</summary>
public sealed class EvaluationRunner(
    ModelAdapterFactory? adapterFactory = null,
    ProcessRunner? processRunner = null,
    Action<string>? log = null)
{
    private readonly ProcessRunner _processRunner = processRunner ?? new ProcessRunner();
    private readonly ModelAdapterFactory _adapterFactory = adapterFactory ?? ModelAdapterFactory.CreateDefault(processRunner);
    private readonly Action<string> _log = log ?? Console.WriteLine;

    public async Task<EvaluationReport> RunAsync(
        EvaluationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (configuration.MaxParallel < 1)
        {
            throw new InvalidOperationException("maxParallel must be a positive integer.");
        }

        if (configuration.Models.Count == 0)
        {
            throw new InvalidOperationException("The evaluation configuration contains no models.");
        }

        var catalog = ScenarioCatalog.Load(configuration.BenchmarkRoot);
        var scenarios = configuration.Scenarios.Count == 0
            ? catalog.Scenarios.OrderBy(s => s.Id, StringComparer.Ordinal).ToList()
            : configuration.Scenarios.Select(catalog.Get).ToList();

        var logLock = new object();
        void Log(string message)
        {
            lock (logLock)
            {
                _log(message);
            }
        }

        Action<string>? debugLog = configuration.Debug ? message => Log($"[debug] {message}") : null;
        debugLog?.Invoke($"Benchmark root: {configuration.BenchmarkRoot}");
        debugLog?.Invoke($"Output directory: {configuration.OutputDirectory}");
        debugLog?.Invoke(
            $"Matrix: {scenarios.Count} scenario(s) x {configuration.Models.Count} model(s) x " +
            $"{Math.Max(1, configuration.Repetitions)} repetition(s), max parallel attempts={configuration.MaxParallel}.");
        if (configuration.MaxParallel > 1)
        {
            Log($"warning: up to {configuration.MaxParallel} attempts will run concurrently; timing rankings may be affected by resource contention and provider throttling.");
        }
        debugLog?.Invoke($"Scenarios: {string.Join(", ", scenarios.Select(s => s.Id))}");
        debugLog?.Invoke($"Models: {string.Join(", ", configuration.Models.Select(m => m.Id))}");
        var environment = await ProbeEnvironmentAsync(configuration, cancellationToken).ConfigureAwait(false);
        debugLog?.Invoke($"Environment: {environment.OperatingSystem}; SDK {environment.DotnetSdkVersion}; git {environment.GitCommit}");
        var startedAt = DateTimeOffset.UtcNow;
        var runId = $"run-{startedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var jobs = scenarios.SelectMany(scenario => configuration.Models.SelectMany(model =>
            Enumerable.Range(1, Math.Max(1, configuration.Repetitions))
                .Select(repetition => (Scenario: scenario, Model: model, Repetition: repetition)))).ToList();
        var attempts = new AttemptResult?[jobs.Count];

        Directory.CreateDirectory(configuration.OutputDirectory);
        if (configuration.Debug)
        {
            var scenarioDetailsPath = ScenarioDetailsMarkdownWriter.Write(
                scenarios,
                configuration.Budgets,
                Path.Combine(configuration.OutputDirectory, runId));
            debugLog?.Invoke($"Scenario details: {scenarioDetailsPath}");
        }

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, jobs.Count),
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = configuration.MaxParallel,
                    CancellationToken = cancellationToken,
                },
                async (index, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var job = jobs[index];
                    Action<string>? attemptDebugLog = debugLog is null
                        ? null : message => debugLog($"[attempt {index + 1}/{jobs.Count}] {message}");
                    attemptDebugLog?.Invoke($"Starting {job.Scenario.Id} / {job.Model.Id}, repetition {job.Repetition}.");
                    var attemptRunner = new AttemptRunner(_adapterFactory, _processRunner, Log, attemptDebugLog);
                    attempts[index] = await attemptRunner.RunAsync(
                        job.Scenario, job.Model, job.Repetition, configuration, environment, token).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log("warning: evaluation cancelled; active attempts have stopped and queued attempts were not started.");
        }

        return new EvaluationReport
        {
            RunId = runId,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Runner = new RunnerInfo { Name = "model-evaluator", Version = EvaluatorVersion.Value },
            Environment = environment,
            Cancelled = cancellationToken.IsCancellationRequested,
            PlannedAttempts = jobs.Count,
            Attempts = attempts.OfType<AttemptResult>().ToList(),
        };
    }

    private async Task<EnvironmentInfo> ProbeEnvironmentAsync(
        EvaluationConfiguration configuration, CancellationToken cancellationToken)
    {
        var sdk = await _processRunner.RunAsync(
            "dotnet", ["--version"], Directory.GetCurrentDirectory(), TimeSpan.FromMinutes(2),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new EnvironmentInfo
        {
            MaxParallel = configuration.MaxParallel,
            OperatingSystem = RuntimeDescription(),
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            DotnetSdkVersion = sdk.Succeeded ? sdk.StandardOutput.Trim() : "unavailable",
            EvaluatorVersion = EvaluatorVersion.Value,
            ExecutionImage = configuration.ExecutionImage
                             ?? Environment.GetEnvironmentVariable("EVAL_EXECUTION_IMAGE"),
            GitCommit = await TryGetGitCommitAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    private static string RuntimeDescription() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()} " +
        $"({System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Trim()})";

    private async Task<string?> TryGetGitCommitAsync(CancellationToken cancellationToken)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("GITHUB_SHA");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        try
        {
            var result = await _processRunner.RunAsync(
                "git", ["rev-parse", "HEAD"], Directory.GetCurrentDirectory(), TimeSpan.FromSeconds(30),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return null;
        }
    }
}
