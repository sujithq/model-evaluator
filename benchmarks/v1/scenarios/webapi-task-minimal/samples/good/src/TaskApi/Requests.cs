using System.Text.Json.Serialization;

namespace TaskApi;

/// <summary>Payload accepted by <c>POST /tasks</c>.</summary>
public sealed record CreateTaskRequest([property: JsonPropertyName("title")] string? Title);

/// <summary>Payload accepted by <c>PUT /tasks/{id}</c>.</summary>
public sealed record UpdateTaskRequest(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("completed")] bool? Completed);
