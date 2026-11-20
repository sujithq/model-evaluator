using ModelEvaluator.Core.Util;

namespace ModelEvaluator.Core.Tests;

public sealed class HashingTests
{
    [Fact]
    public void Sha256_IsStableAndLineEndingInsensitive()
    {
        Assert.Equal(Hashing.Sha256("a\nb"), Hashing.Sha256("a\r\nb"));
        Assert.NotEqual(Hashing.Sha256("a"), Hashing.Sha256("b"));
    }

    [Fact]
    public void Sha256Directory_ReflectsContentAndPaths()
    {
        var first = CreateTree(("a.txt", "one"), ("nested/b.txt", "two"));
        var same = CreateTree(("nested/b.txt", "two"), ("a.txt", "one"));
        var differentContent = CreateTree(("a.txt", "one"), ("nested/b.txt", "changed"));
        var differentPath = CreateTree(("a.txt", "one"), ("nested/c.txt", "two"));

        try
        {
            Assert.Equal(Hashing.Sha256Directory(first), Hashing.Sha256Directory(same));
            Assert.NotEqual(Hashing.Sha256Directory(first), Hashing.Sha256Directory(differentContent));
            Assert.NotEqual(Hashing.Sha256Directory(first), Hashing.Sha256Directory(differentPath));
        }
        finally
        {
            foreach (var directory in new[] { first, same, differentContent, differentPath })
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Sha256Directory_ForMissingDirectory_MatchesEmptyHash() =>
        Assert.Equal(
            Hashing.Sha256(string.Empty),
            Hashing.Sha256Directory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    private static string CreateTree(params (string Path, string Content)[] files)
    {
        var root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return root;
    }
}
