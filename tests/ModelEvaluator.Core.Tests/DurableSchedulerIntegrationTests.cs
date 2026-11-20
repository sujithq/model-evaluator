using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using ModelEvaluator.Durable;

namespace ModelEvaluator.Core.Tests;

public sealed class DurableSchedulerIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalEmulator_RunsReferenceEvaluationAndPersistsReports()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("RUN_DURABLE_EMULATOR_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        const string connection =
            "Endpoint=http://localhost:8080;TaskHub=default;Authentication=None";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        using var worker = DurableHost.BuildWorker(connectionString: connection);
        await worker.StartAsync(cancellation.Token);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDurableTaskClient(options => options.UseDurableTaskScheduler(connection));
            await using var provider = services.BuildServiceProvider();
            var client = provider.GetRequiredService<DurableTaskClient>();
            var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
                nameof(EvaluationOrchestration),
                new DurableEvaluationRequest
                {
                    EvaluationArguments =
                    [
                        "--preset", "smoke-reference",
                        "--repetitions", "2",
                        "--max-parallel", "2",
                    ],
                    WorkingDirectory = RepositoryLocator.Root,
                },
                cancellation.Token);

            var metadata = await client.WaitForInstanceCompletionAsync(
                instanceId,
                getInputsAndOutputs: true,
                cancellation.Token);
            var result = metadata.ReadOutputAs<DurableEvaluationResult>();

            Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
            Assert.NotNull(result);
            Assert.Equal(4, result.PlannedAttempts);
            Assert.Equal(4, result.CompletedAttempts);
            Assert.True(File.Exists(result.JsonReportPath));
            Assert.True(File.Exists(result.MarkdownReportPath));
            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonReportPath));
            var attempts = report.RootElement.GetProperty("attempts").EnumerateArray().ToList();
            Assert.Equal(4, attempts.Count);
            Assert.Equal(
                [1, 2, 1, 2],
                attempts.Select(attempt => attempt.GetProperty("repetition").GetInt32()));
            Assert.All(
                attempts,
                attempt => Assert.EndsWith(
                    $"__{result.RunId}",
                    attempt.GetProperty("attemptId").GetString()));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
