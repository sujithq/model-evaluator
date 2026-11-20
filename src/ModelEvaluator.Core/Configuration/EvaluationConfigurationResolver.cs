namespace ModelEvaluator.Core.Configuration;

/// <summary>Resolves and applies CLI-compatible configuration options for any evaluator host.</summary>
public static class EvaluationConfigurationResolver
{
    public static EvaluationConfiguration Resolve(
        CommandLineOptions options,
        string workingDirectory,
        string applicationDirectory)
    {
        var location = ConfigurationFileLocator.Resolve(
            options.ConfigPath,
            options.Preset,
            workingDirectory,
            applicationDirectory);
        if (!File.Exists(location.Path))
        {
            throw new FileNotFoundException(
                $"Evaluation configuration '{location.Path}' was not found.",
                location.Path);
        }

        var configuration = EvaluationConfiguration.Load(location.Path);
        if (location.IsBundled)
        {
            configuration = ConfigurationFileLocator.RelocateBundledWritablePaths(
                configuration,
                applicationDirectory,
                workingDirectory);
        }

        return options.Apply(configuration);
    }
}
