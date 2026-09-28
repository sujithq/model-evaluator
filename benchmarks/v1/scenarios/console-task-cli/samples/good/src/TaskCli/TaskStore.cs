using System.Text.Json;

namespace TaskCli;

/// <summary>Raised when the task store exists but cannot be read.</summary>
public sealed class TaskStoreCorruptException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>JSON-backed persistence for tasks.</summary>
public sealed class TaskStore(string path)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path { get; } = path;

    /// <summary>Reads all tasks. A missing file means "no tasks".</summary>
    public List<TaskItem> Load()
    {
        if (!File.Exists(Path))
        {
            return [];
        }

        try
        {
            var tasks = JsonSerializer.Deserialize<List<TaskItem>>(File.ReadAllText(Path), SerializerOptions);
            return tasks ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new TaskStoreCorruptException("The task store could not be read.", ex);
        }
    }

    public void Save(IEnumerable<TaskItem> tasks) =>
        File.WriteAllText(Path, JsonSerializer.Serialize(tasks.OrderBy(t => t.Id).ToList(), SerializerOptions));
}
