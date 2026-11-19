using System.Diagnostics;
using System.Text.Json;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Infrastructure;

namespace ModelEvaluator.Core.Adapters;

public sealed class LocalFixtureAdapter : IModelAdapter
{
    public string Id => AdapterIds.LocalFixture;

    public string DisplayName => "Deterministic local fixture";

    public CompatibilityResult CheckCompatibility(TargetConfiguration target)
    {
        if (target.Adapter != Id)
        {
            return new CompatibilityResult(false, $"Target requests '{target.Adapter}', not '{Id}'.");
        }

        if (target.ApiKind != ApiKinds.Local || target.Authentication != AuthenticationKinds.None)
        {
            return new CompatibilityResult(
                false,
                "local-fixture requires apiKind=local and authentication=none.");
        }

        return new CompatibilityResult(true, "Synthetic offline fixture execution is supported.");
    }

    public AdapterPackage DescribePackage(TargetConfiguration target) => new()
    {
        Name = "in-process-fixtures",
        Version = AssemblyMetadata.ApplicationVersion,
    };

    public Task<AdapterCompletion> CompleteAsync(
        AdapterInvocation invocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var fixture = invocation.Scenario.DryRun.Calls.FirstOrDefault(
            call => call.Turn == invocation.TurnIndex && call.ToolRound == invocation.ToolRound);

        if (fixture is null)
        {
            return Task.FromResult(CreateError(
                started,
                stopwatch.Elapsed.TotalMilliseconds,
                new AdapterError
                {
                    Category = "fixture-error",
                    Code = "fixture-not-found",
                    Message =
                        $"No dry-run fixture exists for turn {invocation.TurnIndex}, tool round {invocation.ToolRound}.",
                }));
        }

        var toolCalls = fixture.ToolCalls.Select(call => new ModelToolCall
        {
            Id = call.Id,
            Name = call.Name,
            Arguments = call.Arguments.Clone(),
        }).ToList();

        var error = fixture.Error is null
            ? null
            : new AdapterError
            {
                Category = fixture.Error.Category,
                Code = fixture.Error.Code,
                Message = fixture.Error.Message,
                StatusCode = fixture.Error.StatusCode,
            };
        stopwatch.Stop();

        return Task.FromResult(new AdapterCompletion
        {
            StartedAt = started,
            CompletedAt = DateTimeOffset.UtcNow,
            LatencyMilliseconds = stopwatch.Elapsed.TotalMilliseconds,
            Retries = 0,
            FinishReason = error is null
                ? toolCalls.Count > 0 ? "tool_calls" : "stop"
                : "error",
            Text = fixture.Text,
            ToolCalls = toolCalls,
            Error = error,
            Usage = new TokenUsage(),
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        });
    }

    private static AdapterCompletion CreateError(
        DateTimeOffset startedAt,
        double elapsedMilliseconds,
        AdapterError error) => new()
        {
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            LatencyMilliseconds = elapsedMilliseconds,
            FinishReason = "error",
            Error = error,
            UnavailableMetrics = ["timeToFirstByteMilliseconds", "tokenUsage"],
        };
}
