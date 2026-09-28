using System.Xml.Linq;

namespace ModelEvaluator.Core.Evaluation;

/// <summary>Counts parsed from a VSTest .trx result file.</summary>
public sealed record TestCounters(int Total, int Passed, int Failed, int Skipped)
{
    public static TestCounters Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>Reads pass/fail/skip counts from .trx files produced by <c>dotnet test</c>.</summary>
public static class TrxParser
{
    public static TestCounters ParseDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return TestCounters.Empty;
        }

        var total = 0;
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*.trx", SearchOption.AllDirectories))
        {
            var counters = ParseFile(file);
            total += counters.Total;
            passed += counters.Passed;
            failed += counters.Failed;
            skipped += counters.Skipped;
        }

        return new TestCounters(total, passed, failed, skipped);
    }

    public static TestCounters ParseFile(string path)
    {
        try
        {
            var counters = XDocument.Load(path).Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Counters");
            if (counters is null)
            {
                return TestCounters.Empty;
            }

            var total = ReadInt(counters, "total");
            var passed = ReadInt(counters, "passed");
            var failed = ReadInt(counters, "failed");
            var notExecuted = ReadInt(counters, "notExecuted");
            var skipped = Math.Max(notExecuted, total - passed - failed);

            return new TestCounters(total, passed, failed, skipped);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return TestCounters.Empty;
        }
    }

    private static int ReadInt(XElement element, string attribute) =>
        int.TryParse(element.Attribute(attribute)?.Value, out var value) ? value : 0;
}
