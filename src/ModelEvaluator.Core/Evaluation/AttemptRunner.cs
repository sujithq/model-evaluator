using System.Diagnostics;
using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Evaluation;

/// <summary>Runs one independent attempt: generate, verify instructions, build, test and accept.</summary>
public sealed class AttemptRunner(
    ModelAdapterFactory adapterFactory,
    ProcessRunner processRunner,
    Action<string>? log = null,
    Action<string>? debugLog = null)
{
    private const string DotnetFileName = "dotnet";

    private readonly InstructionAdherenceChecker _instructionChecker = new();
    private readonly Action<string> _log = log ?? (_ => { });

    public async Task<AttemptResult> RunAsync(
        ScenarioPackage scenario,
        ModelConfiguration model,
        int repetition,
        EvaluationConfiguration configuration,
        EnvironmentInfo environment,
        CancellationToken cancellationToken = default)
    {
        var attemptId =
            $"{scenario.Id}__{Sanitize(model.Id)}__rep{repetition:00}__{DateTime.UtcNow:yyyyMMddHHmmssfff}";

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var budget = Budgets.Resolve(scenario.Definition.Budget, configuration.Budgets);

        var artifactsPath = Path.Combine(configuration.OutputDirectory, "attempts", attemptId);
        var workspaceRoot = configuration.WorkspaceRoot ?? Path.Combine(Path.GetTempPath(), "model-evaluator");
        var workspacePath = Path.Combine(workspaceRoot, attemptId, "workspace");

        debugLog?.Invoke($"[{attemptId}] Workspace: {workspacePath}");
        debugLog?.Invoke($"[{attemptId}] Artifacts: {artifactsPath}");
        debugLog?.Invoke($"Benchmark {scenario.Definition.BenchmarkVersion}; prompt SHA-256: {scenario.PromptHash}");
        debugLog?.Invoke(
            $"Budgets: generation={budget.GenerationTimeoutSeconds}s, restore/build/format={budget.BuildTimeoutSeconds}s each, " +
            $"generated tests={budget.TestTimeoutSeconds}s, acceptance={budget.AcceptanceTimeoutSeconds}s.");

        Directory.CreateDirectory(artifactsPath);
        FileSystemHelper.DeleteDirectoryIfExists(workspacePath);
        Directory.CreateDirectory(workspacePath);

        var checks = new List<CheckResult>();
        var acceptance = new AcceptanceSummary();
        var generatedTests = new GeneratedTestSummary();
        var outcome = AttemptOutcome.Success;
        string? failureReason = null;
        ModelAttemptOutput? generation = null;
        var runner = new RunnerInfo { Name = model.Adapter, Version = EvaluatorVersion.Value };

        try
        {
            _log($"[{attemptId}] preparing workspace");
            FileSystemHelper.CopyDirectory(scenario.StarterPath, workspacePath);

            var promptFilePath = Path.Combine(artifactsPath, "prompt.md");
            await File.WriteAllTextAsync(promptFilePath, scenario.ResolvedPrompt, cancellationToken).ConfigureAwait(false);

            var adapter = adapterFactory.Get(model.Adapter);
            var context = new ModelAttemptContext
            {
                Scenario = scenario,
                Model = model,
                WorkspacePath = workspacePath,
                ArtifactsPath = artifactsPath,
                Prompt = scenario.ResolvedPrompt,
                PromptFilePath = promptFilePath,
                Timeout = TimeSpan.FromSeconds(budget.GenerationTimeoutSeconds),
                Repetition = repetition,
                OnOutput = debugLog is null ? null : line => debugLog($"[generation] {line}"),
            };

            _log($"[{attemptId}] generating with model '{model.Id}' via '{model.Adapter}'");
            generation = await adapter.GenerateAsync(context, cancellationToken).ConfigureAwait(false);
            runner = generation.Runner;
            debugLog?.Invoke(
                $"Generation completed: succeeded={generation.Succeeded}, timedOut={generation.TimedOut}, " +
                $"duration={generation.DurationSeconds:0.0}s; runner={runner.Name} ({runner.Version}).");

            if (!generation.Succeeded)
            {
                outcome = generation.InfrastructureFailure
                    ? AttemptOutcome.InfrastructureFailure
                    : generation.TimedOut
                        ? AttemptOutcome.BudgetExceeded
                        : AttemptOutcome.ModelFailure;
                failureReason = generation.FailureReason ?? "The adapter did not produce a solution.";
                checks.Add(CheckResult.Fail("generation.completed", CheckCategory.BuildAndExecution, failureReason));
            }
            else
            {
                checks.Add(CheckResult.Pass(
                    "generation.completed", CheckCategory.BuildAndExecution,
                    $"Generation finished in {generation.DurationSeconds:0.0} s."));

                debugLog?.Invoke("Checking instruction adherence.");
                var instructionChecks = _instructionChecker.Check(workspacePath, scenario).ToList();
                checks.AddRange(instructionChecks);
                foreach (var check in instructionChecks)
                {
                    debugLog?.Invoke($"{check.Id}: {check.Status} - {check.Details}");
                }

                var restore = await RunDotnetAsync(
                    ["restore"], workspacePath, budget.BuildTimeoutSeconds, artifactsPath, "restore", cancellationToken)
                    .ConfigureAwait(false);
                checks.Add(FromCommand("build.restore", CheckCategory.BuildAndExecution, restore));

                if (restore.Succeeded)
                {
                    var build = await RunDotnetAsync(
                        ["build", "--configuration", "Release", "--no-restore"],
                        workspacePath, budget.BuildTimeoutSeconds, artifactsPath, "build", cancellationToken)
                        .ConfigureAwait(false);
                    checks.Add(FromCommand("build.compile", CheckCategory.BuildAndExecution, build));

                    if (build.Succeeded)
                    {
                        (var testCheck, generatedTests) = await RunGeneratedTestsAsync(
                            workspacePath, artifactsPath, budget.TestTimeoutSeconds, cancellationToken).ConfigureAwait(false);
                        checks.Add(testCheck);

                        checks.Add(await RunFormatAsync(
                            workspacePath, artifactsPath, budget.BuildTimeoutSeconds, cancellationToken).ConfigureAwait(false));

                        var acceptanceResult = await RunAcceptanceAsync(
                            scenario, workspacePath, artifactsPath, budget.AcceptanceTimeoutSeconds, cancellationToken)
                            .ConfigureAwait(false);
                        checks.AddRange(acceptanceResult.Checks);
                        acceptance = acceptanceResult.Summary;
                        if (acceptanceResult.InfrastructureFailure)
                        {
                            outcome = AttemptOutcome.InfrastructureFailure;
                            failureReason = acceptanceResult.FailureReason;
                        }
                    }
                    else
                    {
                        checks.Add(SkipDueToBuild("tests.generated", CheckCategory.CodeQuality));
                        checks.Add(SkipDueToBuild("quality.format", CheckCategory.CodeQuality));
                        checks.Add(SkipDueToBuild("acceptance.all-passed", CheckCategory.FunctionalCorrectness));
                    }
                }
                else
                {
                    checks.Add(SkipDueToBuild("build.compile", CheckCategory.BuildAndExecution));
                    checks.Add(SkipDueToBuild("tests.generated", CheckCategory.CodeQuality));
                    checks.Add(SkipDueToBuild("quality.format", CheckCategory.CodeQuality));
                    checks.Add(SkipDueToBuild("acceptance.all-passed", CheckCategory.FunctionalCorrectness));
                }
            }

            if (outcome == AttemptOutcome.Success
                && checks.Any(c => c.Mandatory && c.Status != CheckStatus.Passed))
            {
                outcome = AttemptOutcome.ModelFailure;
                failureReason ??= "One or more mandatory checks did not pass.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = AttemptOutcome.InfrastructureFailure;
            failureReason = "The evaluation run was cancelled.";
        }
        catch (Exception ex)
        {
            outcome = AttemptOutcome.InfrastructureFailure;
            failureReason = $"Harness error: {ex.GetType().Name}: {ex.Message}";
            _log($"[{attemptId}] infrastructure failure: {failureReason}");
        }
        finally
        {
            stopwatch.Stop();
        }

        var generationSeconds = generation?.DurationSeconds ?? 0;
        var result = new AttemptResult
        {
            AttemptId = attemptId,
            ScenarioId = scenario.Id,
            BenchmarkVersion = scenario.Definition.BenchmarkVersion,
            PromptHash = scenario.PromptHash,
            ModelId = model.Id,
            Adapter = model.Adapter,
            ModelSettings = model.Settings,
            Runner = runner,
            Environment = environment,
            Repetition = repetition,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Outcome = outcome,
            FailureReason = failureReason,
            Checks = checks,
            Acceptance = acceptance,
            GeneratedTests = generatedTests,
            Efficiency = new EfficiencyMetrics
            {
                ElapsedSecondsTotal = stopwatch.Elapsed.TotalSeconds,
                ElapsedSecondsGeneration = generationSeconds,
                ElapsedSecondsEvaluation = Math.Max(0, stopwatch.Elapsed.TotalSeconds - generationSeconds),
                ToolCalls = generation?.ToolCalls,
                InputTokens = generation?.InputTokens,
                OutputTokens = generation?.OutputTokens,
                EstimatedCostUsd = generation?.EstimatedCostUsd,
                UnavailableMetrics = generation?.UnavailableMetrics ?? ["toolCalls", "inputTokens", "outputTokens", "estimatedCostUsd"],
            },
            ArtifactsPath = artifactsPath,
        };

        await PersistAsync(result, workspacePath, artifactsPath, configuration, cancellationToken).ConfigureAwait(false);
        foreach (var check in checks.Where(c => c.Category != CheckCategory.InstructionAdherence))
        {
            debugLog?.Invoke($"{check.Id}: {check.Status} - {check.Details}");
        }

        debugLog?.Invoke($"Attempt duration: {stopwatch.Elapsed.TotalSeconds:0.0}s; outcome={result.Outcome}; reason={failureReason ?? "none"}.");
        _log($"[{attemptId}] outcome: {result.Outcome}");
        return result;
    }

    private async Task PersistAsync(
        AttemptResult result,
        string workspacePath,
        string artifactsPath,
        EvaluationConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            debugLog?.Invoke($"Saving generated code and result.json to {artifactsPath}");
            var generatedCodePath = Path.Combine(artifactsPath, "generated");
            if (Directory.Exists(workspacePath))
            {
                FileSystemHelper.CopyDirectory(workspacePath, generatedCodePath);
            }

            await File.WriteAllTextAsync(
                Path.Combine(artifactsPath, "result.json"),
                JsonSerializer.Serialize(result, JsonDefaults.Options),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log($"[{result.AttemptId}] failed to persist artifacts: {ex.Message}");
        }

        if (!configuration.KeepWorkspaces)
        {
            FileSystemHelper.DeleteDirectoryIfExists(Path.GetDirectoryName(workspacePath)!);
            debugLog?.Invoke($"Removed temporary workspace: {workspacePath}");
        }
        else
        {
            debugLog?.Invoke($"Kept workspace: {workspacePath}");
        }
    }

    private async Task<CommandResult> RunDotnetAsync(
        IEnumerable<string> arguments,
        string workingDirectory,
        int timeoutSeconds,
        string artifactsPath,
        string logName,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var argumentList = arguments.ToList();
        debugLog?.Invoke($"[{logName}] Starting dotnet {string.Join(' ', argumentList)}");
        debugLog?.Invoke($"[{logName}] Working directory: {workingDirectory}; timeout={timeoutSeconds}s.");
        var result = await processRunner.RunAsync(
            DotnetFileName,
            argumentList,
            workingDirectory,
            TimeSpan.FromSeconds(timeoutSeconds),
            environment,
            stripCredentials: true,
            onOutput: debugLog is null ? null : line => debugLog($"[{logName}] {line}"),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        debugLog?.Invoke(
            $"[{logName}] Completed: exit={result.ExitCode}, timedOut={result.TimedOut}, duration={result.DurationSeconds:0.0}s.");
        var logsPath = Path.Combine(artifactsPath, "commands");
        Directory.CreateDirectory(logsPath);
        await File.WriteAllTextAsync(
            Path.Combine(logsPath, $"{logName}.log"),
            $"$ dotnet {result.Arguments}{Environment.NewLine}exit={result.ExitCode} timedOut={result.TimedOut} " +
            $"duration={result.DurationSeconds:0.0}s{Environment.NewLine}{result.CombinedOutput}",
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task<(CheckResult Check, GeneratedTestSummary Summary)> RunGeneratedTestsAsync(
        string workspacePath, string artifactsPath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var resultsDirectory = Path.Combine(artifactsPath, "generated-tests");
        Directory.CreateDirectory(resultsDirectory);

        var result = await RunDotnetAsync(
            [
                "test", "--configuration", "Release", "--no-build",
                "--logger", "trx", "--results-directory", resultsDirectory,
            ],
            workspacePath, timeoutSeconds, artifactsPath, "test-generated", cancellationToken).ConfigureAwait(false);

        var counters = TrxParser.ParseDirectory(resultsDirectory);
        var summary = new GeneratedTestSummary
        {
            Passed = counters.Passed,
            Failed = counters.Failed,
            Skipped = counters.Skipped,
        };

        var check = result.Succeeded && counters.Failed == 0 && counters.Total > 0
            ? CheckResult.Pass("tests.generated", CheckCategory.CodeQuality,
                $"{counters.Passed}/{counters.Total} model-authored tests passed.")
            : CheckResult.Fail("tests.generated", CheckCategory.CodeQuality,
                counters.Total == 0
                    ? $"No model-authored tests were executed (exit {result.ExitCode}, timedOut={result.TimedOut})."
                    : $"{counters.Failed} of {counters.Total} model-authored tests failed.");

        return (check, summary);
    }

    private async Task<CheckResult> RunFormatAsync(
        string workspacePath, string artifactsPath, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var result = await RunDotnetAsync(
            ["format", "--verify-no-changes", "--no-restore", "--verbosity", "diagnostic"],
            workspacePath, timeoutSeconds, artifactsPath, "format", cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? CheckResult.Pass("quality.format", CheckCategory.CodeQuality,
                "dotnet format reported no formatting or analyzer violations.")
            : CheckResult.Fail("quality.format", CheckCategory.CodeQuality,
                result.TimedOut
                    ? "dotnet format exceeded its budget."
                    : "dotnet format reported violations; see commands/format.log.");
    }

    private async Task<AcceptanceOutcome> RunAcceptanceAsync(
        ScenarioPackage scenario,
        string workspacePath,
        string artifactsPath,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scenario.Definition.AcceptanceProject))
        {
            return new AcceptanceOutcome(
                [CheckResult.Skip("acceptance.all-passed", CheckCategory.FunctionalCorrectness,
                    "The scenario defines no acceptance project.")],
                new AcceptanceSummary(), true, $"Scenario '{scenario.Id}' has no acceptance project.");
        }

        // Evaluator-owned tests live outside the model workspace so the model cannot read or edit them.
        var acceptanceRoot = Path.Combine(artifactsPath, "acceptance");
        FileSystemHelper.DeleteDirectoryIfExists(acceptanceRoot);
        Directory.CreateDirectory(acceptanceRoot);

        var sharedSource = Path.Combine(scenario.Directory, "..", "..", "acceptance-shared");
        if (Directory.Exists(sharedSource))
        {
            FileSystemHelper.CopyDirectory(sharedSource, Path.Combine(acceptanceRoot, "acceptance-shared"));
        }

        var projectSource = Path.GetDirectoryName(scenario.AcceptanceProjectPath)!;
        var projectDirectory = Path.Combine(acceptanceRoot, "acceptance");
        FileSystemHelper.CopyDirectory(projectSource, projectDirectory);

        var resultsDirectory = Path.Combine(artifactsPath, "acceptance-results");
        Directory.CreateDirectory(resultsDirectory);

        var environment = new Dictionary<string, string>
        {
            ["EVAL_WORKSPACE"] = workspacePath,
            ["EVAL_SCENARIO"] = scenario.Id,
            ["EVAL_TARGET_FRAMEWORK"] = scenario.Definition.TargetFramework,
            ["EVAL_FIXTURES"] = Path.Combine(workspacePath, "fixtures"),
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        };

        var result = await RunDotnetAsync(
            [
                "test", "--configuration", "Release",
                "--logger", "trx", "--results-directory", resultsDirectory,
            ],
            projectDirectory, timeoutSeconds, artifactsPath, "acceptance", cancellationToken, environment)
            .ConfigureAwait(false);

        var counters = TrxParser.ParseDirectory(resultsDirectory);
        var summary = new AcceptanceSummary
        {
            Passed = counters.Passed,
            Failed = counters.Failed,
            Skipped = counters.Skipped,
        };

        if (counters.Total == 0 && !result.Succeeded)
        {
            // The acceptance suite could not run at all: treat as a harness problem, not a model failure.
            return new AcceptanceOutcome(
                [CheckResult.Fail("acceptance.all-passed", CheckCategory.FunctionalCorrectness,
                    $"Acceptance suite did not execute (exit {result.ExitCode}, timedOut={result.TimedOut}).")],
                summary, true, "Evaluator-owned acceptance suite failed to execute.");
        }

        var checks = new List<CheckResult>
        {
            counters.Failed == 0 && counters.Skipped == 0 && counters.Passed > 0
                ? CheckResult.Pass("acceptance.all-passed", CheckCategory.FunctionalCorrectness,
                    $"{counters.Passed}/{counters.Total} acceptance checks passed.")
                : CheckResult.Fail("acceptance.all-passed", CheckCategory.FunctionalCorrectness,
                    $"{counters.Passed} passed, {counters.Failed} failed, {counters.Skipped} skipped " +
                    "(required skipped checks do not count as passes)."),
        };

        return new AcceptanceOutcome(checks, summary, false, null);
    }

    private static CheckResult SkipDueToBuild(string id, CheckCategory category) =>
        CheckResult.Skip(id, category, "Skipped because an earlier build step failed.");

    private static CheckResult FromCommand(string id, CheckCategory category, CommandResult result) =>
        result.Succeeded
            ? CheckResult.Pass(id, category, $"Completed in {result.DurationSeconds:0.0} s.")
            : CheckResult.Fail(id, category,
                result.TimedOut
                    ? $"Exceeded its budget after {result.DurationSeconds:0.0} s."
                    : $"Failed with exit code {result.ExitCode}.");

    private static string Sanitize(string value) =>
        string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-'));

    private sealed record AcceptanceOutcome(
        IReadOnlyList<CheckResult> Checks,
        AcceptanceSummary Summary,
        bool InfrastructureFailure,
        string? FailureReason);
}

/// <summary>Applies configuration overrides on top of scenario budgets.</summary>
public static class Budgets
{
    public static ScenarioBudget Resolve(ScenarioBudget scenario, BudgetOverrides overrides) => new()
    {
        GenerationTimeoutSeconds = overrides.GenerationTimeoutSeconds ?? scenario.GenerationTimeoutSeconds,
        BuildTimeoutSeconds = overrides.BuildTimeoutSeconds ?? scenario.BuildTimeoutSeconds,
        TestTimeoutSeconds = overrides.TestTimeoutSeconds ?? scenario.TestTimeoutSeconds,
        AcceptanceTimeoutSeconds = overrides.AcceptanceTimeoutSeconds ?? scenario.AcceptanceTimeoutSeconds,
    };
}
