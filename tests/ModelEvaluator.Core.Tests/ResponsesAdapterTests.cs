using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Tests.TestSupport;

namespace ModelEvaluator.Core.Tests;

public class ResponsesAdapterTests
{
    [Fact]
    public async Task DirectRestResponsesSendsResponsesShapeAndParsesUsageAndTools()
    {
        var fixture = await Fixture();
        await using var server = new LoopbackHttpServer(fixture);
        var adapter = new DirectResponsesAdapter();
        var target = ScenarioFactory.Target(
            AdapterIds.DirectRestResponses,
            $"{server.Endpoint}v1");

        var completion = await adapter.CompleteAsync(
            Invocation(target),
            CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));
        using var body = JsonDocument.Parse(request.Body);
        var toolCall = Assert.Single(completion.ToolCalls);

        Assert.Null(completion.Error);
        Assert.Equal("completed", completion.FinishReason);
        Assert.Equal("hello", completion.Text);
        Assert.Equal(21, completion.Usage.InputTokens);
        Assert.Equal(13, completion.Usage.OutputTokens);
        Assert.Equal(34, completion.Usage.TotalTokens);
        Assert.Equal(4, completion.Usage.CachedInputTokens);
        Assert.Equal(7, completion.Usage.ReasoningTokens);
        Assert.Equal("call_test", toolCall.Id);
        Assert.Equal("lookup_synthetic_plan", toolCall.Name);
        Assert.Equal("/v1/responses", request.Path);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.Equal("Bearer test-key", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task OpenAiSdkResponsesUsesPinnedSdkAndParsesUsageAndTools()
    {
        var fixture = await Fixture();
        await using var server = new LoopbackHttpServer(fixture);
        var adapter = new OpenAiResponsesAdapter();
        var target = ScenarioFactory.Target(
            AdapterIds.OpenAiSdkResponses,
            $"{server.Endpoint}v1");

        var completion = await adapter.CompleteAsync(
            Invocation(target),
            CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));
        var toolCall = Assert.Single(completion.ToolCalls);

        Assert.Null(completion.Error);
        Assert.Equal("Completed", completion.FinishReason);
        Assert.Equal("hello", completion.Text);
        Assert.Equal(21, completion.Usage.InputTokens);
        Assert.Equal(13, completion.Usage.OutputTokens);
        Assert.Equal(34, completion.Usage.TotalTokens);
        Assert.Equal(4, completion.Usage.CachedInputTokens);
        Assert.Equal(7, completion.Usage.ReasoningTokens);
        Assert.Equal("call_test", toolCall.Id);
        Assert.Equal("/v1/responses", request.Path);
        Assert.StartsWith("2.15.0", adapter.DescribePackage(target).Version);
    }

    [Fact]
    public void ResponsesAdaptersReportSupportedLiveCombination()
    {
        var direct = new DirectResponsesAdapter();
        var sdk = new OpenAiResponsesAdapter();
        var directTarget = new TargetConfiguration
        {
            Id = "direct-responses",
            Adapter = AdapterIds.DirectRestResponses,
            ApiKind = ApiKinds.OpenAi,
            ModelId = "model",
            Endpoint = "https://example.test/v1",
            ApiKey = "key",
            Authentication = AuthenticationKinds.ApiKey,
        };
        var sdkTarget = new TargetConfiguration
        {
            Id = "sdk-responses",
            Adapter = AdapterIds.OpenAiSdkResponses,
            ApiKind = ApiKinds.OpenAi,
            ModelId = "model",
            Endpoint = "https://example.test/v1",
            ApiKey = "key",
            Authentication = AuthenticationKinds.ApiKey,
        };

        Assert.True(direct.CheckCompatibility(directTarget).Supported);
        Assert.True(sdk.CheckCompatibility(sdkTarget).Supported);
        Assert.StartsWith("2.15.0", sdk.DescribePackage(sdkTarget).Version);
    }

    private static AdapterInvocation Invocation(TargetConfiguration target) => new()
    {
        Target = target,
        Scenario = ScenarioFactory.Simple(),
        Messages =
        [
            new ConversationMessage
            {
                Role = "system",
                Content = "Be exact.",
            },
            new ConversationMessage
            {
                Role = "user",
                Content = "Reply with exactly: hello",
            },
        ],
        Parameters = new EffectiveRequestParameters
        {
            MaxOutputTokens = 80,
        },
        MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static Task<string> Fixture() =>
        File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "responses-completion.json"));
}
