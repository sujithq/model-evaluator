using System.Text.Json;

namespace ModelEvaluator.Core.Adapters;

/// <summary>Reads completed-call spans from Copilot's incremental file exporter, not cumulative metrics.</summary>
public static class CopilotTelemetryReader
{
    public static AdapterUsage Read(string path)
    {
        try
        {
            return Parse(File.ReadLines(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AdapterUsage
            {
                UsageIsPartial = true,
                Warnings = [$"Incremental usage could not be read: {path} ({ex.GetType().Name})."],
            };
        }
    }

    public static AdapterUsage SupplementToolCalls(AdapterUsage usage, string path, bool runnerCompleted)
    {
        if (usage.ToolCalls is not null)
        {
            return usage;
        }

        string warning;
        try
        {
            var snapshot = ParseSnapshot(File.ReadLines(path));
            if (runnerCompleted && snapshot.ToolCountsFinalized)
            {
                return usage with { ToolCalls = snapshot.ToolCalls };
            }
            warning = "Tool-call telemetry is incomplete or invalid; toolCalls remains unavailable. Final usage measurements are unchanged.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warning = $"Tool-call telemetry could not be read ({ex.GetType().Name}); toolCalls remains unavailable. Final usage measurements are unchanged.";
        }
        return usage with { Warnings = usage.Warnings.Append(warning).ToList() };
    }

    public static AdapterUsage Parse(IEnumerable<string> lines) => ParseSnapshot(lines).Usage;

    private sealed record TelemetrySnapshot(AdapterUsage Usage, int ToolCalls, bool ToolCountsFinalized);

    private static TelemetrySnapshot ParseSnapshot(IEnumerable<string> lines)
    {
        var calls = new List<JsonElement>();
        var ids = new HashSet<(string Trace, string Span)>();
        var completedTraces = new HashSet<string>(StringComparer.Ordinal);
        var observedTraces = new HashSet<string>(StringComparer.Ordinal);
        var tools = 0;
        var invalid = false;
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (Text(root, "type") != "span")
                {
                    continue;
                }
                if (!root.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
                {
                    invalid = true;
                    continue;
                }
                var operation = Text(attributes, "gen_ai.operation.name");
                if (operation is not ("chat" or "execute_tool" or "invoke_agent"))
                {
                    continue;
                }
                var trace = Text(root, "traceId");
                var span = Text(root, "spanId");
                if (string.IsNullOrEmpty(trace) || string.IsNullOrEmpty(span))
                {
                    invalid = true;
                    continue;
                }
                if (!ids.Add((trace, span)))
                {
                    continue;
                }
                observedTraces.Add(trace);
                if (operation == "invoke_agent")
                {
                    var parent = Text(root, "parentSpanId");
                    if ((string.IsNullOrEmpty(parent) || parent == "0000000000000000")
                        && root.TryGetProperty("endTime", out var endTime) && endTime.ValueKind == JsonValueKind.Array
                        && endTime.GetArrayLength() == 2
                        && endTime.EnumerateArray().All(part => part.ValueKind == JsonValueKind.Number))
                    {
                        completedTraces.Add(trace);
                    }
                    continue;
                }
                if (operation == "chat")
                {
                    calls.Add(attributes.Clone());
                }
                else
                {
                    tools++;
                }
            }
            catch (JsonException)
            {
                // Hard termination may truncate the final JSONL record. Retain earlier complete spans.
                invalid = true;
            }
        }

        decimal? Sum(string name, bool integer = false)
        {
            if (calls.Count == 0)
            {
                return null;
            }
            decimal sum = 0;
            foreach (var call in calls)
            {
                if (!call.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)
                    || number < 0 || (integer && number != decimal.Truncate(number))
                    || number > decimal.MaxValue - sum)
                {
                    invalid = true;
                    return null;
                }
                sum += number;
            }
            return sum;
        }

        long? Count(string name)
        {
            var sum = Sum(name, integer: true);
            if (sum > long.MaxValue)
            {
                invalid = true;
                return null;
            }
            return sum is null ? null : (long)sum.Value;
        }

        var duration = Sum("github.copilot.server_duration");
        var usage = new AdapterUsage
        {
            UsageIsPartial = true,
            InputTokens = Count("gen_ai.usage.input_tokens"),
            OutputTokens = Count("gen_ai.usage.output_tokens"),
            ApiRequests = calls.Count > 0 ? calls.Count : null,
            ToolCalls = tools > 0 ? tools : null,
            ApiDurationSeconds = duration is null ? null : (double)(duration.Value / 1000m),
            // This attribute is the legacy request multiplier, not AI credits or dollars.
            PremiumRequests = Sum("github.copilot.cost"),
            ReportedModels = calls.Select(call =>
                    Text(call, "gen_ai.response.model") ?? Text(call, "gen_ai.request.model"))
                .OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList(),
        };
        var warnings = new List<string>
        {
            "Usage is partial: completed telemetry spans/checkpoints only. In-flight calls and unflushed records may be absent; excluded from complete totals and AI-credit ranking.",
        };
        if (invalid)
        {
            warnings.Add("Some telemetry records or measurements were invalid/truncated and could not be counted.");
        }
        if (calls.Count == 0 && tools == 0)
        {
            warnings.Add("No completed model/tool spans were captured.");
        }
        return new TelemetrySnapshot(usage with { Warnings = warnings }, tools,
            !invalid && completedTraces.Count > 0 && observedTraces.IsSubsetOf(completedTraces));
    }

    /// <summary>Recovers the latest cumulative credit checkpoint from this attempt's fresh session only.</summary>
    public static AdapterUsage ReadCheckpoints(AdapterUsage usage, string sessionPath, string artifactPath)
    {
        try
        {
            using var artifact = new StreamWriter(artifactPath);
            return WithCheckpoints(usage, File.ReadLines(sessionPath), line => artifact.WriteLine(line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return usage with
            {
                Warnings = usage.Warnings.Append($"Usage checkpoints could not be read or saved ({ex.GetType().Name}); AI credits remain unavailable.").ToList(),
            };
        }
    }

    public static AdapterUsage WithCheckpoints(AdapterUsage usage, IEnumerable<string> lines, Action<string>? onCheckpoint = null)
    {
        decimal? credits = null;
        var invalid = false;
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (Text(root, "type") != "session.usage_checkpoint"
                    || (root.TryGetProperty("agentId", out var agent) && agent.ValueKind != JsonValueKind.Null))
                {
                    continue;
                }
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                    || !data.TryGetProperty("totalNanoAiu", out var value) || value.ValueKind != JsonValueKind.Number
                    || !value.TryGetDecimal(out var nano) || nano < 0)
                {
                    invalid = true;
                    continue;
                }
                // Checkpoints are cumulative, not deltas; never add them to each other or to spans.
                credits = Math.Max(credits ?? 0, nano / 1_000_000_000m);
                onCheckpoint?.Invoke(line);
            }
            catch (JsonException)
            {
                invalid = true;
            }
        }
        var warnings = usage.Warnings.ToList();
        if (credits is null)
        {
            warnings.Add("No session AI-credit checkpoint was captured; AI credits remain unavailable.");
        }
        if (invalid)
        {
            warnings.Add("Some session records/checkpoints were invalid or truncated and could not be counted.");
        }
        return usage with { UsageIsPartial = true, AiCredits = credits, Warnings = warnings };
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
