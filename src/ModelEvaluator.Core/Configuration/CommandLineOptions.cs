using System.Globalization;

namespace ModelEvaluator.Core.Configuration;

/// <summary>Parsed CLI options that override the evaluation configuration file.</summary>
public sealed record CommandLineOptions
{
    public string? ConfigPath { get; init; }

    public string? BenchmarkRoot { get; init; }

    public string? OutputDirectory { get; init; }

    public string? WorkspaceRoot { get; init; }

    public string? ExecutionImage { get; init; }

    public IReadOnlyList<string> Models { get; init; } = [];

    public IReadOnlyList<string> Scenarios { get; init; } = [];

    public int? Repetitions { get; init; }

    public int? GenerationTimeoutSeconds { get; init; }

    public int? BuildTimeoutSeconds { get; init; }

    public int? TestTimeoutSeconds { get; init; }

    public int? AcceptanceTimeoutSeconds { get; init; }

    public bool KeepWorkspaces { get; init; }

    public static CommandLineOptions Parse(IEnumerable<string> arguments)
    {
        var options = new CommandLineOptions();
        var queue = new Queue<string>(arguments);

        while (queue.Count > 0)
        {
            var argument = queue.Dequeue();
            switch (argument)
            {
                case "--config":
                    options = options with { ConfigPath = Next(queue, argument) };
                    break;
                case "--benchmark-root":
                    options = options with { BenchmarkRoot = Path.GetFullPath(Next(queue, argument)) };
                    break;
                case "--output":
                    options = options with { OutputDirectory = Path.GetFullPath(Next(queue, argument)) };
                    break;
                case "--workspace-root":
                    options = options with { WorkspaceRoot = Path.GetFullPath(Next(queue, argument)) };
                    break;
                case "--execution-image":
                    options = options with { ExecutionImage = Next(queue, argument) };
                    break;
                case "--models":
                    options = options with { Models = SplitList(Next(queue, argument)) };
                    break;
                case "--scenarios":
                    options = options with { Scenarios = SplitList(Next(queue, argument)) };
                    break;
                case "--repetitions":
                    options = options with { Repetitions = Int(queue, argument) };
                    break;
                case "--generation-timeout":
                    options = options with { GenerationTimeoutSeconds = Int(queue, argument) };
                    break;
                case "--build-timeout":
                    options = options with { BuildTimeoutSeconds = Int(queue, argument) };
                    break;
                case "--test-timeout":
                    options = options with { TestTimeoutSeconds = Int(queue, argument) };
                    break;
                case "--acceptance-timeout":
                    options = options with { AcceptanceTimeoutSeconds = Int(queue, argument) };
                    break;
                case "--keep-workspaces":
                    options = options with { KeepWorkspaces = true };
                    break;
                default:
                    throw new FormatException($"Unknown option '{argument}'.");
            }
        }

        return options;
    }

    /// <summary>Applies the options on top of a configuration file.</summary>
    public EvaluationConfiguration Apply(EvaluationConfiguration configuration)
    {
        var models = configuration.Models;
        if (Models.Count > 0)
        {
            var selected = models.Where(m => Models.Contains(m.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            var unknown = Models.Where(id => !models.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();
            if (unknown.Count > 0)
            {
                throw new KeyNotFoundException(
                    $"Unknown model id(s): {string.Join(", ", unknown)}. Configured models: {string.Join(", ", models.Select(m => m.Id))}.");
            }

            models = selected;
        }

        return configuration with
        {
            BenchmarkRoot = BenchmarkRoot ?? configuration.BenchmarkRoot,
            OutputDirectory = OutputDirectory ?? configuration.OutputDirectory,
            WorkspaceRoot = WorkspaceRoot ?? configuration.WorkspaceRoot,
            ExecutionImage = ExecutionImage ?? configuration.ExecutionImage,
            Repetitions = Repetitions ?? configuration.Repetitions,
            Scenarios = Scenarios.Count > 0 ? Scenarios : configuration.Scenarios,
            Models = models,
            KeepWorkspaces = KeepWorkspaces || configuration.KeepWorkspaces,
            Budgets = new BudgetOverrides
            {
                GenerationTimeoutSeconds = GenerationTimeoutSeconds ?? configuration.Budgets.GenerationTimeoutSeconds,
                BuildTimeoutSeconds = BuildTimeoutSeconds ?? configuration.Budgets.BuildTimeoutSeconds,
                TestTimeoutSeconds = TestTimeoutSeconds ?? configuration.Budgets.TestTimeoutSeconds,
                AcceptanceTimeoutSeconds = AcceptanceTimeoutSeconds ?? configuration.Budgets.AcceptanceTimeoutSeconds,
            },
        };
    }

    private static string Next(Queue<string> queue, string option) => queue.Count > 0
        ? queue.Dequeue()
        : throw new FormatException($"Option '{option}' requires a value.");

    private static int Int(Queue<string> queue, string option)
    {
        var value = Next(queue, option);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new FormatException($"Option '{option}' requires a positive integer, got '{value}'.");
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
