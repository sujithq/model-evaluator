using System.Text.Json;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Infrastructure;
using ModelEvaluator.ApiComparison.Core.Results;

namespace ModelEvaluator.ApiComparison.Core.Evaluation;

public static class ComparisonAnalyzer
{
    public static IReadOnlyList<ComparisonResult> Analyze(
        EvaluationConfiguration configuration,
        IReadOnlyList<TargetConfiguration> selectedTargets,
        IReadOnlyList<AttemptResult> attempts)
    {
        var selected = selectedTargets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        var results = new List<ComparisonResult>();
        foreach (var group in configuration.Comparisons)
        {
            if (!selected.TryGetValue(group.BaselineTargetId, out var baseline))
            {
                continue;
            }

            foreach (var candidateId in group.CandidateTargetIds)
            {
                if (!selected.TryGetValue(candidateId, out var candidate))
                {
                    continue;
                }

                var dimensions = FindChangedDimensions(baseline, candidate);
                var baselinePass = attempts.Where(attempt =>
                        attempt.Target.Id == baseline.Id)
                    .All(attempt => attempt.Passed);
                var candidatePass = attempts.Where(attempt =>
                        attempt.Target.Id == candidate.Id)
                    .All(attempt => attempt.Passed);
                var notes = new List<string>
                {
                    $"Baseline functional outcome: {(baselinePass ? "passed" : "failed")}.",
                    $"Candidate functional outcome: {(candidatePass ? "passed" : "failed")}.",
                };

                if (dimensions.Count != 1)
                {
                    notes.Add(dimensions.Count == 0
                        ? "No configuration dimension changed; this pair is not a controlled comparison."
                        : $"Multiple dimensions changed: {string.Join(", ", dimensions)}.");
                }
                else if (group.IntendedVariable is not null
                         && !string.Equals(group.IntendedVariable, dimensions[0], StringComparison.Ordinal))
                {
                    notes.Add(
                        $"The pair changed '{dimensions[0]}', not the declared '{group.IntendedVariable}' variable.");
                }
                else
                {
                    notes.Add($"Exactly one dimension changed: '{dimensions[0]}'.");
                }

                results.Add(new ComparisonResult
                {
                    Id = group.Id,
                    BaselineTargetId = baseline.Id,
                    CandidateTargetId = candidate.Id,
                    IntendedVariable = group.IntendedVariable,
                    ChangedDimensions = dimensions,
                    Outcome = dimensions.Count == 1
                              && (group.IntendedVariable is null
                                  || string.Equals(
                                      dimensions[0],
                                      group.IntendedVariable,
                                      StringComparison.Ordinal))
                        ? "controlled"
                        : "not-controlled",
                    Notes = notes,
                });
            }
        }

        return results;
    }

    private static List<string> FindChangedDimensions(
        TargetConfiguration baseline,
        TargetConfiguration candidate)
    {
        var changed = new List<string>();
        AddIfChanged(changed, "model", baseline.ModelId, candidate.ModelId);
        if (HasDistinctDeploymentVariable(baseline, candidate))
        {
            AddIfChanged(changed, "deployment", baseline.ModelOrDeployment, candidate.ModelOrDeployment);
        }
        AddIfChanged(changed, "api-kind", baseline.ApiKind, candidate.ApiKind);
        AddIfChanged(changed, "adapter", baseline.Adapter, candidate.Adapter);
        AddIfChanged(changed, "authentication", baseline.Authentication, candidate.Authentication);
        AddIfChanged(changed, "token-scope", baseline.TokenScope, candidate.TokenScope);
        AddIfChanged(
            changed,
            "api-version",
            EffectiveApiVersion(baseline),
            EffectiveApiVersion(candidate));
        AddIfChanged(
            changed,
            "request-parameters",
            Hashing.Sha256(JsonSerializer.Serialize(baseline.Parameters, JsonDefaults.Read)),
            Hashing.Sha256(JsonSerializer.Serialize(candidate.Parameters, JsonDefaults.Read)));
        AddIfChanged(
            changed,
            "endpoint",
            Hashing.Sha256(baseline.ResolveEndpoint() ?? ""),
            Hashing.Sha256(candidate.ResolveEndpoint() ?? ""));
        return changed;
    }

    private static void AddIfChanged(
        ICollection<string> dimensions,
        string dimension,
        string? baseline,
        string? candidate)
    {
        if (!string.Equals(baseline, candidate, StringComparison.Ordinal))
        {
            dimensions.Add(dimension);
        }
    }

    private static string EffectiveApiVersion(TargetConfiguration target) =>
        target.Adapter switch
        {
            AdapterIds.DirectRest when target.ApiKind == ApiKinds.AzureOpenAi =>
                string.IsNullOrWhiteSpace(target.ApiVersion)
                    ? "v1"
                    : target.ApiVersion.ToLowerInvariant(),
            AdapterIds.DirectRest
                or AdapterIds.OpenAiSdk
                or AdapterIds.DirectRestResponses
                or AdapterIds.OpenAiSdkResponses
                or AdapterIds.MicrosoftExtensionsAi
                or AdapterIds.MicrosoftExtensionsAiResponses
                or AdapterIds.SemanticKernel => "v1",
            AdapterIds.CopilotCli => "programmatic-cli",
            _ => "fixture",
        };

    private static bool HasDistinctDeploymentVariable(
        TargetConfiguration baseline,
        TargetConfiguration candidate)
    {
        var baselineDeploymentAliasesModel =
            string.IsNullOrWhiteSpace(baseline.DeploymentId)
            || string.Equals(baseline.DeploymentId, baseline.ModelId, StringComparison.Ordinal);
        var candidateDeploymentAliasesModel =
            string.IsNullOrWhiteSpace(candidate.DeploymentId)
            || string.Equals(candidate.DeploymentId, candidate.ModelId, StringComparison.Ordinal);

        return !baselineDeploymentAliasesModel
               || !candidateDeploymentAliasesModel
               || string.Equals(baseline.ModelId, candidate.ModelId, StringComparison.Ordinal);
    }
}
