namespace ModelEvaluator.Core.Results;

/// <summary>Outcome of a single evaluator-owned check.</summary>
public enum CheckStatus
{
    Passed,
    Failed,
    Skipped,
    NotApplicable,
    Error,
}

/// <summary>Evaluation dimension a check contributes to.</summary>
public enum CheckCategory
{
    BuildAndExecution,
    FunctionalCorrectness,
    InstructionAdherence,
    CodeQuality,
}

/// <summary>Result of a single check performed against a generated project.</summary>
public sealed record CheckResult
{
    public required string Id { get; init; }

    public required CheckCategory Category { get; init; }

    public required CheckStatus Status { get; init; }

    /// <summary>Mandatory checks must pass for an attempt to be successful.</summary>
    public bool Mandatory { get; init; } = true;

    public string Details { get; init; } = string.Empty;

    public double DurationSeconds { get; init; }

    public static CheckResult Pass(string id, CheckCategory category, string details = "", bool mandatory = true) =>
        new() { Id = id, Category = category, Status = CheckStatus.Passed, Details = details, Mandatory = mandatory };

    public static CheckResult Fail(string id, CheckCategory category, string details, bool mandatory = true) =>
        new() { Id = id, Category = category, Status = CheckStatus.Failed, Details = details, Mandatory = mandatory };

    public static CheckResult Skip(string id, CheckCategory category, string details, bool mandatory = true) =>
        new() { Id = id, Category = category, Status = CheckStatus.Skipped, Details = details, Mandatory = mandatory };
}
