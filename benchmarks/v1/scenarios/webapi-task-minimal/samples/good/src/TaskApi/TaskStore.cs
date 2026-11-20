using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TaskApi;

/// <summary>SQLite-backed persistence for tasks. Thread-safe against concurrent HTTP requests.</summary>
public sealed class TaskStore
{
    private readonly string _connectionString;
    private readonly object _writeLock = new();

    public TaskStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void EnsureCreated()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS tasks (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                title      TEXT    NOT NULL,
                completed  INTEGER NOT NULL,
                created_at TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<TaskItem> Query(string status, string? search)
    {
        var results = new List<TaskItem>();

        using var connection = Open();
        using var command = connection.CreateCommand();
        var text = "SELECT id, title, completed, created_at FROM tasks WHERE 1 = 1";

        if (status == "pending")
        {
            text += " AND completed = 0";
        }
        else if (status == "completed")
        {
            text += " AND completed = 1";
        }

        if (!string.IsNullOrEmpty(search))
        {
            text += " AND instr(lower(title), lower($search)) > 0";
            command.Parameters.AddWithValue("$search", search);
        }

        text += " ORDER BY id ASC";
        command.CommandText = text;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(Read(reader));
        }

        return results;
    }

    public TaskItem? FindById(int id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title, completed, created_at FROM tasks WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public TaskItem Insert(string title, DateTimeOffset createdAt)
    {
        var truncated = TruncateToMilliseconds(createdAt);

        lock (_writeLock)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO tasks (title, completed, created_at) VALUES ($title, 0, $created_at); " +
                "SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$created_at", FormatTimestamp(truncated));
            var id = Convert.ToInt32((long)command.ExecuteScalar()!, CultureInfo.InvariantCulture);

            return new TaskItem
            {
                Id = id,
                Title = title,
                Completed = false,
                CreatedAt = truncated,
            };
        }
    }

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var ticksPerMs = TimeSpan.TicksPerMillisecond;
        return new DateTimeOffset(utc.Ticks - (utc.Ticks % ticksPerMs), TimeSpan.Zero);
    }

    public TaskItem? Update(int id, string title, bool completed)
    {
        lock (_writeLock)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE tasks SET title = $title, completed = $completed WHERE id = $id;";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
            command.Parameters.AddWithValue("$id", id);
            var rows = command.ExecuteNonQuery();
            if (rows == 0)
            {
                return null;
            }
        }

        return FindById(id);
    }

    public bool Delete(int id)
    {
        lock (_writeLock)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM tasks WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        }
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static TaskItem Read(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Title = reader.GetString(1),
        Completed = reader.GetInt32(2) != 0,
        CreatedAt = ParseTimestamp(reader.GetString(3)),
    };

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(
            value,
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
