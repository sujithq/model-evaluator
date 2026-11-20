using System.Text.RegularExpressions;

namespace ModelEvaluator.Core.Util;

/// <summary>Minimal glob matcher supporting <c>*</c>, <c>**</c> and <c>?</c> on '/'-separated paths.</summary>
public static class GlobMatcher
{
    public static bool IsMatch(string relativePath, string pattern)
    {
        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('.', '/');
        return ToRegex(pattern).IsMatch(normalizedPath);
    }

    public static bool AnyMatch(IEnumerable<string> relativePaths, string pattern)
    {
        var regex = ToRegex(pattern);
        return relativePaths.Any(p => regex.IsMatch(p.Replace('\\', '/').TrimStart('.', '/')));
    }

    private static Regex ToRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/').TrimStart('.', '/');
        var regex = "^";
        for (var i = 0; i < normalized.Length; i++)
        {
            var c = normalized[i];
            if (c == '*')
            {
                if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    // '**/' matches zero or more path segments.
                    if (i + 2 < normalized.Length && normalized[i + 2] == '/')
                    {
                        regex += "(?:[^/]*/)*";
                        i += 2;
                    }
                    else
                    {
                        regex += ".*";
                        i++;
                    }
                }
                else
                {
                    regex += "[^/]*";
                }
            }
            else if (c == '?')
            {
                regex += "[^/]";
            }
            else
            {
                regex += Regex.Escape(c.ToString());
            }
        }

        regex += "$";
        return new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
