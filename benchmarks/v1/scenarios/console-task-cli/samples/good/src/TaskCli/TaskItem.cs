namespace TaskCli;

/// <summary>A single task in the store.</summary>
public sealed record TaskItem
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public bool Completed { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
