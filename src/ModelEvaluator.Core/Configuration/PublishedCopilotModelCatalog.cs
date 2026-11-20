using System.Reflection;
using System.Text.Json;

namespace ModelEvaluator.Core.Configuration;

public enum PublishedCopilotModelStatus
{
    Supported,
    ScheduledRetirement,
    Retired,
    NotListed,
    NotApplicable,
}

public sealed record PublishedCopilotModelResult
{
    public required string ModelId { get; init; }

    public string? ProviderModel { get; init; }

    public string? PublishedName { get; init; }

    public required PublishedCopilotModelStatus Status { get; init; }

    public string? CopilotCli { get; init; }

    public string? Detail { get; init; }
}

/// <summary>Checks configured models against the official catalog embedded at build time.</summary>
public sealed class PublishedCopilotModelCatalog
{
    private const string ResourceName = "ModelEvaluator.Core.Data.copilot-model-catalog.json";

    private readonly IReadOnlyDictionary<string, SupportedModel> _supported;
    private readonly IReadOnlyDictionary<string, DeprecatedModel> _deprecated;

    private PublishedCopilotModelCatalog(CatalogDocument document)
    {
        _supported = document.Supported.ToDictionary(model => Normalize(model.Name), StringComparer.Ordinal);
        _deprecated = document.Deprecated.ToDictionary(model => Normalize(model.Name), StringComparer.Ordinal);
    }

    public static PublishedCopilotModelCatalog Load()
    {
        using var stream = typeof(PublishedCopilotModelCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"Embedded Copilot model catalog '{ResourceName}' was not found.");
        var document = JsonSerializer.Deserialize<CatalogDocument>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The embedded Copilot model catalog is empty.");
        return new PublishedCopilotModelCatalog(document);
    }

    public PublishedCopilotModelResult Check(ModelConfiguration model)
    {
        var entry = ModelCatalog.Describe(model);
        if (!IsCopilotRunner(entry))
        {
            return Result(model.Id, entry.ProviderModel, PublishedCopilotModelStatus.NotApplicable,
                detail: "The configured runner is not GitHub Copilot CLI.");
        }

        if (entry.ProviderModel is null)
        {
            return Result(model.Id, null, PublishedCopilotModelStatus.NotListed,
                detail: "No --model value was found in the configured runner arguments.");
        }

        var key = Normalize(entry.ProviderModel);
        if (_deprecated.TryGetValue(key, out var deprecated))
        {
            var status = string.Equals(deprecated.Status, "Scheduled", StringComparison.OrdinalIgnoreCase)
                ? PublishedCopilotModelStatus.ScheduledRetirement
                : PublishedCopilotModelStatus.Retired;
            return Result(model.Id, entry.ProviderModel, status, deprecated.Name,
                detail: $"{deprecated.RetirementDate:yyyy-MM-dd}: use {deprecated.SuggestedAlternative}.");
        }

        if (_supported.TryGetValue(key, out var supported))
        {
            return Result(model.Id, entry.ProviderModel, PublishedCopilotModelStatus.Supported,
                supported.Name, supported.CopilotCli,
                $"{supported.Provider}; {supported.ReleaseStatus}.");
        }

        return Result(model.Id, entry.ProviderModel, PublishedCopilotModelStatus.NotListed,
            detail: "The model does not appear in GitHub's published supported or deprecation tables.");
    }

    private static bool IsCopilotRunner(ModelCatalogEntry entry) =>
        string.Equals(entry.RunnerName, "github-copilot-cli", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileNameWithoutExtension(entry.Command), "copilot", StringComparison.OrdinalIgnoreCase);

    private static PublishedCopilotModelResult Result(
        string modelId,
        string? providerModel,
        PublishedCopilotModelStatus status,
        string? publishedName = null,
        string? copilotCli = null,
        string? detail = null) => new()
        {
            ModelId = modelId,
            ProviderModel = providerModel,
            PublishedName = publishedName,
            Status = status,
            CopilotCli = copilotCli,
            Detail = detail,
        };

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private sealed record CatalogDocument
    {
        public IReadOnlyList<SupportedModel> Supported { get; init; } = [];

        public IReadOnlyList<DeprecatedModel> Deprecated { get; init; } = [];
    }

    private sealed record SupportedModel
    {
        public required string Name { get; init; }

        public required string Provider { get; init; }

        public required string ReleaseStatus { get; init; }

        public required string CopilotCli { get; init; }
    }

    private sealed record DeprecatedModel
    {
        public required string Name { get; init; }

        public DateOnly RetirementDate { get; init; }

        public required string Status { get; init; }

        public required string SuggestedAlternative { get; init; }
    }
}
