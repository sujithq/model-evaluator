// Deliberately broken variant used to prove that the evaluator detects contract violations.
// - Does not retry: every job is attempted exactly once (breaks the transient-failure contract).
// - Does not flush results.json after each job (breaks the shutdown-safety contract).
using System.Text.Json;

namespace JobWorker;

public sealed class JobProcessor(WorkerOptions options, IClock clock, IJobLog log)
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<IReadOnlyList<JobResult>> RunAsync(CancellationToken cancellationToken)
    {
        _ = clock; // clock is intentionally unused in the broken variant.
        Directory.CreateDirectory(options.OutputDirectory);

        var files = Directory.EnumerateFiles(options.JobsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        var results = new List<JobResult>();

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                log.ShutdownRequested();
                // Broken: partial results are not flushed on shutdown.
                return results;
            }

            JobDefinition? job;
            try
            {
                await using var stream = File.OpenRead(file);
                job = await JsonSerializer.DeserializeAsync<JobDefinition>(stream, ReadOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                continue;
            }

            if (job is null || string.IsNullOrEmpty(job.Id) || job.Payload is null)
            {
                continue;
            }

            // Broken: no retry loop. A single attempt only.
            var success = !job.Fatal && job.FailuresBeforeSuccess == 0;
            var jobResult = success
                ? new JobResult
                {
                    Id = job.Id,
                    Status = "succeeded",
                    Attempts = 1,
                    Result = job.Payload.ToUpperInvariant(),
                    Error = null,
                }
                : new JobResult
                {
                    Id = job.Id,
                    Status = "failed",
                    Attempts = 1,
                    Result = null,
                    Error = "single-attempt failure",
                };

            results.Add(jobResult);
            if (success)
            {
                log.JobSucceeded(jobResult.Id, jobResult.Attempts);
            }
            else
            {
                log.JobFailed(jobResult.Id, jobResult.Attempts);
            }
        }

        // Broken: results.json is only written once, at the very end.
        var path = Path.Combine(options.OutputDirectory, "results.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(results, WriteOptions), cancellationToken)
            .ConfigureAwait(false);

        log.Processed(results.Count);
        return results;
    }
}
