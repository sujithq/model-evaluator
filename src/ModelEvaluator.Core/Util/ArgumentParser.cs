using System.Text;

namespace ModelEvaluator.Core.Util;

/// <summary>Splits a command line string into arguments, honouring single and double quotes.</summary>
public static class ArgumentParser
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return arguments;
        }

        var current = new StringBuilder();
        var quote = '\0';
        var hasValue = false;

        foreach (var c in commandLine)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                hasValue = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasValue || current.Length > 0)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasValue = false;
                }

                continue;
            }

            current.Append(c);
        }

        if (hasValue || current.Length > 0)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }
}
