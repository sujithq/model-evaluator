using System.Collections.Concurrent;
using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Tests;

public sealed class ParallelEvaluationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));

    public ParallelEvaluationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Run_OverlapsAttemptsWithinLimitAndKeepsStableOrder(int maxParallel)
    {
        var adapter = new GatedAdapter(maxParallel);
        var messages = new List<string>();
        var config = Configuration(maxParallel);
        var runner = new EvaluationRunner(new ModelAdapterFactory([adapter]), log: messages.Add);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var task = runner.RunAsync(config, cancellation.Token);
        try
        {
            await adapter.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(maxParallel, adapter.Active);
            Assert.Equal(maxParallel, adapter.Started);
        }
        finally
        {
            adapter.Release.TrySetResult();
        }

        var report = await task;
        Assert.False(report.Cancelled);
        Assert.Equal(6, report.PlannedAttempts);
        Assert.Equal(0, report.NotStartedAttempts);
        Assert.Equal(maxParallel, report.Environment.MaxParallel);
        Assert.Equal(maxParallel, adapter.Peak);
        Assert.Equal(0, adapter.Active);
        Assert.Equal(
            ["one:1", "one:2", "one:3", "two:1", "two:2", "two:3"],
            report.Attempts.Select(a => $"{a.ModelId}:{a.Repetition}"));
        Assert.Equal(6, report.Attempts.Select(a => a.AttemptId).Distinct().Count());
        Assert.Equal(6, adapter.Workspaces.Distinct().Count());
        Assert.Single(report.Attempts, a => a.Outcome == AttemptOutcome.InfrastructureFailure);
        Assert.Equal(5, report.Attempts.Count(a => a.Outcome == AttemptOutcome.ModelFailure));
        foreach (var attempt in report.Attempts)
        {
            Assert.NotNull(attempt.ArtifactsPath);
            Assert.True(File.Exists(Path.Combine(attempt.ArtifactsPath, "result.json")));
            Assert.Equal($"{attempt.ModelId}:{attempt.Repetition}",
                File.ReadAllText(Path.Combine(attempt.ArtifactsPath, "generated", "marker.txt")));
            Assert.Equal(maxParallel, attempt.Environment.MaxParallel);
        }

        Assert.Empty(Directory.EnumerateDirectories(config.WorkspaceRoot!));
        var liveOutput = messages.Where(m => m.Contains("[generation] marker", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, liveOutput.Count);
        Assert.All(liveOutput, m => Assert.StartsWith("[debug] [attempt ", m));
        Assert.Equal(maxParallel > 1, messages.Any(m => m.Contains("resource contention", StringComparison.Ordinal)));
        using var json = JsonDocument.Parse(File.ReadAllText(JsonReportWriter.Write(report, Path.Combine(_root, "reports"))));
        Assert.Equal(maxParallel, json.RootElement.GetProperty("environment").GetProperty("maxParallel").GetInt32());
        Assert.Equal(6, json.RootElement.GetProperty("plannedAttempts").GetInt32());
        Assert.Contains($"Maximum parallel attempts: {maxParallel}", MarkdownReportWriter.Render(report));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_StopsQueuedAttemptsAndPreservesActiveResults(bool keepWorkspaces)
    {
        var adapter = new GatedAdapter(2);
        var config = Configuration(2) with { KeepWorkspaces = keepWorkspaces };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var task = new EvaluationRunner(new ModelAdapterFactory([adapter]), log: _ => { })
            .RunAsync(config, cancellation.Token);
        await adapter.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cancellation.Cancel();
        var report = await task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(report.Cancelled);
        Assert.Equal(2, adapter.Started);
        Assert.Equal(0, adapter.Active);
        Assert.Equal(2, report.Attempts.Count);
        Assert.Equal(4, report.NotStartedAttempts);
        Assert.All(report.Attempts, attempt =>
        {
            Assert.Equal(AttemptOutcome.InfrastructureFailure, attempt.Outcome);
            Assert.Equal("The evaluation run was cancelled.", attempt.FailureReason);
            Assert.NotNull(attempt.ArtifactsPath);
            Assert.True(File.Exists(Path.Combine(attempt.ArtifactsPath, "result.json")));
            Assert.True(File.Exists(Path.Combine(attempt.ArtifactsPath, "generated", "marker.txt")));
        });
        Assert.All(adapter.Workspaces, workspace => Assert.Equal(keepWorkspaces, Directory.Exists(workspace)));
        Assert.Contains("Cancelled run: partial results", MarkdownReportWriter.Render(report));
        using var json = JsonDocument.Parse(File.ReadAllText(JsonReportWriter.Write(report, Path.Combine(_root, "reports"))));
        Assert.True(json.RootElement.GetProperty("cancelled").GetBoolean());
        Assert.Equal(4, json.RootElement.GetProperty("notStartedAttempts").GetInt32());
    }

    [Fact]
    public async Task Run_PreservesOrderWhenSecondAttemptFinishesFirst()
    {
        var adapter = new GatedAdapter(2, completeSecondFirst: true);
        var completed = new List<int>();
        var config = Configuration(2) with
        {
            Models = [new ModelConfiguration { Id = "one", Adapter = adapter.Key }],
            Repetitions = 2,
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var task = new EvaluationRunner(new ModelAdapterFactory([adapter]), log: message =>
        {
            if (message.Contains("] outcome:", StringComparison.Ordinal))
            {
                var repetition = message.Contains("__rep02__", StringComparison.Ordinal) ? 2 : 1;
                completed.Add(repetition);
                if (repetition == 2)
                {
                    adapter.SecondCompleted.TrySetResult();
                }
            }
        }).RunAsync(config, cancellation.Token);
        try
        {
            await adapter.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            adapter.Release.TrySetResult();
        }

        var report = await task;
        Assert.Equal([2, 1], completed);
        Assert.Equal([1, 2], report.Attempts.Select(attempt => attempt.Repetition));
    }

    [Fact]
    public async Task Run_WithAlreadyCancelledTokenStartsNothing()
    {
        var adapter = new GatedAdapter(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EvaluationRunner(new ModelAdapterFactory([adapter]), log: _ => { })
                .RunAsync(Configuration(2), new CancellationToken(true)));
        Assert.Equal(0, adapter.Started);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Run_RejectsInvalidConfiguredLimit(int limit) =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EvaluationRunner().RunAsync(Configuration(limit)));

    [Fact]
    public async Task Run_CollidingSanitizedModelIdsStillHaveUniqueWorkspaces()
    {
        var adapter = new GatedAdapter(2);
        var config = Configuration(2) with
        {
            Repetitions = 1,
            Models =
            [
                new ModelConfiguration { Id = "same/id", Adapter = adapter.Key },
                new ModelConfiguration { Id = "same?id", Adapter = adapter.Key },
            ],
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var task = new EvaluationRunner(new ModelAdapterFactory([adapter]), log: _ => { }).RunAsync(config, cancellation.Token);
        await adapter.Ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        adapter.Release.TrySetResult();
        var report = await task;
        Assert.Equal(2, report.Attempts.Select(a => a.AttemptId).Distinct().Count());
        Assert.Equal(2, adapter.Workspaces.Distinct().Count());
    }

    private EvaluationConfiguration Configuration(int maxParallel) => new()
    {
        BenchmarkRoot = RepositoryLocator.BenchmarkRoot,
        OutputDirectory = Path.Combine(_root, "artifacts"),
        WorkspaceRoot = Path.Combine(_root, "workspaces"),
        Scenarios = ["console-task-cli"],
        Models =
        [
            new ModelConfiguration { Id = "one", Adapter = "gated" },
            new ModelConfiguration { Id = "two", Adapter = "gated" },
        ],
        Repetitions = 3,
        MaxParallel = maxParallel,
        Debug = true,
    };

    private sealed class GatedAdapter(int initialWave, bool completeSecondFirst = false) : IModelAdapter
    {
        private int _active;
        private int _started;
        private int _prepared;
        private int _peak;
        private readonly object _gate = new();

        public string Key => "gated";

        public int Active => Volatile.Read(ref _active);

        public int Started => Volatile.Read(ref _started);

        public int Peak => Volatile.Read(ref _peak);

        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<string> Workspaces { get; } = new();

        public async Task<ModelAttemptOutput> GenerateAsync(ModelAttemptContext context, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            Interlocked.Increment(ref _started);
            lock (_gate)
            {
                _peak = Math.Max(_peak, active);
            }

            try
            {
                Workspaces.Enqueue(context.WorkspacePath);
                await File.WriteAllTextAsync(Path.Combine(context.WorkspacePath, "marker.txt"),
                    $"{context.Model.Id}:{context.Repetition}", cancellationToken);
                context.OnOutput?.Invoke("marker");
                if (Interlocked.Increment(ref _prepared) == initialWave)
                {
                    Ready.TrySetResult();
                }

                await Release.Task.WaitAsync(cancellationToken);
                if (completeSecondFirst && context.Repetition == 1)
                {
                    await SecondCompleted.Task.WaitAsync(cancellationToken);
                }

                if (context.Model.Id == "one" && context.Repetition == 1)
                {
                    throw new InvalidOperationException("Deliberate adapter failure.");
                }

                return new ModelAttemptOutput
                {
                    Succeeded = false,
                    FailureReason = "Deliberate generation failure.",
                    Runner = new RunnerInfo { Name = Key, Version = "test" },
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
