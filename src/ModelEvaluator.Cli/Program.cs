using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Infrastructure;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Evaluation cancelled.");
            return 130;
        }
        catch (Exception exception) when (IsExpectedCliFailure(exception))
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0];
        var options = ParseOptions(args.Skip(1));
        return command switch
        {
            "list-scenarios" => ListScenarios(options),
            "list-adapters" => ListAdapters(),
            "validate" => Validate(options),
            "run" => await RunEvaluationAsync(options).ConfigureAwait(false),
            _ => UnknownCommand(command),
        };
    }

    private static int ListScenarios(Dictionary<string, string?> options)
    {
        var scenarioRoot = options.TryGetValue("--scenario-root", out var root)
            ? RequireValue("--scenario-root", root)
            : ResolveBundledPath("scenarios/v1");
        var catalog = ScenarioCatalog.Load(Path.GetFullPath(scenarioRoot));
        Console.WriteLine($"Scenario package: {Path.GetFullPath(scenarioRoot)}");
        foreach (var scenario in catalog.Scenarios)
        {
            Console.WriteLine(
                $"  {scenario.Id,-30} v{scenario.Version,-8} {scenario.Category,-18} " +
                $"fingerprint={Hashing.Short(ScenarioCatalog.Fingerprint(scenario))}");
            Console.WriteLine($"    {scenario.Description}");
        }

        return 0;
    }

    private static int ListAdapters()
    {
        foreach (var adapter in ModelAdapterFactory.CreateDefault().All.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            var target = adapter.Id == AdapterIds.LocalFixture
                ? new TargetConfiguration
                {
                    Id = "probe",
                    Adapter = AdapterIds.LocalFixture,
                    ApiKind = ApiKinds.Local,
                    ModelId = "probe",
                    Authentication = AuthenticationKinds.None,
                }
                : null;
            var package = target is null
                ? ""
                : $" ({adapter.DescribePackage(target).Name}@{adapter.DescribePackage(target).Version})";
            Console.WriteLine($"  {adapter.Id,-32} {adapter.DisplayName}{package}");
        }

        return 0;
    }

    private static int Validate(Dictionary<string, string?> options)
    {
        var configPath = options.TryGetValue("--config", out var value)
            ? RequireValue("--config", value)
            : ResolveBundledPath("config/evaluation.offline.json");
        var configuration = ConfigurationLoader.Load(configPath);
        var requireCredentials = HasFlag(options, "--live");
        var validation = ConfigurationValidator.Validate(configuration, requireCredentials);
        ScenarioCatalog.Load(configuration.ScenarioRoot);

        foreach (var warning in validation.Warnings)
        {
            Console.Error.WriteLine($"warning: {warning}");
        }

        foreach (var error in validation.Errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        Console.WriteLine(
            validation.IsValid
                ? $"Configuration is valid for {(requireCredentials ? "live" : "offline/static")} validation."
                : "Configuration is invalid.");
        return validation.IsValid ? 0 : 2;
    }

    private static async Task<int> RunEvaluationAsync(Dictionary<string, string?> options)
    {
        var configPath = options.TryGetValue("--config", out var value)
            ? RequireValue("--config", value)
            : ResolveBundledPath("config/evaluation.offline.json");
        var configuration = ConfigurationLoader.Load(configPath);
        configuration = ConfigurationLoader.WithPaths(
            configuration,
            options.TryGetValue("--scenario-root", out var root)
                ? RequireValue("--scenario-root", root)
                : null,
            options.TryGetValue("--output", out var output)
                ? RequireValue("--output", output)
                : null);
        var repetitions = options.TryGetValue("--repetitions", out var repetitionValue)
            ? ParsePositiveInt("--repetitions", RequireValue("--repetitions", repetitionValue))
            : (int?)null;
        var runOptions = new EvaluationRunOptions
        {
            DryRun = HasFlag(options, "--dry-run"),
            ScenarioIds = ParseList(options, "--scenarios"),
            TargetIds = ParseList(options, "--targets"),
            Repetitions = repetitions,
            OnProgress = message => Console.WriteLine(message),
        };

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        EvaluationReport report;
        try
        {
            report = await new EvaluationRunner().RunAsync(
                configuration,
                runOptions,
                cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }

        var runDirectory = Path.Combine(configuration.OutputDirectory, report.RunId);
        var jsonPath = await JsonReportWriter.WriteAsync(
            report,
            runDirectory,
            cancellation.Token).ConfigureAwait(false);
        var markdownPath = await MarkdownReportWriter.WriteAsync(
            report,
            runDirectory,
            cancellation.Token).ConfigureAwait(false);
        ConsoleReportWriter.Write(report);
        Console.WriteLine($"JSON: {jsonPath}");
        Console.WriteLine($"Markdown: {markdownPath}");
        return report.Attempts.Count == report.PlannedAttempts && report.Attempts.All(attempt => attempt.Passed)
            ? 0
            : 1;
    }

    private static Dictionary<string, string?> ParseOptions(IEnumerable<string> arguments)
    {
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var enumerator = arguments.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var argument = enumerator.Current;
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{argument}'.");
            }

            if (!options.TryAdd(argument, null))
            {
                throw new ArgumentException($"Option '{argument}' was specified more than once.");
            }

            if (!argument.Equals("--dry-run", StringComparison.Ordinal)
                && !argument.Equals("--live", StringComparison.Ordinal))
            {
                options[argument] = enumerator.MoveNext()
                    ? enumerator.Current
                    : throw new ArgumentException($"Option '{argument}' requires a value.");
            }
        }

        return options;
    }

    private static bool HasFlag(Dictionary<string, string?> options, string name) =>
        options.ContainsKey(name);

    private static IReadOnlyList<string> ParseList(
        Dictionary<string, string?> options,
        string name)
    {
        if (!options.TryGetValue(name, out var value))
        {
            return [];
        }

        return RequireValue(name, value)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string RequireValue(string option, string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"Option '{option}' requires a non-empty value.")
            : value;

    private static int ParsePositiveInt(string option, string value) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"Option '{option}' must be a positive integer.");

    private static string ResolveBundledPath(string relativePath)
    {
        var workingDirectoryCandidate = Path.GetFullPath(relativePath);
        if (Directory.Exists(workingDirectoryCandidate) || File.Exists(workingDirectoryCandidate))
        {
            return workingDirectoryCandidate;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static bool IsExpectedCliFailure(Exception exception) =>
        exception is ArgumentException
            or DirectoryNotFoundException
            or FileNotFoundException
            or InvalidDataException
            or InvalidOperationException
            or IOException
            or JsonException
            or KeyNotFoundException
            or UnauthorizedAccessException
            or UriFormatException;

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            model-evaluator - compare AI model behavior and .NET integration paths

            Commands
              list-scenarios [--scenario-root <path>]
              list-adapters
              validate [--config <path>] [--live]
              run [--config <path>] [--dry-run] [--output <path>]
                  [--scenario-root <path>] [--scenarios <id,id,...>]
                  [--targets <id,id,...>] [--repetitions <count>]

            run writes results.json and report.md. Use --dry-run for a complete
            offline execution with synthetic fixtures and no external AI service.
            """);
    }
}
