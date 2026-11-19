namespace ModelEvaluator.Core.Configuration;

public sealed class ConfigurationValidationResult
{
    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public bool IsValid => Errors.Count == 0;
}

public static class ConfigurationValidator
{
    private static readonly HashSet<string> Adapters =
        [AdapterIds.LocalFixture, AdapterIds.DirectRest, AdapterIds.OpenAiSdk];

    private static readonly HashSet<string> ApiKinds =
        [Configuration.ApiKinds.Local, Configuration.ApiKinds.OpenAi, Configuration.ApiKinds.AzureOpenAi];

    private static readonly HashSet<string> EvidenceStates =
        ["unknown", "confirmed", "not-confirmed", "not-applicable"];

    private static readonly HashSet<string> ReasoningEfforts =
        ["none", "minimal", "low", "medium", "high"];

    public static ConfigurationValidationResult Validate(
        EvaluationConfiguration configuration,
        bool requireLiveCredentials)
    {
        var result = new ConfigurationValidationResult();

        if (configuration.SchemaVersion != 1)
        {
            result.Errors.Add($"Unsupported configuration schemaVersion '{configuration.SchemaVersion}'. Expected 1.");
        }

        if (configuration.Repetitions < 1)
        {
            result.Errors.Add("repetitions must be at least 1.");
        }

        if (configuration.MaxParallel < 1)
        {
            result.Errors.Add("maxParallel must be at least 1.");
        }

        if (configuration.MaxRetries < 0)
        {
            result.Errors.Add("maxRetries cannot be negative.");
        }

        if (configuration.TimeoutSeconds < 1)
        {
            result.Errors.Add("timeoutSeconds must be at least 1.");
        }

        var enabledTargets = configuration.Targets.Where(target => target.Enabled).ToList();
        if (enabledTargets.Count == 0)
        {
            result.Errors.Add("The configuration does not contain an enabled target.");
        }

        foreach (var duplicate in enabledTargets.GroupBy(target => target.Id, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            result.Errors.Add($"Target id '{duplicate.Key}' is duplicated.");
        }

        foreach (var target in enabledTargets)
        {
            ValidateTarget(target, requireLiveCredentials, result);
        }

        var targetIds = enabledTargets.Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in configuration.Comparisons)
        {
            if (!targetIds.Contains(group.BaselineTargetId))
            {
                result.Errors.Add(
                    $"Comparison '{group.Id}' references unknown baseline target '{group.BaselineTargetId}'.");
            }

            foreach (var targetId in group.CandidateTargetIds.Where(id => !targetIds.Contains(id)))
            {
                result.Errors.Add($"Comparison '{group.Id}' references unknown candidate target '{targetId}'.");
            }
        }

        return result;
    }

