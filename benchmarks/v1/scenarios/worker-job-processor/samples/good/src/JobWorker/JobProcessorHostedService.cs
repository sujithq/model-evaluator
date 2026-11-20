using Microsoft.Extensions.Hosting;

namespace JobWorker;

/// <summary>
/// Hosted service that drives the <see cref="JobProcessor"/> and stops the host once processing is done
/// (either because every job finished or because the host received a shutdown signal).
/// </summary>
public sealed class JobProcessorHostedService(JobProcessor processor, IHostApplicationLifetime lifetime)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await processor.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown - the processor has already flushed its results.
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}
