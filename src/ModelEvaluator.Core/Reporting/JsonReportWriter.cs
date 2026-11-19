using System.Text;
using System.Text.Json;
using ModelEvaluator.Core.Infrastructure;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Reporting;

public static class JsonReportWriter
{
    public static async Task<string> WriteAsync(
        EvaluationReport report,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "results.json");
        var json = JsonSerializer.Serialize(report, JsonDefaults.Write);
        await File.WriteAllTextAsync(
            path,
            json + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
        return path;
    }
}
