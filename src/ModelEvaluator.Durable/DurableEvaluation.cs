using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Durable;

public sealed record DurableEvaluationRequest
{
    public required IReadOnlyList<string> EvaluationArguments { get; init; }

    public required string WorkingDirectory { get; init; }
}

public sealed record DurableAttemptJob
{
    public required string AttemptId { get; init; }

    public required string ScenarioId { get; init; }

    public required string ModelId { get; init; }

    public required int Repetition { get; init; }
}

public sealed record DurableEvaluationPlan
{
    public required DurableEvaluationRequest Request { get; init; }

    public required string RunId { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required string BaselineModelId { get; init; }

    public required int MaxParallel { get; init; }

    public required IReadOnlyList<DurableAttemptJob> Jobs { get; init; }
}

public sealed record DurableAttemptRequest
{
    public required DurableEvaluationRequest Evaluation { get; init; }

    public required DurableAttemptJob Job { get; init; }
}

public sealed record DurableEvaluationAggregation
{
    public required DurableEvaluationPlan Plan { get; init; }

    public required IReadOnlyList<AttemptResult> Attempts { get; init; }
}

public sealed record DurableEvaluationResult
{
    public required string RunId { get; init; }

    public required string JsonReportPath { get; init; }

    public required string MarkdownReportPath { get; init; }

    public int PlannedAttempts { get; init; }

    public int CompletedAttempts { get; init; }

    public int SuccessfulAttempts { get; init; }

    public int InfrastructureFailures { get; init; }

    public bool Cancelled { get; init; }
}

public static class DurableEvaluationPlanner
{
    public static DurableEvaluationPlan Create(
        DurableEvaluationRequest request,
        EvaluationConfiguration configuration,
        DateTimeOffset? startedAt = null,
        string? runId = null)
    {
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
            ? catalog.Scenarios.OrderBy(scenario => scenario.Id, StringComparer.Ordinal).ToList()
            : configuration.Scenarios.Select(catalog.Get).ToList();
        var effectiveStartedAt = startedAt ?? DateTimeOffset.UtcNow;
        var effectiveRunId = runId ?? $"run-{effectiveStartedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var jobs = scenarios.SelectMany(scenario => configuration.Models.SelectMany(model =>
            Enumerable.Range(1, Math.Max(1, configuration.Repetitions))
                .Select(repetition => new DurableAttemptJob
                {
                    AttemptId = CreateAttemptId(effectiveRunId, scenario.Id, model.Id, repetition),
                    ScenarioId = scenario.Id,
                    ModelId = model.Id,
                    Repetition = repetition,
                }))).ToList();

        return new DurableEvaluationPlan
        {
            Request = request,
            RunId = effectiveRunId,
            StartedAt = effectiveStartedAt,
            BaselineModelId = configuration.Models[0].Id,
            MaxParallel = configuration.MaxParallel,
            Jobs = jobs,
        };
    }

    private static string CreateAttemptId(
        string runId,
        string scenarioId,
        string modelId,
        int repetition) =>
        $"{Sanitize(scenarioId)}__{Sanitize(modelId)}__rep{repetition:00}__{runId}";

    private static string Sanitize(string value) =>
        string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '.' ? character : '-'));
}

public sealed class EvaluationOrchestration :
    TaskOrchestrator<DurableEvaluationRequest, DurableEvaluationResult>
{
    public override async Task<DurableEvaluationResult> RunAsync(
        TaskOrchestrationContext context,
        DurableEvaluationRequest input)
    {
        var plan = await context.CallActivityAsync<DurableEvaluationPlan>(
            nameof(PrepareEvaluationActivity),
            input);
        var attempts = new List<AttemptResult>(plan.Jobs.Count);

        foreach (var batch in plan.Jobs.Chunk(plan.MaxParallel))
        {
            var tasks = batch.Select(job =>
                context.CallActivityAsync<AttemptResult>(
                    nameof(RunEvaluationAttemptActivity),
                    new DurableAttemptRequest
                    {
                        Evaluation = input,
                        Job = job,
                    }));
            attempts.AddRange(await Task.WhenAll(tasks));
        }

        return await context.CallActivityAsync<DurableEvaluationResult>(
            nameof(FinalizeEvaluationActivity),
            new DurableEvaluationAggregation
            {
                Plan = plan,
                Attempts = attempts,
            });
    }
}

