using ModelEvaluator.ApiComparison.Core.Adapters;
using ModelEvaluator.ApiComparison.Core.Configuration;
using ModelEvaluator.ApiComparison.Core.Execution;
using ModelEvaluator.ApiComparison.Core.Tests.TestSupport;

namespace ModelEvaluator.ApiComparison.Core.Tests;

public class CopilotCliAdapterTests
{
    [Fact]
    public async Task ParsesFinalMessageAndUsageFromCopilotJsonl()
    {
        var runner = new StubCopilotCliRunner(new CopilotCliRunResult
        {
            ExitCode = 0,
            StandardOutput =
                """
                {"type":"assistant.message","data":{"model":"gpt-6.2-sol","content":"hello","toolRequests":[],"phase":"final_answer"}}
                """,
            UsageJson =
                """
                {"providerModelMetrics":[{"modelId":"gpt-6.2-sol","metrics":{"usage":{"inputTokens":100,"outputTokens":12,"cacheReadTokens":25,"reasoningTokens":4}}}]}
                """,
        });
        var adapter = new CopilotCliAdapter(runner);

        var completion = await adapter.CompleteAsync(CreateInvocation(), CancellationToken.None);

        Assert.Null(completion.Error);
        Assert.Equal("completed", completion.FinishReason);
        Assert.Equal("hello", completion.Text);
        Assert.Empty(completion.ToolCalls);
        Assert.Equal(100, completion.Usage.InputTokens);
        Assert.Equal(12, completion.Usage.OutputTokens);
        Assert.Equal(112, completion.Usage.TotalTokens);
        Assert.Equal(25, completion.Usage.CachedInputTokens);
        Assert.Equal(4, completion.Usage.ReasoningTokens);
        Assert.Contains("EVALUATION_PAYLOAD_JSON", runner.Request?.Prompt);
        Assert.Equal("gpt-6.2-sol", runner.Request?.ModelId);
        Assert.Equal("low", runner.Request?.ReasoningEffort);
        Assert.StartsWith("1.0.0", adapter.DescribePackage(Target()).Version);
    }

    [Fact]
    public async Task ParsesPromptMediatedToolCallEnvelope()
    {
        var runner = new StubCopilotCliRunner(new CopilotCliRunResult
        {
            ExitCode = 0,
            StandardOutput =
                """
                {"type":"assistant.message","data":{"model":"gpt-6.2-sol","content":"{\"__modelEvaluatorToolCalls\":[{\"id\":\"call-1\",\"name\":\"lookup_synthetic_plan\",\"arguments\":{\"memberId\":\"SYNTH-100\"}}]}","toolRequests":[]}}
                """,
            UsageJson = "{}",
        });
        var adapter = new CopilotCliAdapter(runner);

        var completion = await adapter.CompleteAsync(CreateInvocation(), CancellationToken.None);
        var toolCall = Assert.Single(completion.ToolCalls);

        Assert.Null(completion.Error);
        Assert.Equal("tool_calls", completion.FinishReason);
        Assert.Equal("call-1", toolCall.Id);
        Assert.Equal("lookup_synthetic_plan", toolCall.Name);
        Assert.Equal(
            "SYNTH-100",
            toolCall.Arguments.GetProperty("memberId").GetString());
    }

    [Fact]
    public async Task ClassifiesMalformedToolCallEnvelope()
    {
        var runner = new StubCopilotCliRunner(new CopilotCliRunResult
        {
            ExitCode = 0,
            StandardOutput =
                """
                {"type":"assistant.message","data":{"model":"gpt-6.2-sol","content":"{\"__modelEvaluatorToolCalls\":[{\"id\":","toolRequests":[]}}
                """,
        });
        var adapter = new CopilotCliAdapter(runner);

        var completion = await adapter.CompleteAsync(CreateInvocation(), CancellationToken.None);

        Assert.Equal("invalid-response", completion.Error?.Category);
        Assert.Equal("invalid-tool-call-envelope", completion.Error?.Code);
    }

