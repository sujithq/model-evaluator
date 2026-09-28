using System.Diagnostics;
using System.Text;

namespace ModelEvaluator.Core.Execution;

/// <summary>Result of an external command execution.</summary>
public sealed record CommandResult
{
    public required string FileName { get; init; }

    public required string Arguments { get; init; }

    public required int ExitCode { get; init; }

    public required string StandardOutput { get; init; }

    public required string StandardError { get; init; }

    public required double DurationSeconds { get; init; }

    public required bool TimedOut { get; init; }

    public bool Succeeded => !TimedOut && ExitCode == 0;

    public string CombinedOutput => string.IsNullOrEmpty(StandardError)
        ? StandardOutput
        : StandardOutput + System.Environment.NewLine + StandardError;
}

/// <summary>Runs external commands with a hard timeout and a controlled environment.</summary>
public sealed class ProcessRunner
{
    /// <summary>Environment variables that must never reach a generated project or its tests.</summary>
    public static readonly string[] CredentialVariablePrefixes =
    [
        "OPENAI_", "AZURE_OPENAI_", "ANTHROPIC_", "GITHUB_TOKEN", "GH_TOKEN", "COPILOT_",
        "GOOGLE_API_KEY", "GEMINI_", "AWS_", "MISTRAL_", "GROQ_", "XAI_", "HF_TOKEN",
    ];

    public async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? environment = null,
        bool stripCredentials = true,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var argumentList = arguments.ToList();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (stripCredentials)
        {
            foreach (var key in startInfo.Environment.Keys.ToList())
            {
                if (CredentialVariablePrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    startInfo.Environment.Remove(key);
                }
            }
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            stderr.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
        }

        stopwatch.Stop();

        return new CommandResult
        {
            FileName = fileName,
            Arguments = string.Join(' ', argumentList),
            ExitCode = timedOut ? -1 : SafeExitCode(process),
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            DurationSeconds = stopwatch.Elapsed.TotalSeconds,
            TimedOut = timedOut,
        };
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }
        catch (NotSupportedException)
        {
            // Platform does not support tree kill.
        }
    }
}