public sealed class PrepareEvaluationActivity(ILogger<PrepareEvaluationActivity> logger) :
    TaskActivity<DurableEvaluationRequest, DurableEvaluationPlan>
{
    public override Task<DurableEvaluationPlan> RunAsync(
        TaskActivityContext context,
        DurableEvaluationRequest input)
    {
        var configuration = Resolve(input);
        var plan = DurableEvaluationPlanner.Create(input, configuration);
        logger.LogInformation(
            "Prepared {AttemptCount} attempts with maximum parallelism {MaxParallel} for run {RunId}.",
            plan.Jobs.Count,
            plan.MaxParallel,
            plan.RunId);

        if (configuration.Debug)
        {
            var catalog = ScenarioCatalog.Load(configuration.BenchmarkRoot);
            var scenarios = plan.Jobs
                .Select(job => job.ScenarioId)
                .Distinct(StringComparer.Ordinal)
                .Select(catalog.Get)
                .ToList();
            ScenarioDetailsMarkdownWriter.Write(
                scenarios,
                configuration.Budgets,
                Path.Combine(configuration.OutputDirectory, plan.RunId));
        }

        return Task.FromResult(plan);
    }

    private static EvaluationConfiguration Resolve(DurableEvaluationRequest input)
    {
        var options = CommandLineOptions.Parse(input.EvaluationArguments);
        return EvaluationConfigurationResolver.Resolve(
            options,
            input.WorkingDirectory,
            AppContext.BaseDirectory);
    }
}

public sealed class RunEvaluationAttemptActivity(ILogger<RunEvaluationAttemptActivity> logger) :
    TaskActivity<DurableAttemptRequest, AttemptResult>
{
    public override async Task<AttemptResult> RunAsync(
        TaskActivityContext context,
        DurableAttemptRequest input)
    {
        var options = CommandLineOptions.Parse(input.Evaluation.EvaluationArguments);
        var configuration = EvaluationConfigurationResolver.Resolve(
            options,
            input.Evaluation.WorkingDirectory,
            AppContext.BaseDirectory);
        var resultPath = Path.Combine(
            configuration.OutputDirectory,
            "attempts",
            input.Job.AttemptId,
            "result.json");
        if (File.Exists(resultPath))
        {
            var existing = JsonSerializer.Deserialize<AttemptResult>(
                await File.ReadAllTextAsync(resultPath).ConfigureAwait(false),
                JsonDefaults.Options)
                ?? throw new InvalidDataException($"Could not deserialize completed attempt '{resultPath}'.");
            logger.LogInformation("Reusing completed attempt {AttemptId}.", input.Job.AttemptId);
            return existing;
        }

        return await new EvaluationRunner(log: message => logger.LogInformation("{Message}", message))
            .RunAttemptAsync(
                configuration,
                input.Job.ScenarioId,
                input.Job.ModelId,
                input.Job.Repetition,
                input.Job.AttemptId,
                CancellationToken.None)
            .ConfigureAwait(false);
    }
}

public sealed class FinalizeEvaluationActivity(ILogger<FinalizeEvaluationActivity> logger) :
    TaskActivity<DurableEvaluationAggregation, DurableEvaluationResult>
{
    public override Task<DurableEvaluationResult> RunAsync(
        TaskActivityContext context,
        DurableEvaluationAggregation input)
    {
        if (input.Attempts.Count == 0)
        {
            throw new InvalidOperationException("The durable evaluation produced no attempts.");
        }

        var options = CommandLineOptions.Parse(input.Plan.Request.EvaluationArguments);
        var configuration = EvaluationConfigurationResolver.Resolve(
            options,
            input.Plan.Request.WorkingDirectory,
            AppContext.BaseDirectory);
        var report = new EvaluationReport
        {
            BaselineModelId = input.Plan.BaselineModelId,
            RunId = input.Plan.RunId,
            StartedAt = input.Plan.StartedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Runner = new RunnerInfo { Name = "model-evaluator", Version = EvaluatorVersion.Value },
            Environment = input.Attempts[0].Environment,
            Cancelled = false,
            PlannedAttempts = input.Plan.Jobs.Count,
            Attempts = input.Attempts,
        };
        var outputDirectory = Path.Combine(configuration.OutputDirectory, report.RunId);
        var jsonPath = JsonReportWriter.Write(report, outputDirectory);
        var markdownPath = MarkdownReportWriter.Write(report, outputDirectory);
        logger.LogInformation("Persisted durable evaluation report for run {RunId}.", report.RunId);

        return Task.FromResult(new DurableEvaluationResult
        {
            RunId = report.RunId,
            JsonReportPath = jsonPath,
            MarkdownReportPath = markdownPath,
            PlannedAttempts = report.PlannedAttempts ?? report.Attempts.Count,
            CompletedAttempts = report.Attempts.Count,
            SuccessfulAttempts = report.Attempts.Count(attempt => attempt.IsSuccess),
            InfrastructureFailures = report.Attempts.Count(
                attempt => attempt.Outcome == AttemptOutcome.InfrastructureFailure),
            Cancelled = report.Cancelled,
        });
    }
}
