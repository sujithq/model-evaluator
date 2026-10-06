namespace ModelEvaluator.Core.Configuration;

/// <summary>Resolved evaluator configuration file and whether it belongs to the installed tool.</summary>
public sealed record ConfigurationFileLocation(string Path, bool IsBundled);

/// <summary>A named configuration distributed with the installed tool.</summary>
public sealed record PackagedConfigurationPreset(string Name, string FileName, string Description);

/// <summary>Locates caller-owned or tool-bundled evaluator configuration files.</summary>
public static class ConfigurationFileLocator
{
    public const string DefaultRelativePath = "config/evaluation.json";

    private static readonly IReadOnlyList<PackagedConfigurationPreset> PackagedPresets =
    [
        new("default", "evaluation.json", "Reference-good and reference-broken models against benchmark v1."),
        new("auto", "evaluation.auto.example.json", "GitHub Copilot Auto routing against benchmark v1."),
        new("copilot-matrix", "evaluation.copilot-matrix.example.json", "Named GitHub Copilot models against benchmark v1."),
        new("models", "evaluation.models.example.json", "Generic command-line model adapter example."),
        new("smoke", "evaluation.smoke.example.json", "GitHub Copilot models against the minimal smoke benchmark."),
        new("smoke-reference", "evaluation.smoke.reference.json", "Local reference models against the minimal smoke benchmark."),
    ];

    public static IReadOnlyList<PackagedConfigurationPreset> Presets => PackagedPresets;

    public static ConfigurationFileLocation Resolve(
        string? requestedPath,
        string? preset,
        string workingDirectory,
        string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        if (!string.IsNullOrWhiteSpace(requestedPath) && !string.IsNullOrWhiteSpace(preset))
        {
            throw new FormatException("Options '--config' and '--preset' cannot be used together.");
        }

        if (!string.IsNullOrWhiteSpace(preset))
        {
            var match = PackagedPresets.FirstOrDefault(
                candidate => candidate.Name.Equals(preset, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new KeyNotFoundException(
                    $"Unknown preset '{preset}'. Available presets: {string.Join(", ", PackagedPresets.Select(candidate => candidate.Name))}.");
            }

            return new ConfigurationFileLocation(
                Path.Combine(applicationBaseDirectory, "config", match.FileName),
                IsBundled: true);
        }

        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            var path = Path.IsPathFullyQualified(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(requestedPath, workingDirectory);
            return new ConfigurationFileLocation(path, IsBundled: false);
        }

        var repositoryDefault = Path.GetFullPath(DefaultRelativePath, workingDirectory);
        if (File.Exists(repositoryDefault))
        {
            return new ConfigurationFileLocation(repositoryDefault, IsBundled: false);
        }

        return new ConfigurationFileLocation(
            Path.GetFullPath(DefaultRelativePath, applicationBaseDirectory),
            IsBundled: true);
    }

    public static EvaluationConfiguration RelocateBundledWritablePaths(
        EvaluationConfiguration configuration,
        string applicationBaseDirectory,
        string workingDirectory) =>
        configuration with
        {
            OutputDirectory = RelocateIfInstalled(configuration.OutputDirectory, applicationBaseDirectory, workingDirectory),
            WorkspaceRoot = configuration.WorkspaceRoot is null
                ? null
                : RelocateIfInstalled(configuration.WorkspaceRoot, applicationBaseDirectory, workingDirectory),
        };

    private static string RelocateIfInstalled(string path, string applicationBaseDirectory, string workingDirectory)
    {
        var relative = Path.GetRelativePath(applicationBaseDirectory, path);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
        {
            return path;
        }

        return Path.GetFullPath(relative, workingDirectory);
    }
}
