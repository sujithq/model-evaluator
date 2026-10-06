using System.Text.Json;
using TaskBoard;

namespace TaskBoard.Tests;

public sealed class TaskStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "taskboard-tests", Guid.NewGuid().ToString("N"));

    public TaskStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Cleanup is best effort.
        }
    }

    private string StorePath => Path.Combine(_directory, "tasks.json");

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var store = new TaskStore(StorePath);

        Assert.Empty(store.Load());
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsTasksInIdOrder()
    {
        var store = new TaskStore(StorePath);
        var created = DateTimeOffset.UtcNow;
        store.Save(new[]
        {
            new TaskItem { Id = 2, Title = "Second", Completed = false, CreatedAt = created },
            new TaskItem { Id = 1, Title = "First", Completed = true, CreatedAt = created },
        });

        var loaded = store.Load();

        Assert.Equal(new[] { 1, 2 }, loaded.Select(t => t.Id));
        Assert.True(loaded[0].Completed);
        Assert.Equal("Second", loaded[1].Title);
    }

    [Fact]
    public void Save_WritesDocumentedCamelCaseJson()
    {
        var store = new TaskStore(StorePath);
        store.Save(new[]
        {
            new TaskItem
            {
                Id = 1,
                Title = "Sample",
                Completed = true,
                CreatedAt = DateTimeOffset.Parse("2024-01-02T03:04:05Z"),
            },
        });

        using var document = JsonDocument.Parse(File.ReadAllText(StorePath));
        var element = document.RootElement.EnumerateArray().Single();

        Assert.Equal(1, element.GetProperty("id").GetInt32());
        Assert.Equal("Sample", element.GetProperty("title").GetString());
        Assert.True(element.GetProperty("completed").GetBoolean());
        Assert.NotNull(element.GetProperty("createdAt").GetString());
    }

    [Fact]
    public void NextId_StartsAtOneAndIncrementsBeyondMax()
    {
        Assert.Equal(1, TaskStore.NextId(Array.Empty<TaskItem>()));

        var tasks = new[]
        {
            new TaskItem { Id = 1, Title = "a", CreatedAt = DateTimeOffset.UtcNow },
            new TaskItem { Id = 4, Title = "b", CreatedAt = DateTimeOffset.UtcNow },
        };

        Assert.Equal(5, TaskStore.NextId(tasks));
    }
}
