using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Tests;

public sealed class ConfigurationFileLocatorTests
{
    [Fact]
    public void Presets_HaveUniqueNamesAndExistingConfigurationFiles()
    {
        Assert.Equal(
            ["default", "auto", "copilot-matrix", "models", "smoke", "smoke-reference"],
            ConfigurationFileLocator.Presets.Select(preset => preset.Name));
        Assert.Equal(
            ConfigurationFileLocator.Presets.Count,
            ConfigurationFileLocator.Presets.Select(preset => preset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ConfigurationFileLocator.Presets, preset =>
            Assert.True(
                File.Exists(Path.Combine(RepositoryLocator.Root, "config", preset.FileName)),
                $"Packaged preset '{preset.Name}' references missing config/{preset.FileName}."));
    }

    [Fact]
    public void Resolve_PrefersRequestedFileInWorkingDirectory()
    {
        using var directories = new TestDirectories();
        var relativePath = Path.Combine("config", "custom.json");
        var workingFile = directories.CreateWorkingFile(relativePath);
        directories.CreateApplicationFile(relativePath);

        var location = ConfigurationFileLocator.Resolve(
            relativePath,
            preset: null,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(workingFile, location.Path);
        Assert.False(location.IsBundled);
    }

    [Fact]
    public void Resolve_DoesNotFallbackCallerConfigToBundledFile()
    {
        using var directories = new TestDirectories();
        var relativePath = Path.Combine("config", "custom.json");
        directories.CreateApplicationFile(relativePath);

        var location = ConfigurationFileLocator.Resolve(
            relativePath,
            preset: null,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(Path.Combine(directories.WorkingDirectory, relativePath), location.Path);
        Assert.False(location.IsBundled);
    }

    [Fact]
    public void Resolve_PresetUsesBundledConfiguration()
    {
        using var directories = new TestDirectories();
        var bundledFile = directories.CreateApplicationFile(Path.Combine("config", "evaluation.auto.example.json"));

        var location = ConfigurationFileLocator.Resolve(
            requestedPath: null,
            preset: "AUTO",
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(bundledFile, location.Path);
        Assert.True(location.IsBundled);
    }

    [Fact]
    public void Resolve_RejectsUnknownPreset()
    {
        using var directories = new TestDirectories();

        var exception = Assert.Throws<KeyNotFoundException>(() => ConfigurationFileLocator.Resolve(
            requestedPath: null,
            preset: "missing",
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory));

        Assert.Contains("Available presets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsConfigAndPresetTogether()
    {
        using var directories = new TestDirectories();

        Assert.Throws<FormatException>(() => ConfigurationFileLocator.Resolve(
            requestedPath: "custom.json",
            preset: "auto",
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory));
    }

    [Fact]
    public void Resolve_PrefersRepositoryDefault()
    {
        using var directories = new TestDirectories();
        var repositoryFile = directories.CreateWorkingFile(ConfigurationFileLocator.DefaultRelativePath);
        directories.CreateApplicationFile(ConfigurationFileLocator.DefaultRelativePath);

        var location = ConfigurationFileLocator.Resolve(
            requestedPath: null,
            preset: null,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(repositoryFile, location.Path);
        Assert.False(location.IsBundled);
    }

    [Fact]
    public void Resolve_UsesBundledDefaultOutsideRepository()
    {
        using var directories = new TestDirectories();
        var bundledFile = directories.CreateApplicationFile(ConfigurationFileLocator.DefaultRelativePath);

        var location = ConfigurationFileLocator.Resolve(
            requestedPath: null,
            preset: null,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(bundledFile, location.Path);
        Assert.True(location.IsBundled);
    }

    [Fact]
    public void Resolve_DoesNotRedirectMissingAbsolutePath()
    {
        using var directories = new TestDirectories();
        var requestedPath = Path.Combine(directories.WorkingDirectory, "missing.json");

        var location = ConfigurationFileLocator.Resolve(
            requestedPath,
            preset: null,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(requestedPath, location.Path);
        Assert.False(location.IsBundled);
    }

    [Fact]
    public void RelocateBundledWritablePaths_MovesOnlyInstalledPathsToWorkingDirectory()
    {
        using var directories = new TestDirectories();
        var benchmarkRoot = Path.Combine(directories.ApplicationBaseDirectory, "benchmarks", "v1");
        var configuration = new EvaluationConfiguration
        {
            BenchmarkRoot = benchmarkRoot,
            OutputDirectory = Path.Combine(directories.ApplicationBaseDirectory, "artifacts", "evaluations"),
            WorkspaceRoot = Path.Combine(directories.ApplicationBaseDirectory, ".workspaces"),
        };

        var relocated = ConfigurationFileLocator.RelocateBundledWritablePaths(
            configuration,
            directories.ApplicationBaseDirectory,
            directories.WorkingDirectory);

        Assert.Equal(benchmarkRoot, relocated.BenchmarkRoot);
        Assert.Equal(
            Path.Combine(directories.WorkingDirectory, "artifacts", "evaluations"),
            relocated.OutputDirectory);
        Assert.Equal(Path.Combine(directories.WorkingDirectory, ".workspaces"), relocated.WorkspaceRoot);
    }

    [Fact]
    public void RelocateBundledWritablePaths_PreservesExternalOutputPath()
    {
        using var directories = new TestDirectories();
        var externalOutput = Path.Combine(directories.WorkingDirectory, "existing-output");
        var configuration = new EvaluationConfiguration
        {
            OutputDirectory = externalOutput,
        };

        var relocated = ConfigurationFileLocator.RelocateBundledWritablePaths(
            configuration,
            directories.ApplicationBaseDirectory,
            directories.WorkingDirectory);

        Assert.Equal(externalOutput, relocated.OutputDirectory);
    }

    private sealed class TestDirectories : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "model-evaluator-tests", Guid.NewGuid().ToString("N"));

        public TestDirectories()
        {
            WorkingDirectory = Path.Combine(root, "working");
            ApplicationBaseDirectory = Path.Combine(root, "application");
            Directory.CreateDirectory(WorkingDirectory);
            Directory.CreateDirectory(ApplicationBaseDirectory);
        }

        public string WorkingDirectory { get; }

        public string ApplicationBaseDirectory { get; }

        public string CreateWorkingFile(string relativePath) => CreateFile(WorkingDirectory, relativePath);

        public string CreateApplicationFile(string relativePath) => CreateFile(ApplicationBaseDirectory, relativePath);

        public void Dispose() => Directory.Delete(root, recursive: true);

        private static string CreateFile(string directory, string relativePath)
        {
            var path = Path.GetFullPath(relativePath, directory);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");
            return path;
        }
    }
}
