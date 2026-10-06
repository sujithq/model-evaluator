namespace JobWorker;

/// <summary>Abstraction over waits so tests can run without real timers.</summary>
public interface IClock
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}

/// <summary>Delay-based implementation used at runtime.</summary>
public sealed class SystemClock : IClock
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) =>
        milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, cancellationToken);
}

/// <summary>Writes the human-readable log lines defined by the contract.</summary>
public interface IJobLog
{
    void JobSucceeded(string id, int attempts);

    void JobFailed(string id, int attempts);

    void Processed(int count);

    void ShutdownRequested();
}

/// <summary>Writes log lines to a <see cref="TextWriter"/> (stdout by default).</summary>
public sealed class ConsoleJobLog(TextWriter writer) : IJobLog
{
    private readonly object _sync = new();

    public void JobSucceeded(string id, int attempts) =>
        WriteLine($"Job {id} succeeded after {attempts} attempt(s)");

    public void JobFailed(string id, int attempts) =>
        WriteLine($"Job {id} failed after {attempts} attempt(s)");

    public void Processed(int count) =>
        WriteLine($"Processed {count} job(s)");

    public void ShutdownRequested() =>
        WriteLine("Shutdown requested");

    private void WriteLine(string line)
    {
        lock (_sync)
        {
            writer.WriteLine(line);
            writer.Flush();
        }
    }
}
