using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelEvaluator.Acceptance;
using Xunit;

namespace WorkerJobProcessor.Acceptance;

/// <summary>
/// Evaluator-owned acceptance tests for the worker-job-processor scenario. The tests exercise the worker
/// as a black box: they start the built assembly with a jobs directory and an output directory, then
/// inspect stdout, stderr, exit code and the resulting <c>results.json</c>.
/// </summary>
public sealed class WorkerJobProcessorTests : IDisposable
{
    private readonly string _assembly = EvaluationContext.FindApplicationAssembly();
    private readonly string _outputRoot = EvaluationContext.CreateTempDirectory();
    private readonly List<string> _extraDirectories = new();

    public void Dispose()
    {
        foreach (var dir in _extraDirectories.Append(_outputRoot))
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private string NewOutputDirectory()
    {
        var path = Path.Combine(_outputRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private string ScenarioJobsDirectory => Path.Combine(EvaluationContext.FixturesDirectory, "jobs");

    private AppResult RunWithFixtures(
        IReadOnlyDictionary<string, string>? extraEnvironment = null,
        int timeoutSeconds = 60)
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        if (extraEnvironment is not null)
        {
            foreach (var (k, v) in extraEnvironment)
            {
                env[k] = v;
            }
        }

        var result = AppRunner.Run(_assembly, [], output, env, timeoutSeconds);
        return result with { StandardOutput = result.StandardOutput, StandardError = result.StandardError };
    }

    private static JsonElement[] LoadResults(string outputDirectory)
    {
        var path = Path.Combine(outputDirectory, "results.json");
        Assert.True(File.Exists(path), $"results.json was not written to '{outputDirectory}'.");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray().ToArray();
    }

    private static Dictionary<string, JsonElement> IndexById(JsonElement[] results) =>
        results.ToDictionary(e => e.GetProperty("id").GetString()!, e => e);

    // ---------- Happy-path / contract shape ----------

    [Fact]
    public void ProcessesAllFixtureJobs_WithExitCodeZero()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.False(result.TimedOut, "Worker did not exit within the smoke timeout.");
        var results = LoadResults(output);
        Assert.Equal(6, results.Length);
    }

    [Fact]
    public void ResultsFile_HasDocumentedShapeForEveryEntry()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);
        Assert.Equal(0, result.ExitCode);

