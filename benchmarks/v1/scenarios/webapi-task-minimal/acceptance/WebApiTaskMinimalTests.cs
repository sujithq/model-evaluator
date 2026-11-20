using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelEvaluator.Acceptance;
using Xunit;

namespace WebApiTaskMinimal.Acceptance;

/// <summary>
/// Evaluator-owned acceptance checks for the minimal Web API task manager. Each test starts the generated
/// application against an isolated SQLite database file so the tests can run in parallel without shared
/// state. These tests are never visible to the evaluated model and cover cases beyond the examples in the
/// scenario instructions.
/// </summary>
public sealed class WebApiTaskMinimalTests
{
    private static readonly string AssemblyPath = EvaluationContext.FindApplicationAssembly();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Task(int Id, string Title, bool Completed, DateTimeOffset CreatedAt);

    private static async Task<WebAppHost> StartAsync(string? databasePath = null, string? workingDirectory = null)
    {
        workingDirectory ??= EvaluationContext.CreateTempDirectory();
        databasePath ??= Path.Combine(workingDirectory, "tasks.db");
        return await WebAppHost.StartAsync(
            AssemblyPath,
            workingDirectory,
            new Dictionary<string, string> { ["TASKS_DB_PATH"] = databasePath },
            "/health");
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, Json)
            ?? throw new InvalidOperationException($"Response body was empty; status={response.StatusCode}.");
    }

    private static StringContent JsonBody(string body) =>
        new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async global::System.Threading.Tasks.Task Health_ReturnsPlainOk()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_OnEmptyStore_ReturnsEmptyArray()
    {
        await using var host = await StartAsync();

        var tasks = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks"));

        Assert.Empty(tasks);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_ReturnsCreatedWithLocationAndBody()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Buy milk" }"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await ReadAsync<Task>(response);
        Assert.Equal(1, task.Id);
        Assert.Equal("Buy milk", task.Title);
        Assert.False(task.Completed);
        Assert.Equal(TimeSpan.Zero, task.CreatedAt.Offset);
        Assert.Equal($"/tasks/{task.Id}", response.Headers.Location?.ToString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_UsesCamelCasePropertyNames()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Case check" }"""));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"id\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"title\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"completed\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"createdAt\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"CreatedAt\"", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_RejectsMissingBlankAndTooLongTitles()
    {
        await using var host = await StartAsync();

        var missing = await host.Client.PostAsync("/tasks", JsonBody("""{ }"""));
        var blank = await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "   " }"""));
        var tooLong = await host.Client.PostAsync(
            "/tasks",
            JsonBody($$"""{ "title": "{{new string('a', 201)}}" }"""));

        foreach (var response in new[] { missing, blank, tooLong })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PostTask_AcceptsExactly200Characters()
    {
        await using var host = await StartAsync();
        var boundary = new string('t', 200);

        var response = await host.Client.PostAsync(
            "/tasks",
            JsonBody($$"""{ "title": "{{boundary}}" }"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var task = await ReadAsync<Task>(response);
        Assert.Equal(200, task.Title.Length);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTask_ReturnsSingleTask_And404_ForUnknownId()
    {
        await using var host = await StartAsync();
        var created = await ReadAsync<Task>(await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Solo" }""")));

        var fetched = await host.Client.GetAsync($"/tasks/{created.Id}");
        var missing = await host.Client.GetAsync("/tasks/9999");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("Solo", (await ReadAsync<Task>(fetched)).Title);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_ReturnsResultsInAscendingIdOrder()
    {
        await using var host = await StartAsync();
        foreach (var title in new[] { "Third", "First", "Second" })
        {
            await host.Client.PostAsync("/tasks", JsonBody($$"""{ "title": "{{title}}" }"""));
        }

        var tasks = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks"));

        Assert.Equal(new[] { 1, 2, 3 }, tasks.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_StatusFilter_SplitsPendingAndCompleted()
    {
        await using var host = await StartAsync();
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "One" }"""));
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Two" }"""));
        await host.Client.PutAsync("/tasks/2", JsonBody("""{ "title": "Two", "completed": true }"""));

        var pending = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks?status=pending"));
        var completed = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks?status=completed"));
        var all = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks?status=all"));

        Assert.Equal(new[] { 1 }, pending.Select(t => t.Id).ToArray());
        Assert.Equal(new[] { 2 }, completed.Select(t => t.Id).ToArray());
        Assert.Equal(2, all.Length);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_RejectsUnsupportedStatus_WithProblemDetails()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/tasks?status=archived");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_SearchFilterIsCaseInsensitiveSubstringMatch()
    {
        await using var host = await StartAsync();
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Buy milk" }"""));
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Write report" }"""));
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Read book" }"""));

        var matches = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks?search=REPORT"));
        var noMatch = await ReadAsync<Task[]>(await host.Client.GetAsync("/tasks?search=xylophone"));

        Assert.Single(matches);
        Assert.Equal("Write report", matches[0].Title);
        Assert.Empty(noMatch);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task GetTasks_CombinesStatusAndSearchFilters()
    {
        await using var host = await StartAsync();
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Read book" }"""));
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Read paper" }"""));
        await host.Client.PutAsync("/tasks/1", JsonBody("""{ "title": "Read book", "completed": true }"""));

        var completedRead = await ReadAsync<Task[]>(
            await host.Client.GetAsync("/tasks?status=completed&search=read"));

        Assert.Single(completedRead);
        Assert.Equal("Read book", completedRead[0].Title);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PutTask_UpdatesTitleAndCompleted_PreservingCreatedAt()
    {
        await using var host = await StartAsync();
        var created = await ReadAsync<Task>(
            await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Original" }""")));

        var updated = await ReadAsync<Task>(await host.Client.PutAsync(
            $"/tasks/{created.Id}",
            JsonBody("""{ "title": "Renamed", "completed": true }""")));

        Assert.Equal("Renamed", updated.Title);
        Assert.True(updated.Completed);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PutTask_ReturnsNotFoundForUnknownId()
    {
        await using var host = await StartAsync();

        var response = await host.Client.PutAsync(
            "/tasks/12345",
            JsonBody("""{ "title": "Nope", "completed": false }"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PutTask_RejectsBlankTitleAndTooLongTitle()
    {
        await using var host = await StartAsync();
        var created = await ReadAsync<Task>(
            await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Original" }""")));

        var blank = await host.Client.PutAsync(
            $"/tasks/{created.Id}",
            JsonBody("""{ "title": "   ", "completed": false }"""));
        var tooLong = await host.Client.PutAsync(
            $"/tasks/{created.Id}",
            JsonBody($$"""{ "title": "{{new string('t', 201)}}", "completed": false }"""));

        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("application/problem+json", blank.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DeleteTask_ReturnsNoContent_ThenNotFoundOnRepeat()
    {
        await using var host = await StartAsync();
        var created = await ReadAsync<Task>(
            await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Doomed" }""")));

        var first = await host.Client.DeleteAsync($"/tasks/{created.Id}");
        var second = await host.Client.DeleteAsync($"/tasks/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Ids_AreNotReusedAfterDelete()
    {
        await using var host = await StartAsync();
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "One" }"""));
        await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Two" }"""));
        await host.Client.DeleteAsync("/tasks/2");

        var created = await ReadAsync<Task>(
            await host.Client.PostAsync("/tasks", JsonBody("""{ "title": "Three" }""")));

        Assert.True(created.Id >= 3, $"Expected id >= 3 but got {created.Id}.");
    }

    [Fact]
    public async global::System.Threading.Tasks.Task NonAsciiTitlesRoundTripUnchanged()
    {
        await using var host = await StartAsync();

        var created = await ReadAsync<Task>(await host.Client.PostAsync(
            "/tasks",
            JsonBody("""{ "title": "Café au lait – naïve" }""")));
        var fetched = await ReadAsync<Task>(await host.Client.GetAsync($"/tasks/{created.Id}"));

        Assert.Equal("Café au lait – naïve", fetched.Title);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task UnknownRouteReturns404()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync("/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task DataSurvivesApplicationRestart()
    {
        var workingDirectory = EvaluationContext.CreateTempDirectory();
        var databasePath = Path.Combine(workingDirectory, "persist.db");

        int createdId;
        await using (var host = await StartAsync(databasePath, workingDirectory))
        {
            var created = await ReadAsync<Task>(await host.Client.PostAsync(
                "/tasks",
                JsonBody("""{ "title": "Survivor" }""")));
            createdId = created.Id;
        }

        await using (var host = await StartAsync(databasePath, workingDirectory))
        {
            var fetched = await host.Client.GetAsync($"/tasks/{createdId}");

            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            Assert.Equal("Survivor", (await ReadAsync<Task>(fetched)).Title);
        }
    }
}
