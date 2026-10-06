using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;

namespace ModelEvaluator.Core.Tests;

public sealed class ModelAvailabilityProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task LocalAdapter_NeedsNoProviderAccount()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(
            new ModelConfiguration { Id = "reference-good", Adapter = "local-sample" }, Timeout);

        Assert.Equal(ModelAvailability.NotApplicable, result.Availability);
    }

    [Fact]
    public async Task MissingCommandSetting_IsUnknown()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(
            new ModelConfiguration { Id = "broken", Adapter = "command-line" }, Timeout);

        Assert.Equal(ModelAvailability.Unknown, result.Availability);
        Assert.Contains("command", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingExecutable_IsUnknown()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(
            Runner("nonexistent-test-executable", string.Empty), Timeout);

        Assert.Equal(ModelAvailability.Unknown, result.Availability);
        Assert.Contains("nonexistent-test-executable", result.Detail);
    }

    [Fact]
    public async Task SuccessfulRunner_IsAvailable()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(Script("ok", 0), Timeout);

        Assert.Equal(ModelAvailability.Available, result.Availability);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task RunnerRejectingTheModel_IsUnavailable()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(
            Script("Model probe-test from --model flag is not available.", 1), Timeout);

        Assert.Equal(ModelAvailability.Unavailable, result.Availability);
        Assert.Contains("not available", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnauthenticatedRunner_IsUnknownRatherThanUnavailable()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(
            Script("not logged in, model is not available", 1), Timeout);

        Assert.Equal(ModelAvailability.Unknown, result.Availability);
        Assert.Contains("not authenticated", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnrecognisedFailure_IsUnknown()
    {
        var result = await new ModelAvailabilityProbe().ProbeAsync(
            Script("something else broke", 3), Timeout);

        Assert.Equal(ModelAvailability.Unknown, result.Availability);
        Assert.Contains("something else broke", result.Detail);
    }

    // The message is single quoted so it survives the shell's own re-parsing of the argument string.
    private static ModelConfiguration Script(string message, int exitCode) => OperatingSystem.IsWindows()
        ? Runner("powershell.exe", $"-NoProfile -NonInteractive -Command \"Write-Output '{message}'; exit {exitCode}\"")
        : Runner("sh", $"-c \"echo '{message}'; exit {exitCode}\"");

    private static ModelConfiguration Runner(string command, string arguments) => new()
    {
        Id = "probe-test",
        Adapter = "command-line",
        Settings = new Dictionary<string, string> { ["command"] = command, ["arguments"] = arguments },
    };
}
