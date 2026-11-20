using ModelEvaluator.Core.Configuration;
using ModelEvaluator.Durable;

namespace ModelEvaluator.Core.Tests;

public sealed class DurableEvaluationPlannerTests
{
    [Fact]
    public void Create_ExpandsHydraFusionMatrixInStableOrder()
    {
        var request = new DurableEvaluationRequest
        {
            EvaluationArguments =
            [
                "--preset", "hydrafusion",
                "--scenarios", "console-task-cli",
                "--repetitions", "3",
                "--max-parallel", "2",
            ],
            WorkingDirectory = RepositoryLocator.Root,
        };
        var options = CommandLineOptions.Parse(request.EvaluationArguments);
        var configuration = EvaluationConfigurationResolver.Resolve(
            options,
            request.WorkingDirectory,
            AppContext.BaseDirectory);
        var startedAt = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);

        var plan = DurableEvaluationPlanner.Create(
            request,
            configuration,
            startedAt,
            "run-test");

        Assert.Equal("run-test", plan.RunId);
        Assert.Equal(startedAt, plan.StartedAt);
        Assert.Equal("copilot-gpt-6-astra", plan.BaselineModelId);
        Assert.Equal(2, plan.MaxParallel);
        Assert.Equal(15, plan.Jobs.Count);
        Assert.Equal(
            [
                "copilot-gpt-6-astra:1",
                "copilot-gpt-6-astra:2",
                "copilot-gpt-6-astra:3",
                "copilot-claude-opus-5.5:1",
                "copilot-claude-opus-5.5:2",
                "copilot-claude-opus-5.5:3",
                "copilot-auto:1",
                "copilot-auto:2",
                "copilot-auto:3",
                "copilot-hydrafusion:1",
                "copilot-hydrafusion:2",
                "copilot-hydrafusion:3",
                "copilot-hydrafusion-max:1",
                "copilot-hydrafusion-max:2",
                "copilot-hydrafusion-max:3",
            ],
            plan.Jobs.Select(job => $"{job.ModelId}:{job.Repetition}"));
        Assert.Equal(plan.Jobs.Count, plan.Jobs.Select(job => job.AttemptId).Distinct().Count());
        Assert.All(plan.Jobs, job => Assert.EndsWith("__run-test", job.AttemptId));
    }

    [Fact]
    public void Create_UsesOneRepetitionWhenConfigurationIsZero()
    {
        var request = new DurableEvaluationRequest
        {
            EvaluationArguments = ["--preset", "smoke-reference"],
            WorkingDirectory = RepositoryLocator.Root,
        };
        var configuration = EvaluationConfigurationResolver.Resolve(
            CommandLineOptions.Parse(request.EvaluationArguments),
            request.WorkingDirectory,
            AppContext.BaseDirectory) with
        {
            Repetitions = 0,
        };

        var plan = DurableEvaluationPlanner.Create(
            request,
            configuration,
            runId: "run-test");

        Assert.Equal(configuration.Models.Count, plan.Jobs.Count);
        Assert.All(plan.Jobs, job => Assert.Equal(1, job.Repetition));
    }
}
