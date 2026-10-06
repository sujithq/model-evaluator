using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Evaluation;
using ModelEvaluator.Core.Reporting;
using ModelEvaluator.Core.Results;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Cli;

/// <summary>Entry point of the evaluation CLI.</summary>
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
            Console.Error.WriteLine("warning: evaluation cancelled before a report could be created.");
            return 130;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
                                       or KeyNotFoundException or InvalidDataException or InvalidOperationException
                                       or FormatException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0];
        var options = CommandLineOptions.Parse(args.Skip(1));

        return command switch
        {
            "evaluate" => await EvaluateAsync(options).ConfigureAwait(false),
            "list-scenarios" => ListScenarios(options),
            "list-models" => await ListModelsAsync(options).ConfigureAwait(false),
            "validate" => Validate(options),
            "version" => PrintVersion(),
            _ => Unknown(command),
        };
    }

    private static async Task<int> EvaluateAsync(CommandLineOptions options)
    {
        var configuration = options.Apply(LoadConfiguration(options));

        if (configuration.Repetitions < 3)
        {
            Console.WriteLine(
                $"warning: {configuration.Repetitions} repetition(s) configured; baselines require at least three independent attempts.");
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        EvaluationReport report;
        try
        {
            var runner = new EvaluationRunner(log: Console.WriteLine);
            report = await runner.RunAsync(configuration, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        var outputDirectory = Path.Combine(configuration.OutputDirectory, report.RunId);
        var jsonPath = JsonReportWriter.Write(report, outputDirectory);
        var markdownPath = MarkdownReportWriter.Write(report, outputDirectory);

        Console.WriteLine();
        Console.WriteLine($"JSON report:     {jsonPath}");
        Console.WriteLine($"Markdown report: {markdownPath}");
        if (configuration.Debug)
        {
            Console.WriteLine(
                $"Scenario details: {Path.Combine(outputDirectory, ScenarioDetailsMarkdownWriter.FileName)}");
        }

        foreach (var summary in ReportAggregator.Summarise(report))
        {
            Console.WriteLine(
                $"  {summary.ScenarioId} / {summary.ModelId}: " +
                $"{summary.SuccessfulAttempts}/{summary.TotalAttempts} successful attempts");
        }

        var infrastructureFailures = report.Attempts.Count(a => a.Outcome == AttemptOutcome.InfrastructureFailure);
        if (report.Cancelled)
        {
            Console.Error.WriteLine($"warning: partial report saved; {report.NotStartedAttempts} attempt(s) were not started.");
            return 130;
        }

        if (infrastructureFailures > 0)
        {
            Console.Error.WriteLine($"warning: {infrastructureFailures} attempt(s) failed for infrastructure reasons.");
            return 3;
        }

        return 0;
    }

    private static int ListScenarios(CommandLineOptions options)
    {
        var configuration = options.Apply(LoadConfiguration(options));
        var catalog = ScenarioCatalog.Load(configuration.BenchmarkRoot);

        Console.WriteLine($"Benchmark root: {catalog.BenchmarkRoot}");
        Console.WriteLine($"Shared instructions hash: {catalog.SharedInstructionsHash}");
        Console.WriteLine();

        foreach (var scenario in catalog.Scenarios.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            Console.WriteLine($"{scenario.Id} ({scenario.Definition.ProjectType})");
            Console.WriteLine($"  name:        {scenario.Definition.Name}");
            Console.WriteLine($"  version:     {scenario.Definition.BenchmarkVersion}");
            Console.WriteLine($"  framework:   {scenario.Definition.TargetFramework} (SDK {scenario.Definition.SdkVersion})");
            Console.WriteLine($"  prompt hash: {scenario.PromptHash}");
            Console.WriteLine($"  samples:     {string.Join(", ", scenario.Definition.Samples.Keys)}");
        }

        return 0;
    }

    private static async Task<int> ListModelsAsync(CommandLineOptions options)
    {
        var configurationPath = ConfigurationPath(options);
        var configuration = LoadConfiguration(options);
        var models = SelectForListing(configuration, options.Models);
        var entries = models.Select(ModelCatalog.Describe).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();

        Console.WriteLine($"Configuration: {Path.GetFullPath(configurationPath)}");
        if (entries.Count == 0)
        {
            Console.WriteLine("No models are configured. Add entries to the configuration's 'models' array.");
            return 0;
        }

        Console.WriteLine(
            $"{entries.Count} configured model id(s); {entries.Count(e => e.Enabled)} enabled without an explicit --models filter.");
        Console.WriteLine();

        var idWidth = Math.Max(2, entries.Max(e => e.Id.Length));
        var adapterWidth = Math.Max(7, entries.Max(e => e.Adapter.Length));
        var providerWidth = Math.Max(14, entries.Max(e => (e.ProviderModel ?? "-").Length));

        Console.WriteLine(
            $"{"ID".PadRight(idWidth)}  {"ENABLED".PadRight(7)}  {"ADAPTER".PadRight(adapterWidth)}  " +
            $"{"PROVIDER MODEL".PadRight(providerWidth)}  RUNNER");

        foreach (var entry in entries)
        {
            Console.WriteLine(
                $"{entry.Id.PadRight(idWidth)}  {(entry.Enabled ? "yes" : "no").PadRight(7)}  " +
                $"{entry.Adapter.PadRight(adapterWidth)}  {(entry.ProviderModel ?? "-").PadRight(providerWidth)}  " +
                $"{entry.RunnerName ?? entry.Command ?? entry.SampleVariant ?? "-"}");

            if (options.Debug && entry.Description is not null)
            {
                Console.WriteLine($"{new string(' ', idWidth)}  {entry.Description}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Any id above can be passed to --models, including disabled ones.");
        Console.WriteLine("Provider model ids come from the configured runner arguments and do not prove account access.");

        return options.Probe
            ? await ProbeModelsAsync(models, options).ConfigureAwait(false)
            : 0;
    }

    private static async Task<int> ProbeModelsAsync(
        IReadOnlyList<ModelConfiguration> models, CommandLineOptions options)
    {
        var timeout = TimeSpan.FromSeconds(options.ProbeTimeoutSeconds ?? 120);
        Console.WriteLine();
        Console.Error.WriteLine(
            $"warning: probing invokes the configured runner once per model with a minimal prompt; " +
            $"available models consume real provider usage.");
        Console.WriteLine($"Probing {models.Count} model(s) with a {timeout.TotalSeconds:0} s limit each.");
        Console.WriteLine();

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var results = new List<ModelAvailabilityResult>();
        try
        {
            var probe = new ModelAvailabilityProbe();
            foreach (var model in models.OrderBy(m => m.Id, StringComparer.Ordinal))
            {
                var result = await probe.ProbeAsync(model, timeout, cancellation.Token).ConfigureAwait(false);
                results.Add(result);
                Console.WriteLine(
                    $"{model.Id}: {Label(result.Availability)}" +
                    $"{(result.DurationSeconds > 0 ? $" ({result.DurationSeconds:0.0} s)" : string.Empty)}" +
                    $"{(result.Detail is null ? string.Empty : $" - {result.Detail}")}");
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        var unconfirmed = results.Count(r => r.Availability == ModelAvailability.Unknown);
        var unavailable = results.Count(r => r.Availability == ModelAvailability.Unavailable);

        Console.WriteLine();
        Console.WriteLine(
            $"{results.Count(r => r.Availability == ModelAvailability.Available)} available, " +
            $"{unavailable} unavailable, " +
            $"{unconfirmed} unconfirmed, " +
            $"{results.Count(r => r.Availability == ModelAvailability.NotApplicable)} local.");

        if (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("warning: probing was cancelled before every model was checked.");
            return 130;
        }

        if (unconfirmed > 0)
        {
            Console.Error.WriteLine(
                "warning: unconfirmed models were neither accepted nor explicitly rejected by the runner.");
        }

        return unavailable == 0 ? 0 : 1;
    }

    private static string Label(ModelAvailability availability) => availability switch
    {
        ModelAvailability.Available => "available",
        ModelAvailability.Unavailable => "unavailable",
        ModelAvailability.NotApplicable => "local",
        _ => "unconfirmed",
    };

    /// <summary>Lists every configured model, or exactly the requested ids, including disabled ones.</summary>
    private static IReadOnlyList<ModelConfiguration> SelectForListing(
        EvaluationConfiguration configuration, IReadOnlyList<string> requested)
    {
        if (requested.Count == 0)
        {
            return configuration.Models;
        }

        var unknown = requested
            .Where(id => !configuration.Models.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (unknown.Count > 0)
        {
            throw new KeyNotFoundException(
                $"Unknown model id(s): {string.Join(", ", unknown)}. Configured models: {string.Join(", ", configuration.Models.Select(m => m.Id))}.");
        }

        return configuration.Models
            .Where(m => requested.Contains(m.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static int Validate(CommandLineOptions options)
    {
        var configuration = options.Apply(LoadConfiguration(options));
        var catalog = ScenarioCatalog.Load(configuration.BenchmarkRoot);
        var adapters = ModelAdapterFactory.CreateDefault().Keys;
        var problems = new List<string>();
        if (configuration.MaxParallel < 1)
        {
            problems.Add("maxParallel must be a positive integer.");
        }

        foreach (var scenario in catalog.Scenarios)
        {
            if (!Directory.Exists(scenario.StarterPath))
            {
                problems.Add($"{scenario.Id}: starter directory '{scenario.StarterPath}' is missing.");
            }

            if (!File.Exists(scenario.AcceptanceProjectPath))
            {
                problems.Add($"{scenario.Id}: acceptance project '{scenario.AcceptanceProjectPath}' is missing.");
            }

            foreach (var (name, variant) in scenario.Definition.Samples)
            {
                if (!Directory.Exists(scenario.SamplePath(variant)))
                {
                    problems.Add($"{scenario.Id}: sample '{name}' path '{variant.Path}' is missing.");
                }

                var overlay = scenario.SampleOverlayPath(variant);
                if (overlay is not null && !Directory.Exists(overlay))
                {
                    problems.Add($"{scenario.Id}: sample '{name}' overlay '{variant.Overlay}' is missing.");
                }
            }
        }

        foreach (var model in configuration.Models
                     .Where(model => !adapters.Contains(model.Adapter, StringComparer.OrdinalIgnoreCase)))
        {
            problems.Add($"model '{model.Id}': unknown adapter '{model.Adapter}'.");
        }

        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"error: {problem}");
        }

        Console.WriteLine(problems.Count == 0
            ? $"{catalog.Scenarios.Count} scenario package(s) and {configuration.Models.Count} model configuration(s) are valid."
            : $"{problems.Count} problem(s) found.");

        return problems.Count == 0 ? 0 : 1;
    }

    private static int PrintVersion()
    {
        Console.WriteLine($"model-evaluator {EvaluatorVersion.Value}");
        return 0;
    }

    private static string ConfigurationPath(CommandLineOptions options) =>
        options.ConfigPath ?? "config/evaluation.json";

    private static EvaluationConfiguration LoadConfiguration(CommandLineOptions options)
    {
        var path = ConfigurationPath(options);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Evaluation configuration '{Path.GetFullPath(path)}' was not found.", path);
        }

        return EvaluationConfiguration.Load(path);
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            model-evaluator - compare how AI models build .NET projects from fixed instructions.

            Usage:
              model-evaluator evaluate [options]        Run the evaluation matrix and write both reports.
              model-evaluator list-scenarios [options]  List benchmark scenarios and prompt hashes.
              model-evaluator list-models [options]     List the model ids --models accepts.
              model-evaluator validate [options]        Validate scenario packages and model configuration.
              model-evaluator version                   Print the harness version.

            Options:
              --config <path>              Evaluation configuration file (default: config/evaluation.json).
              --benchmark-root <path>      Override the benchmark package root.
              --models <a,b>               Restrict the run to these model ids.
              --scenarios <a,b>            Restrict the run to these scenario ids.
              --repetitions <n>            Independent attempts per model and scenario.
              --max-parallel <n>           Maximum concurrent attempts (default: 1).
              --output <path>              Output directory for reports and artifacts.
              --workspace-root <path>      Root for disposable per-attempt workspaces.
              --execution-image <name>     Record the execution image or runner label.
              --generation-timeout <sec>   Override the model generation budget.
              --build-timeout <sec>        Override the restore/build budget.
              --test-timeout <sec>         Override the generated test budget.
              --acceptance-timeout <sec>   Override the acceptance check budget.
              --keep-workspaces            Keep attempt workspaces for debugging.
              --debug                      Print detailed progress and write scenario-details.md.
              --probe                      list-models: ask the runner which models the authenticated
                                           account can use. Invokes the runner once per model and
                                           consumes real provider usage for available models.
              --probe-timeout <sec>        Time limit for one availability probe (default: 120).
            """);
    }
}