    private static void ValidateTarget(
        TargetConfiguration target,
        bool requireLiveCredentials,
        ConfigurationValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(target.Id))
        {
            result.Errors.Add("Every target must have a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(target.ModelId))
        {
            result.Errors.Add($"Target '{target.Id}' must specify modelId.");
        }

        ValidateParameters(target, result);
        ValidateEvidence(target, result);

        if (!Adapters.Contains(target.Adapter))
        {
            result.Errors.Add(
                $"Target '{target.Id}' uses unsupported adapter '{target.Adapter}'. " +
                $"Supported adapters: {string.Join(", ", Adapters.Order())}.");
            return;
        }

        if (!ApiKinds.Contains(target.ApiKind))
        {
            result.Errors.Add($"Target '{target.Id}' uses unsupported apiKind '{target.ApiKind}'.");
            return;
        }

        if (target.Adapter == AdapterIds.LocalFixture)
        {
            if (target.ApiKind != Configuration.ApiKinds.Local
                || target.Authentication != AuthenticationKinds.None)
            {
                result.Errors.Add(
                    $"Target '{target.Id}' is unsupported: local-fixture requires apiKind=local and authentication=none.");
            }

            return;
        }

        if (target.ApiKind is not (Configuration.ApiKinds.OpenAi or Configuration.ApiKinds.AzureOpenAi))
        {
            result.Errors.Add(
                $"Target '{target.Id}' is unsupported: adapter '{target.Adapter}' cannot use apiKind '{target.ApiKind}'.");
            return;
        }

        if (target.ApiKind == Configuration.ApiKinds.OpenAi
            && target.Authentication != AuthenticationKinds.ApiKey)
        {
            result.Errors.Add(
                $"Target '{target.Id}' is unsupported: OpenAI currently supports api-key authentication in this harness.");
        }

        if (target.ApiKind == Configuration.ApiKinds.AzureOpenAi
            && target.Authentication is not (AuthenticationKinds.ApiKey or AuthenticationKinds.DefaultCredential))
        {
            result.Errors.Add(
                $"Target '{target.Id}' is unsupported: Azure OpenAI supports api-key or default-credential authentication.");
        }

        if (target.Authentication == AuthenticationKinds.ApiKey
            && requireLiveCredentials
            && string.IsNullOrWhiteSpace(target.ResolveApiKey()))
        {
            result.Errors.Add(
                $"Target '{target.Id}' has no API key. Set '{target.ApiKeyEnvironmentVariable ?? "the configured environment variable"}', " +
                "user-secrets key Targets:<index>:ApiKey, or run in dry mode.");
        }

        if (requireLiveCredentials && string.IsNullOrWhiteSpace(target.ResolveEndpoint()))
        {
            result.Errors.Add(
                $"Target '{target.Id}' has no endpoint. Set the endpoint in user secrets, a " +
                $"{ConfigurationLoader.EnvironmentPrefix} environment override, or '{target.EndpointEnvironmentVariable ?? "the configured environment variable"}'.");
        }

        var endpointValue = target.ResolveEndpoint();
        if (!string.IsNullOrWhiteSpace(endpointValue))
        {
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint))
            {
                result.Errors.Add($"Target '{target.Id}' endpoint is not an absolute URI.");
            }
            else if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                     && !(string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                          && endpoint.IsLoopback))
            {
                result.Errors.Add(
                    $"Target '{target.Id}' endpoint must use HTTPS; HTTP is allowed only for loopback test endpoints.");
            }
            else if (target.Authentication == AuthenticationKinds.DefaultCredential
                     && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    $"Target '{target.Id}' uses default-credential authentication, which requires an HTTPS endpoint.");
            }
        }

        if (target.ApiKind == Configuration.ApiKinds.AzureOpenAi
            && string.IsNullOrWhiteSpace(target.ModelOrDeployment))
        {
            result.Errors.Add($"Target '{target.Id}' requires an exact Azure deployment id.");
        }

        if (target.Adapter == AdapterIds.OpenAiSdk
            && target.ApiKind == Configuration.ApiKinds.AzureOpenAi
            && !string.IsNullOrWhiteSpace(target.ApiVersion)
            && !string.Equals(target.ApiVersion, "v1", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add(
                $"Target '{target.Id}' selects API version '{target.ApiVersion}', but the openai-sdk adapter " +
                "uses Azure OpenAI's /openai/v1 surface. Use direct-rest for a deployment-specific apiVersion.");
        }

        if (string.Equals(target.Evidence.EuDataZone, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add($"Target '{target.Id}' has no EU Data Zone deployment evidence; this is not a pass.");
        }

        if (string.Equals(target.Evidence.Capacity, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            result.Warnings.Add($"Target '{target.Id}' has no confirmed capacity evidence; this is not a pass.");
        }
    }

    private static void ValidateParameters(
        TargetConfiguration target,
        ConfigurationValidationResult result)
    {
        if (target.Parameters.MaxOutputTokens is <= 0)
        {
            result.Errors.Add($"Target '{target.Id}' maxOutputTokens must be positive when specified.");
        }

        if (target.Parameters.Temperature is < 0 or > 2)
        {
            result.Errors.Add($"Target '{target.Id}' temperature must be between 0 and 2.");
        }

        if (target.Parameters.TopP is < 0 or > 1)
        {
            result.Errors.Add($"Target '{target.Id}' topP must be between 0 and 1.");
        }

        if (!string.IsNullOrWhiteSpace(target.Parameters.ReasoningEffort)
            && !ReasoningEfforts.Contains(target.Parameters.ReasoningEffort))
        {
            result.Errors.Add(
                $"Target '{target.Id}' reasoningEffort must be one of {string.Join(", ", ReasoningEfforts)}.");
        }
    }

    private static void ValidateEvidence(
        TargetConfiguration target,
        ConfigurationValidationResult result)
    {
        var evidence = new[]
        {
            ("EU Data Zone", target.Evidence.EuDataZone, target.Evidence.EuDataZoneReference),
            ("capacity", target.Evidence.Capacity, target.Evidence.CapacityReference),
        };
        foreach (var (name, state, reference) in evidence)
        {
            if (!EvidenceStates.Contains(state))
            {
                result.Errors.Add(
                    $"Target '{target.Id}' has invalid {name} evidence state '{state}'. " +
                    $"Expected one of {string.Join(", ", EvidenceStates.Order())}.");
            }
            else if (state == "confirmed" && string.IsNullOrWhiteSpace(reference))
            {
                result.Warnings.Add(
                    $"Target '{target.Id}' marks {name} as confirmed without a reference; it will be reported unverified.");
            }
        }
    }
}
