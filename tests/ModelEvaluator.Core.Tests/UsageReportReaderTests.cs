using System.Text.Json.Nodes;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Scenarios;

namespace ModelEvaluator.Core.Tests;

public sealed class UsageReportReaderTests
{
    // Captured from an approved no-tool probe with Copilot CLI 1.0.87-0.
    private const string NativeUsage = """
        {
          "totalPremiumRequestCost": 1,
          "totalUserRequests": 1,
          "totalNanoAiu": 199580000,
          "tokenDetails": {
            "input": { "tokenCount": 3 },
            "cache_read": { "tokenCount": 0 },
            "cache_write": { "tokenCount": 15944 },
            "output": { "tokenCount": 5 }
          },
          "totalApiDurationMs": 1474,
          "modelMetrics": {
            "gpt-6-luna": {
              "requests": { "count": 1, "cost": 1 },
              "usage": {
                "inputTokens": 15947,
                "outputTokens": 5,
                "cacheReadTokens": 0,
                "cacheWriteTokens": 15944,
                "reasoningTokens": 0
              },
              "totalNanoAiu": 199580000
            }
          },
          "agentMetrics": {
            "main": {
              "totalNanoAiu": 199580000,
              "modelMetrics": {
                "gpt-6-luna": {
                  "requests": { "count": 1, "cost": 1 },
                  "usage": { "inputTokens": 15947, "outputTokens": 5 }
                }
              }
            }
          },
          "currentModel": "gpt-6-luna",
          "lastCallInputTokens": 15947,
          "lastCallOutputTokens": 5
        }
        """;

    [Fact]
    public void Parse_CopilotPreservesUnitsAndDoesNotDoubleCountBreakdowns()
    {
        var usage = UsageReportReader.Parse(NativeUsage, "copilot-cli");

        Assert.Equal(0.19958m, usage.AiCredits);
        Assert.Equal(1m, usage.PremiumRequests);
        Assert.Equal(15947, usage.InputTokens);
        Assert.Equal(5, usage.OutputTokens);
        Assert.Equal(0, usage.CacheReadTokens);
        Assert.Equal(15944, usage.CacheWriteTokens);
        Assert.Equal(0, usage.ReasoningTokens);
        Assert.Equal(1, usage.ApiRequests);
        Assert.Equal(1.474, usage.ApiDurationSeconds);
        Assert.Equal(["gpt-6-luna"], usage.ReportedModels);
        Assert.Null(usage.ToolCalls);
        Assert.Null(usage.EstimatedCostUsd);
        Assert.Equal(["toolCalls", "estimatedCostUsd"], usage.UnavailableMetrics);
        Assert.Empty(usage.Warnings);
    }

    [Fact]
    public void Parse_SumsModelsButMissingFieldMakesAggregateUnavailable()
    {
        var json = JsonNode.Parse(NativeUsage)!.AsObject();
        json["modelMetrics"]!["second-model"] = JsonNode.Parse(
            """{"usage":{"inputTokens":10,"outputTokens":7},"requests":{"count":2}}""");

        var usage = UsageReportReader.Parse(json.ToJsonString(), "copilot-cli");

        Assert.Equal(15957, usage.InputTokens);
        Assert.Equal(12, usage.OutputTokens);
        Assert.Equal(3, usage.ApiRequests);
        Assert.Null(usage.CacheReadTokens);
        Assert.Null(usage.CacheWriteTokens);
        Assert.Null(usage.ReasoningTokens);
        Assert.Equal(0.19958m, usage.AiCredits);
        Assert.Equal(["gpt-6-luna", "second-model"], usage.ReportedModels);
    }

    [Fact]
    public void Parse_LegacyRequestsAreNotConvertedToCreditsOrDollars()
    {
        var usage = UsageReportReader.Parse("""{"totalPremiumRequestCost":1.5}""", "copilot-cli");

        Assert.Equal(1.5m, usage.PremiumRequests);
        Assert.Null(usage.AiCredits);
        Assert.Null(usage.EstimatedCostUsd);
        Assert.Null(usage.InputTokens);
    }