        foreach (var entry in LoadResults(output))
        {
            Assert.Equal(JsonValueKind.String, entry.GetProperty("id").ValueKind);
            var status = entry.GetProperty("status").GetString();
            Assert.True(status is "succeeded" or "failed", $"Unexpected status '{status}'.");
            Assert.True(entry.GetProperty("attempts").GetInt32() >= 1);

            if (status == "succeeded")
            {
                Assert.Equal(JsonValueKind.String, entry.GetProperty("result").ValueKind);
                Assert.Equal(JsonValueKind.Null, entry.GetProperty("error").ValueKind);
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, entry.GetProperty("result").ValueKind);
                Assert.Equal(JsonValueKind.String, entry.GetProperty("error").ValueKind);
                Assert.False(string.IsNullOrEmpty(entry.GetProperty("error").GetString()));
            }
        }
    }

    [Fact]
    public void ResultsFile_PreservesOrdinalFileNameOrder()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);
        Assert.Equal(0, result.ExitCode);

        var ids = LoadResults(output).Select(e => e.GetProperty("id").GetString()).ToArray();
        Assert.Equal(new[] { "alpha", "beta", "gamma", "delta", "epsilon", "zeta" }, ids);
    }

    [Fact]
    public void PlainSuccess_HasSingleAttemptAndUppercasedPayload()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        AppRunner.Run(_assembly, [], output, env);

        var alpha = IndexById(LoadResults(output))["alpha"];
        Assert.Equal("succeeded", alpha.GetProperty("status").GetString());
        Assert.Equal(1, alpha.GetProperty("attempts").GetInt32());
        Assert.Equal("HELLO WORLD", alpha.GetProperty("result").GetString());
    }

    [Fact]
    public void TransientFailures_SucceedOnAttemptAfterFailuresBeforeSuccess()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        AppRunner.Run(_assembly, [], output, env);

        var beta = IndexById(LoadResults(output))["beta"];
        Assert.Equal("succeeded", beta.GetProperty("status").GetString());
        Assert.Equal(3, beta.GetProperty("attempts").GetInt32());
        Assert.Equal("RETRY ME", beta.GetProperty("result").GetString());
    }

    [Fact]
    public void JobExceedingMaxAttempts_IsFailedWithAttemptsEqualToMaxAttempts()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        AppRunner.Run(_assembly, [], output, env);

        var gamma = IndexById(LoadResults(output))["gamma"];
        Assert.Equal("failed", gamma.GetProperty("status").GetString());
        Assert.Equal(3, gamma.GetProperty("attempts").GetInt32());
        Assert.Equal(JsonValueKind.Null, gamma.GetProperty("result").ValueKind);
    }

    [Fact]
    public void FatalJob_AlwaysFailsAndDoesNotStopTheWorker()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        var result = AppRunner.Run(_assembly, [], output, env);
        Assert.Equal(0, result.ExitCode);

        var indexed = IndexById(LoadResults(output));
        var delta = indexed["delta"];
        Assert.Equal("failed", delta.GetProperty("status").GetString());
        Assert.Equal(3, delta.GetProperty("attempts").GetInt32());
        Assert.True(indexed.ContainsKey("epsilon"), "Worker stopped instead of continuing after the fatal job.");
    }

    [Fact]
    public void NonAsciiPayload_IsUppercasedWithoutMangling()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        AppRunner.Run(_assembly, [], output, env);

        var epsilon = IndexById(LoadResults(output))["epsilon"];
        Assert.Equal("succeeded", epsilon.GetProperty("status").GetString());
        Assert.Equal("CAFÉ ☕ RÉSUMÉ", epsilon.GetProperty("result").GetString());
    }

    [Fact]
    public void EmptyPayload_ProducesEmptySuccessfulResult()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        AppRunner.Run(_assembly, [], output, env);

        var zeta = IndexById(LoadResults(output))["zeta"];
        Assert.Equal("succeeded", zeta.GetProperty("status").GetString());
        Assert.Equal(1, zeta.GetProperty("attempts").GetInt32());
        Assert.Equal(string.Empty, zeta.GetProperty("result").GetString());
    }

    // ---------- Log output ----------

    [Fact]
    public void PerJobLogLines_MatchTheContract()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
        };
        var result = AppRunner.Run(_assembly, [], output, env);

        var lines = result.OutputLines;
        Assert.Contains("Job alpha succeeded after 1 attempt(s)", lines);
        Assert.Contains("Job beta succeeded after 3 attempt(s)", lines);
        Assert.Contains("Job gamma failed after 3 attempt(s)", lines);
        Assert.Contains("Job delta failed after 3 attempt(s)", lines);
        Assert.Contains("Job epsilon succeeded after 1 attempt(s)", lines);
        Assert.Contains("Job zeta succeeded after 1 attempt(s)", lines);
        Assert.Contains("Processed 6 job(s)", lines);
    }

    // ---------- Configuration overrides ----------

    [Fact]
    public void MaxAttemptsOverride_CanTurnFailedJobIntoSucceeded()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = ScenarioJobsDirectory,
            ["OUTPUT_DIRECTORY"] = output,
            ["MAX_ATTEMPTS"] = "6",
        };
        AppRunner.Run(_assembly, [], output, env);

        var indexed = IndexById(LoadResults(output));
        var gamma = indexed["gamma"];
        Assert.Equal("succeeded", gamma.GetProperty("status").GetString());
        Assert.Equal(6, gamma.GetProperty("attempts").GetInt32());
        Assert.Equal("GIVE UP", gamma.GetProperty("result").GetString());

        // Fatal jobs still fail with attempts == MAX_ATTEMPTS.
        var delta = indexed["delta"];
        Assert.Equal("failed", delta.GetProperty("status").GetString());
        Assert.Equal(6, delta.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public void RetryDelayOverride_IsHonoured()
    {
        var jobs = CreateJobsDirectory();
        WriteJob(jobs, "job-01.json", new { id = "wait", payload = "later", failuresBeforeSuccess = 2 });
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = output,
            ["MAX_ATTEMPTS"] = "3",
            ["RETRY_DELAY_MS"] = "400",
        };

        var stopwatch = Stopwatch.StartNew();
        var result = AppRunner.Run(_assembly, [], output, env);
        stopwatch.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.True(stopwatch.ElapsedMilliseconds >= 700,
            $"Retry delay was not honoured (elapsed {stopwatch.ElapsedMilliseconds} ms).");
        var entry = LoadResults(output).Single();
        Assert.Equal("succeeded", entry.GetProperty("status").GetString());
        Assert.Equal(3, entry.GetProperty("attempts").GetInt32());
    }

    // ---------- Empty / missing directories ----------

    [Fact]
    public void EmptyJobsDirectory_ProducesEmptyResultsAndSuccessExit()
    {
        var jobs = CreateJobsDirectory();
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Processed 0 job(s)", result.OutputLines);
        Assert.Equal("[]", File.ReadAllText(Path.Combine(output, "results.json")).Trim());
    }

    [Fact]
    public void MissingJobsDirectory_ReportsErrorAndExitsWithTwo()
    {
        var missing = Path.Combine(_outputRoot, "does-not-exist-" + Guid.NewGuid().ToString("N"));
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = missing,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"Error: jobs directory '{missing}' was not found.", result.StandardError);
    }

    [Fact]
    public void MissingJobsDirectoryEnvVar_ReportsErrorAndExitsWithTwo()
    {
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = string.Empty,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: JOBS_DIRECTORY is not set.", result.StandardError);
    }

    [Fact]
    public void MissingOutputDirectoryEnvVar_ReportsErrorAndExitsWithTwo()
    {
        var jobs = CreateJobsDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = string.Empty,
        };

        var result = AppRunner.Run(_assembly, [], jobs, env);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: OUTPUT_DIRECTORY is not set.", result.StandardError);
    }

    [Fact]
    public void InvalidMaxAttempts_IsRejected()
    {
        var jobs = CreateJobsDirectory();
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = output,
            ["MAX_ATTEMPTS"] = "0",
        };

        var result = AppRunner.Run(_assembly, [], output, env);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: MAX_ATTEMPTS must be a positive integer.", result.StandardError);
    }

    [Fact]
    public void NonJsonFilesInJobsDirectory_AreIgnored()
    {
        var jobs = CreateJobsDirectory();
        WriteJob(jobs, "job-01.json", new { id = "only-one", payload = "x" });
        File.WriteAllText(Path.Combine(jobs, "readme.txt"), "ignore me");
        File.WriteAllText(Path.Combine(jobs, "notes.md"), "ignore me too");
        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], output, env);

        Assert.Equal(0, result.ExitCode);
        var results = LoadResults(output);
        Assert.Single(results);
        Assert.Equal("only-one", results[0].GetProperty("id").GetString());
    }

    [Fact]
    public void OutputDirectory_IsCreatedIfMissing()
    {
        var jobs = CreateJobsDirectory();
        WriteJob(jobs, "job-01.json", new { id = "only-one", payload = "x" });
        var output = Path.Combine(_outputRoot, "nested", "deeper", Guid.NewGuid().ToString("N"));
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = output,
        };

        var result = AppRunner.Run(_assembly, [], jobs, env);

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(output));
        Assert.True(File.Exists(Path.Combine(output, "results.json")));
    }

    // ---------- Graceful shutdown ----------

    [Fact]
    public void GracefulShutdown_FlushesPartialResultsAndExitsZero()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return; // POSIX signal-driven test is only meaningful on Unix.
        }

        // Generate a batch of jobs that take long enough to allow a mid-run signal.
        var jobs = CreateJobsDirectory();
        for (var i = 0; i < 20; i++)
        {
            WriteJob(jobs, $"job-{i:D2}.json",
                new { id = $"slow-{i:D2}", payload = "z", failuresBeforeSuccess = 4 });
        }

        var output = NewOutputDirectory();
        var env = new Dictionary<string, string>
        {
            ["JOBS_DIRECTORY"] = jobs,
            ["OUTPUT_DIRECTORY"] = output,
            ["MAX_ATTEMPTS"] = "5",
            ["RETRY_DELAY_MS"] = "500",
        };

        var buffer = new StringBuilder();
        var process = AppRunner.Start(_assembly, [], output, env, buffer);
        try
        {
            // Wait for the worker to actually start doing work before signalling.
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000 && !File.Exists(Path.Combine(output, "results.json")))
            {
                Thread.Sleep(50);
            }

            Thread.Sleep(600); // let it get partway through a retry.

            using (var killer = Process.Start(new ProcessStartInfo
            {
                FileName = "kill",
                ArgumentList = { "-TERM", process.Id.ToString() },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!)
            {
                killer.WaitForExit(5000);
            }

            Assert.True(process.WaitForExit(10_000),
                $"Worker did not exit within 10 s of SIGTERM. Output so far:{Environment.NewLine}{buffer}");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            AppRunner.Kill(process);
        }

        var combined = buffer.ToString();
        Assert.Contains("Shutdown requested", combined);

        // results.json must exist, be valid JSON, and only contain completed jobs (no partial job).
        var results = LoadResults(output);
        foreach (var entry in results)
        {
            Assert.True(entry.GetProperty("attempts").GetInt32() >= 1);
        }

        // Sanity: we shut down before finishing all 20 jobs.
        Assert.True(results.Length < 20, "The worker somehow processed every job before shutdown.");
    }

    // ---------- Helpers ----------

    private string CreateJobsDirectory()
    {
        var path = Path.Combine(_outputRoot, "jobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteJob(string directory, string fileName, object job)
    {
        var json = JsonSerializer.Serialize(job);
        File.WriteAllText(Path.Combine(directory, fileName), json);
    }
}
