# Reproducing an evaluation configuration

Every attempt records everything needed to rerun it. Reproduction means pinning the same five
inputs: benchmark version and prompt hash, model identifier and settings, runner name and version,
execution environment, and budgets.

## What is recorded

`results.json` stores, per attempt:

| Field | Meaning |
| --- | --- |
| `scenarioId`, `benchmarkVersion` | Which versioned scenario package was used |
| `promptHash` | SHA-256 of the fully resolved prompt (shared + scenario instructions + contracts) |
| `modelId`, `adapter` | Model configuration that produced the code |
| `runner.name`, `runner.version` | Agent runner used for generation |
| `environment` | OS, architecture, .NET SDK version, evaluator version, execution image, git commit, `maxParallel` |
| `repetition`, `startedAt`, `completedAt` | Attempt identity and timing |

The per-attempt artifact directory (`artifacts/evaluations/run-*/attempts/<attemptId>/`) additionally
keeps the exact `prompt.md`, the generated code, the agent transcript, the output of every command,
the test result files and the attempt's own `result.json`.

## Rerunning a published configuration

1. Check out the commit recorded in `environment.gitCommit`.
2. Install the SDK pinned by `global.json` (`environment.dotnetSdkVersion` shows what was used).
3. Confirm the benchmark package is unchanged:

   ```bash
   dotnet run --project src/ModelEvaluator.Cli -- list-scenarios
   ```

   The printed prompt hash must match `promptHash` in the report. A different hash means a different
   benchmark; results are not comparable.
4. Rerun the same matrix, using the same model ids, repetitions, concurrency limit and budgets:

   ```bash
   dotnet run --project src/ModelEvaluator.Cli -- evaluate \
     --config config/evaluation.json \
     --scenarios console-task-cli \
     --models reference-good,reference-broken \
     --repetitions 3 \
     --max-parallel 1 \
     --execution-image ubuntu-latest
   ```

Use `--execution-image` to record the image or runner label the evaluation ran on; it is stored in
`environment.executionImage` and is otherwise unknowable from inside the process.

## Keeping comparisons fair

* Give every compared model the same scenario, the same repetitions and the same budgets. Budgets
  come from `scenario.json` and may be overridden globally (`budgets` in the configuration or the
  `--*-timeout` options), never per model.
* Drive compared models through the same runner. If runners differ, report the full
  model-and-runner configuration rather than the model name alone.
* Use at least three independent attempts per model and scenario for a baseline. The CLI warns when
  fewer are configured.
* Each attempt gets a fresh workspace and a fresh model context; nothing carries over between
  repetitions.
* Match `environment.maxParallel` when reproducing a run. Prefer `--max-parallel 1` for controlled
  timing comparisons. A higher value runs independent attempts concurrently and can introduce CPU,
  disk, memory and provider contention, even though workspaces and artifacts are separate.

## Determinism and variance

Models are not deterministic, so the reports treat reliability as a measurement: successful attempts
out of total attempts, with sample counts and the spread of elapsed time across repetitions. Compare
success rates with their sample counts, not single runs.

Infrastructure variance (NuGet outages, runner crashes) is classified as `InfrastructureFailure` and
excluded from model failure counts, but is still reported - rerun those attempts before publishing.

## Debugging a specific attempt

```bash
dotnet run --project src/ModelEvaluator.Cli -- evaluate \
  --scenarios console-task-cli --models reference-broken --repetitions 1 \
  --keep-workspaces --workspace-root .workspaces
```

`--keep-workspaces` preserves the disposable workspace so the generated solution can be inspected
and rebuilt by hand. Workspaces are otherwise deleted after each attempt.
