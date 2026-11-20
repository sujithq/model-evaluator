using System.Text.Json.Serialization;

namespace JobWorker;

/// <summary>Raw shape of a job file as it appears on disk.</summary>
public sealed record JobDefinition
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("payload")]
    public string? Payload { get; init; }

    [JsonPropertyName("failuresBeforeSuccess")]
    public int FailuresBeforeSuccess { get; init; }

    [JsonPropertyName("fatal")]
    public bool Fatal { get; init; }
}

/// <summary>Result of processing a single job. Shape is serialized into results.json verbatim.</summary>
public sealed record JobResult
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("attempts")]
    public required int Attempts { get; init; }

    [JsonPropertyName("result")]
    public string? Result { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
