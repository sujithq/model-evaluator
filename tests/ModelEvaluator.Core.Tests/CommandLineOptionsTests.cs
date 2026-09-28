using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Tests;

public sealed class CommandLineOptionsTests
{
    private static readonly EvaluationConfiguration BaseConfiguration = new()
    {
        BenchmarkRoot = "/benchmarks/v1",
        OutputDirectory = "/artifacts",
        Repetitions = 3,
        Models =
        [
            new ModelConfiguration { Id = "alpha", Adapter = "local-sample" },
            new ModelConfiguration { Id = "beta", Adapter = "command-line" },
        ],
    };

    [Fact]
    public void Parse_ReadsKnownOptions()
    {
        var options = CommandLineOptions.Parse(
            ["--models", "alpha, beta", "--scenarios", "one", "--repetitions", "5", "--keep-workspaces", "--debug"]);

        Assert.Equal(["alpha", "beta"], options.Models);
        Assert.Equal(["one"], options.Scenarios);
        Assert.Equal(5, options.Repetitions);
        Assert.True(options.KeepWorkspaces);
        Assert.True(options.Debug);
    }

    [Fact]
    public void Parse_RejectsUnknownOption() =>
        Assert.Throws<FormatException>(() => CommandLineOptions.Parse(["--nope"]));

    [Fact]
    public void Parse_RejectsMissingValue() =>
        Assert.Throws<FormatException>(() => CommandLineOptions.Parse(["--models"]));

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("many")]
    public void Parse_RejectsNonPositiveRepetitions(string value) =>
        Assert.Throws<FormatException>(() => CommandLineOptions.Parse(["--repetitions", value]));

    [Fact]
    public void Apply_FiltersModelsAndOverridesBudgets()
    {
        var options = CommandLineOptions.Parse(
            ["--models", "beta", "--repetitions", "7", "--generation-timeout", "120"]);

        var configuration = options.Apply(BaseConfiguration);

        Assert.Equal("beta", Assert.Single(configuration.Models).Id);
        Assert.Equal(7, configuration.Repetitions);
        Assert.Equal(120, configuration.Budgets.GenerationTimeoutSeconds);
    }

    [Fact]
    public void Apply_ThrowsForUnknownModel()
    {
        var options = CommandLineOptions.Parse(["--models", "gamma"]);

        Assert.Throws<KeyNotFoundException>(() => options.Apply(BaseConfiguration));
    }

    [Fact]
    public void Apply_WithoutOverrides_KeepsConfiguration()
    {
        var configuration = CommandLineOptions.Parse([]).Apply(BaseConfiguration);

        Assert.Equal(BaseConfiguration.Models.Count, configuration.Models.Count);
        Assert.Equal(BaseConfiguration.Repetitions, configuration.Repetitions);
        Assert.Equal(BaseConfiguration.BenchmarkRoot, configuration.BenchmarkRoot);
        Assert.False(configuration.Debug);
    }

    [Fact]
    public void Apply_DebugEnablesDiagnosticsWithoutChangingOtherOptions()
    {
        var configuration = CommandLineOptions.Parse(["--debug"]).Apply(BaseConfiguration);

        Assert.True(configuration.Debug);
        Assert.Equal(BaseConfiguration, configuration with { Debug = false });
    }

    [Fact]
    public void Apply_PreservesDebugEnabledInConfiguration() =>
        Assert.True(CommandLineOptions.Parse([]).Apply(BaseConfiguration with { Debug = true }).Debug);

    [Fact]
    public void Load_ResolvesPathsRelativeToTheConfigurationFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "evaluation.json");
        File.WriteAllText(path,
            """
            {
              "benchmarkRoot": "../benchmarks/v1",
              "outputDirectory": "out",
              "repetitions": 4,
              "debug": true,
              "models": [ { "id": "alpha", "adapter": "local-sample", "settings": { "variant": "good" } } ]
            }
            """);

        try
        {
            var configuration = EvaluationConfiguration.Load(path);

            Assert.Equal(4, configuration.Repetitions);
            Assert.True(configuration.Debug);
            Assert.Equal(Path.GetFullPath(Path.Combine(directory, "out")), configuration.OutputDirectory);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(directory, "../benchmarks/v1")),
                configuration.BenchmarkRoot);
            Assert.Equal("good", configuration.Models.Single().GetSetting("variant"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
