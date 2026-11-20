namespace TaskCli;

/// <summary>Exit codes defined by the scenario contract.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Usage = 1;
    public const int Validation = 2;
    public const int NotFound = 3;
    public const int CorruptStore = 4;
}

/// <summary>Executes the CLI commands against a task store.</summary>
public sealed class CommandProcessor(TaskStore store, TextWriter output, TextWriter error)
{
    private const int MaximumTitleLength = 200;

    public int Execute(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            output.WriteLine("Usage: taskcli <add|list|complete|delete> [arguments]");
            output.WriteLine("  add <title>                                  Add a task.");
            output.WriteLine("  list [--status all|pending|completed]        List tasks.");
            output.WriteLine("  complete <id>                                Mark a task as completed.");
            output.WriteLine("  delete <id>                                  Delete a task.");
            return ExitCodes.Usage;
        }

        var command = arguments[0];
        var rest = arguments.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "add" => Add(rest),
                "list" => List(rest),
                "complete" => Complete(rest),
                "delete" => Delete(rest),
                _ => UnknownCommand(command),
            };
        }
        catch (TaskStoreCorruptException)
        {
            error.WriteLine("Error: task store is corrupt.");
            return ExitCodes.CorruptStore;
        }
    }

    private int Add(string[] arguments)
    {
        var title = string.Join(' ', arguments).Trim();

        if (title.Length == 0)
        {
            error.WriteLine("Error: title is required.");
            return ExitCodes.Validation;
        }

        if (title.Length > MaximumTitleLength)
        {
            error.WriteLine($"Error: title must be {MaximumTitleLength} characters or fewer.");
            return ExitCodes.Validation;
        }

        var tasks = store.Load();
        var id = tasks.Count == 0 ? 1 : tasks.Max(t => t.Id) + 1;
        tasks.Add(new TaskItem
        {
            Id = id,
            Title = title,
            Completed = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        store.Save(tasks);
        output.WriteLine($"Added task {id}: {title}");
        return ExitCodes.Success;
    }

    private int List(string[] arguments)
    {
        var status = "all";
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] != "--status")
            {
                error.WriteLine($"Error: unknown status '{arguments[i]}'.");
                return ExitCodes.Validation;
            }

            if (i + 1 >= arguments.Length)
            {
                error.WriteLine("Error: unknown status ''.");
                return ExitCodes.Validation;
            }

            status = arguments[i + 1];
            i++;
        }

        if (status is not ("all" or "pending" or "completed"))
        {
            error.WriteLine($"Error: unknown status '{status}'.");
            return ExitCodes.Validation;
        }

        var tasks = store.Load()
            .Where(t => status switch
            {
                "pending" => !t.Completed,
                "completed" => t.Completed,
                _ => true,
            })
            .OrderBy(t => t.Id)
            .ToList();

        if (tasks.Count == 0)
        {
            output.WriteLine("No tasks found.");
            return ExitCodes.Success;
        }

        foreach (var task in tasks)
        {
            output.WriteLine($"{task.Id} [{(task.Completed ? "x" : " ")}] {task.Title}");
        }

        return ExitCodes.Success;
    }

    private int Complete(string[] arguments)
    {
        if (!TryParseId(arguments, out var id))
        {
            return ExitCodes.Validation;
        }

        var tasks = store.Load();
        var index = tasks.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            error.WriteLine($"Error: task {id} not found.");
            return ExitCodes.NotFound;
        }

        tasks[index] = tasks[index] with { Completed = true };
        store.Save(tasks);
        output.WriteLine($"Completed task {id}");
        return ExitCodes.Success;
    }

    private int Delete(string[] arguments)
    {
        if (!TryParseId(arguments, out var id))
        {
            return ExitCodes.Validation;
        }

        var tasks = store.Load();
        var index = tasks.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            error.WriteLine($"Error: task {id} not found.");
            return ExitCodes.NotFound;
        }

        tasks.RemoveAt(index);
        store.Save(tasks);
        output.WriteLine($"Deleted task {id}");
        return ExitCodes.Success;
    }

    private int UnknownCommand(string command)
    {
        error.WriteLine($"Error: unknown command '{command}'.");
        return ExitCodes.Usage;
    }

    private bool TryParseId(string[] arguments, out int id)
    {
        if (arguments.Length == 0 || !int.TryParse(arguments[0], out id))
        {
            error.WriteLine("Error: id must be an integer.");
            id = 0;
            return false;
        }

        return true;
    }
}
