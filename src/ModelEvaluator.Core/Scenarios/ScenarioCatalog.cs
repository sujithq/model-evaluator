using System.Text;
using System.Text.Json;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Scenarios;

/// <summary>Time budgets for one attempt of a scenario.</summary>
public sealed record ScenarioBudget
{
    public int GenerationTimeoutSeconds { get; init; } = 1800;

    public int BuildTimeoutSeconds { get; init; } = 600;

    public int TestTimeoutSeconds { get; init; } = 600;

    public int AcceptanceTimeoutSeconds { get; init; } = 900;
}

/// <summary>A reference implementation used to validate the harness itself.</summary>
public sealed record SampleVariantDefinition
{
    /// <summary>Directory (relative to the scenario) copied into the workspace.</summary>
    public required string Path { get; init; }

    /// <summary>Optional directory copied on top of <see cref="Path"/> to introduce deliberate defects.</summary>
    public string? Overlay { get; init; }

    /// <summary>Human readable note describing why the variant exists.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>The versioned, on-disk definition of a benchmark scenario.</summary>
public sealed record ScenarioDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string ProjectType { get; init; }

    public required string BenchmarkVersion { get; init; }

    public string TargetFramework { get; init; } = "net10.0";

    public string SdkVersion { get; init; } = "10.0.100";

    public string InstructionsFile { get; init; } = "instructions.md";

    public string? ContractsFile { get; init; } = "contracts.md";

    public string StarterDirectory { get; init; } = "starter";

    public string AcceptanceProject { get; init; } = string.Empty;

    /// <summary>NuGet package ids the generated solution may reference.</summary>
    public IReadOnlyList<string> AllowedPackages { get; init; } = [];

    /// <summary>Relative glob patterns that must match at least one file in the workspace.</summary>
    public IReadOnlyList<string> RequiredGlobs { get; init; } = [];

    /// <summary>Supplied files that must be preserved byte-for-byte by the model.</summary>
    public IReadOnlyList<string> PreservedPaths { get; init; } = [];

    public ScenarioBudget Budget { get; init; } = new();

    public IReadOnlyDictionary<string, SampleVariantDefinition> Samples { get; init; } =
        new Dictionary<string, SampleVariantDefinition>();
}

/// <summary>A loaded scenario package: definition, resolved instructions and absolute paths.</summary>
public sealed class ScenarioPackage
{
    public ScenarioPackage(ScenarioDefinition definition, string directory, string resolvedPrompt)
    {
        Definition = definition;
        Directory = directory;
        ResolvedPrompt = resolvedPrompt;
        PromptHash = Hashing.Sha256(resolvedPrompt);
    }

    public ScenarioDefinition Definition { get; }

    /// <summary>Absolute path of the scenario package directory.</summary>
    public string Directory { get; }

    /// <summary>Shared instructions plus scenario instructions plus contracts, exactly as given to a model.</summary>
    public string ResolvedPrompt { get; }

    public string PromptHash { get; }

    public string Id => Definition.Id;

    public string StarterPath => System.IO.Path.Combine(Directory, Definition.StarterDirectory);

    public string AcceptanceProjectPath =>
        System.IO.Path.Combine(Directory, Definition.AcceptanceProject.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string SamplePath(SampleVariantDefinition variant) =>
        System.IO.Path.Combine(Directory, variant.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string? SampleOverlayPath(SampleVariantDefinition variant) => variant.Overlay is null
        ? null
        : System.IO.Path.Combine(Directory, variant.Overlay.Replace('/', System.IO.Path.DirectorySeparatorChar));
}

/// <summary>Loads versioned scenario packages from a benchmark directory.</summary>
public sealed class ScenarioCatalog
{
    public const string SharedInstructionsRelativePath = "shared/instructions.md";

    private readonly Dictionary<string, ScenarioPackage> _scenarios = new(StringComparer.OrdinalIgnoreCase);

    private ScenarioCatalog(string benchmarkRoot, string sharedInstructions)
    {
        BenchmarkRoot = benchmarkRoot;
        SharedInstructions = sharedInstructions;
        SharedInstructionsHash = Hashing.Sha256(sharedInstructions);
    }

    public string BenchmarkRoot { get; }

    public string SharedInstructions { get; }

    public string SharedInstructionsHash { get; }

    public IReadOnlyCollection<ScenarioPackage> Scenarios => _scenarios.Values;

    /// <summary>Loads every <c>scenario.json</c> under <c>&lt;benchmarkRoot&gt;/scenarios</c>.</summary>
    public static ScenarioCatalog Load(string benchmarkRoot)
    {
        benchmarkRoot = System.IO.Path.GetFullPath(benchmarkRoot);
        var sharedPath = System.IO.Path.Combine(
            benchmarkRoot,
            SharedInstructionsRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

        if (!File.Exists(sharedPath))
        {
            throw new FileNotFoundException(
                $"Shared instruction file not found at '{sharedPath}'.", sharedPath);
        }

        var catalog = new ScenarioCatalog(benchmarkRoot, File.ReadAllText(sharedPath));
        var scenariosRoot = System.IO.Path.Combine(benchmarkRoot, "scenarios");
        if (!System.IO.Directory.Exists(scenariosRoot))
        {
            throw new DirectoryNotFoundException($"No 'scenarios' directory under '{benchmarkRoot}'.");
        }

        foreach (var file in System.IO.Directory
                     .EnumerateFiles(scenariosRoot, "scenario.json", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
        {
            var directory = System.IO.Path.GetDirectoryName(file)!;
            var definition = JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(file), JsonDefaults.Options)
                             ?? throw new InvalidDataException($"Could not parse scenario definition '{file}'.");

            var package = new ScenarioPackage(definition, directory, catalog.BuildPrompt(definition, directory));
            if (!catalog._scenarios.TryAdd(definition.Id, package))
            {
                throw new InvalidDataException($"Duplicate scenario id '{definition.Id}' in '{file}'.");
            }
        }

        return catalog;
    }

    public ScenarioPackage Get(string id) => _scenarios.TryGetValue(id, out var scenario)
        ? scenario
        : throw new KeyNotFoundException($"Unknown scenario '{id}'. Known scenarios: {string.Join(", ", _scenarios.Keys)}.");

    public bool TryGet(string id, out ScenarioPackage? scenario) => _scenarios.TryGetValue(id, out scenario);

    private string BuildPrompt(ScenarioDefinition definition, string directory)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Benchmark {definition.Id} (version {definition.BenchmarkVersion})");
        builder.AppendLine();
        builder.AppendLine(SharedInstructions.Trim());
        builder.AppendLine();

        var instructionsPath = System.IO.Path.Combine(directory, definition.InstructionsFile);
        if (!File.Exists(instructionsPath))
        {
            throw new FileNotFoundException(
                $"Scenario '{definition.Id}' is missing its instruction file '{definition.InstructionsFile}'.",
                instructionsPath);
        }

        builder.AppendLine(File.ReadAllText(instructionsPath).Trim());

        if (!string.IsNullOrWhiteSpace(definition.ContractsFile))
        {
            var contractsPath = System.IO.Path.Combine(directory, definition.ContractsFile);
            if (!File.Exists(contractsPath))
            {
                throw new FileNotFoundException(
                    $"Scenario '{definition.Id}' is missing its contract file '{definition.ContractsFile}'.",
                    contractsPath);
            }

            builder.AppendLine();
            builder.AppendLine(File.ReadAllText(contractsPath).Trim());
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
