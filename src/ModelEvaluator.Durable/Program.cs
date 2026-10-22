using Grpc.Core;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ModelEvaluator.Durable;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "worker" => await RunWorkerAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "submit" => await SubmitAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "status" => await StatusAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
                "-h" or "--help" or "help" or null => PrintUsage(),
                var command => Unknown(command),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("error: operation cancelled or timed out.");
            return 130;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                                       or KeyNotFoundException or InvalidDataException
                                       or InvalidOperationException or FormatException
                                       or TimeoutException or RpcException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunWorkerAsync(string[] args)
    {
        if (args.Length > 0)
        {
            throw new FormatException($"Unknown worker option '{args[0]}'.");
        }

        var connectionString = DurableSchedulerConnection.FromEnvironment();
        Console.WriteLine($"Starting Durable Task worker for {DescribeConnection(connectionString)}");
        await DurableHost.BuildWorker(args, connectionString).RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> SubmitAsync(string[] args)
    {
        var detach = args.Contains("--detach", StringComparer.Ordinal);
        var evaluationArguments = args.Where(argument => argument != "--detach").ToArray();
        if (evaluationArguments.Length == 0)
        {
            throw new FormatException(
                "The submit command requires evaluator options such as '--preset smoke-reference'.");
        }

        await using var provider = CreateClientProvider();
        var client = provider.GetRequiredService<DurableTaskClient>();
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(EvaluationOrchestration),
            new DurableEvaluationRequest
            {
                EvaluationArguments = evaluationArguments,
                WorkingDirectory = Environment.CurrentDirectory,
            }).ConfigureAwait(false);
        Console.WriteLine($"Scheduled orchestration: {instanceId}");

        if (detach)
        {
            Console.WriteLine($"Check later with: dotnet run --project .\\src\\ModelEvaluator.Durable -- status {instanceId}");
            return 0;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromHours(24));
        var metadata = await client.WaitForInstanceCompletionAsync(
            instanceId,
            getInputsAndOutputs: true,
            cancellation.Token).ConfigureAwait(false);
        return PrintMetadata(metadata);
    }

    private static async Task<int> StatusAsync(string[] args)
    {
        if (args.Length != 1)
        {
            throw new FormatException("Usage: status <instance-id>");
        }

        await using var provider = CreateClientProvider();
        var client = provider.GetRequiredService<DurableTaskClient>();
        var metadata = await client.GetInstanceAsync(
            args[0],
            getInputsAndOutputs: true).ConfigureAwait(false);
        if (metadata is null)
        {
            Console.Error.WriteLine($"error: orchestration '{args[0]}' was not found.");
            return 4;
        }

        return PrintMetadata(metadata);
    }

    private static ServiceProvider CreateClientProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDurableTaskClient(options =>
            options.UseDurableTaskScheduler(DurableSchedulerConnection.FromEnvironment()));
        return services.BuildServiceProvider();
    }

    private static int PrintMetadata(OrchestrationMetadata metadata)
    {
        Console.WriteLine($"Instance: {metadata.InstanceId}");
        Console.WriteLine($"Status:   {metadata.RuntimeStatus}");
        if (metadata.RuntimeStatus == OrchestrationRuntimeStatus.Completed)
        {
            var result = metadata.ReadOutputAs<DurableEvaluationResult>();
            if (result is not null)
            {
                Console.WriteLine($"Run:      {result.RunId}");
                Console.WriteLine($"Attempts: {result.SuccessfulAttempts}/{result.CompletedAttempts} successful");
                Console.WriteLine($"JSON:     {result.JsonReportPath}");
                Console.WriteLine($"Markdown: {result.MarkdownReportPath}");
            }

            return 0;
        }

        if (metadata.RuntimeStatus == OrchestrationRuntimeStatus.Failed)
        {
            Console.Error.WriteLine($"Failure: {metadata.FailureDetails?.ErrorMessage}");
            return 3;
        }

        return 1;
    }

    private static string DescribeConnection(string connectionString) =>
        string.Join(';', connectionString.Split(';').Where(part =>
            !part.StartsWith("Authentication=", StringComparison.OrdinalIgnoreCase)));

    private static int PrintUsage()
    {
        Console.WriteLine(
            """
            model-evaluator durable host

            Environment:
              DURABLE_TASK_CONNECTION  Full scheduler connection string.
              ENDPOINT                 Scheduler endpoint (default http://localhost:8080).
              TASKHUB                  Task hub (default default).

            Commands:
              worker
                  Start a worker connected to the Durable Task Scheduler.

              submit [--detach] <model-evaluator options>
                  Schedule an evaluation. Evaluator options are the same as the existing CLI,
                  for example: submit --preset smoke-reference --repetitions 3

              status <instance-id>
                  Query a previously submitted orchestration.
            """);
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        PrintUsage();
        return 2;
    }
}
