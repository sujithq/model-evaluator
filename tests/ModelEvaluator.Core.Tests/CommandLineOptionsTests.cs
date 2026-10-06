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
            new ModelConfiguration { Id = "alpha", Adapter = "local-sample", Enabled = true },
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
    public void Parse_ReadsProbeOptions()
    {
        var options = CommandLineOptions.Parse(["--probe", "--probe-timeout", "45"]);

        Assert.True(options.Probe);
        Assert.Equal(45, options.ProbeTimeoutSeconds);
    }

    [Fact]
    public void Parse_DefaultsProbeOptionsToOff()
    {
        var options = CommandLineOptions.Parse([]);

        Assert.False(options.Probe);
        Assert.Null(options.ProbeTimeoutSeconds);
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

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void Parse_RejectsInvalidParallelLimit(string value) =>
        Assert.Throws<FormatException>(() => CommandLineOptions.Parse(["--max-parallel", value]));

    [Fact]
    public void Parse_ParallelLimitRequiresValue() =>
        Assert.Throws<FormatException>(() => CommandLineOptions.Parse(["--max-parallel"]));

    [Fact]
    public void Apply_ParallelLimitDefaultsToOneAndOverridesConfiguration()
    {
        Assert.Equal(1, CommandLineOptions.Parse([]).Apply(BaseConfiguration).MaxParallel);
        Assert.Equal(4, CommandLineOptions.Parse([]).Apply(BaseConfiguration with { MaxParallel = 4 }).MaxParallel);
        Assert.Equal(2, CommandLineOptions.Parse(["--max-parallel", "2"]).Apply(BaseConfiguration with { MaxParallel = 4 }).MaxParallel);
    }

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
    public void Apply_WithoutModelOverride_SelectsOnlyEnabledModels()
    {
        var configuration = CommandLineOptions.Parse([]).Apply(BaseConfiguration);

        Assert.Equal("alpha", Assert.Single(configuration.Models).Id);
    }

    [Fact]
    public void Apply_ExplicitModelOverride_CanSelectDisabledModel()
    {
        var configuration = CommandLineOptions.Parse(["--models", "beta"]).Apply(BaseConfiguration);

        Assert.Equal("beta", Assert.Single(configuration.Models).Id);
    }

    [Fact]
    public void Apply_ThrowsForUnknownModel()
    {
        var options = CommandLineOptions.Parse(["--models", "gamma"]);

        Assert.Throws<KeyNotFoundException>(() => options.Apply(BaseConfiguration));
    }

    [Fact]
    public void Apply_WithoutOverrides_KeepsEnabledConfiguration()
    {
        var configuration = CommandLineOptions.Parse([]).Apply(BaseConfiguration);

        Assert.Equal(BaseConfiguration.Models.Where(model => model.Enabled), configuration.Models);
        Assert.Equal(BaseConfiguration.Repetitions, configuration.Repetitions);
        Assert.Equal(BaseConfiguration.BenchmarkRoot, configuration.BenchmarkRoot);
        Assert.False(configuration.Debug);
    }

    [Fact]
    public void Apply_DebugEnablesDiagnosticsWithoutChangingOtherOptions()
    {
        var configuration = CommandLineOptions.Parse(["--debug"]).Apply(BaseConfiguration);
        var expected = CommandLineOptions.Parse([]).Apply(BaseConfiguration);

        Assert.True(configuration.Debug);
        Assert.Equal(expected.Models, configuration.Models);
        Assert.Equal(
            expected with { Models = [] },
            configuration with { Debug = false, Models = [] });
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
              "maxParallel": 2,
              "models": [ { "id": "alpha", "adapter": "local-sample", "settings": { "variant": "good" } } ]
            }
            """);

        try
        {
            var configuration = EvaluationConfiguration.Load(path);

            Assert.Equal(4, configuration.Repetitions);
            Assert.True(configuration.Debug);
            Assert.Equal(2, configuration.MaxParallel);
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
