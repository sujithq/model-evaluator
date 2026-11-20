using JobWorker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var configuration = WorkerConfiguration.FromEnvironment(Environment.GetEnvironmentVariables());
if (configuration.Error is not null)
{
    await Console.Error.WriteLineAsync(configuration.Error).ConfigureAwait(false);
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.None);

builder.Services.AddSingleton(configuration.Options!);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IJobLog>(new ConsoleJobLog(Console.Out));
builder.Services.AddSingleton<JobProcessor>();
builder.Services.AddHostedService<JobProcessorHostedService>();

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
return 0;
