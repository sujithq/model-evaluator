using System.Collections;

namespace JobWorker;

/// <summary>Resolved worker configuration.</summary>
public sealed record WorkerOptions(string JobsDirectory, string OutputDirectory, int MaxAttempts, int RetryDelayMs);

/// <summary>Outcome of reading configuration from the environment.</summary>
public sealed record WorkerConfiguration(WorkerOptions? Options, string? Error)
{
    private const int DefaultMaxAttempts = 3;
    private const int DefaultRetryDelayMs = 50;

    public static WorkerConfiguration FromEnvironment(IDictionary environment)
    {
        var jobs = Get(environment, "JOBS_DIRECTORY");
        if (string.IsNullOrEmpty(jobs))
        {
            return Fail("Error: JOBS_DIRECTORY is not set.");
        }

        if (!Directory.Exists(jobs))
        {
            return Fail($"Error: jobs directory '{jobs}' was not found.");
        }

        var output = Get(environment, "OUTPUT_DIRECTORY");
        if (string.IsNullOrEmpty(output))
        {
            return Fail("Error: OUTPUT_DIRECTORY is not set.");
        }

        var maxAttempts = DefaultMaxAttempts;
        var maxAttemptsRaw = Get(environment, "MAX_ATTEMPTS");
        if (!string.IsNullOrEmpty(maxAttemptsRaw))
        {
            if (!int.TryParse(maxAttemptsRaw, out maxAttempts) || maxAttempts < 1)
            {
                return Fail("Error: MAX_ATTEMPTS must be a positive integer.");
            }
        }

        var retryDelay = DefaultRetryDelayMs;
        var retryDelayRaw = Get(environment, "RETRY_DELAY_MS");
        if (!string.IsNullOrEmpty(retryDelayRaw))
        {
            if (!int.TryParse(retryDelayRaw, out retryDelay) || retryDelay < 0)
            {
                return Fail("Error: RETRY_DELAY_MS must be a non-negative integer.");
            }
        }

        return new WorkerConfiguration(new WorkerOptions(jobs, output, maxAttempts, retryDelay), Error: null);
    }

    private static WorkerConfiguration Fail(string message) => new(Options: null, Error: message);

    private static string? Get(IDictionary environment, string key) =>
        environment.Contains(key) ? environment[key] as string : null;
}
