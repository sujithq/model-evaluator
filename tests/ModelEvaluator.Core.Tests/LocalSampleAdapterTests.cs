using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Tests;

public sealed class LocalSampleAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));

    public LocalSampleAdapterTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task GenerateAsync_CopiesSampleAndAppliesOverlay()
    {
        Write("scenario/samples/good/src/App/Program.cs", "// good");
        Write("scenario/samples/good/README.md", "# good");
        Write("scenario/samples/broken-overlay/src/App/Program.cs", "// broken");

        var output = await RunAsync("broken");

        Assert.True(output.Succeeded, output.FailureReason);
        Assert.Equal("// broken", File.ReadAllText(Path.Combine(_root, "workspace", "src", "App", "Program.cs")));
        Assert.Equal("# good", File.ReadAllText(Path.Combine(_root, "workspace", "README.md")));
        Assert.Contains("estimatedCostUsd", output.UnavailableMetrics);
    }

    [Fact]
    public async Task GenerateAsync_WithUnknownVariant_IsAnInfrastructureFailure()
    {
        Write("scenario/samples/good/README.md", "# good");

        var output = await RunAsync("missing");

        Assert.False(output.Succeeded);
        Assert.True(output.InfrastructureFailure);
        Assert.Contains("missing", output.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    private async Task<ModelAttemptOutput> RunAsync(string variant)
    {
        var workspace = Path.Combine(_root, "workspace");
        var artifacts = Path.Combine(_root, "artifacts");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(artifacts);

        var scenario = new ScenarioPackage(
            new ScenarioDefinition
            {
                Id = "sample-scenario",
                Name = "Sample",
                ProjectType = "Console",
                BenchmarkVersion = "1.0.0",
                Samples = new Dictionary<string, SampleVariantDefinition>
                {
                    ["good"] = new() { Path = "samples/good" },
                    ["broken"] = new() { Path = "samples/good", Overlay = "samples/broken-overlay" },
                },
            },
            Path.Combine(_root, "scenario"),
            "prompt");

        return await new LocalSampleAdapter().GenerateAsync(
            new ModelAttemptContext
            {
                Scenario = scenario,
                Model = new ModelConfiguration
                {
                    Id = "reference",
                    Adapter = LocalSampleAdapter.AdapterKey,
                    Settings = new Dictionary<string, string> { ["variant"] = variant },
                },
                WorkspacePath = workspace,
                ArtifactsPath = artifacts,
                Prompt = "prompt",
                PromptFilePath = Path.Combine(artifacts, "prompt.md"),
                Timeout = TimeSpan.FromMinutes(1),
                Repetition = 1,
            },
            CancellationToken.None);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}

public sealed class ModelAdapterFactoryTests
{
    [Fact]
    public void CreateDefault_ExposesShippedAdapters()
    {
        var factory = ModelAdapterFactory.CreateDefault();

        Assert.Contains(LocalSampleAdapter.AdapterKey, factory.Keys);
        Assert.Contains(CommandLineAdapter.AdapterKey, factory.Keys);
        Assert.IsType<LocalSampleAdapter>(factory.Get("Local-Sample"));
    }

    [Fact]
    public void Get_ThrowsForUnknownAdapter() =>
        Assert.Throws<KeyNotFoundException>(() => ModelAdapterFactory.CreateDefault().Get("nope"));
}
