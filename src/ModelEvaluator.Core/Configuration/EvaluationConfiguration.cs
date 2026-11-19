namespace ModelEvaluator.Core.Configuration;

public static class AdapterIds
{
    public const string LocalFixture = "local-fixture";
    public const string DirectRest = "direct-rest";
    public const string OpenAiSdk = "openai-sdk";
}

public static class ApiKinds
{
    public const string Local = "local";
    public const string OpenAi = "openai";
    public const string AzureOpenAi = "azure-openai";
}

public static class AuthenticationKinds
{
    public const string None = "none";
    public const string ApiKey = "api-key";
    public const string DefaultCredential = "default-credential";
}

public sealed class EvaluationConfiguration
{
    public int SchemaVersion { get; init; } = 1;

    public string ScenarioRoot { get; init; } = "scenarios/v1";

    public string OutputDirectory { get; init; } = "artifacts/evaluations";

    public int Repetitions { get; init; } = 1;

    public int MaxParallel { get; init; } = 1;

    public int MaxRetries { get; init; }

    public int TimeoutSeconds { get; init; } = 120;

    public List<TargetConfiguration> Targets { get; init; } = [];

    public List<ComparisonGroupConfiguration> Comparisons { get; init; } = [];
}

public sealed class TargetConfiguration
{
    public required string Id { get; init; }

    public bool Enabled { get; init; } = true;

    public required string Adapter { get; init; }

    public required string ApiKind { get; init; }

    public required string ModelId { get; init; }

    public string? DeploymentId { get; init; }

    public string? Endpoint { get; init; }

    public string? EndpointEnvironmentVariable { get; init; }

    public string? ApiKey { get; init; }

    public string? ApiKeyEnvironmentVariable { get; init; }

    public string Authentication { get; init; } = AuthenticationKinds.ApiKey;

    public string? ApiVersion { get; init; }

    public string TokenScope { get; init; } = "https://cognitiveservices.azure.com/.default";

    public RequestParametersConfiguration Parameters { get; init; } = new();

    public ReadinessEvidenceConfiguration Evidence { get; init; } = new();

    public string ModelOrDeployment => string.IsNullOrWhiteSpace(DeploymentId) ? ModelId : DeploymentId;

    public string? ResolveEndpoint() =>
        FirstNonEmpty(EndpointEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(EndpointEnvironmentVariable), Endpoint);

    public string? ResolveApiKey() =>
        FirstNonEmpty(ApiKeyEnvironmentVariable is null
            ? null
            : Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable), ApiKey);

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : string.IsNullOrWhiteSpace(fallback) ? null : fallback;
}

public sealed class RequestParametersConfiguration
{
    public int? MaxOutputTokens { get; init; }

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public string? ReasoningEffort { get; init; }
}

public sealed class ReadinessEvidenceConfiguration
{
    public string EuDataZone { get; init; } = "unknown";

    public string? EuDataZoneReference { get; init; }

    public string Capacity { get; init; } = "unknown";

    public string? CapacityReference { get; init; }
}

public sealed class ComparisonGroupConfiguration
{
    public required string Id { get; init; }

    public required string BaselineTargetId { get; init; }

    public List<string> CandidateTargetIds { get; init; } = [];

    public string? IntendedVariable { get; init; }
}
