using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Durable;

public sealed record DurableEvaluationRequest
{
    public required IReadOnlyList<string> EvaluationArguments { get; init; }

    public required string WorkingDirectory { get; init; }
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

public sealed class EvaluationOrchestration :
    TaskOrchestrator<DurableEvaluationRequest, DurableEvaluationResult>
{
    public override Task<DurableEvaluationResult> RunAsync(
        TaskOrchestrationContext context,
        DurableEvaluationRequest input) =>
        context.CallActivityAsync<DurableEvaluationResult>(nameof(RunEvaluationActivity), input);
}

public sealed class RunEvaluationActivity(ILogger<RunEvaluationActivity> logger) :
    TaskActivity<DurableEvaluationRequest, DurableEvaluationResult>
{
    public override async Task<DurableEvaluationResult> RunAsync(
        TaskActivityContext context,
        DurableEvaluationRequest input)
    {
        var options = CommandLineOptions.Parse(input.EvaluationArguments);
        var configuration = EvaluationConfigurationResolver.Resolve(
            options,
            input.WorkingDirectory,
            AppContext.BaseDirectory);
        var execution = await new EvaluationExecutionService(message => logger.LogInformation("{Message}", message))
            .RunAsync(configuration, CancellationToken.None)
            .ConfigureAwait(false);
        var report = execution.Report;

        return new DurableEvaluationResult
        {
            RunId = report.RunId,
            JsonReportPath = execution.JsonReportPath,
            MarkdownReportPath = execution.MarkdownReportPath,
            PlannedAttempts = report.PlannedAttempts ?? report.Attempts.Count,
            CompletedAttempts = report.Attempts.Count,
            SuccessfulAttempts = report.Attempts.Count(attempt => attempt.IsSuccess),
            InfrastructureFailures = report.Attempts.Count(
                attempt => attempt.Outcome == AttemptOutcome.InfrastructureFailure),
            Cancelled = report.Cancelled,
        };
    }
}
