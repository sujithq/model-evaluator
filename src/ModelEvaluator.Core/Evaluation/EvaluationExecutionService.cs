using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Evaluation;

/// <summary>Runs and persists an evaluation independently of its CLI or durable host.</summary>
public sealed class EvaluationExecutionService(Action<string>? log = null)
{
    private readonly Action<string> _log = log ?? (_ => { });

    public async Task<EvaluationExecutionResult> RunAsync(
        EvaluationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var report = await new EvaluationRunner(log: _log)
            .RunAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
        var outputDirectory = Path.Combine(configuration.OutputDirectory, report.RunId);
        var jsonPath = JsonReportWriter.Write(report, outputDirectory);
        var markdownPath = MarkdownReportWriter.Write(report, outputDirectory);

        return new EvaluationExecutionResult
        {
            Report = report,
            OutputDirectory = outputDirectory,
            JsonReportPath = jsonPath,
            MarkdownReportPath = markdownPath,
        };
    }
}

public sealed record EvaluationExecutionResult
{
    public required EvaluationReport Report { get; init; }

    public required string OutputDirectory { get; init; }

    public required string JsonReportPath { get; init; }

    public required string MarkdownReportPath { get; init; }
}
