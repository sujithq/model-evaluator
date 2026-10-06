// Deliberately broken variant used to prove that the evaluator detects contract violations.
// - `POST /tasks` returns `200 OK` instead of `201 Created` and does not emit a `Location` header.
// - Title validation is bypassed: blank and 300-character titles are accepted.
// - The `status` query parameter validation is removed for `GET /tasks`.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace TaskApi;

public static class TaskEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Text("ok", "text/plain"));

        endpoints.MapGet("/tasks", (
            [FromQuery(Name = "status")] string? status,
            [FromQuery(Name = "search")] string? search,
            TaskStore store) =>
        {
            var normalized = string.IsNullOrEmpty(status) ? "all" : status;
            return Results.Ok(store.Query(normalized, string.IsNullOrEmpty(search) ? null : search));
        });

        endpoints.MapGet("/tasks/{id:int}", (int id, TaskStore store) =>
        {
            var task = store.FindById(id);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        endpoints.MapPost("/tasks", (CreateTaskRequest? request, TaskStore store) =>
        {
            var title = request?.Title ?? string.Empty;
            var created = store.Insert(title, DateTimeOffset.UtcNow);
            return Results.Ok(created);
        });

        endpoints.MapPut("/tasks/{id:int}", (int id, UpdateTaskRequest? request, TaskStore store) =>
        {
            var title = request?.Title ?? string.Empty;
            var completed = request?.Completed ?? false;
            var updated = store.Update(id, title, completed);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        endpoints.MapDelete("/tasks/{id:int}", (int id, TaskStore store) =>
            store.Delete(id) ? Results.NoContent() : Results.NotFound());
    }
}