    [Theory]
    [InlineData("""{"totalNanoAiu":-1}""")]
    [InlineData("""{"totalNanoAiu":"bad"}""")]
    [InlineData("""{"modelMetrics":[]}""")]
    [InlineData("""{"modelMetrics":{"model":{"usage":{"inputTokens":1.5}}}}""")]
    [InlineData("""{"modelMetrics":{"model":{"usage":{"inputTokens":9223372036854775808}}}}""")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{")]
    public void Parse_InvalidNativeMetricsAreUnavailableWithWarnings(string json)
    {
        var usage = UsageReportReader.Parse(json, "copilot-cli");

        Assert.NotEmpty(usage.Warnings);
        Assert.Null(usage.InputTokens);
        Assert.Null(usage.AiCredits);
    }

    [Fact]
    public void Parse_ZeroIsARealMeasurement()
    {
        var usage = UsageReportReader.Parse("""{"totalNanoAiu":0,"totalPremiumRequestCost":0}""", "copilot-cli");

        Assert.Equal(0m, usage.AiCredits);
        Assert.Equal(0m, usage.PremiumRequests);
        Assert.DoesNotContain("aiCredits", usage.UnavailableMetrics);
        Assert.Empty(usage.Warnings);
    }

    [Fact]
    public void Parse_NormalizedSchemaRemainsSupported()
    {
        var usage = UsageReportReader.Parse(
            """{"toolCalls":42,"inputTokens":123,"outputTokens":17,"estimatedCostUsd":0.42}""", "normalized");

        Assert.Equal(42, usage.ToolCalls);
        Assert.Equal(123, usage.InputTokens);
        Assert.Equal(17, usage.OutputTokens);
        Assert.Equal(0.42m, usage.EstimatedCostUsd);
        Assert.Null(usage.AiCredits);
        Assert.Empty(usage.Warnings);
    }

    [Fact]
    public void Parse_InvalidNormalizedUsageIsNotSilentlyAccepted()
    {
        var usage = UsageReportReader.Parse("""{"toolCalls":-1}""", "normalized");

        Assert.NotEmpty(usage.Warnings);
        Assert.Null(usage.ToolCalls);
    }

    [Fact]
    public void Read_MissingFileReturnsDiagnosticWithoutInventingMeasurements()
    {
        var usage = UsageReportReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), "copilot-cli");

        Assert.NotEmpty(usage.Warnings);
        Assert.Null(usage.AiCredits);
        Assert.Null(usage.InputTokens);
    }

    [Fact]
    public async Task CommandLineAdapter_MapsNativeUsageToAttemptOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "eval-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "native usage.json"), NativeUsage);
            var scenario = new ScenarioPackage(new ScenarioDefinition
            {
                Id = "test",
                Name = "Test",
                ProjectType = "Console",
                BenchmarkVersion = "1.0.0",
            }, root, "prompt");
            var output = await new CommandLineAdapter(new ProcessRunner()).GenerateAsync(new ModelAttemptContext
            {
                Scenario = scenario,
                Model = new ModelConfiguration
                {
                    Id = "test",
                    Adapter = "command-line",
                    Settings = new Dictionary<string, string>
                    {
                        ["command"] = "dotnet",
                        ["arguments"] = "--version",
                        ["usageFile"] = "native usage.json",
                        ["usageFormat"] = "copilot-cli",
                    },
                },
                WorkspacePath = root,
                ArtifactsPath = root,
                Prompt = "prompt",
                PromptFilePath = Path.Combine(root, "prompt.md"),
                Repetition = 1,
                Timeout = TimeSpan.FromSeconds(30),
            }, CancellationToken.None);

            Assert.True(output.Succeeded);
            Assert.Equal(0.19958m, output.AiCredits);
            Assert.Equal(15947, output.InputTokens);
            Assert.Equal(5, output.OutputTokens);
            Assert.Equal(1, output.ApiRequests);
            Assert.Equal(15944, output.CacheWriteTokens);
            Assert.Equal(["gpt-6-luna"], output.ReportedModels);
            Assert.Empty(output.UsageWarnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
