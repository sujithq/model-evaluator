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
        if (configuration.Models.Count == 0)
        {
            throw new InvalidOperationException("The evaluation configuration contains no models.");
        }

        var catalog = ScenarioCatalog.Load(configuration.BenchmarkRoot);
        var scenarios = configuration.Scenarios.Count == 0
            ? catalog.Scenarios.OrderBy(s => s.Id, StringComparer.Ordinal).ToList()
            : configuration.Scenarios.Select(catalog.Get).ToList();

        Action<string>? debugLog = configuration.Debug ? message => _log($"[debug] {message}") : null;
        debugLog?.Invoke($"Benchmark root: {configuration.BenchmarkRoot}");
        debugLog?.Invoke($"Output directory: {configuration.OutputDirectory}");
        debugLog?.Invoke(
            $"Matrix: {scenarios.Count} scenario(s) x {configuration.Models.Count} model(s) x " +
            $"{Math.Max(1, configuration.Repetitions)} repetition(s), executed sequentially.");
        debugLog?.Invoke($"Scenarios: {string.Join(", ", scenarios.Select(s => s.Id))}");
        debugLog?.Invoke($"Models: {string.Join(", ", configuration.Models.Select(m => m.Id))}");
        var environment = await ProbeEnvironmentAsync(configuration, cancellationToken).ConfigureAwait(false);
        debugLog?.Invoke($"Environment: {environment.OperatingSystem}; SDK {environment.DotnetSdkVersion}; git {environment.GitCommit}");
        var attemptRunner = new AttemptRunner(_adapterFactory, _processRunner, _log, debugLog);
        var startedAt = DateTimeOffset.UtcNow;
        var runId = $"run-{startedAt:yyyyMMdd-HHmmss}";
        var attempts = new List<AttemptResult>();

        Directory.CreateDirectory(configuration.OutputDirectory);
        if (configuration.Debug)
        {
            var scenarioDetailsPath = ScenarioDetailsMarkdownWriter.Write(
                scenarios,
                configuration.Budgets,
                Path.Combine(configuration.OutputDirectory, runId));
            debugLog?.Invoke($"Scenario details: {scenarioDetailsPath}");
        }

        foreach (var scenario in scenarios)
        {
            foreach (var model in configuration.Models)
            {
                for (var repetition = 1; repetition <= Math.Max(1, configuration.Repetitions); repetition++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    debugLog?.Invoke($"Starting {scenario.Id} / {model.Id}, repetition {repetition}.");
                    var attempt = await attemptRunner.RunAsync(
                        scenario, model, repetition, configuration, environment, cancellationToken).ConfigureAwait(false);
                    attempts.Add(attempt);
                }
            }
        }

        return new EvaluationReport
        {
            RunId = runId,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Runner = new RunnerInfo { Name = "model-evaluator", Version = EvaluatorVersion.Value },
            Environment = environment,
            Attempts = attempts,
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
