using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelEvaluator.Core.Scenarios;

public sealed class ScenarioDefinition
{
    public int SchemaVersion { get; init; } = 1;

    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string Title { get; init; }

    public required string Category { get; init; }

    public string Description { get; init; } = "";

    public string SystemPrompt { get; init; } = "";

    public List<ScenarioTurn> Turns { get; init; } = [];

    public ScenarioRequestParameters Parameters { get; init; } = new();

    public ScenarioResponseFormat? ResponseFormat { get; init; }

    public List<ScenarioTool> Tools { get; init; } = [];

    public int MaxToolRounds { get; init; } = 3;

    public ScenarioValidation Validation { get; init; } = new();

    public DryRunDefinition DryRun { get; init; } = new();

    [JsonIgnore]
    public string SourcePath { get; internal set; } = "";
}

public sealed class ScenarioTurn
{
    public required string User { get; init; }
}

public sealed class ScenarioRequestParameters
{
    public int? MaxOutputTokens { get; init; }

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public string? ReasoningEffort { get; init; }
}

public sealed class ScenarioResponseFormat
{
    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public JsonElement Schema { get; init; }

    public bool Strict { get; init; } = true;
}

public sealed class ScenarioTool
{
    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public JsonElement Parameters { get; init; }

    public JsonElement MockResult { get; init; }
}

public sealed class ScenarioValidation
{
    public List<string> ContainsAll { get; init; } = [];

    public List<string> DoesNotContain { get; init; } = [];

    public JsonElement? ExactJson { get; init; }

    public string? ExpectedToolName { get; init; }

    public JsonElement? ExpectedToolArguments { get; init; }

    public List<string> AllowedErrorCategories { get; init; } = [];

    public bool AllowSuccessfulCompletionWhenErrorAllowed { get; init; } = true;
}

public sealed class DryRunDefinition
{
    public List<DryRunCall> Calls { get; init; } = [];
}

public sealed class DryRunCall
{
    public int Turn { get; init; }

    public int ToolRound { get; init; }

    public string? Text { get; init; }

    public List<DryRunToolCall> ToolCalls { get; init; } = [];

    public DryRunError? Error { get; init; }
}

public sealed class DryRunToolCall
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public JsonElement Arguments { get; init; }
}

public sealed class DryRunError
{
    public required string Category { get; init; }

    public required string Code { get; init; }

    public string Message { get; init; } = "Deterministic fixture error.";

    public int? StatusCode { get; init; }
}
