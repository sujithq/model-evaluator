namespace ModelEvaluator.Core.Tests;

/// <summary>Locates the repository root so tests can read the real benchmark packages.</summary>
public static class RepositoryLocator
{
    public static string Root { get; } = Find();

    public static string BenchmarkRoot => Path.Combine(Root, "benchmarks", "v1");

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ModelEvaluator.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
