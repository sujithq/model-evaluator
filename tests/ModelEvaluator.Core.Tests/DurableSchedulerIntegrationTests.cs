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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
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
                    EvaluationArguments = ["--preset", "smoke-reference"],
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
            Assert.Equal(2, result.CompletedAttempts);
            Assert.True(File.Exists(result.JsonReportPath));
            Assert.True(File.Exists(result.MarkdownReportPath));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
