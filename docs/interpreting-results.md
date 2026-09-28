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
| Efficiency | - | Generation/evaluation wall time, reported AI credits, tokens/cache metrics, API requests, and tool calls or USD where available. |
| Reliability | - | Successful attempts out of total attempts, with sample counts and variation across repetitions. |

Functional correctness comes exclusively from evaluator-owned acceptance tests that the model never
saw. The model's own tests are graded as code quality, because a model can trivially make its own
tests pass.

## Per-task rankings

Both reports include a ranking for each comparable scenario/benchmark-version/prompt-hash/runner
and concurrency-limit group. The JSON `rankings` array contains the same ordering and measurements as Markdown.
Run several models in one evaluation matrix to compare them; individual run reports are not
automatically merged.

`environment.maxParallel` records the configured concurrency limit, not guaranteed simultaneous
activity or a speed-up factor. Parallel attempts compete for machine resources and provider
capacity; timing cannot be compared directly with sequential runs. Summaries and rankings keep
different concurrency limits separate. A cancelled run is explicitly marked as partial; queued
attempts that never started are counted in `notStartedAttempts`, not treated as model failures.

If the final Copilot usage export is missing, the evaluator can recover completed-call telemetry
as partial usage. `efficiency.usageIsPartial` marks these observations in JSON; Markdown lists them
separately. Partial values never qualify as complete consumption totals or AI-credit ranking coverage.
Partial AI credits require a persisted usage checkpoint from this attempt's fresh Copilot session;
token counts and legacy request multipliers cannot substitute for it. In-flight calls or unflushed
records may be missing. Unknown consumption must not be interpreted as a free run.

Ranking is lexicographic, not an arbitrary weighted score:

1. Higher success rate: attempts where **all mandatory checks** pass divided by evaluable attempts.
2. Higher acceptance accuracy: the mean of each attempt's `passed / (passed + failed + skipped)`.
   An attempt that never executes acceptance tests scores zero, not an omitted observation.
3. Lower mean AI-credit consumption per evaluable attempt, including unsuccessful attempts.
4. Lower mean total wall time per evaluable attempt.

Infrastructure failures are excluded from these four metrics, but their count remains visible.
Rows with only infrastructure failures are unranked; local reference samples are also unranked
because copying sample code is not model generation. All-attempt summary totals still include
reported usage from infrastructure failures, so real consumption is not hidden.

If any ranked model lacks credit data for any evaluable attempt, the **credit tie-breaker is disabled
for the entire comparison group**. Time then breaks accuracy ties, and the report explicitly says
so. A missing value never means free. Legacy premium requests and reported USD remain separate
measurements and are not silently substituted for AI credits. Complete zeros are valid measurements.

Exact ties share the same rank (1, 1, 3); alphabetical ordering only stabilizes display. Fewer than
three evaluable attempts makes a rank provisional. Always inspect attempt counts and infrastructure
failures alongside rank: a tiny or infrastructure-biased sample is not evidence of superiority.
Rank 1 is only relative ordering, not a guarantee that any model passed; check the success column.

Accuracy comes from evaluator-owned acceptance tests, not the model's own tests. Build, instruction
adherence and code-quality checks affect the mandatory-check success rate. Mean generation time is
shown separately from total time, which includes evaluator restore/build/tests. API duration is
also recorded but does not replace either wall-clock measurement.

Copilot usage is parsed from the native export, with top-level AI-credit consumption and tokens
aggregated across `modelMetrics` only. Agent breakdowns are not added again. Input/output token
totals do not add cache or reasoning subtotals a second time. Tool-call counts and invoice USD are
not in the verified export, so remain unavailable. See [native usage mapping](model-adapters.md#reporting-usage).

Partial aggregate metrics are shown as unavailable rather than as complete totals. Credit coverage
counts reveal how many attempts reported the metric, and per-attempt values remain available in JSON.
Usage collection warnings are printed during execution and included in both reports.

## Reading the Markdown report

* **Overall success rate** - successful attempts over total attempts across the matrix.
  This operational summary includes infrastructure failures in its denominator, unlike ranking success rate.
* **Per-task rankings** - accuracy-first ordering, cost coverage, mean wall/generation time,
  infrastructure failures and provisional/excluded rows, grouped by comparable benchmark and runner.
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
