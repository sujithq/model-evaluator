using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TaskApi;

namespace TaskApi.Tests;

public sealed class TaskApiTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        "taskapi-tests",
        Guid.NewGuid().ToString("N"),
        "tasks.db");

    private readonly WebApplicationFactory<Program> _factory;

    public TaskApiTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        Environment.SetEnvironmentVariable("TASKS_DB_PATH", _databasePath);
        _factory = new WebApplicationFactory<Program>();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("TASKS_DB_PATH", null);
        try
        {
            Directory.Delete(Path.GetDirectoryName(_databasePath)!, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_CreatesTaskAndReturnsLocation()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tasks", new { title = "Buy milk" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/tasks/1", response.Headers.Location?.ToString());
        var task = await response.Content.ReadFromJsonAsync<TaskItem>();
        Assert.NotNull(task);
        Assert.Equal("Buy milk", task!.Title);
        Assert.False(task.Completed);
    }

    [Fact]
    public async Task Post_RejectsBlankTitle()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/tasks", new { title = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_OrdersByIdAscending()
    {
        using var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/tasks", new { title = "First" });
        await client.PostAsJsonAsync("/tasks", new { title = "Second" });

        var tasks = await client.GetFromJsonAsync<TaskItem[]>("/tasks");

        Assert.NotNull(tasks);
        Assert.Collection(tasks!,
            t => Assert.Equal("First", t.Title),
            t => Assert.Equal("Second", t.Title));
    }

    [Fact]
    public async Task Put_UpdatesFieldsAndPreservesCreatedAt()
    {
        using var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/tasks", new { title = "Original" });
        var task = await created.Content.ReadFromJsonAsync<TaskItem>();

        var response = await client.PutAsJsonAsync(
            $"/tasks/{task!.Id}",
            new { title = "Updated", completed = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<TaskItem>();
        Assert.Equal("Updated", updated!.Title);
        Assert.True(updated.Completed);
        Assert.Equal(task.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task Delete_RemovesTask()
    {
        using var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/tasks", new { title = "Doomed" });
        var task = await created.Content.ReadFromJsonAsync<TaskItem>();

        var deleted = await client.DeleteAsync($"/tasks/{task!.Id}");
        var fetched = await client.GetAsync($"/tasks/{task.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }
}
