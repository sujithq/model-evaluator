using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Tests;

public sealed class CopilotTelemetryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));

    // Shape observed with CLI 1.0.87-0 and a local OpenAI-compatible mock provider.
    private const string Chat = """
        {"type":"span","traceId":"trace","spanId":"call1","attributes":{"gen_ai.operation.name":"chat","gen_ai.request.model":"test-model","gen_ai.usage.input_tokens":17,"gen_ai.usage.output_tokens":2,"github.copilot.cost":1,"github.copilot.server_duration":7}}
        """;
    private const string FinalUsage = """
        {"totalNanoAiu":2000000000,"modelMetrics":{"test-model":{"requests":{"count":3},"usage":{"inputTokens":100,"outputTokens":20}}}}
        """;
    private const string Checkpoint = """
        {"type":"session.usage_checkpoint","data":{"totalNanoAiu":250000000}}
        """;
    private const string Tool = """
        {"type":"span","traceId":"trace","spanId":"tool1","attributes":{"gen_ai.operation.name":"execute_tool"}}
        """;
    private const string CompletedRoot = """
        {"type":"span","traceId":"trace","spanId":"root","endTime":[123,0],"attributes":{"gen_ai.operation.name":"invoke_agent"}}
        """;

    public CopilotTelemetryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supplement_CountsUniqueToolsIncludingSubagentsWithoutReplacingFinalUsage(bool withTools)
    {
        var path = Path.Combine(_root, "telemetry.jsonl");
        var lines = new List<string> { Chat, CompletedRoot };
        if (withTools)
        {
            lines.AddRange([Tool, Tool, Tool.Replace("tool1", "child-tool"),
                """{"type":"metric","name":"github.copilot.tool.call.count","value":99}"""]);
        }
        File.WriteAllLines(path, lines);
        var final = UsageReportReader.Parse(FinalUsage, "copilot-cli");

        var merged = CopilotTelemetryReader.SupplementToolCalls(final, path, runnerCompleted: true);

        Assert.Equal(final with { ToolCalls = withTools ? 2 : 0 }, merged);
        Assert.False(merged.UsageIsPartial);
        Assert.Empty(merged.Warnings);
        Assert.DoesNotContain("toolCalls", merged.UnavailableMetrics);
        Assert.Contains("estimatedCostUsd", merged.UnavailableMetrics);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("truncated")]
    [InlineData("no-root")]
    [InlineData("other-trace")]
    [InlineData("interrupted")]
    public void Supplement_UnreliableTelemetryLeavesFinalUsageIntact(string mode)
    {
        var path = Path.Combine(_root, "telemetry.jsonl");
        var lines = new List<string> { Tool, CompletedRoot };
        if (mode == "empty") { lines.Clear(); }
        if (mode == "no-root") { lines.Remove(CompletedRoot); }
        if (mode == "truncated") { lines.Add("{truncated"); }
        if (mode == "other-trace") { lines.Add(Tool.Replace("\"trace\"", "\"unclosed-trace\"")); }
        if (mode != "missing") { File.WriteAllLines(path, lines); }
        var final = UsageReportReader.Parse(FinalUsage, "copilot-cli");

        var merged = CopilotTelemetryReader.SupplementToolCalls(final, path, runnerCompleted: mode != "interrupted");

        Assert.Null(merged.ToolCalls);
        Assert.Equal(final, merged with { Warnings = final.Warnings });
        Assert.Contains(merged.Warnings, warning => warning.Contains("toolCalls remains unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public void Supplement_PreservesAlreadyReportedToolCount()
    {
        var final = UsageReportReader.Parse(FinalUsage, "copilot-cli") with { ToolCalls = 7 };
        Assert.Equal(final, CopilotTelemetryReader.SupplementToolCalls(final, "missing.jsonl", runnerCompleted: true));
    }

    [Fact]
    public void Parse_DeduplicatesSpansAndIgnoresParentTotalsAndMetricSnapshots()
    {
        var usage = CopilotTelemetryReader.Parse([
            Chat, Chat,
            Chat.Replace("call1", "child-call"),
            """{"type":"span","traceId":"trace","spanId":"parent","attributes":{"gen_ai.operation.name":"invoke_agent","gen_ai.usage.input_tokens":999}}""",
            """{"type":"metric","attributes":{"gen_ai.operation.name":"chat","gen_ai.usage.input_tokens":999}}""",
            """{"type":"span","traceId":"trace","spanId":"tool1","attributes":{"gen_ai.operation.name":"execute_tool"}}""",
        ]);

        Assert.True(usage.UsageIsPartial);
        Assert.Equal(34, usage.InputTokens);
        Assert.Equal(4, usage.OutputTokens);
        Assert.Equal(2, usage.ApiRequests);
        Assert.Equal(1, usage.ToolCalls);
        Assert.Equal(2m, usage.PremiumRequests);
        Assert.Equal(0.014, usage.ApiDurationSeconds);
        Assert.Null(usage.AiCredits);
        Assert.Null(usage.EstimatedCostUsd);
        Assert.Equal(["test-model"], usage.ReportedModels);
    }

    [Fact]
    public void Parse_TruncatedTailRetainsEarlierSpansWithWarning()
    {
        var usage = CopilotTelemetryReader.Parse([Chat, """{"type":"span","attributes":"""]);
        Assert.Equal(17, usage.InputTokens);
        Assert.Contains(usage.Warnings, warning => warning.Contains("invalid/truncated", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Checkpoints_UseCumulativeHighWaterMarkWithoutAddingSubagents(bool repeat)
    {
        var lines = new List<string>
        {
            Checkpoint,
            Checkpoint.Replace("250000000", "500000000"),
            Checkpoint.Replace("\"data\"", "\"agentId\":\"child\",\"data\"").Replace("250000000", "9000000000"),
        };
        if (repeat)
        {
            lines.AddRange(lines.ToArray());
        }
        var usage = CopilotTelemetryReader.WithCheckpoints(CopilotTelemetryReader.Parse([Chat]), lines);
        Assert.True(usage.UsageIsPartial);
        Assert.Equal(0.5m, usage.AiCredits);
        Assert.Equal(17, usage.InputTokens);
        Assert.Equal(1m, usage.PremiumRequests);
    }

    [Fact]
    public void Checkpoints_MissingInvalidAndTruncatedRemainExplicit()
    {
        var partial = CopilotTelemetryReader.Parse([]);
        var missing = CopilotTelemetryReader.WithCheckpoints(partial, ["{}"]);
        Assert.Null(missing.AiCredits);
        Assert.Contains(missing.Warnings, warning => warning.Contains("No session AI-credit", StringComparison.Ordinal));
        var recovered = CopilotTelemetryReader.WithCheckpoints(partial,
            [Checkpoint, Checkpoint.Replace("250000000", "-1"), "{truncated"]);
        Assert.Equal(0.25m, recovered.AiCredits);
        Assert.Contains(recovered.Warnings, warning => warning.Contains("invalid or truncated", StringComparison.Ordinal));
    }

    [Fact]
    public void Checkpoints_SaveOnlyUsageRecordsNotConversationContent()
    {
        var session = Path.Combine(_root, "events.jsonl");
        var artifact = Path.Combine(_root, "checkpoints.jsonl");
        File.WriteAllLines(session, [
            """{"type":"user.message","data":{"content":"private-prompt"}}""", Checkpoint]);
        var usage = CopilotTelemetryReader.ReadCheckpoints(CopilotTelemetryReader.Parse([]), session, artifact);
        Assert.Equal(0.25m, usage.AiCredits);
        Assert.Equal(Checkpoint, Assert.Single(File.ReadAllLines(artifact)));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("9223372036854775808")]
    [InlineData("\"invalid\"")]
    public void Parse_InvalidTokenCountDoesNotBecomeZero(string value)
    {
        var usage = CopilotTelemetryReader.Parse([Chat.Replace(":17", ":" + value)]);
        Assert.Null(usage.InputTokens);
        Assert.Equal(2, usage.OutputTokens);
        Assert.Contains(usage.Warnings, warning => warning.Contains("invalid/truncated", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MissingFieldOnOneCallDoesNotProduceUnderstatedTotal()
    {
        var usage = CopilotTelemetryReader.Parse([Chat, Chat.Replace("call1", "call2")
            .Replace(",\"gen_ai.usage.input_tokens\":17", "")]);
        Assert.Null(usage.InputTokens);
        Assert.Equal(4, usage.OutputTokens);
        Assert.Equal(2, usage.ApiRequests);
    }

    [Fact]
    public void Read_MissingFileAndUnknownSchemaAreExplicit()
    {
        var missing = CopilotTelemetryReader.Read(Path.Combine(_root, "missing.jsonl"));
        Assert.True(missing.UsageIsPartial);
        Assert.Null(missing.ApiRequests);
        Assert.Contains(missing.Warnings, warning => warning.Contains("could not be read", StringComparison.Ordinal));
        var unknown = CopilotTelemetryReader.Parse(["{}", "null"]);
        Assert.Null(unknown.ToolCalls);
        Assert.Null(unknown.InputTokens);
        Assert.Contains(unknown.Warnings, warning => warning.Contains("No completed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("complete")]
    [InlineData("malformed")]
    [InlineData("selected")]
    public async Task Adapter_PreservesIncrementalUsageAndPrefersFinalExport(string mode)
    {
        var telemetry = string.Join('\n', Chat, Tool, Tool, CompletedRoot);
        var script = OperatingSystem.IsWindows()
            ? "$session = Join-Path $env:COPILOT_HOME ('session-state\\' + $args[-1]); " +
              "[IO.Directory]::CreateDirectory($session) | Out-Null; " +
              $"[IO.File]::WriteAllText((Join-Path $session 'events.jsonl'), '{Checkpoint}'); " +
              $"[IO.File]::WriteAllText($env:COPILOT_OTEL_FILE_EXPORTER_PATH, '{telemetry}'); " +
              "if ($env:COPILOT_OTEL_EXPORTER_TYPE -ne 'file' -or " +
              "$env:OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT -ne 'false') { exit 7 }; " +
              (mode is "complete" or "malformed"
                  ? $"[IO.File]::WriteAllText($env:EVAL_USAGE_FILE, '{(mode == "complete" ? FinalUsage : "{bad")}')"
                  : mode == "selected" ? "" : "[Console]::WriteLine('ready'); Start-Sleep -Seconds 60")
            : "mkdir -p \"$COPILOT_HOME/session-state/$2\"; " +
              $"printf '%s\\n' '{Checkpoint}' > \"$COPILOT_HOME/session-state/$2/events.jsonl\"; " +
              $"printf '%s\\n' '{telemetry}' > \"$COPILOT_OTEL_FILE_EXPORTER_PATH\"; " +
              "[ \"$COPILOT_OTEL_EXPORTER_TYPE\" = file ] || exit 7; " +
              "[ \"$OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT\" = false ] || exit 7; " +
              (mode is "complete" or "malformed"
                  ? $"printf '%s' '{(mode == "complete" ? FinalUsage : "{bad")}' > \"$EVAL_USAGE_FILE\""
                  : mode == "selected" ? "" : "echo ready; exec sleep 60");
        var scriptPath = Path.Combine(_root, OperatingSystem.IsWindows() ? "runner.ps1" : "runner.sh");
        await File.WriteAllTextAsync(scriptPath, script);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new ModelAttemptContext
        {
            Scenario = new ScenarioPackage(new ScenarioDefinition
            {
                Id = "test",
                Name = "Test",
                ProjectType = "Console",
                BenchmarkVersion = "1",
            }, _root, "prompt"),
            Model = new ModelConfiguration
            {
                Id = "test",
                Adapter = CommandLineAdapter.AdapterKey,
                Settings = new Dictionary<string, string>
                {
                    ["command"] = OperatingSystem.IsWindows() ? "powershell.exe" : "sh",
                    ["arguments"] = (OperatingSystem.IsWindows()
                        ? $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\""
                        : $"\"{scriptPath}\"") + (mode == "selected" ? " --session-id existing" : ""),
                    ["usageFormat"] = "copilot-cli",
                },
                Environment = new Dictionary<string, string>
                {
                    ["COPILOT_HOME"] = Path.Combine(_root, "home"),
                    ["COPILOT_OTEL_EXPORTER_TYPE"] = "otlp-http",
                    ["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"] = "true",
                },
            },
            WorkspacePath = _root,
            ArtifactsPath = _root,
            Prompt = "prompt",
            PromptFilePath = Path.Combine(_root, "prompt.md"),
            Repetition = 1,
            Timeout = TimeSpan.FromSeconds(mode == "timeout" ? 10 : 60),
            OnOutput = line => { if (line == "ready") { ready.TrySetResult(); } },
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var task = new CommandLineAdapter(new ProcessRunner()).GenerateAsync(context, cancellation.Token);
        if (mode is "timeout" or "cancel")
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (mode == "cancel")
            {
                cancellation.Cancel();
            }
        }
        var output = await task;
        Assert.Equal(mode == "timeout", output.TimedOut);
        Assert.Equal(mode == "cancel", output.InfrastructureFailure);
        Assert.Equal(mode != "complete", output.UsageIsPartial);
        Assert.Equal(mode == "complete" ? 100 : 17, output.InputTokens);
        Assert.Equal(mode == "complete" ? 2m : mode == "selected" ? (decimal?)null : 0.25m, output.AiCredits);
        Assert.Equal(mode != "complete", File.Exists(Path.Combine(_root, "usage.partial.json")));
        Assert.True(File.Exists(Path.Combine(_root, "usage.telemetry.jsonl")));
        Assert.Equal(1, output.ToolCalls);
        Assert.DoesNotContain("toolCalls", output.UnavailableMetrics);
        if (mode == "complete")
        {
            Assert.Empty(output.UsageWarnings);
            Assert.Equal(FinalUsage, File.ReadAllText(Path.Combine(_root, "usage.json")));
        }
        if (mode != "complete")
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "usage.partial.json")));
            Assert.True(json.RootElement.GetProperty("usageIsPartial").GetBoolean());
            Assert.Contains(output.UsageWarnings, warning => warning.Contains("partial", StringComparison.Ordinal));
            if (mode == "selected")
            {
                Assert.Contains(output.UsageWarnings, warning => warning.Contains("skipped", StringComparison.Ordinal));
                Assert.False(File.Exists(Path.Combine(_root, "usage.checkpoints.jsonl")));
            }
        }
    }
}
