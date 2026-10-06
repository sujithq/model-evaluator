using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Configuration;

/// <summary>One configured model id, described for listing.</summary>
public sealed record ModelCatalogEntry
{
    /// <summary>Identifier accepted by the <c>--models</c> filter.</summary>
    public required string Id { get; init; }

    /// <summary>True when the model runs without an explicit <c>--models</c> filter.</summary>
    public required bool Enabled { get; init; }

    public required string Adapter { get; init; }

    public string? Description { get; init; }

    /// <summary>Executable the command-line adapter invokes.</summary>
    public string? Command { get; init; }

    /// <summary>Provider model id parsed from the runner arguments, not a guarantee of access.</summary>
    public string? ProviderModel { get; init; }

    public string? RunnerName { get; init; }

    public string? RunnerVersion { get; init; }

    /// <summary>Sample variant used by the local-sample adapter.</summary>
    public string? SampleVariant { get; init; }

    /// <summary>True when the model needs an external runner and provider authentication.</summary>
    public bool RequiresRunner =>
        string.Equals(Adapter, CommandLineAdapter.AdapterKey, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Describes the model ids a configuration makes available to <c>--models</c>.</summary>
public static class ModelCatalog
{
    private const string ModelOption = "--model";

    public static IReadOnlyList<ModelCatalogEntry> Describe(EvaluationConfiguration configuration) =>
        configuration.Models.Select(Describe).ToList();

    public static ModelCatalogEntry Describe(ModelConfiguration model)
    {
        var commandLine = string.Equals(model.Adapter, CommandLineAdapter.AdapterKey, StringComparison.OrdinalIgnoreCase);
        var localSample = string.Equals(model.Adapter, LocalSampleAdapter.AdapterKey, StringComparison.OrdinalIgnoreCase);

        return new ModelCatalogEntry
        {
            Id = model.Id,
            Enabled = model.Enabled,
            Adapter = model.Adapter,
            Description = Normalise(model.Description),
            Command = commandLine ? Normalise(model.GetSetting("command")) : null,
            ProviderModel = commandLine ? FindProviderModel(model.GetSetting("arguments")) : null,
            RunnerName = commandLine ? Normalise(model.GetSetting("runnerName")) : null,
            RunnerVersion = commandLine ? Normalise(model.GetSetting("runnerVersion")) : null,
            SampleVariant = localSample ? model.GetSetting("variant", "good") : null,
        };
    }

    /// <summary>Reads the provider model id from <c>--model &lt;id&gt;</c> or <c>--model=&lt;id&gt;</c>.</summary>
    public static string? FindProviderModel(string? arguments)
    {
        var parts = ArgumentParser.Split(arguments);
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].StartsWith(ModelOption + "=", StringComparison.Ordinal))
            {
                return Normalise(parts[index][(ModelOption.Length + 1)..]);
            }

            if (string.Equals(parts[index], ModelOption, StringComparison.Ordinal) && index + 1 < parts.Count)
            {
                return Normalise(parts[index + 1]);
            }
        }

        return null;
    }

    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
