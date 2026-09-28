using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModelEvaluator.Acceptance;

/// <summary>
/// Locates the artifacts produced by the evaluated model. Acceptance tests are evaluator-owned and run
/// outside the model workspace, so every path is discovered through environment variables set by the runner.
/// </summary>
public static class EvaluationContext
{
    /// <summary>Root of the generated solution.</summary>
    public static string Workspace { get; } =
        Environment.GetEnvironmentVariable("EVAL_WORKSPACE")
        ?? throw new InvalidOperationException("EVAL_WORKSPACE is not set; acceptance tests must be started by the evaluator.");

    public static string TargetFramework { get; } =
        Environment.GetEnvironmentVariable("EVAL_TARGET_FRAMEWORK") ?? "net10.0";

    public static string FixturesDirectory { get; } =
        Environment.GetEnvironmentVariable("EVAL_FIXTURES") ?? Path.Combine(Workspace, "fixtures");

    /// <summary>Finds the runnable application assembly built from <c>src/</c>, preferring the shallowest match.</summary>
    public static string FindApplicationAssembly(string? nameContains = null)
    {
        var candidates = EnumerateAssemblies(nameContains)
            .Where(path => File.Exists(Path.ChangeExtension(path, ".runtimeconfig.json")))
            .ToList();

        return BestMatch(candidates, "runnable application", nameContains);
    }

    /// <summary>Finds the library assembly built from <c>src/</c>, preferring the shallowest match.</summary>
    public static string FindLibraryAssembly(string? nameContains = null)
    {
        var candidates = EnumerateAssemblies(nameContains).ToList();
        return BestMatch(candidates, "library", nameContains);
    }

    /// <summary>Creates an empty, disposable directory for one test.</summary>
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "eval-acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static IEnumerable<string> EnumerateAssemblies(string? nameContains)
    {
        var sourceRoot = Path.Combine(Workspace, "src");
        if (!Directory.Exists(sourceRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(sourceRoot, "*.dll", SearchOption.AllDirectories)
            .Where(path =>
            {
                var normalized = path.Replace('\\', '/');
                return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                       && normalized.Contains($"/{TargetFramework}/", StringComparison.OrdinalIgnoreCase)
                       && !normalized.Contains("/ref/", StringComparison.OrdinalIgnoreCase)
                       && !normalized.Contains("/refint/", StringComparison.OrdinalIgnoreCase)
                       && !normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
                       && !normalized.Contains("/publish/", StringComparison.OrdinalIgnoreCase);
            })
            .Where(path => nameContains is null
                           || Path.GetFileNameWithoutExtension(path).Contains(nameContains, StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length);
    }

    private static string BestMatch(IReadOnlyList<string> candidates, string kind, string? nameContains)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No {kind} assembly{Hint(nameContains)} was found under '{Path.Combine(Workspace, "src")}' for {TargetFramework}.");
        }

        return candidates[0];
    }

    private static string Hint(string? nameContains) =>
        nameContains is null ? string.Empty : $" matching '{nameContains}'";
}
