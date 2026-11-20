using System.Net;
using System.Text.Json;
using ModelEvaluator.ApiComparison.Core.Adapters;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Execution;
using ModelEvaluator.ApiComparison.Core.Scenarios;
using ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

namespace ModelEvaluator.ApiComparison.Core.Tests;

public class DirectRestAdapterTests
{
    [Fact]
    public async Task SendsOpenAiRequestAndCapturesReasoningUsageAndTtfb()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        var handler = new StubHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, fixture));
        var adapter = new DirectRestAdapter(new HttpClient(handler));

        var completion = await adapter.CompleteAsync(
            CreateInvocation(ScenarioFactory.Target()),
            CancellationToken.None);

        Assert.Null(completion.Error);
        Assert.Equal("hello", completion.Text);
        Assert.Equal(21, completion.Usage.InputTokens);
        Assert.Equal(13, completion.Usage.OutputTokens);
        Assert.Equal(7, completion.Usage.ReasoningTokens);
        Assert.Equal(5, completion.Usage.CachedInputTokens);
        Assert.NotNull(completion.TimeToFirstByteMilliseconds);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://example.test/v1/chat/completions", request.Uri.ToString());
        Assert.Contains("Bearer", request.Headers["Authorization"]);
        var body = JsonDocument.Parse(request.Body);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(512, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task UsesEntraBearerTokenForAzureV1Endpoint()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        var handler = new StubHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, fixture));
        var adapter = new DirectRestAdapter(
            new HttpClient(handler),
            new StaticTokenCredential("entra-token"));
        var target = new TargetConfiguration
        {
            Id = "azure",
            Adapter = AdapterIds.DirectRest,
            ApiKind = ApiKinds.AzureOpenAi,
            ModelId = "azure-deployment",
            Endpoint = "https://azure.example",
            Authentication = AuthenticationKinds.DefaultCredential,
            Parameters = new RequestParametersConfiguration
            {
                MaxOutputTokens = 200,
            },
        };

        await adapter.CompleteAsync(CreateInvocation(target), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://azure.example/openai/v1/chat/completions",
            request.Uri.ToString());
        Assert.Equal("Bearer entra-token", request.Headers["Authorization"]);
        Assert.False(request.Headers.ContainsKey("api-key"));
    }

    [Fact]
    public async Task RetriesTransientFailureAndCountsRetry()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        var handler = new StubHttpMessageHandler(index => index == 0
            ? TestResponses.Json(
                HttpStatusCode.TooManyRequests,
                """{"error":{"type":"rate_limit","code":"rate_limit","message":"retry"}}""",
                TimeSpan.Zero)
            : TestResponses.Json(HttpStatusCode.OK, fixture));
        var adapter = new DirectRestAdapter(new HttpClient(handler));
        var invocation = CreateInvocation(ScenarioFactory.Target()) with
        {
            MaxRetries = 1,
        };

        var completion = await adapter.CompleteAsync(invocation, CancellationToken.None);

        Assert.Null(completion.Error);
        Assert.Equal(1, completion.Retries);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ClassifiesUnsupportedParametersWithoutCrashing()
    {
        var handler = new StubHttpMessageHandler(_ => TestResponses.Json(
            HttpStatusCode.BadRequest,
            """{"error":{"type":"invalid_request_error","code":"unsupported_value","param":"temperature","message":"temperature is not supported"}}"""));
        var adapter = new DirectRestAdapter(new HttpClient(handler));
        var parameters = new EffectiveRequestParameters
        {
            MaxOutputTokens = 80,
            Temperature = 0.25,
        };
        var invocation = CreateInvocation(ScenarioFactory.Target()) with
        {
            Parameters = parameters,
        };

        var completion = await adapter.CompleteAsync(invocation, CancellationToken.None);

        Assert.Equal("unsupported-parameter", completion.Error?.Category);
        Assert.Equal("unsupported_value", completion.Error?.Code);
        Assert.Contains("temperature", Assert.Single(handler.Requests).Body);
    }

    [Fact]
    public async Task RecordsMalformedToolArgumentsAsAFunctionalToolError()
    {
        const string response =
            """{"id":"chatcmpl-tool","object":"chat.completion","created":1730000000,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call-bad","type":"function","function":{"name":"lookup_synthetic_plan","arguments":"{not-json"}}]},"finish_reason":"tool_calls"}]}""";
        var handler = new StubHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, response));
        var adapter = new DirectRestAdapter(new HttpClient(handler));

        var completion = await adapter.CompleteAsync(
            CreateInvocation(ScenarioFactory.Target()),
            CancellationToken.None);

        var toolCall = Assert.Single(completion.ToolCalls);
        Assert.NotNull(toolCall.ArgumentError);
        Assert.True(toolCall.Arguments.GetProperty("invalidJson").GetBoolean());
        Assert.Equal("{not-json", toolCall.Arguments.GetProperty("rawArguments").GetString());
        Assert.Null(completion.Error);
    }

    [Fact]
    public async Task SendsStrictStructuredOutputAndToolSchemasVerbatim()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        var handler = new StubHttpMessageHandler(_ => TestResponses.Json(HttpStatusCode.OK, fixture));
        var adapter = new DirectRestAdapter(new HttpClient(handler));
        var catalog = ScenarioCatalog.Load(Path.Combine(RepositoryLocator.Root, "scenarios", "v1"));
        var target = ScenarioFactory.Target();

        await adapter.CompleteAsync(
            CreateInvocation(target) with
            {
                Scenario = catalog.Get("structured-output"),
            },
            CancellationToken.None);
        await adapter.CompleteAsync(
            CreateInvocation(target) with
            {
                Scenario = catalog.Get("deterministic-tool-calling"),
            },
            CancellationToken.None);

        var structuredBody = JsonDocument.Parse(handler.Requests[0].Body).RootElement;
        Assert.Equal("json_schema", structuredBody
            .GetProperty("response_format").GetProperty("type").GetString());
        Assert.True(structuredBody
            .GetProperty("response_format").GetProperty("json_schema").GetProperty("strict").GetBoolean());
        var toolBody = JsonDocument.Parse(handler.Requests[1].Body).RootElement;
        Assert.Equal(
            "lookup_synthetic_plan",
            toolBody.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
    }

    private static AdapterInvocation CreateInvocation(TargetConfiguration target) => new()
    {
        Target = target,
        Scenario = ScenarioFactory.Simple(),
        Messages =
        [
            new ConversationMessage { Role = "system", Content = "Be exact." },
            new ConversationMessage { Role = "user", Content = "Reply with exactly: hello" },
        ],
        Parameters = new EffectiveRequestParameters
        {
            MaxOutputTokens = target.Parameters.MaxOutputTokens ?? 512,
        },
        MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(10),
    };

}
