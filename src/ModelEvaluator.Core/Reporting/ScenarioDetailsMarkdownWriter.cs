using System.Globalization;
using System.Text;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Reporting;

/// <summary>Writes the selected scenario definitions and resolved prompts for a debug run.</summary>
public static class ScenarioDetailsMarkdownWriter
{
    public const string FileName = "scenario-details.md";

    public static string Write(
        IReadOnlyCollection<ScenarioPackage> scenarios,
        BudgetOverrides budgetOverrides,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, FileName);
        File.WriteAllText(
            path,
            Render(scenarios, budgetOverrides),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string Render(
        IReadOnlyCollection<ScenarioPackage> scenarios,
        BudgetOverrides budgetOverrides)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Evaluation scenario details");
        builder.AppendLine();
        builder.AppendLine($"Selected scenarios: {scenarios.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();

        foreach (var scenario in scenarios)
        {
            var definition = scenario.Definition;
            var budget = Budgets.Resolve(definition.Budget, budgetOverrides);

            builder.AppendLine($"## `{definition.Id}` - {definition.Name}");
            builder.AppendLine();
            builder.AppendLine("| Property | Value |");
            builder.AppendLine("| --- | --- |");
            builder.AppendLine(Row("Project type", definition.ProjectType));
            builder.AppendLine(Row("Benchmark version", Code(definition.BenchmarkVersion)));
            builder.AppendLine(Row("Target framework", Code(definition.TargetFramework)));
            builder.AppendLine(Row(".NET SDK", Code(definition.SdkVersion)));
            builder.AppendLine(Row("Prompt SHA-256", Code(scenario.PromptHash)));
            builder.AppendLine(Row("Scenario directory", Code(scenario.Directory)));
            builder.AppendLine(Row("Starter directory", Code(definition.StarterDirectory)));
            builder.AppendLine(Row("Acceptance project", Code(definition.AcceptanceProject)));
            builder.AppendLine(Row("Instructions file", Code(definition.InstructionsFile)));
            builder.AppendLine(Row("Contracts file", OptionalCode(definition.ContractsFile)));
            builder.AppendLine();

            builder.AppendLine("### Effective time budgets");
            builder.AppendLine();
            builder.AppendLine("| Stage | Seconds |");
            builder.AppendLine("| --- | ---: |");
            builder.AppendLine(Row("Generation", Number(budget.GenerationTimeoutSeconds)));
            builder.AppendLine(Row("Restore, build and format (each)", Number(budget.BuildTimeoutSeconds)));
            builder.AppendLine(Row("Generated tests", Number(budget.TestTimeoutSeconds)));
            builder.AppendLine(Row("Acceptance tests", Number(budget.AcceptanceTimeoutSeconds)));
            builder.AppendLine();

            WriteList(builder, "Allowed NuGet packages", definition.AllowedPackages);
            WriteList(builder, "Required file globs", definition.RequiredGlobs);
            WriteList(builder, "Preserved paths", definition.PreservedPaths);

            builder.AppendLine("### Samples");
            builder.AppendLine();
            if (definition.Samples.Count == 0)
            {
                builder.AppendLine("_None._");
            }
            else
            {
                builder.AppendLine("| Name | Path | Overlay | Description |");
                builder.AppendLine("| --- | --- | --- | --- |");
                foreach (var sample in definition.Samples.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    builder.AppendLine(Row(
                        Code(sample.Key),
                        Code(sample.Value.Path),
                        OptionalCode(sample.Value.Overlay),
                        sample.Value.Description));
                }
            }

            builder.AppendLine();
            builder.AppendLine("### Resolved prompt");
            builder.AppendLine();
            AppendCodeBlock(builder, scenario.ResolvedPrompt);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void WriteList(StringBuilder builder, string heading, IReadOnlyList<string> values)
    {
        builder.AppendLine($"### {heading}");
        builder.AppendLine();
        if (values.Count == 0)
        {
            builder.AppendLine("_None._");
        }
        else
        {
            foreach (var value in values)
            {
                builder.AppendLine($"- {Code(value)}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendCodeBlock(StringBuilder builder, string content)
    {
        var longestRun = 0;
        var currentRun = 0;
        foreach (var character in content)
        {
            currentRun = character == '`' ? currentRun + 1 : 0;
            longestRun = Math.Max(longestRun, currentRun);
        }

        var fence = new string('`', Math.Max(3, longestRun + 1));
        builder.AppendLine($"{fence}markdown");
        builder.Append(content);
        if (!content.EndsWith('\n'))
        {
            builder.AppendLine();
        }

        builder.AppendLine(fence);
    }

    private static string Row(params string[] cells) =>
        "| " + string.Join(" | ", cells.Select(Escape)) + " |";

    private static string Code(string value) => $"`{value.Replace("`", "\\`", StringComparison.Ordinal)}`";

    private static string OptionalCode(string? value) => string.IsNullOrWhiteSpace(value) ? "_None_" : Code(value);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