    [Fact]
    public async Task ClassifiesUnsupportedSamplingParametersWithoutStartingCli()
    {
        var runner = new StubCopilotCliRunner();
        var adapter = new CopilotCliAdapter(runner);
        var invocation = CreateInvocation() with
        {
            Parameters = new EffectiveRequestParameters
            {
                MaxOutputTokens = 40,
                Temperature = 0.25,
            },
        };

        var completion = await adapter.CompleteAsync(invocation, CancellationToken.None);

        Assert.Equal("unsupported-parameter", completion.Error?.Category);
        Assert.Equal("copilot-cli-unsupported-parameter", completion.Error?.Code);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task RejectsModelSubstitution()
    {
        var runner = new StubCopilotCliRunner(new CopilotCliRunResult
        {
            ExitCode = 0,
            StandardOutput =
                """
                {"type":"assistant.message","data":{"model":"gpt-6.2-astra","content":"hello","toolRequests":[]}}
                """,
        });
        var adapter = new CopilotCliAdapter(runner);

        var completion = await adapter.CompleteAsync(CreateInvocation(), CancellationToken.None);

        Assert.Equal("invalid-response", completion.Error?.Category);
        Assert.Equal("model-mismatch", completion.Error?.Code);
    }

    [Fact]
    public async Task ClassifiesUnavailableModelFromCliFailure()
    {
        var runner = new StubCopilotCliRunner(new CopilotCliRunResult
        {
            ExitCode = 1,
            StandardError = "The model 'gpt-6.2-sol' is not available.",
        });
        var adapter = new CopilotCliAdapter(runner);

        var completion = await adapter.CompleteAsync(CreateInvocation(), CancellationToken.None);

        Assert.Equal("model-or-deployment", completion.Error?.Category);
        Assert.Equal("copilot-cli-exit-1", completion.Error?.Code);
    }

    [Fact]
    public void ReportsSupportedConfigurationAndCliProbeFailures()
    {
        var available = new CopilotCliAdapter(new StubCopilotCliRunner());
        var missing = new CopilotCliAdapter(new StubCopilotCliRunner(
            new CopilotCliProbe
            {
                Available = false,
                Version = "not-installed",
                Error = "CLI missing.",
            }));

        Assert.True(available.CheckCompatibility(Target()).Supported);
        Assert.False(missing.CheckCompatibility(Target()).Supported);
    }

    private static AdapterInvocation CreateInvocation() => new()
    {
        Target = Target(),
        Scenario = ScenarioFactory.Simple(),
        Messages =
        [
            new ConversationMessage { Role = "system", Content = "Be exact." },
            new ConversationMessage { Role = "user", Content = "Reply with hello." },
        ],
        Parameters = new EffectiveRequestParameters
        {
            MaxOutputTokens = 80,
            ReasoningEffort = "low",
        },
        MaxRetries = 0,
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static TargetConfiguration Target() => new()
    {
        Id = "copilot-gpt-6-2-sol",
        Adapter = AdapterIds.CopilotCli,
        ApiKind = ApiKinds.GitHubCopilot,
        ModelId = "gpt-6.2-sol",
        Authentication = AuthenticationKinds.CliSession,
    };

    private sealed class StubCopilotCliRunner : ICopilotCliRunner
    {
        private readonly CopilotCliRunResult _result;
        private readonly CopilotCliProbe _probe;

        public StubCopilotCliRunner(CopilotCliRunResult? result = null)
            : this(
                new CopilotCliProbe
                {
                    Available = true,
                    Version = "1.0.0-test",
                },
                result ?? new CopilotCliRunResult())
        {
        }

        public StubCopilotCliRunner(CopilotCliProbe probe)
            : this(probe, new CopilotCliRunResult())
        {
        }

        private StubCopilotCliRunner(CopilotCliProbe probe, CopilotCliRunResult result)
        {
            _probe = probe;
            _result = result;
        }

        public int Calls { get; private set; }

        public CopilotCliRunRequest? Request { get; private set; }

        public CopilotCliProbe Probe() => _probe;

        public Task<CopilotCliRunResult> RunAsync(
            CopilotCliRunRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            return Task.FromResult(_result);
        }
    }
}
