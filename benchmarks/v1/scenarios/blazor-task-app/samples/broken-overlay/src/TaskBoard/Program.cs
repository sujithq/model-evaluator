// Deliberately broken variant used to prove that the evaluator detects contract violations.
using Microsoft.AspNetCore.Http;
using TaskBoard;
using TaskBoard.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents();
builder.Services.AddAntiforgery();
builder.Services.AddSingleton(sp =>
{
    var path = Environment.GetEnvironmentVariable("TASKS_FILE");
    if (string.IsNullOrWhiteSpace(path))
    {
        var environment = sp.GetRequiredService<IWebHostEnvironment>();
        path = Path.Combine(environment.ContentRootPath, "tasks.json");
    }

    return new TaskStore(path);
});

var app = builder.Build();

app.UseAntiforgery();

app.MapGet("/health", () => Results.Text("ok", "text/plain"));

app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.Equals("/tasks", StringComparison.Ordinal)
        && context.Request.Query.TryGetValue("status", out var value))
    {
        var status = value.ToString();
        if (status.Length > 0 && status is not ("all" or "pending" or "completed"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(
                "<!DOCTYPE html><html><body><p>Unknown status filter.</p></body></html>");
            return;
        }
    }

    await next();
});

app.MapRazorComponents<App>();

// BROKEN: title-length validation is missing; only the required-title check remains.
app.MapPost("/tasks/add", async (HttpRequest request, TaskStore store) =>
{
    var form = await request.ReadFormAsync();
    var title = (form["title"].ToString() ?? string.Empty).Trim();
    if (title.Length == 0)
    {
        return ValidationError("Title is required.");
    }

    var tasks = store.Load();
    var id = TaskStore.NextId(tasks);
    tasks.Add(new TaskItem
    {
        Id = id,
        Title = title,
        Completed = false,
        CreatedAt = DateTimeOffset.UtcNow,
    });
    store.Save(tasks);
    return Results.Redirect("/tasks");
}).DisableAntiforgery();

app.MapPost("/tasks/{id:int}/complete", (int id, TaskStore store) =>
{
    var tasks = store.Load();
    var index = tasks.FindIndex(t => t.Id == id);
    if (index < 0)
    {
        return Results.NotFound("Task not found.");
    }

    tasks[index] = tasks[index] with { Completed = true };
    store.Save(tasks);
    return Results.Redirect("/tasks");
}).DisableAntiforgery();

app.MapPost("/tasks/{id:int}/edit", async (int id, HttpRequest request, TaskStore store) =>
{
    var form = await request.ReadFormAsync();
    if (!TaskValidator.TryValidate(form["title"], out var title, out var error))
    {
        return ValidationError(error);
    }

    var tasks = store.Load();
    var index = tasks.FindIndex(t => t.Id == id);
    if (index < 0)
    {
        return Results.NotFound("Task not found.");
    }

    tasks[index] = tasks[index] with { Title = title };
    store.Save(tasks);
    return Results.Redirect("/tasks");
}).DisableAntiforgery();

// BROKEN: the in-memory list is mutated but never persisted, so deletes are lost on restart / next request.
app.MapPost("/tasks/{id:int}/delete", (int id, TaskStore store) =>
{
    var tasks = store.Load();
    var index = tasks.FindIndex(t => t.Id == id);
    if (index < 0)
    {
        return Results.NotFound("Task not found.");
    }

    tasks.RemoveAt(index);
    // store.Save(tasks) omitted on purpose.
    return Results.Redirect("/tasks");
}).DisableAntiforgery();

app.Run();

static IResult ValidationError(string message) =>
    Results.Content(
        $"<!DOCTYPE html><html><body><p>{message}</p></body></html>",
        "text/html; charset=utf-8",
        statusCode: StatusCodes.Status400BadRequest);

public partial class Program;
