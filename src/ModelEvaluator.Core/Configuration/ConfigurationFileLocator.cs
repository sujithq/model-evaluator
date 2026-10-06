namespace ModelEvaluator.Core.Configuration;

/// <summary>Resolved evaluator configuration file and whether it belongs to the installed tool.</summary>
public sealed record ConfigurationFileLocation(string Path, bool IsBundled);

/// <summary>Locates repository, user-supplied, or tool-bundled evaluator configuration files.</summary>
public static class ConfigurationFileLocator
{
    public const string DefaultRelativePath = "config/evaluation.json";

    public static ConfigurationFileLocation Resolve(
        string? requestedPath,
        string workingDirectory,
        string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);

        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            if (Path.IsPathFullyQualified(requestedPath))
            {
                return new ConfigurationFileLocation(Path.GetFullPath(requestedPath), IsBundled: false);
            }

            var workingCandidate = Path.GetFullPath(requestedPath, workingDirectory);
            if (File.Exists(workingCandidate))
            {
                return new ConfigurationFileLocation(workingCandidate, IsBundled: false);
            }

            var bundledCandidate = Path.GetFullPath(requestedPath, applicationBaseDirectory);
            return File.Exists(bundledCandidate)
                ? new ConfigurationFileLocation(bundledCandidate, IsBundled: true)
                : new ConfigurationFileLocation(workingCandidate, IsBundled: false);
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
