using System.Text.Json;
using System.Text.RegularExpressions;
using ModelEvaluator.Core.Infrastructure;

namespace ModelEvaluator.Core.Scenarios;

public sealed class ScenarioCatalog
{
    private static readonly Regex IdentifierPattern = new(
        @"^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, ScenarioDefinition> _scenarios;

    private ScenarioCatalog(IReadOnlyList<ScenarioDefinition> scenarios)
    {
        Scenarios = scenarios;
        _scenarios = scenarios.ToDictionary(scenario => scenario.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ScenarioDefinition> Scenarios { get; }

    public static ScenarioCatalog Load(string scenarioRoot)
    {
        var fullPath = Path.GetFullPath(scenarioRoot);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Scenario directory was not found: {fullPath}");
        }

        var scenarios = new List<ScenarioDefinition>();
        foreach (var file in Directory.EnumerateFiles(fullPath, "*.json", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var json = File.ReadAllText(file);
            ScenarioDefinition? scenario;
            try
            {
                scenario = JsonSerializer.Deserialize<ScenarioDefinition>(json, JsonDefaults.Read);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Scenario '{file}' is not valid JSON: {exception.Message}", exception);
            }

            if (scenario is null)
            {
                throw new InvalidDataException($"Scenario '{file}' did not deserialize.");
            }

            ValidateScenario(scenario, file);
            scenario.SourcePath = file;
            scenarios.Add(scenario);
        }

        if (scenarios.Count == 0)
        {
            throw new InvalidDataException($"No scenario JSON files were found under '{fullPath}'.");
        }

        var duplicate = scenarios.GroupBy(scenario => scenario.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Scenario id '{duplicate.Key}' is duplicated.");
        }

        return new ScenarioCatalog(scenarios.OrderBy(scenario => scenario.Id, StringComparer.Ordinal).ToList());
    }

    public ScenarioDefinition Get(string id) =>
        _scenarios.TryGetValue(id, out var scenario)
            ? scenario
            : throw new KeyNotFoundException(
                $"Scenario '{id}' was not found. Available scenarios: {string.Join(", ", _scenarios.Keys)}.");

    public static string Fingerprint(ScenarioDefinition scenario)
    {
        var fairInputs = new
        {
            scenario.Id,
            scenario.Version,
            scenario.SystemPrompt,
            scenario.Turns,
            scenario.Parameters,
            scenario.ResponseFormat,
            tools = scenario.Tools.Select(tool => new
            {
                tool.Name,
                tool.Description,
                tool.Parameters,
            }),
            scenario.MaxToolRounds,
        };

        return Hashing.Sha256(fairInputs);
    }

    private static void ValidateScenario(ScenarioDefinition scenario, string file)
    {
        if (scenario.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Scenario '{file}' uses unsupported schemaVersion '{scenario.SchemaVersion}'.");
        }

        if (!IdentifierPattern.IsMatch(scenario.Id))
        {
            throw new InvalidDataException(
                $"Scenario '{file}' has invalid id '{scenario.Id}'. Use lowercase kebab-case.");
        }

        if (string.IsNullOrWhiteSpace(scenario.Version))
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' requires a version.");
        }

        if (scenario.Turns.Count == 0)
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' requires at least one turn.");
        }

        if (scenario.Turns.Any(turn => string.IsNullOrWhiteSpace(turn.User)))
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' contains an empty user turn.");
        }

        if (scenario.Parameters.MaxOutputTokens is <= 0)
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' maxOutputTokens must be positive.");
        }

        if (scenario.Parameters.Temperature is < 0 or > 2)
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' temperature must be between 0 and 2.");
        }

        if (scenario.Parameters.TopP is < 0 or > 1)
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' topP must be between 0 and 1.");
        }

        if (!string.IsNullOrWhiteSpace(scenario.Parameters.ReasoningEffort)
            && scenario.Parameters.ReasoningEffort is not ("none" or "minimal" or "low" or "medium" or "high"))
        {
            throw new InvalidDataException(
                $"Scenario '{scenario.Id}' reasoningEffort must be none, minimal, low, medium, or high.");
        }

        var duplicateTool = scenario.Tools.GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTool is not null)
        {
            throw new InvalidDataException(
                $"Scenario '{scenario.Id}' contains duplicate tool '{duplicateTool.Key}'.");
        }

        if (scenario.DryRun.Calls.Count == 0)
        {
            throw new InvalidDataException($"Scenario '{scenario.Id}' has no dry-run fixture calls.");
        }

        var duplicateAlternativeRequirement = scenario.Validation.ContainsAnyOf
            .GroupBy(requirement => requirement.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAlternativeRequirement is not null)
        {
            throw new InvalidDataException(
                $"Scenario '{scenario.Id}' contains duplicate alternative requirement " +
                $"'{duplicateAlternativeRequirement.Key}'.");
        }

        foreach (var requirement in scenario.Validation.ContainsAnyOf)
        {
            if (string.IsNullOrWhiteSpace(requirement.Id)
                || !IdentifierPattern.IsMatch(requirement.Id))
            {
                throw new InvalidDataException(
                    $"Scenario '{scenario.Id}' contains an invalid alternative requirement id. " +
                    "Use lowercase kebab-case.");
            }

            if (requirement.Terms.Count == 0
                || requirement.Terms.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException(
                    $"Scenario '{scenario.Id}' alternative requirement '{requirement.Id}' " +
                    "must contain non-empty accepted terms.");
            }
        }
    }
}
