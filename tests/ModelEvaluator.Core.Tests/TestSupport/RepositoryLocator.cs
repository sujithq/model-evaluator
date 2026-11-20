namespace ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

internal static class RepositoryLocator
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ModelEvaluator.sln"))
                || File.Exists(Path.Combine(directory.FullName, "ModelEvaluator.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
