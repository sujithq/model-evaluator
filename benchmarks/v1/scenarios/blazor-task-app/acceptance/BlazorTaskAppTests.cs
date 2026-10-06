using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ModelEvaluator.Acceptance;
using Xunit;

namespace BlazorTaskApp.Acceptance;

/// <summary>
/// Evaluator-owned acceptance checks for the Blazor task-management app. Tests run outside the workspace,
/// start the built application through <see cref="WebAppHost"/> on a free loopback port and drive the
/// documented user journeys via plain HTTP form posts (no browser automation required).
/// </summary>
public sealed class BlazorTaskAppTests
{
    private static readonly string ApplicationAssembly = EvaluationContext.FindApplicationAssembly();

    private sealed record HttpResult(HttpStatusCode Status, string Body, string? Location);

    /// <summary>Bundles one running host and a redirect-free <see cref="HttpClient"/> for one test.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        public required WebAppHost Host { get; init; }

        public required HttpClient Client { get; init; }

        public required string WorkingDirectory { get; init; }

        public required string TasksFile { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.DisposeAsync().ConfigureAwait(false);
            try
            {
                Directory.Delete(WorkingDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private static async Task<Fixture> StartAsync(string? existingTasksFile = null)
    {
        var directory = EvaluationContext.CreateTempDirectory();
        var tasksFile = existingTasksFile ?? Path.Combine(directory, "tasks.json");
        var environment = new Dictionary<string, string>
        {
            ["TASKS_FILE"] = tasksFile,
        };

        var host = await WebAppHost.StartAsync(
            ApplicationAssembly,
            directory,
            environment,
            readinessPath: "/health",
            timeoutSeconds: 120).ConfigureAwait(false);

        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(host.BaseAddress),
            Timeout = TimeSpan.FromSeconds(30),
        };

        return new Fixture
        {
            Host = host,
            Client = client,
            WorkingDirectory = directory,
            TasksFile = tasksFile,
        };
    }

    private static async Task<HttpResult> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new HttpResult(response.StatusCode, body, response.Headers.Location?.OriginalString);
    }

    private static async Task<HttpResult> PostFormAsync(
        HttpClient client,
        string path,
        IEnumerable<KeyValuePair<string, string>>? fields = null)
    {
        using var content = new FormUrlEncodedContent(fields ?? []);
        using var response = await client.PostAsync(path, content).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        return new HttpResult(response.StatusCode, body, response.Headers.Location?.OriginalString);
    }

    private static Task<HttpResult> AddTaskAsync(HttpClient client, string title) =>
        PostFormAsync(client, "/tasks/add", [new("title", title)]);

    private static Task<HttpResult> EditTaskAsync(HttpClient client, int id, string title) =>
        PostFormAsync(client, $"/tasks/{id}/edit", [new("title", title)]);

    private static Task<HttpResult> CompleteTaskAsync(HttpClient client, int id) =>
        PostFormAsync(client, $"/tasks/{id}/complete");

    private static Task<HttpResult> DeleteTaskAsync(HttpClient client, int id) =>
        PostFormAsync(client, $"/tasks/{id}/delete");

    [Fact]
    public async Task Health_ReturnsOkPlainText()
    {
        await using var fixture = await StartAsync();

        var result = await GetAsync(fixture.Client, "/health");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("ok", result.Body.Trim());
    }

    [Fact]
    public async Task Tasks_EmptyState_RendersDocumentedText()
    {
        await using var fixture = await StartAsync();

        var result = await GetAsync(fixture.Client, "/tasks");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Contains("No tasks found.", result.Body, StringComparison.Ordinal);
        Assert.Contains("action=\"/tasks/add\"", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_ValidTitle_RedirectsAndListsPendingTask()
    {
        await using var fixture = await StartAsync();

        var added = await AddTaskAsync(fixture.Client, "Buy milk");
        Assert.Equal(HttpStatusCode.Found, added.Status);
        Assert.Equal("/tasks", added.Location);

        var listing = await GetAsync(fixture.Client, "/tasks");
        Assert.Equal(HttpStatusCode.OK, listing.Status);
        Assert.Contains("id=\"task-1\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("Buy milk", listing.Body, StringComparison.Ordinal);
        Assert.Contains("[ ]", listing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("No tasks found.", listing.Body, StringComparison.Ordinal);
        Assert.Contains("action=\"/tasks/1/complete\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("action=\"/tasks/1/edit\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("action=\"/tasks/1/delete\"", listing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_BlankTitle_ReturnsFourHundred()
    {
        await using var fixture = await StartAsync();

        var result = await AddTaskAsync(fixture.Client, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Contains("Title is required.", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_TitleLongerThanTwoHundred_ReturnsFourHundred()
    {
        await using var fixture = await StartAsync();
        var tooLong = new string('a', 201);

        var result = await AddTaskAsync(fixture.Client, tooLong);

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Contains("Title must be 200 characters or fewer.", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_TitleOfExactlyTwoHundredCharacters_IsAccepted()
    {
        await using var fixture = await StartAsync();
        var boundary = new string('a', 200);

        var result = await AddTaskAsync(fixture.Client, boundary);

        Assert.Equal(HttpStatusCode.Found, result.Status);
        var listing = await GetAsync(fixture.Client, "/tasks");
        Assert.Contains(boundary, listing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_MarksTaskAndIsIdempotent()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "First");

        var first = await CompleteTaskAsync(fixture.Client, 1);
        var again = await CompleteTaskAsync(fixture.Client, 1);
        var listing = await GetAsync(fixture.Client, "/tasks");

        Assert.Equal(HttpStatusCode.Found, first.Status);
        Assert.Equal("/tasks", first.Location);
        Assert.Equal(HttpStatusCode.Found, again.Status);
        Assert.Contains("[x]", listing.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"task-1\"", listing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_UnknownId_ReturnsNotFound()
    {
        await using var fixture = await StartAsync();

        var result = await CompleteTaskAsync(fixture.Client, 42);

        Assert.Equal(HttpStatusCode.NotFound, result.Status);
    }

    [Fact]
    public async Task Edit_ChangesTitle_AndValidates()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "Original");

        var edited = await EditTaskAsync(fixture.Client, 1, "Renamed");
        var blank = await EditTaskAsync(fixture.Client, 1, "");
        var missing = await EditTaskAsync(fixture.Client, 99, "Renamed");
        var listing = await GetAsync(fixture.Client, "/tasks");

        Assert.Equal(HttpStatusCode.Found, edited.Status);
        Assert.Equal("/tasks", edited.Location);
        Assert.Equal(HttpStatusCode.BadRequest, blank.Status);
        Assert.Contains("Title is required.", blank.Body, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Contains("Renamed", listing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(">Original<", listing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_RemovesTaskAndRedirects()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "First");
        await AddTaskAsync(fixture.Client, "Second");

        var deleted = await DeleteTaskAsync(fixture.Client, 1);
        var listing = await GetAsync(fixture.Client, "/tasks");

        Assert.Equal(HttpStatusCode.Found, deleted.Status);
        Assert.Equal("/tasks", deleted.Location);
        Assert.DoesNotContain("id=\"task-1\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"task-2\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("Second", listing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        await using var fixture = await StartAsync();

        var result = await DeleteTaskAsync(fixture.Client, 7);

        Assert.Equal(HttpStatusCode.NotFound, result.Status);
    }

    [Fact]
    public async Task Ids_AreNotReusedAfterDeletingATaskInTheMiddle()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "First");
        await AddTaskAsync(fixture.Client, "Second");
        await AddTaskAsync(fixture.Client, "Third");
        await DeleteTaskAsync(fixture.Client, 2);
        await AddTaskAsync(fixture.Client, "Fourth");

        var listing = await GetAsync(fixture.Client, "/tasks");

        Assert.Contains("id=\"task-1\"", listing.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"task-2\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"task-3\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"task-4\"", listing.Body, StringComparison.Ordinal);
        Assert.Contains("Fourth", listing.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filter_Pending_ShowsOnlyPendingTasks()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "First");
        await AddTaskAsync(fixture.Client, "Second");
        await CompleteTaskAsync(fixture.Client, 2);

        var pending = await GetAsync(fixture.Client, "/tasks?status=pending");

        Assert.Equal(HttpStatusCode.OK, pending.Status);
        Assert.Contains("id=\"task-1\"", pending.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"task-2\"", pending.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filter_Completed_ShowsOnlyCompletedTasks()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "First");
        await AddTaskAsync(fixture.Client, "Second");
        await CompleteTaskAsync(fixture.Client, 2);

        var completed = await GetAsync(fixture.Client, "/tasks?status=completed");

        Assert.Equal(HttpStatusCode.OK, completed.Status);
        Assert.DoesNotContain("id=\"task-1\"", completed.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"task-2\"", completed.Body, StringComparison.Ordinal);
        Assert.Contains("[x]", completed.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filter_All_ShowsEveryTask()
    {
        await using var fixture = await StartAsync();
        await AddTaskAsync(fixture.Client, "First");
        await AddTaskAsync(fixture.Client, "Second");
        await CompleteTaskAsync(fixture.Client, 2);

        var all = await GetAsync(fixture.Client, "/tasks?status=all");

        Assert.Equal(HttpStatusCode.OK, all.Status);
        Assert.Contains("id=\"task-1\"", all.Body, StringComparison.Ordinal);
        Assert.Contains("id=\"task-2\"", all.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Filter_UnknownStatus_ReturnsFourHundredWithExactMessage()
    {
        await using var fixture = await StartAsync();

        var result = await GetAsync(fixture.Client, "/tasks?status=archived");

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Contains("Unknown status filter.", result.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonAsciiTitle_IsPreservedExactly()
    {
        await using var fixture = await StartAsync();
        const string title = "Café au lait — 250 ml";

        var added = await AddTaskAsync(fixture.Client, title);
        Assert.Equal(HttpStatusCode.Found, added.Status);

        // The rendered HTML may HTML-encode non-ASCII characters, but the persisted store must round-trip
        // the exact bytes provided by the client.
        Assert.True(File.Exists(fixture.TasksFile));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.TasksFile));
        var stored = document.RootElement.EnumerateArray().Single().GetProperty("title").GetString();
        Assert.Equal(title, stored);
    }

    [Fact]
    public async Task Persistence_SurvivesApplicationRestart()
    {
        var directory = EvaluationContext.CreateTempDirectory();
        var tasksFile = Path.Combine(directory, "tasks.json");
        try
        {
            await using (var first = await StartAsync(existingTasksFile: tasksFile))
            {
                await AddTaskAsync(first.Client, "Persist me");
                await CompleteTaskAsync(first.Client, 1);
            }

            await using var second = await StartAsync(existingTasksFile: tasksFile);
            var listing = await GetAsync(second.Client, "/tasks");

            Assert.Equal(HttpStatusCode.OK, listing.Status);
            Assert.Contains("id=\"task-1\"", listing.Body, StringComparison.Ordinal);
            Assert.Contains("Persist me", listing.Body, StringComparison.Ordinal);
            Assert.Contains("[x]", listing.Body, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    [Fact]
    public async Task Store_UsesTheDocumentedJsonShape()
    {
        await using var fixture = await StartAsync();

        await AddTaskAsync(fixture.Client, "First");
        await CompleteTaskAsync(fixture.Client, 1);

        Assert.True(File.Exists(fixture.TasksFile), "tasks.json was not created.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.TasksFile));

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var task = document.RootElement.EnumerateArray().Single();
        Assert.Equal(1, task.GetProperty("id").GetInt32());
        Assert.Equal("First", task.GetProperty("title").GetString());
        Assert.True(task.GetProperty("completed").GetBoolean());
        Assert.True(
            DateTimeOffset.TryParse(task.GetProperty("createdAt").GetString(), out _),
            "createdAt is not an ISO-8601 timestamp.");
    }
}
