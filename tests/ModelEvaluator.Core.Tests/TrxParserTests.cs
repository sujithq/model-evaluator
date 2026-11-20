using ModelEvaluator.Core.Evaluation;

namespace ModelEvaluator.Core.Tests;

public sealed class TrxParserTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));

    public TrxParserTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void ParseDirectory_SumsCountersAcrossFiles()
    {
        WriteTrx("first.trx", total: 10, passed: 8, failed: 1, notExecuted: 1);
        WriteTrx("second.trx", total: 5, passed: 5, failed: 0, notExecuted: 0);

        var counters = TrxParser.ParseDirectory(_directory);

        Assert.Equal(15, counters.Total);
        Assert.Equal(13, counters.Passed);
        Assert.Equal(1, counters.Failed);
        Assert.Equal(1, counters.Skipped);
    }

    [Fact]
    public void ParseFile_DerivesSkippedWhenNotExecutedIsMissing()
    {
        WriteTrx("derived.trx", total: 6, passed: 4, failed: 1, notExecuted: 0);

        var counters = TrxParser.ParseFile(Path.Combine(_directory, "derived.trx"));

        Assert.Equal(1, counters.Skipped);
    }

    [Fact]
    public void ParseFile_ReturnsEmptyForInvalidXml()
    {
        var path = Path.Combine(_directory, "broken.trx");
        File.WriteAllText(path, "<not-xml");

        Assert.Equal(TestCounters.Empty, TrxParser.ParseFile(path));
    }

    [Fact]
    public void ParseDirectory_ReturnsEmptyForMissingDirectory() =>
        Assert.Equal(TestCounters.Empty, TrxParser.ParseDirectory(Path.Combine(_directory, "missing")));

    private void WriteTrx(string name, int total, int passed, int failed, int notExecuted) =>
        File.WriteAllText(
            Path.Combine(_directory, name),
            $"""
             <?xml version="1.0" encoding="UTF-8"?>
             <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
               <ResultSummary outcome="Completed">
                 <Counters total="{total}" executed="{passed + failed}" passed="{passed}" failed="{failed}" notExecuted="{notExecuted}" />
               </ResultSummary>
             </TestRun>
             """);
}
