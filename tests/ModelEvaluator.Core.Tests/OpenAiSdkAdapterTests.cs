using System.Text.Json;
using ModelEvaluator.Core.Adapters;
using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Core.Execution;
using ModelEvaluator.Core.Tests.TestSupport;

namespace ModelEvaluator.Core.Tests;

public class OpenAiSdkAdapterTests
{
    [Fact]
    public async Task UsesPinnedSdkAgainstOpenAiCompatibleLoopbackEndpoint()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        await using var server = new LoopbackHttpServer(fixture);
        var adapter = new OpenAiSdkAdapter();
        var target = ScenarioFactory.Target(
            AdapterIds.OpenAiSdk,
            $"{server.Endpoint}v1");
        var invocation = CreateInvocation(target) with
        {
            Parameters = new EffectiveRequestParameters
            {
                MaxOutputTokens = 222,
                Temperature = 0.25,
            },
        };

        var completion = await adapter.CompleteAsync(invocation, CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(completion.Error);
        Assert.Equal("hello", completion.Text);
        Assert.Equal(21, completion.Usage.InputTokens);
        Assert.Equal(13, completion.Usage.OutputTokens);
        Assert.Equal(7, completion.Usage.ReasoningTokens);
        Assert.Null(completion.TimeToFirstByteMilliseconds);
        Assert.Equal("/v1/chat/completions", request.Path);
        Assert.Equal("Bearer test-key", request.Headers["Authorization"]);
        var body = JsonDocument.Parse(request.Body);
        Assert.Equal(222, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
        Assert.Equal(0.25, body.RootElement.GetProperty("temperature").GetDouble());
        Assert.StartsWith("2.15.0", adapter.DescribePackage(target).Version);
    }

    [Fact]
    public async Task ClassifiesSdkUnsupportedParameterResponse()
    {
        const string error =
            """{"error":{"type":"invalid_request_error","code":"unsupported_value","param":"temperature","message":"temperature is not supported"}}""";
        await using var server = new LoopbackHttpServer(error, System.Net.HttpStatusCode.BadRequest);
        var adapter = new OpenAiSdkAdapter();
        var target = ScenarioFactory.Target(
            AdapterIds.OpenAiSdk,
            $"{server.Endpoint}v1");

        var completion = await adapter.CompleteAsync(CreateInvocation(target), CancellationToken.None);

        Assert.Equal("unsupported-parameter", completion.Error?.Category);
        Assert.Equal("unsupported_value", completion.Error?.Code);
        Assert.Equal(400, completion.Error?.StatusCode);
    }

    [Fact]
    public void ReportsSupportedAndUnsupportedCombinations()
    {
        var adapter = new OpenAiSdkAdapter();
        var openAiApiKey = ScenarioFactory.Target(AdapterIds.OpenAiSdk);
        var openAiEntra = new TargetConfiguration
        {
            Id = "openai-entra",
            Adapter = AdapterIds.OpenAiSdk,
            ApiKind = ApiKinds.OpenAi,
            ModelId = "model",
            Endpoint = "https://example.test/v1",
            Authentication = AuthenticationKinds.DefaultCredential,
        };
        var azureApiKey = new TargetConfiguration
        {
            Id = "azure",
            Adapter = AdapterIds.OpenAiSdk,
            ApiKind = ApiKinds.AzureOpenAi,
            ModelId = "deployment",
            Endpoint = "https://azure.example",
            ApiKey = "test-key",
            Authentication = AuthenticationKinds.ApiKey,
        };

        Assert.True(adapter.CheckCompatibility(openAiApiKey).Supported);
        Assert.False(adapter.CheckCompatibility(openAiEntra).Supported);
        Assert.True(adapter.CheckCompatibility(azureApiKey).Supported);
        Assert.StartsWith("2.15.0", adapter.DescribePackage(azureApiKey).Version);
    }

    [Fact]
    public async Task UsesAzureAiOpenAiSdkContractAgainstLoopbackEndpoint()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        await using var server = new LoopbackHttpServer(fixture);
        var target = new TargetConfiguration
        {
            Id = "azure-loopback",
            Adapter = AdapterIds.OpenAiSdk,
            ApiKind = ApiKinds.AzureOpenAi,
            ModelId = "deployment-exact",
            Endpoint = server.Endpoint.ToString(),
            ApiKey = "azure-key",
            Authentication = AuthenticationKinds.ApiKey,
        };
        var adapter = new OpenAiSdkAdapter();

        var completion = await adapter.CompleteAsync(CreateInvocation(target), CancellationToken.None);
        var request = await server.Request.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(completion.Error);
        Assert.Equal("hello", completion.Text);
        Assert.Equal(
            "/openai/v1/chat/completions",
            request.Path);
        Assert.Equal("Bearer azure-key", request.Headers["Authorization"]);
        Assert.Equal("deployment-exact", JsonDocument.Parse(request.Body)
            .RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task UsesInjectedEntraCredentialAgainstAzureV1Loopback()
    {
        var fixture = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "rest-completion.json"));
        var handler = new StubHttpMessageHandler(_ => TestResponses.Json(
            System.Net.HttpStatusCode.OK,
            fixture));
        var target = new TargetConfiguration
        {
            Id = "azure-entra-loopback",
            Adapter = AdapterIds.OpenAiSdk,
            ApiKind = ApiKinds.AzureOpenAi,
            ModelId = "deployment-exact",
            Endpoint = "https://azure.example",
            Authentication = AuthenticationKinds.DefaultCredential,
        };
        var adapter = new OpenAiSdkAdapter(
            new StaticTokenCredential("entra-token"),
            new HttpClient(handler));

        var completion = await adapter.CompleteAsync(CreateInvocation(target), CancellationToken.None);
        var request = Assert.Single(handler.Requests);

        Assert.Null(completion.Error);
        Assert.Equal(
            "https://azure.example/openai/v1/chat/completions",
            request.Uri.ToString());
        Assert.Equal("Bearer entra-token", request.Headers["Authorization"]);
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
            MaxOutputTokens = 80,
        },
        MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(10),
    };
}
