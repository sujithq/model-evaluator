using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ModelEvaluator.Durable;

public static class DurableHost
{
    public static IHost BuildWorker(string[]? args = null, string? connectionString = null)
    {
        var builder = Host.CreateApplicationBuilder(args ?? []);
        builder.Services.AddDurableTaskWorker()
            .AddTasks(tasks =>
            {
                tasks.AddOrchestrator<EvaluationOrchestration>();
                tasks.AddActivity<PrepareEvaluationActivity>();
                tasks.AddActivity<RunEvaluationAttemptActivity>();
                tasks.AddActivity<FinalizeEvaluationActivity>();
            })
            .UseDurableTaskScheduler(connectionString ?? DurableSchedulerConnection.FromEnvironment());
        return builder.Build();
    }
}
