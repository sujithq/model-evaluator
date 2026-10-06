using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Tests;

public sealed class ConfigurationFileLocatorTests
{
    [Fact]
    public void Resolve_PrefersRequestedFileInWorkingDirectory()
    {
        using var directories = new TestDirectories();
        var relativePath = Path.Combine("config", "custom.json");
        var workingFile = directories.CreateWorkingFile(relativePath);
        directories.CreateApplicationFile(relativePath);

        var location = ConfigurationFileLocator.Resolve(
            relativePath,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(workingFile, location.Path);
        Assert.False(location.IsBundled);
    }

    [Fact]
    public void Resolve_FallsBackToRequestedBundledFile()
    {
        using var directories = new TestDirectories();
        var relativePath = Path.Combine("config", "custom.json");
        var bundledFile = directories.CreateApplicationFile(relativePath);

        var location = ConfigurationFileLocator.Resolve(
            relativePath,
            directories.WorkingDirectory,
            directories.ApplicationBaseDirectory);

        Assert.Equal(bundledFile, location.Path);
        Assert.True(location.IsBundled);
    }

    [Fact]
    public void Resolve_PrefersRepositoryDefault()
    {
        using var directories = new TestDirectories();
        var repositoryFile = directories.CreateWorkingFile(ConfigurationFileLocator.DefaultRelativePath);
        directories.CreateApplicationFile(ConfigurationFileLocator.DefaultRelativePath);

        var location = ConfigurationFileLocator.Resolve(
            requestedPath: null,
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
