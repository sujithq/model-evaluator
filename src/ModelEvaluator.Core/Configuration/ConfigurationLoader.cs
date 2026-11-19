using Microsoft.Extensions.Configuration;
using ModelEvaluator.Core.Infrastructure;

namespace ModelEvaluator.Core.Configuration;

public static class ConfigurationLoader
{
    public const string UserSecretsId = "Sujithq.ModelEvaluator.Cli";
    public const string EnvironmentPrefix = "MODEL_EVALUATOR_";

    public static EvaluationConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Evaluation configuration was not found: {fullPath}", fullPath);
        }

        var root = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(fullPath)!)
            .AddJsonFile(Path.GetFileName(fullPath), optional: false, reloadOnChange: false)
            .AddUserSecrets(typeof(ConfigurationLoader).Assembly, optional: true)
            .AddEnvironmentVariables(EnvironmentPrefix)
            .Build();

        var configuration = root.Get<EvaluationConfiguration>()
                            ?? throw new InvalidDataException(
                                $"Configuration '{fullPath}' could not be bound to an evaluation configuration.");

        return new EvaluationConfiguration
        {
            SchemaVersion = configuration.SchemaVersion,
            ScenarioRoot = ResolvePath(fullPath, configuration.ScenarioRoot),
            OutputDirectory = ResolvePath(fullPath, configuration.OutputDirectory),
            Repetitions = configuration.Repetitions,
            MaxParallel = configuration.MaxParallel,
            MaxRetries = configuration.MaxRetries,
            TimeoutSeconds = configuration.TimeoutSeconds,
            Targets = configuration.Targets,
            Comparisons = configuration.Comparisons,
        };
    }

    public static EvaluationConfiguration WithPaths(
        EvaluationConfiguration configuration,
        string? scenarioRoot,
        string? outputDirectory)
    {
        return new EvaluationConfiguration
        {
            SchemaVersion = configuration.SchemaVersion,
            ScenarioRoot = scenarioRoot is null ? configuration.ScenarioRoot : Path.GetFullPath(scenarioRoot),
            OutputDirectory = outputDirectory is null
                ? configuration.OutputDirectory
                : Path.GetFullPath(outputDirectory),
            Repetitions = configuration.Repetitions,
            MaxParallel = configuration.MaxParallel,
            MaxRetries = configuration.MaxRetries,
            TimeoutSeconds = configuration.TimeoutSeconds,
            Targets = configuration.Targets,
            Comparisons = configuration.Comparisons,
        };
    }

    private static string ResolvePath(string configurationPath, string value) =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configurationPath)!, value));
}
