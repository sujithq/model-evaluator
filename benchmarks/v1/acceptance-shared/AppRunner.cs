using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ModelEvaluator.Acceptance;

/// <summary>Result of running a generated application.</summary>
public sealed record AppResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public string[] OutputLines => StandardOutput
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries);

    public override string ToString() =>
        $"exit={ExitCode} timedOut={TimedOut}{Environment.NewLine}stdout:{Environment.NewLine}{StandardOutput}" +
        $"{Environment.NewLine}stderr:{Environment.NewLine}{StandardError}";
}

/// <summary>Runs generated applications in a disposable working directory without provider credentials.</summary>
public static class AppRunner
{
    public static AppResult Run(
        string assemblyPath,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        int timeoutSeconds = 60)
    {
        using var process = Create(assemblyPath, arguments, workingDirectory, environment);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            Kill(process);
            return new AppResult(-1, stdout.ToString(), stderr.ToString(), TimedOut: true);
        }

        process.WaitForExit();
        return new AppResult(process.ExitCode, stdout.ToString(), stderr.ToString(), TimedOut: false);
    }

    /// <summary>Starts a long running application (web API, worker or Blazor app).</summary>
    public static Process Start(
        string assemblyPath,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        StringBuilder output)
    {
        var process = Create(assemblyPath, arguments, workingDirectory, environment);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (output) { output.AppendLine(e.Data); } } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (NotSupportedException)
        {
            // Platform limitation.
        }
    }

    private static Process Create(
        string assemblyPath,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }
}
