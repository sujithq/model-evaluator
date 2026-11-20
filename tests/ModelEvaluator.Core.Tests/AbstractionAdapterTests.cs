using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Scenarios;
using ModelEvaluator.Core.Tests.TestSupport;

namespace ModelEvaluator.Core.Tests;

public class AbstractionAdapterTests
{
    [Fact]
    public async Task MicrosoftExtensionsAiChatUsesOpenAiCompatibleEndpoint()
    {
        var fixture = await Fixture("rest-completion.json");
        await using var server = new LoopbackHttpServer(fixture);
        var target = ScenarioFactory.Target(
            AdapterIds.MicrosoftExtensionsAi,
            $"{server.Endpoint}v1");
        var adapter = new MicrosoftExtensionsAiAdapter(useResponsesApi: false);

        var completion = await adapter.CompleteAsync(
            Invocation(target),
            CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(completion.Error);
        Assert.Equal("hello", completion.Text);
        Assert.Equal("stop", completion.FinishReason);
        Assert.Equal(21, completion.Usage.InputTokens);
        Assert.Equal(13, completion.Usage.OutputTokens);
        Assert.Equal(34, completion.Usage.TotalTokens);
        Assert.Equal(5, completion.Usage.CachedInputTokens);
        Assert.Equal(7, completion.Usage.ReasoningTokens);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal(
            "Microsoft.Extensions.AI.OpenAI",
            adapter.DescribePackage(target).Name);
        Assert.StartsWith("11.0.0", adapter.DescribePackage(target).Version);
    }

    [Fact]
    public async Task MicrosoftExtensionsAiResponsesUsesResponsesEndpoint()
    {
        var fixture = await Fixture("responses-completion.json");
        await using var server = new LoopbackHttpServer(fixture);
        var target = ScenarioFactory.Target(
            AdapterIds.MicrosoftExtensionsAiResponses,
            $"{server.Endpoint}v1");
        var adapter = new MicrosoftExtensionsAiAdapter(useResponsesApi: true);

        var completion = await adapter.CompleteAsync(
            Invocation(target),
            CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));
        var toolCall = Assert.Single(completion.ToolCalls);

        Assert.Null(completion.Error);
        Assert.Equal("hello", completion.Text);
        Assert.Equal("tool_calls", completion.FinishReason);
        Assert.Equal(21, completion.Usage.InputTokens);
        Assert.Equal(13, completion.Usage.OutputTokens);
        Assert.Equal(34, completion.Usage.TotalTokens);
        Assert.Equal(4, completion.Usage.CachedInputTokens);
        Assert.Equal(7, completion.Usage.ReasoningTokens);
        Assert.Equal("call_test", toolCall.Id);
        Assert.Equal("lookup_synthetic_plan", toolCall.Name);
        Assert.Equal("/v1/responses", request.Path);
    }

    [Fact]
    public async Task SemanticKernelUsesExactToolSchemaAndParsesToolCall()
    {
        var fixture = await Fixture("rest-tool-completion.json");
        await using var server = new LoopbackHttpServer(fixture);
        var target = ScenarioFactory.Target(
            AdapterIds.SemanticKernel,
            $"{server.Endpoint}v1");
        var adapter = new SemanticKernelAdapter();
        var scenario = ScenarioCatalog.Load(
            Path.Combine(RepositoryLocator.Root, "scenarios", "v1"))
            .Get("deterministic-tool-calling");
        var invocation = Invocation(target) with
        {
            Scenario = scenario,
        };

        var completion = await adapter.CompleteAsync(
            invocation,
            CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));
        using var body = JsonDocument.Parse(request.Body);
        var tool = Assert.Single(body.RootElement.GetProperty("tools").EnumerateArray());
        var toolCall = Assert.Single(completion.ToolCalls);

