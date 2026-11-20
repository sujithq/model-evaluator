using System.Text;
using System.Text.Json;
using TaskCli;

namespace TaskCli.Tests;

public sealed class CommandProcessorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "taskcli-tests", Guid.NewGuid().ToString("N"));
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public CommandProcessorTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
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

    private int Execute(params string[] arguments) =>
        new CommandProcessor(new TaskStore(StorePath), _output, _error).Execute(arguments);

    [Fact]
    public void Add_StoresTaskAndPrintsConfirmation()
    {
        var exitCode = Execute("add", "Write", "tests");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("Added task 1: Write tests", _output.ToString().Trim());
        Assert.Single(new TaskStore(StorePath).Load());
    }

    [Fact]
    public void Add_RejectsMissingTitle()
    {
        var exitCode = Execute("add", " ");

        Assert.Equal(ExitCodes.Validation, exitCode);
        Assert.Contains("Error: title is required.", _error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(200, ExitCodes.Success)]
    [InlineData(201, ExitCodes.Validation)]
    public void Add_EnforcesTitleLengthBoundary(int length, int expected)
    {
        var exitCode = Execute("add", new string('x', length));

        Assert.Equal(expected, exitCode);
    }

    [Fact]
    public void List_ReportsEmptyStore()
    {
        var exitCode = Execute("list");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("No tasks found.", _output.ToString().Trim());
    }

    [Fact]
    public void List_FiltersPendingTasks()
    {
        Execute("add", "One");
        Execute("add", "Two");
        Execute("complete", "1");
        _output.GetStringBuilder().Clear();

        Execute("list", "--status", "pending");

        Assert.Equal("2 [ ] Two", _output.ToString().Trim());
    }

    [Fact]
    public void List_RejectsUnknownStatus()
    {
        var exitCode = Execute("list", "--status", "archived");

        Assert.Equal(ExitCodes.Validation, exitCode);
    }

    [Fact]
    public void Complete_ReportsUnknownId()
    {
        var exitCode = Execute("complete", "5");

        Assert.Equal(ExitCodes.NotFound, exitCode);
        Assert.Contains("Error: task 5 not found.", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_RemovesTaskWithoutReusingIds()
    {
        Execute("add", "One");
        Execute("add", "Two");
        Execute("add", "Three");
        Execute("delete", "2");
        _output.GetStringBuilder().Clear();

        Execute("add", "Four");

        Assert.Equal("Added task 4: Four", _output.ToString().Trim());
    }

    [Fact]
    public void Delete_RejectsNonIntegerId()
    {
        var exitCode = Execute("delete", "abc");

        Assert.Equal(ExitCodes.Validation, exitCode);
    }

    [Fact]
    public void CorruptStore_IsReported()
    {
        File.WriteAllText(StorePath, "not json", Encoding.UTF8);

        var exitCode = Execute("list");

        Assert.Equal(ExitCodes.CorruptStore, exitCode);
    }

    [Fact]
    public void UnknownCommand_ReturnsUsageExitCode()
    {
        var exitCode = Execute("archive");

        Assert.Equal(ExitCodes.Usage, exitCode);
    }

    [Fact]
    public void Store_WritesDocumentedJsonShape()
    {
        Execute("add", "One");

        using var document = JsonDocument.Parse(File.ReadAllText(StorePath));
        var task = document.RootElement.EnumerateArray().Single();

        Assert.Equal(1, task.GetProperty("id").GetInt32());
        Assert.Equal("One", task.GetProperty("title").GetString());
        Assert.False(task.GetProperty("completed").GetBoolean());
        Assert.True(DateTimeOffset.TryParse(task.GetProperty("createdAt").GetString(), out _));
    }
}
