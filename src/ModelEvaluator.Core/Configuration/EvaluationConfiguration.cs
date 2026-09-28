using System.Text.Json;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Configuration;

/// <summary>One model-and-runner configuration under evaluation.</summary>
public sealed record ModelConfiguration
{
    /// <summary>Identifier used in reports. Must describe the full model + runner configuration.</summary>
    public required string Id { get; init; }

    /// <summary>Adapter key, for example <c>local-sample</c> or <c>command-line</c>.</summary>
    public required string Adapter { get; init; }

    public string? Description { get; init; }

    /// <summary>Adapter specific settings. Recorded verbatim in every result.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>Extra environment variables for the adapter process (never forwarded to generated code).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    public string GetSetting(string key, string fallback = "") =>
        Settings.TryGetValue(key, out var value) ? value : fallback;
}

/// <summary>Budget overrides applied on top of the scenario budgets.</summary>
public sealed record BudgetOverrides
{
    public int? GenerationTimeoutSeconds { get; init; }

    public int? BuildTimeoutSeconds { get; init; }

    public int? TestTimeoutSeconds { get; init; }

    public int? AcceptanceTimeoutSeconds { get; init; }
}

/// <summary>Root configuration for an evaluation run.</summary>
public sealed record EvaluationConfiguration
{
    /// <summary>Benchmark package root, relative to the configuration file or absolute.</summary>
    public string BenchmarkRoot { get; init; } = "benchmarks/v1";

    public string OutputDirectory { get; init; } = "artifacts/evaluations";

    /// <summary>Workspace root for disposable per-attempt workspaces. Defaults to a temp directory.</summary>
    public string? WorkspaceRoot { get; init; }

    /// <summary>Number of independent attempts per model and scenario. Minimum of three for a baseline.</summary>
    public int Repetitions { get; init; } = 3;

    /// <summary>Maximum simultaneous attempts; each attempt's stages remain sequential.</summary>
    public int MaxParallel { get; init; } = 1;

    /// <summary>Scenario ids to evaluate. Empty means every scenario in the benchmark package.</summary>
    public IReadOnlyList<string> Scenarios { get; init; } = [];

    public IReadOnlyList<ModelConfiguration> Models { get; init; } = [];

    public BudgetOverrides Budgets { get; init; } = new();

    /// <summary>Container image or runner label the evaluation executes in, recorded for traceability.</summary>
    public string? ExecutionImage { get; init; }

    /// <summary>Keep attempt workspaces on disk for debugging.</summary>
    public bool KeepWorkspaces { get; init; }

    /// <summary>Print detailed progress and live child-process output during evaluation.</summary>
    public bool Debug { get; init; }

    public static EvaluationConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        var configuration = JsonSerializer.Deserialize<EvaluationConfiguration>(json, JsonDefaults.Options)
                            ?? throw new InvalidDataException($"Could not parse evaluation configuration '{fullPath}'.");

        var baseDirectory = Path.GetDirectoryName(fullPath)!;
        return configuration with
        {
            BenchmarkRoot = Resolve(baseDirectory, configuration.BenchmarkRoot)!,
            OutputDirectory = Resolve(baseDirectory, configuration.OutputDirectory)!,
            WorkspaceRoot = Resolve(baseDirectory, configuration.WorkspaceRoot),
        };
    }

    private static string? Resolve(string baseDirectory, string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : Path.GetFullPath(Path.Combine(baseDirectory, value));
}