        Assert.Null(completion.Error);
        Assert.Equal("ToolCalls", completion.FinishReason);
        Assert.Equal("call_test", toolCall.Id);
        Assert.Equal("lookup_synthetic_plan", toolCall.Name);
        Assert.Equal("SYNTH-100", toolCall.Arguments.GetProperty("memberId").GetString());
        Assert.Equal(
            "lookup_synthetic_plan",
            tool.GetProperty("function").GetProperty("name").GetString());
        Assert.True(tool.GetProperty("function").GetProperty("strict").GetBoolean());
        Assert.StartsWith("1.82.0", adapter.DescribePackage(target).Version);
    }

    [Fact]
    public async Task SemanticKernelReplaysToolCallAndMatchingResultId()
    {
        var toolResponse = await Fixture("rest-tool-completion.json");
        var finalResponse = await Fixture("rest-completion.json");
        var handler = new StubHttpMessageHandler(index =>
            TestResponses.Json(
                System.Net.HttpStatusCode.OK,
                index == 0 ? toolResponse : finalResponse));
        var target = ScenarioFactory.Target(
            AdapterIds.SemanticKernel,
            "https://example.test/v1");
        var adapter = new SemanticKernelAdapter(
            httpClient: new HttpClient(handler));
        var scenario = ScenarioCatalog.Load(
            Path.Combine(RepositoryLocator.Root, "scenarios", "v1"))
            .Get("deterministic-tool-calling");
        var firstInvocation = Invocation(target) with
        {
            Scenario = scenario,
        };

        var first = await adapter.CompleteAsync(
            firstInvocation,
            CancellationToken.None);
        var toolCall = Assert.Single(first.ToolCalls);
        var secondInvocation = firstInvocation with
        {
            Messages =
            [
                .. firstInvocation.Messages,
                new ConversationMessage
                {
                    Role = "assistant",
                    ToolCalls = first.ToolCalls,
                },
                new ConversationMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Content = """{"benefitCode":"EVAL-BASIC"}""",
                },
            ],
            ToolRound = 1,
        };

        var second = await adapter.CompleteAsync(
            secondInvocation,
            CancellationToken.None);
        using var secondBody = JsonDocument.Parse(handler.Requests[1].Body);
        var messages = secondBody.RootElement
            .GetProperty("messages")
            .EnumerateArray()
            .ToList();
        var assistantToolCall = Assert.Single(
            messages[^2].GetProperty("tool_calls").EnumerateArray());

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.Equal("hello", second.Text);
        Assert.Equal(
            assistantToolCall.GetProperty("id").GetString(),
            messages[^1].GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public void ReportsAllAbstractionCombinations()
    {
        (IModelAdapter adapter, string id)[] targets =
        [
            (new MicrosoftExtensionsAiAdapter(false), AdapterIds.MicrosoftExtensionsAi),
            (new MicrosoftExtensionsAiAdapter(true), AdapterIds.MicrosoftExtensionsAiResponses),
            (new SemanticKernelAdapter(), AdapterIds.SemanticKernel),
        ];

        foreach (var (adapter, id) in targets)
        {
            var target = ScenarioFactory.Target(id, "https://example.test/v1");
            Assert.True(adapter.CheckCompatibility(target).Supported);
        }
    }

    [Fact]
    public async Task ReportsFrameworkOnlyReasoningGapsExplicitly()
    {
        var meai = new MicrosoftExtensionsAiAdapter(useResponsesApi: true);
        var semanticKernel = new SemanticKernelAdapter();
        var meaiInvocation = Invocation(
            ScenarioFactory.Target(
                AdapterIds.MicrosoftExtensionsAiResponses,
                "https://example.test/v1")) with
        {
            Parameters = new EffectiveRequestParameters
            {
                MaxOutputTokens = 80,
                ReasoningEffort = "minimal",
            },
        };
        var semanticKernelInvocation = Invocation(
            ScenarioFactory.Target(
                AdapterIds.SemanticKernel,
                "https://example.test/v1")) with
        {
            Parameters = new EffectiveRequestParameters
            {
                MaxOutputTokens = 80,
                ReasoningEffort = "none",
            },
        };

        var meaiCompletion = await meai.CompleteAsync(
            meaiInvocation,
            CancellationToken.None);
        var semanticKernelCompletion = await semanticKernel.CompleteAsync(
            semanticKernelInvocation,
            CancellationToken.None);

        Assert.Equal("unsupported-parameter", meaiCompletion.Error?.Category);
        Assert.Equal(
            "microsoft-extensions-ai-unsupported-reasoning-effort",
            meaiCompletion.Error?.Code);
        Assert.Equal("unsupported-parameter", semanticKernelCompletion.Error?.Category);
        Assert.Equal(
            "semantic-kernel-unsupported-reasoning-effort",
            semanticKernelCompletion.Error?.Code);
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

    private static Task<string> Fixture(string name) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
