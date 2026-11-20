using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace TaskApi;

/// <summary>Registers the HTTP endpoints defined by the scenario contract.</summary>
public static class TaskEndpoints
{
    private const int MaximumTitleLength = 200;
    private static readonly string[] AllowedStatuses = ["all", "pending", "completed"];

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Text("ok", "text/plain"));

        endpoints.MapGet("/tasks", (
            [FromQuery(Name = "status")] string? status,
            [FromQuery(Name = "search")] string? search,
            TaskStore store) =>
        {
            var normalizedStatus = string.IsNullOrEmpty(status) ? "all" : status;
            if (Array.IndexOf(AllowedStatuses, normalizedStatus) < 0)
            {
                return Results.Problem(
                    detail: $"Unsupported status '{status}'. Allowed values are: all, pending, completed.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid status");
            }

            return Results.Ok(store.Query(normalizedStatus, string.IsNullOrEmpty(search) ? null : search));
        });

        endpoints.MapGet("/tasks/{id:int}", (int id, TaskStore store) =>
        {
            var task = store.FindById(id);
            return task is null ? Results.NotFound() : Results.Ok(task);
        });

        endpoints.MapPost("/tasks", (CreateTaskRequest? request, TaskStore store) =>
        {
            if (!TryValidateTitle(request?.Title, out var problem))
            {
                return problem;
            }

            var created = store.Insert(request!.Title!, DateTimeOffset.UtcNow);
            return Results.Created($"/tasks/{created.Id}", created);
        });

        endpoints.MapPut("/tasks/{id:int}", (int id, UpdateTaskRequest? request, TaskStore store) =>
        {
            if (request is null || !request.Completed.HasValue)
            {
                return Problem("The 'completed' field is required.");
            }

            if (!TryValidateTitle(request.Title, out var problem))
            {
                return problem;
            }

            var updated = store.Update(id, request.Title!, request.Completed.Value);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        endpoints.MapDelete("/tasks/{id:int}", (int id, TaskStore store) =>
            store.Delete(id) ? Results.NoContent() : Results.NotFound());
    }

    private static bool TryValidateTitle(string? title, out IResult problem)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            problem = Problem("The 'title' field is required and must not be blank.");
            return false;
        }

        if (title.Length > MaximumTitleLength)
        {
            problem = Problem($"The 'title' field must be {MaximumTitleLength} characters or fewer.");
            return false;
        }

        problem = Results.Empty;
        return true;
    }

    private static IResult Problem(string detail) => Results.Problem(
        detail: detail,
        statusCode: StatusCodes.Status400BadRequest,
        title: "Validation failed");
}
