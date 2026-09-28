# Interpreting results

Each evaluation run produces two reports in `artifacts/evaluations/run-<timestamp>/`:

* `results.json` - machine-readable: every attempt, every check, every metric.
* `report.md` - Markdown comparison of models per scenario, with benchmark versions and prompt
  hashes.

## Outcomes

| Outcome | Meaning |
| --- | --- |
| `Success` | Every mandatory build, behaviour and instruction check passed. |
| `ModelFailure` | The model produced code that failed at least one mandatory check. |
| `BudgetExceeded` | The model exceeded its assigned time budget. Counts as an unsuccessful attempt. |
| `InfrastructureFailure` | The harness or environment failed (misconfigured adapter, acceptance suite could not execute). Not a model failure. |

A skipped mandatory check never counts as a pass. When a build fails, the downstream checks are
recorded as skipped *because of* the build failure, and the attempt is still a `ModelFailure`.

## Dimensions

| Dimension | Check ids | Reported measurements |
| --- | --- | --- |
| Build and execution | `generation.completed`, `build.restore`, `build.compile` | Restore and build results plus the runtime smoke checks performed by the acceptance suite. |
| Functional correctness | `acceptance.all-passed` | Independent acceptance checks passed, failed and skipped. |
| Instruction adherence | `instructions.*` | Framework, dependencies, structure, nullable, SDK pin, preserved files, required paths. |
| Code quality | `quality.format`, `tests.generated` | Fixed analyzer and formatting rules (`dotnet format --verify-no-changes`) plus the model's own test results. |
| Efficiency | - | Elapsed time, tool calls, token usage and estimated cost where available. |
| Reliability | - | Successful attempts out of total attempts, with sample counts and variation across repetitions. |

Functional correctness comes exclusively from evaluator-owned acceptance tests that the model never
saw. The model's own tests are graded as code quality, because a model can trivially make its own
tests pass.

## Reading the Markdown report

* **Overall success rate** - successful attempts over total attempts across the matrix.
* **Per-scenario comparison** - one row per model and scenario with the benchmark version, prompt
  hash, success rate, per-dimension pass counts and efficiency aggregates.
* **Attempts** - one row per attempt with the outcome, failure reason, acceptance counts and elapsed
  time. Timeouts, failed builds and infrastructure errors appear here explicitly.
* **Unavailable measurements** - metrics the runner did not report (typically tokens or cost). They
  are shown as `n/a` and listed by name; they are never estimated or silently treated as zero.

## Reading `results.json`

```jsonc
{
  "runId": "run-20260101-120000",
  "runner": { "name": "...", "version": "..." },
  "environment": { "operatingSystem": "...", "dotnetSdkVersion": "...", "gitCommit": "..." },
  "summaries": [ { "scenarioId": "...", "modelId": "...", "totalAttempts": 3, "successfulAttempts": 3, "successRate": 1.0 } ],
  "attempts": [
    {
      "attemptId": "console-task-cli__model-a__rep01__20260101120000123",
      "scenarioId": "console-task-cli",
      "benchmarkVersion": "1.0.0",
      "promptHash": "…",
      "outcome": "Success",
      "checks": [ { "id": "build.compile", "category": "BuildAndExecution", "status": "Passed", "mandatory": true, "details": "…" } ],
      "acceptance": { "passed": 19, "failed": 0, "skipped": 0 },
      "generatedTests": { "passed": 13, "failed": 0, "skipped": 0 },
      "efficiency": { "elapsedSecondsTotal": 91.2, "inputTokens": null, "unavailableMetrics": ["inputTokens"] }
    }
  ]
}
```

Useful queries:

```bash
results=artifacts/evaluations/run-*/results.json

# Success rate per model
jq -r '.attempts | group_by(.modelId)[] | "\(.[0].modelId): \([.[] | select(.outcome=="Success")] | length)/\(length)"' $results

# Every failed mandatory check
jq -r '.attempts[] | .attemptId as $a | .checks[] | select(.mandatory and .status != "Passed") | "\($a) \(.id) \(.status) \(.details)"' $results

# Attempts that ran out of budget or hit infrastructure problems
jq -r '.attempts[] | select(.outcome == "BudgetExceeded" or .outcome == "InfrastructureFailure") | "\(.attemptId) \(.outcome) \(.failureReason)"' $results
```

## What not to conclude

* Do not compare results across benchmark versions or prompt hashes; instructions or grading changed.
* Do not compare models evaluated through different runners without naming the runner - the runner
  contributes as much as the model.
* Do not read a single attempt as a model's capability. Use at least three independent attempts and
  report the sample count alongside the rate.
* Keep any qualitative human assessment separate from these automated correctness results.
