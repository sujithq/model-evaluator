namespace TaskBoard;

/// <summary>Validates task titles against the scenario contract.</summary>
public static class TaskValidator
{
    public const int MaximumTitleLength = 200;

    public const string TitleRequiredMessage = "Title is required.";

    public const string TitleTooLongMessage = "Title must be 200 characters or fewer.";

    /// <summary>
    /// Attempts to normalise <paramref name="raw"/> into a valid title. Returns the trimmed title on success
    /// and the exact contract error message on failure.
    /// </summary>
    public static bool TryValidate(string? raw, out string title, out string error)
    {
        title = (raw ?? string.Empty).Trim();

        if (title.Length == 0)
        {
            error = TitleRequiredMessage;
            return false;
        }

        if (title.Length > MaximumTitleLength)
        {
            error = TitleTooLongMessage;
            return false;
        }

        error = string.Empty;
        return true;
    }
}
