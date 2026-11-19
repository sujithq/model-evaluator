using System.Text.Json;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Infrastructure;
using ModelEvaluator.Core.Results;

namespace ModelEvaluator.Core.Evaluation;

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
        AddIfChanged(changed, "deployment", baseline.DeploymentId, candidate.DeploymentId);
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
            AdapterIds.DirectRest or AdapterIds.OpenAiSdk => "v1",
            _ => "fixture",
        };
}
