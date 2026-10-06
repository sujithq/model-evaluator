using System.Text.Json;
using JobWorker;

namespace JobWorker.Tests;

public sealed class JobProcessorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jobworker-tests", Guid.NewGuid().ToString("N"));
    private readonly string _jobs;
    private readonly string _output;
    private readonly RecordingLog _log = new();
    private readonly ImmediateClock _clock = new();

    public JobProcessorTests()
    {
        _jobs = Path.Combine(_root, "jobs");
        _output = Path.Combine(_root, "out");
        Directory.CreateDirectory(_jobs);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private JobProcessor CreateProcessor(int maxAttempts = 3, int retryDelayMs = 0)
    {
        var options = new WorkerOptions(_jobs, _output, maxAttempts, retryDelayMs);
        return new JobProcessor(options, _clock, _log);
    }

    private void WriteJob(string name, object job) =>
        File.WriteAllText(Path.Combine(_jobs, name), JsonSerializer.Serialize(job));

    private JsonElement[] ReadResults()
    {
        var text = File.ReadAllText(Path.Combine(_output, "results.json"));
        return JsonDocument.Parse(text).RootElement.EnumerateArray().ToArray();
    }

    [Fact]
    public async Task SuccessfulJob_HasSingleAttemptAndUppercasedResult()
    {
        WriteJob("job-01.json", new { id = "alpha", payload = "hi" });

        await CreateProcessor().RunAsync(CancellationToken.None);

        var result = ReadResults().Single();
        Assert.Equal("alpha", result.GetProperty("id").GetString());
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Equal(1, result.GetProperty("attempts").GetInt32());
        Assert.Equal("HI", result.GetProperty("result").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("error").ValueKind);
    }

    [Fact]
    public async Task JobWithTransientFailures_SucceedsAfterCorrectNumberOfAttempts()
    {
        WriteJob("job-01.json", new { id = "beta", payload = "retry", failuresBeforeSuccess = 2 });

        await CreateProcessor(maxAttempts: 3).RunAsync(CancellationToken.None);

        var result = ReadResults().Single();
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Equal(3, result.GetProperty("attempts").GetInt32());
        Assert.Equal("RETRY", result.GetProperty("result").GetString());
    }

    [Fact]
    public async Task JobExceedingMaxAttempts_IsFailedWithMaxAttempts()
    {
        WriteJob("job-01.json", new { id = "gamma", payload = "nope", failuresBeforeSuccess = 5 });

        await CreateProcessor(maxAttempts: 3).RunAsync(CancellationToken.None);

        var result = ReadResults().Single();
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(3, result.GetProperty("attempts").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("result").ValueKind);
        Assert.False(string.IsNullOrEmpty(result.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task FatalJob_AlwaysFails()
    {
        WriteJob("job-01.json", new { id = "delta", payload = "boom", fatal = true });

        await CreateProcessor(maxAttempts: 5).RunAsync(CancellationToken.None);

        var result = ReadResults().Single();
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(5, result.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task Jobs_AreProcessedInOrdinalFileNameOrder()
    {
        WriteJob("job-02.json", new { id = "second", payload = "b" });
        WriteJob("job-01.json", new { id = "first", payload = "a" });

        await CreateProcessor().RunAsync(CancellationToken.None);

        var results = ReadResults();
        Assert.Equal("first", results[0].GetProperty("id").GetString());
        Assert.Equal("second", results[1].GetProperty("id").GetString());
    }

    [Fact]
    public async Task EmptyDirectory_ProducesEmptyResultsFileAndLogsZero()
    {
        await CreateProcessor().RunAsync(CancellationToken.None);

        Assert.Equal("[]", File.ReadAllText(Path.Combine(_output, "results.json")).Trim());
        Assert.Contains("Processed 0 job(s)", _log.Lines);
    }

    [Fact]
    public async Task RaisingMaxAttempts_TurnsFailedJobIntoSucceeded()
    {
        WriteJob("job-01.json", new { id = "gamma", payload = "flip", failuresBeforeSuccess = 5 });

        await CreateProcessor(maxAttempts: 6).RunAsync(CancellationToken.None);

        var result = ReadResults().Single();
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Equal(6, result.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task LogLines_FollowContract()
    {
        WriteJob("job-01.json", new { id = "alpha", payload = "hi" });
        WriteJob("job-02.json", new { id = "delta", payload = "boom", fatal = true });

        await CreateProcessor(maxAttempts: 2).RunAsync(CancellationToken.None);

        Assert.Contains("Job alpha succeeded after 1 attempt(s)", _log.Lines);
        Assert.Contains("Job delta failed after 2 attempt(s)", _log.Lines);
        Assert.Contains("Processed 2 job(s)", _log.Lines);
    }

    private sealed class ImmediateClock : IClock
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingLog : IJobLog
    {
        public List<string> Lines { get; } = new();

        public void JobSucceeded(string id, int attempts) =>
            Lines.Add($"Job {id} succeeded after {attempts} attempt(s)");

        public void JobFailed(string id, int attempts) =>
            Lines.Add($"Job {id} failed after {attempts} attempt(s)");

        public void Processed(int count) =>
            Lines.Add($"Processed {count} job(s)");

        public void ShutdownRequested() => Lines.Add("Shutdown requested");
    }
}
