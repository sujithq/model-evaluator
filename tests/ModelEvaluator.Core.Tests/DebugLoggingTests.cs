using System.Collections.Concurrent;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Tests;

public sealed class DebugLoggingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));

    public DebugLoggingTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Evaluation_DebugControlsDetailedAndLiveOutput(bool debug)
    {
        var messages = new ConcurrentQueue<string>();
        var adapter = new EmptySolutionAdapter();
        var configuration = new EvaluationConfiguration
        {
            BenchmarkRoot = RepositoryLocator.BenchmarkRoot,
            OutputDirectory = Path.Combine(_root, "artifacts"),
            WorkspaceRoot = Path.Combine(_root, "workspaces"),
            Scenarios = ["console-task-cli"],
            Models =
            [
                new ModelConfiguration
                {
                    Id = "test-model",
                    Adapter = adapter.Key,
                    Settings = new Dictionary<string, string> { ["arguments"] = "private-provider-argument" },
                    Environment = new Dictionary<string, string> { ["API_KEY"] = "private-environment-value" },
                },
            ],
            Repetitions = 1,
            Debug = debug,
        };

        var runner = new EvaluationRunner(new ModelAdapterFactory([adapter]), log: messages.Enqueue);
        var report = await runner.RunAsync(configuration);

        var attempt = Assert.Single(report.Attempts);
        Assert.Equal(AttemptOutcome.ModelFailure, attempt.Outcome);
        Assert.Equal(2m, attempt.Efficiency.AiCredits);
        Assert.Equal(100, attempt.Efficiency.InputTokens);
        Assert.Equal(5, attempt.Efficiency.ApiRequests);
        Assert.Equal(["reported-test-model"], attempt.Efficiency.ReportedModels);
        Assert.Contains("test usage warning", attempt.Efficiency.UsageWarnings);
        Assert.Contains(messages, m => m.Contains("warning: test usage warning", StringComparison.Ordinal));
        Assert.Contains(attempt.Checks, c => c.Id == "build.restore" && c.Status == CheckStatus.Failed);
        Assert.NotNull(attempt.ArtifactsPath);
        Assert.True(File.Exists(Path.Combine(attempt.ArtifactsPath, "result.json")));
        Assert.True(File.Exists(Path.Combine(attempt.ArtifactsPath, "commands", "restore.log")));
        Assert.Empty(Directory.EnumerateDirectories(configuration.WorkspaceRoot));
        var scenarioDetailsPath = Path.Combine(
            configuration.OutputDirectory,
            report.RunId,
            ScenarioDetailsMarkdownWriter.FileName);
        Assert.Equal(debug, File.Exists(scenarioDetailsPath));
        Assert.Equal(debug, adapter.HadOutputObserver);
        Assert.Contains(messages, m => m.Contains("outcome: ModelFailure", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("private-provider-argument", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, m => m.Contains("private-environment-value", StringComparison.Ordinal));

        if (debug)
        {
            Assert.Contains(messages, m => m.StartsWith("[debug] Matrix:", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("Workspace: ", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("Budgets: generation=", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("[generation] adapter progress", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("[restore] Starting dotnet restore", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("[restore] Completed: exit=", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("[restore] ", StringComparison.Ordinal)
                                           && m.Contains("error", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(messages, m => m.Contains("build.compile: Skipped", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("Removed temporary workspace:", StringComparison.Ordinal));
            Assert.Contains(messages, m => m.Contains("Scenario details:", StringComparison.Ordinal));
            var scenarioDetails = await File.ReadAllTextAsync(scenarioDetailsPath);
            var scenario = ScenarioCatalog.Load(configuration.BenchmarkRoot).Get("console-task-cli");
            Assert.Contains("## `console-task-cli`", scenarioDetails, StringComparison.Ordinal);
            Assert.Contains("### Resolved prompt", scenarioDetails, StringComparison.Ordinal);
            Assert.Contains(scenario.ResolvedPrompt, scenarioDetails, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain(messages, m => m.StartsWith("[debug]", StringComparison.Ordinal));
            Assert.DoesNotContain(messages, m => m.Contains("adapter progress", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandLineAdapter_PreservesTranscriptWithOptionalLiveOutput(bool observeOutput)
    {
        var messages = new ConcurrentQueue<string>();
        var scenario = new ScenarioPackage(
            new ScenarioDefinition
            {
                Id = "test",
                Name = "Test",
                ProjectType = "Console",
                BenchmarkVersion = "1.0.0",
            },
            _root,
            "prompt");
        var context = new ModelAttemptContext
        {
            Scenario = scenario,
            Model = new ModelConfiguration
            {
                Id = "test",
                Adapter = CommandLineAdapter.AdapterKey,
                Settings = new Dictionary<string, string>
                {
                    ["command"] = "dotnet",
                    ["arguments"] = "--version",
                },
            },
            WorkspacePath = _root,
            ArtifactsPath = _root,
            Prompt = "prompt",
            PromptFilePath = Path.Combine(_root, "prompt.md"),
            Repetition = 1,
            Timeout = TimeSpan.FromSeconds(30),
            OnOutput = observeOutput ? messages.Enqueue : null,
        };

        var output = await new CommandLineAdapter(new ProcessRunner()).GenerateAsync(context, CancellationToken.None);

        Assert.True(output.Succeeded, output.FailureReason);
        var transcript = await File.ReadAllLinesAsync(Path.Combine(_root, "transcript.log"));
        Assert.NotEmpty(transcript);
        if (observeOutput)
        {
            Assert.Equal(transcript, messages.ToArray());
        }
        else
        {
            Assert.Empty(messages);
        }
    }

    private sealed class EmptySolutionAdapter : IModelAdapter
    {
        public string Key => "empty-solution";

        public bool HadOutputObserver { get; private set; }

        public Task<ModelAttemptOutput> GenerateAsync(ModelAttemptContext context, CancellationToken cancellationToken)
        {
            HadOutputObserver = context.OnOutput is not null;
            context.OnOutput?.Invoke("adapter progress");
            return Task.FromResult(new ModelAttemptOutput
            {
                Succeeded = true,
                Runner = new RunnerInfo { Name = Key, Version = "test" },
                AiCredits = 2m,
                InputTokens = 100,
                ApiRequests = 5,
                ReportedModels = ["reported-test-model"],
                UsageWarnings = ["test usage warning"],
            });
        }
    }
}
