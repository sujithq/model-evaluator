using System.Text.Json;
using System.Xml.Linq;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Evaluation;

/// <summary>
/// Verifies the checkable parts of the shared and scenario instructions: layout, framework,
/// dependencies, nullable reference types, README and preservation of supplied files.
/// </summary>
public sealed class InstructionAdherenceChecker
{
    public IReadOnlyList<CheckResult> Check(string workspace, ScenarioPackage scenario)
    {
        var definition = scenario.Definition;
        var results = new List<CheckResult>();
        var relativeFiles = FileSystemHelper.EnumerateSourceFiles(workspace)
            .Select(f => Path.GetRelativePath(workspace, f).Replace('\\', '/'))
            .ToList();

        results.Add(Bool(
            "instructions.solution-file",
            relativeFiles.Any(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                                   || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)),
            "A solution file is required at the repository root.",
            "Solution file found."));

        results.Add(Bool(
            "instructions.src-layout",
            relativeFiles.Any(f => f.StartsWith("src/", StringComparison.OrdinalIgnoreCase)
                                   && f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)),
            "Application code must live under src/ in a C# project.",
            "Application project found under src/."));

        results.Add(Bool(
            "instructions.tests-layout",
            relativeFiles.Any(f => f.StartsWith("tests/", StringComparison.OrdinalIgnoreCase)
                                   && f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)),
            "Tests must live under tests/ in a C# project.",
            "Test project found under tests/."));

        results.Add(Bool(
            "instructions.readme",
            relativeFiles.Any(f => f.Equals("README.md", StringComparison.OrdinalIgnoreCase)),
            "A README.md with setup, run and test commands is required at the repository root.",
            "README.md found."));

        results.Add(CheckGlobalJson(workspace, definition));
        results.AddRange(CheckProjects(workspace, definition));
        results.Add(CheckPreservedFiles(workspace, scenario));

        foreach (var glob in definition.RequiredGlobs)
        {
            results.Add(Bool(
                $"instructions.required-path:{glob}",
                GlobMatcher.AnyMatch(relativeFiles, glob),
                $"No file matched the required pattern '{glob}'.",
                $"Pattern '{glob}' matched."));
        }

        return results;
    }

    private static IEnumerable<CheckResult> CheckProjects(string workspace, ScenarioDefinition definition)
    {
        var projects = FileSystemHelper.EnumerateSourceFiles(workspace, "*.csproj").ToList();
        if (projects.Count == 0)
        {
            yield return CheckResult.Fail(
                "instructions.target-framework", CheckCategory.InstructionAdherence, "No C# projects were found.");
            yield return CheckResult.Fail(
                "instructions.nullable-enabled", CheckCategory.InstructionAdherence, "No C# projects were found.");
            yield return CheckResult.Fail(
                "instructions.allowed-dependencies", CheckCategory.InstructionAdherence, "No C# projects were found.");
            yield break;
        }

        var sharedProperties = LoadSharedProperties(workspace);
        var frameworkViolations = new List<string>();
        var nullableViolations = new List<string>();
        var dependencyViolations = new List<string>();

        foreach (var project in projects)
        {
            var relative = Path.GetRelativePath(workspace, project).Replace('\\', '/');
            XDocument document;
            try
            {
                document = XDocument.Load(project);
            }
            catch (System.Xml.XmlException ex)
            {
                frameworkViolations.Add($"{relative}: unreadable project file ({ex.Message})");
                continue;
            }

            var frameworks = document.Descendants()
                .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .SelectMany(e => e.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToList();

            if (frameworks.Count == 0 && sharedProperties.TryGetValue("TargetFramework", out var shared))
            {
                frameworks.Add(shared);
            }

            if (frameworks.Count == 0 || frameworks.Any(f => !f.Equals(definition.TargetFramework, StringComparison.OrdinalIgnoreCase)))
            {
                frameworkViolations.Add(
                    $"{relative}: expected {definition.TargetFramework}, found {(frameworks.Count == 0 ? "none" : string.Join(';', frameworks))}");
            }

            var nullable = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Nullable")?.Value.Trim();
            nullable ??= sharedProperties.GetValueOrDefault("Nullable");
            if (!string.Equals(nullable, "enable", StringComparison.OrdinalIgnoreCase))
            {
                nullableViolations.Add($"{relative}: <Nullable> is '{nullable ?? "not set"}'");
            }

            foreach (var package in document.Descendants()
                         .Where(e => e.Name.LocalName == "PackageReference")
                         .Select(e => e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value)
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Select(id => id!))
            {
                if (!definition.AllowedPackages.Contains(package, StringComparer.OrdinalIgnoreCase))
                {
                    dependencyViolations.Add($"{relative}: {package}");
                }
            }
        }

        yield return Violations(
            "instructions.target-framework", frameworkViolations,
            $"All projects target {definition.TargetFramework}.");
        yield return Violations(
            "instructions.nullable-enabled", nullableViolations,
            "Nullable reference types are enabled in all projects.");
        yield return Violations(
            "instructions.allowed-dependencies", dependencyViolations,
            "Only allowed dependencies are referenced.");
    }

    private static Dictionary<string, string> LoadSharedProperties(string workspace)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in FileSystemHelper.EnumerateSourceFiles(workspace, "Directory.Build.props"))
        {
            try
            {
                foreach (var element in XDocument.Load(file).Descendants()
                             .Where(e => e.Name.LocalName is "TargetFramework" or "Nullable"))
                {
                    properties[element.Name.LocalName] = element.Value.Trim();
                }
            }
            catch (System.Xml.XmlException)
            {
                // Ignore unreadable shared property files; per-project checks still apply.
            }
        }

        return properties;
    }

    private static CheckResult CheckGlobalJson(string workspace, ScenarioDefinition definition)
    {
        var path = Path.Combine(workspace, "global.json");
        if (!File.Exists(path))
        {
            return CheckResult.Fail(
                "instructions.sdk-pinned", CheckCategory.InstructionAdherence,
                "global.json is missing; the SDK version must stay pinned.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var version = document.RootElement.TryGetProperty("sdk", out var sdk)
                          && sdk.TryGetProperty("version", out var value)
                ? value.GetString()
                : null;

            return version == definition.SdkVersion
                ? CheckResult.Pass("instructions.sdk-pinned", CheckCategory.InstructionAdherence,
                    $"global.json pins SDK {version}.")
                : CheckResult.Fail("instructions.sdk-pinned", CheckCategory.InstructionAdherence,
                    $"global.json pins SDK '{version ?? "none"}' instead of '{definition.SdkVersion}'.");
        }
        catch (JsonException ex)
        {
            return CheckResult.Fail(
                "instructions.sdk-pinned", CheckCategory.InstructionAdherence, $"global.json is not valid JSON: {ex.Message}");
        }
    }

    private static CheckResult CheckPreservedFiles(string workspace, ScenarioPackage scenario)
    {
        var modified = new List<string>();
        foreach (var relative in scenario.Definition.PreservedPaths)
        {
            var original = Path.Combine(scenario.StarterPath, relative.Replace('/', Path.DirectorySeparatorChar));
            var candidate = Path.Combine(workspace, relative.Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(original))
            {
                if (!Directory.Exists(candidate)
                    || Hashing.Sha256Directory(original) != Hashing.Sha256Directory(candidate))
                {
                    modified.Add(relative);
                }
            }
            else if (File.Exists(original))
            {
                if (!File.Exists(candidate)
                    || Hashing.Sha256(File.ReadAllText(original)) != Hashing.Sha256(File.ReadAllText(candidate)))
                {
                    modified.Add(relative);
                }
            }
        }

        return Violations(
            "instructions.preserved-files", modified, "Supplied requirements and fixtures are unchanged.");
    }

    private static CheckResult Violations(string id, IReadOnlyCollection<string> violations, string passDetails) =>
        violations.Count == 0
            ? CheckResult.Pass(id, CheckCategory.InstructionAdherence, passDetails)
            : CheckResult.Fail(id, CheckCategory.InstructionAdherence, string.Join("; ", violations));

    private static CheckResult Bool(string id, bool condition, string failDetails, string passDetails) =>
        condition
            ? CheckResult.Pass(id, CheckCategory.InstructionAdherence, passDetails)
            : CheckResult.Fail(id, CheckCategory.InstructionAdherence, failDetails);
}
