namespace ModelEvaluator.Core.Util;

/// <summary>File system helpers used to create isolated workspaces.</summary>
public static class FileSystemHelper
{
    private static readonly string[] IgnoredDirectories = ["bin", "obj", ".git", "node_modules", ".vs"];

    /// <summary>Recursively copies <paramref name="source"/> into <paramref name="destination"/>.</summary>
    /// <param name="overwrite">When true existing files are replaced (used for sample overlays).</param>
    /// <param name="skipBuildOutput">When true, build output folders are not copied.</param>
    public static void CopyDirectory(string source, string destination, bool overwrite = true, bool skipBuildOutput = true)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {source}");
        }

        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            if (!overwrite && File.Exists(target))
            {
                continue;
            }

            File.Copy(file, target, overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (skipBuildOutput && IgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name), overwrite, skipBuildOutput);
        }
    }

    /// <summary>Deletes a directory tree if present, ignoring transient IO errors.</summary>
    public static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a stale file handle must not fail the evaluation.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    /// <summary>Enumerates files under a root, skipping build output and VCS folders.</summary>
    public static IEnumerable<string> EnumerateSourceFiles(string root, string pattern = "*")
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var segments = relative.Split('/');
            if (segments.Any(s => IgnoredDirectories.Contains(s, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            yield return file;
        }
    }
}
