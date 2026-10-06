using System.Text.Json;

namespace TaskBoard;

/// <summary>JSON-backed persistence for tasks.</summary>
public sealed class TaskStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();

    public TaskStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    /// <summary>Reads all tasks. A missing or empty file means "no tasks".</summary>
    public List<TaskItem> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(Path))
            {
                return [];
            }

            var text = File.ReadAllText(Path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return [];
            }

            var tasks = JsonSerializer.Deserialize<List<TaskItem>>(text, SerializerOptions);
            return tasks ?? [];
        }
    }

    public void Save(IEnumerable<TaskItem> tasks)
    {
        lock (_gate)
        {
            var ordered = tasks.OrderBy(t => t.Id).ToList();
            File.WriteAllText(Path, JsonSerializer.Serialize(ordered, SerializerOptions));
        }
    }

    /// <summary>Computes the next id as <c>max(existing id) + 1</c>, starting at 1.</summary>
    public static int NextId(IReadOnlyCollection<TaskItem> tasks) =>
        tasks.Count == 0 ? 1 : tasks.Max(t => t.Id) + 1;
}
