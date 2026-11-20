using System.Diagnostics;
using ModelEvaluator.Core.Execution;

namespace ModelEvaluator.Core.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Cancel_KillsProcessAndDrainsOutputBeforeReturning()
    {
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (command, arguments) = SleepingProcess();
        var task = new ProcessRunner().RunAsync(command, arguments, Path.GetTempPath(), TimeSpan.FromMinutes(1),
            onOutput: line =>
            {
                if (int.TryParse(line, out var pid))
                {
                    ready.TrySetResult(pid);
                }
            }, cancellationToken: cancellation.Token);
        var pid = await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        using var process = Process.GetProcessById(pid);
        cancellation.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(process.HasExited);
        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.Contains(pid.ToString(), result.StandardOutput);
    }

    [Fact]
    public async Task Timeout_KillsProcessAndIsNotCancellation()
    {
        var (command, arguments) = SleepingProcess();
        var result = await new ProcessRunner().RunAsync(
            command, arguments, Path.GetTempPath(), TimeSpan.FromSeconds(3));

        Assert.True(result.TimedOut);
        Assert.False(result.Cancelled);
        Assert.False(result.Succeeded);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task AlreadyCancelled_DoesNotStartProcess()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProcessRunner().RunAsync("nonexistent-test-executable", [], Path.GetTempPath(), TimeSpan.FromSeconds(1),
                cancellationToken: new CancellationToken(true)));
    }

    private static (string Command, string[] Arguments) SleepingProcess() => OperatingSystem.IsWindows()
        ? ("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "[Console]::WriteLine($PID); Start-Sleep -Seconds 60"])
        : ("sh", ["-c", "echo $$; exec sleep 60"]);
}
