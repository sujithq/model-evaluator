namespace ModelEvaluator.Durable;

internal static class DurableSchedulerConnection
{
    public static string FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("DURABLE_TASK_CONNECTION");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var endpoint = Environment.GetEnvironmentVariable("ENDPOINT") ?? "http://localhost:8080";
        var taskHub = Environment.GetEnvironmentVariable("TASKHUB") ?? "default";
        return endpoint.Contains("Authentication=", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : $"Endpoint={endpoint};TaskHub={taskHub};Authentication=None";
    }
}
