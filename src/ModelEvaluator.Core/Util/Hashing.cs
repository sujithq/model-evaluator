using System.Security.Cryptography;
using System.Text;

namespace ModelEvaluator.Core.Util;

/// <summary>Deterministic hashing helpers used to make results traceable.</summary>
public static class Hashing
{
    /// <summary>Returns the lower-case SHA-256 hash of the supplied text, with line endings normalised.</summary>
    public static string Sha256(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Returns a stable hash for the contents of a directory tree (relative paths plus file contents).
    /// </summary>
    public static string Sha256Directory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Sha256(string.Empty);
        }

        var builder = new StringBuilder();
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(directory, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal);

        foreach (var relative in files)
        {
            var content = File.ReadAllText(Path.Combine(directory, relative));
            builder.Append(relative).Append('\n').Append(Sha256(content)).Append('\n');
        }

        return Sha256(builder.ToString());
    }
}
