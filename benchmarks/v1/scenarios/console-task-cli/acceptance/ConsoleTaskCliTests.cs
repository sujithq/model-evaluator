using System.Text.Json;
using ModelEvaluator.Acceptance;
using Xunit;

namespace ConsoleTaskCli.Acceptance;

/// <summary>
/// Evaluator-owned acceptance checks for the console task-management CLI. These tests are never visible to
/// the evaluated model and cover cases beyond the examples in the scenario instructions.
/// </summary>
public sealed class ConsoleTaskCliTests : IDisposable
{
    private readonly string _assembly = EvaluationContext.FindApplicationAssembly();
    private readonly string _workingDirectory = EvaluationContext.CreateTempDirectory();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Temporary directory cleanup is best effort.
        }
    }

    private AppResult Run(params string[] arguments) =>
        AppRunner.Run(_assembly, arguments, _workingDirectory);

    private string StorePath => Path.Combine(_workingDirectory, "tasks.json");

    [Fact]
    public void Add_PrintsConfirmationAndCreatesStore()
    {
        var result = Run("add", "Buy milk");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("Added task 1: Buy milk", result.OutputLines.Single());
        Assert.True(File.Exists(StorePath), "tasks.json was not created.");
    }

    [Fact]
    public void Add_JoinsTitleArgumentsAndPreservesNonAsciiCharacters()
    {
        var result = Run("add", "Café", "au", "lait");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("Added task 1: Café au lait", result.OutputLines.Single());
    }

    [Fact]
    public void Add_RejectsEmptyTitle()
    {
        var result = Run("add", "   ");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: title is required.", result.StandardError);
    }

    [Fact]
    public void Add_RejectsTitleLongerThanTwoHundredCharacters()
    {
        var tooLong = new string('a', 201);

        var result = Run("add", tooLong);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: title must be 200 characters or fewer.", result.StandardError);
    }

    [Fact]
    public void Add_AcceptsBoundaryTitleOfExactlyTwoHundredCharacters()
    {
        var boundary = new string('a', 200);

        var result = Run("add", boundary);

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal($"Added task 1: {boundary}", result.OutputLines.Single());
    }

    [Fact]
    public void List_ReportsEmptyStore()
    {
        var result = Run("list");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal("No tasks found.", result.OutputLines.Single());
    }

    [Fact]
    public void List_RendersPendingAndCompletedTasksInIdOrder()
    {
        Run("add", "First");
        Run("add", "Second");
        Run("complete", "1");

        var result = Run("list");

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.Equal(["1 [x] First", "2 [ ] Second"], result.OutputLines);
    }

    [Fact]
    public void List_FiltersByStatus()
    {
        Run("add", "First");
        Run("add", "Second");
        Run("complete", "2");

        var pending = Run("list", "--status", "pending");
        var completed = Run("list", "--status", "completed");
        var all = Run("list", "--status", "all");

        Assert.Equal(["1 [ ] First"], pending.OutputLines);
        Assert.Equal(["2 [x] Second"], completed.OutputLines);
        Assert.Equal(2, all.OutputLines.Length);
    }

    [Fact]
    public void List_RejectsUnknownStatus()
    {
        var result = Run("list", "--status", "archived");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: unknown status 'archived'.", result.StandardError);
    }

    [Fact]
    public void Complete_IsIdempotentAndReportsUnknownIds()
    {
        Run("add", "First");

        var first = Run("complete", "1");
        var again = Run("complete", "1");
        var missing = Run("complete", "42");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("Completed task 1", first.OutputLines.Single());
        Assert.Equal(0, again.ExitCode);
        Assert.Equal(3, missing.ExitCode);
        Assert.Contains("Error: task 42 not found.", missing.StandardError);
    }

    [Fact]
    public void Complete_RejectsNonIntegerId()
    {
        var result = Run("complete", "abc");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Error: id must be an integer.", result.StandardError);
    }

    [Fact]
    public void Delete_RemovesTaskAndPersistsAcrossExecutions()
    {
        Run("add", "First");
        Run("add", "Second");

        var deleted = Run("delete", "1");
        var listed = Run("list");

        Assert.Equal(0, deleted.ExitCode);
        Assert.Equal("Deleted task 1", deleted.OutputLines.Single());
        Assert.Equal(["2 [ ] Second"], listed.OutputLines);
    }

    [Fact]
    public void Delete_ReportsUnknownId()
    {
        var result = Run("delete", "7");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Error: task 7 not found.", result.StandardError);
    }

    [Fact]
    public void Ids_AreNotReusedAfterDeletingATaskInTheMiddle()
    {
        Run("add", "First");
        Run("add", "Second");
        Run("add", "Third");
        Run("delete", "2");

        var added = Run("add", "Fourth");

        Assert.Equal("Added task 4: Fourth", added.OutputLines.Single());
    }

    [Fact]
    public void Store_UsesTheDocumentedJsonShape()
    {
        Run("add", "First");
        Run("complete", "1");

        using var document = JsonDocument.Parse(File.ReadAllText(StorePath));

        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var task = document.RootElement.EnumerateArray().Single();
        Assert.Equal(1, task.GetProperty("id").GetInt32());
        Assert.Equal("First", task.GetProperty("title").GetString());
        Assert.True(task.GetProperty("completed").GetBoolean());
        Assert.True(
            DateTimeOffset.TryParse(task.GetProperty("createdAt").GetString(), out _),
            "createdAt is not an ISO-8601 timestamp.");
    }

    [Fact]
    public void CorruptStore_IsReportedWithExitCodeFour()
    {
        File.WriteAllText(StorePath, "{ this is not valid json");

        var result = Run("list");

        Assert.Equal(4, result.ExitCode);
        Assert.Contains("Error: task store is corrupt.", result.StandardError);
    }

    [Fact]
    public void NoArguments_PrintsUsageAndExitsWithOne()
    {
        var result = Run();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Usage", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommand_IsReported()
    {
        var result = Run("archive", "1");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Error: unknown command 'archive'.", result.StandardError);
    }

    [Fact]
    public void Smoke_ApplicationStartsAndStopsWithinTheTimeout()
    {
        var result = Run("list");

        Assert.False(result.TimedOut, "The application did not exit within the smoke-test timeout.");
    }
}
