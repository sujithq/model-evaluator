using System.Text.Json;

namespace JobWorker;

/// <summary>
/// Core processing pipeline: reads job files from disk, runs the retry loop, and writes the results
/// document after every job. Kept independent from the hosted service to make unit-testing straightforward.
/// </summary>
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

    /// <summary>Processes every job in <see cref="WorkerOptions.JobsDirectory"/>.</summary>
    /// <returns>The list of results in processing order.</returns>
    public async Task<IReadOnlyList<JobResult>> RunAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);

        var files = Directory.EnumerateFiles(options.JobsDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();

        var results = new List<JobResult>(capacity: files.Length);

        // Always emit a results.json for the empty directory case before we process anything.
        WriteResults(results);

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                log.ShutdownRequested();
                WriteResults(results);
                return results;
            }

            var result = await ProcessFileAsync(file, cancellationToken).ConfigureAwait(false);

            if (result is null)
            {
                // Cancellation happened mid-processing; skip this partial job and stop.
                log.ShutdownRequested();
                WriteResults(results);
                return results;
            }

            results.Add(result);

            if (result.Status == "succeeded")
            {
                log.JobSucceeded(result.Id, result.Attempts);
            }
            else
            {
                log.JobFailed(result.Id, result.Attempts);
            }

            WriteResults(results);
        }

        log.Processed(results.Count);
        return results;
    }

    private async Task<JobResult?> ProcessFileAsync(string file, CancellationToken cancellationToken)
    {
        JobDefinition? job;
        string fallbackId = Path.GetFileNameWithoutExtension(file);
        try
        {
            await using var stream = File.OpenRead(file);
            job = await JsonSerializer.DeserializeAsync<JobDefinition>(stream, ReadOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Malformed(fallbackId, $"invalid job file: {ex.Message}");
        }

        if (job is null || string.IsNullOrEmpty(job.Id) || job.Payload is null)
        {
            return Malformed(fallbackId, "job file is missing required 'id' or 'payload'.");
        }

        return await AttemptAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private async Task<JobResult?> AttemptAsync(JobDefinition job, CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (attempts < options.MaxAttempts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            attempts++;
            var success = !job.Fatal && attempts > job.FailuresBeforeSuccess;
            if (success)
            {
                return new JobResult
                {
                    Id = job.Id!,
                    Status = "succeeded",
                    Attempts = attempts,
                    Result = job.Payload!.ToUpperInvariant(),
                    Error = null,
                };
            }

            if (attempts >= options.MaxAttempts)
            {
                break;
            }

            try
            {
                await clock.DelayAsync(options.RetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        var reason = job.Fatal
            ? "job is marked as fatal"
            : $"job did not succeed within {options.MaxAttempts} attempt(s)";

        return new JobResult
        {
            Id = job.Id!,
            Status = "failed",
            Attempts = attempts,
            Result = null,
            Error = reason,
        };
    }

    private static JobResult Malformed(string id, string message) => new()
    {
        Id = id,
        Status = "failed",
        Attempts = 1,
        Result = null,
        Error = message,
    };

    private void WriteResults(IReadOnlyList<JobResult> results)
    {
        var path = Path.Combine(options.OutputDirectory, "results.json");
        var json = JsonSerializer.Serialize(results, WriteOptions);
        File.WriteAllText(path, json);
    }
}
