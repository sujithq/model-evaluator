# Benchmark suites

`model-evaluator` has two scenario formats for different questions.

| Suite | Repository source | What it measures |
| --- | --- | --- |
| Project-generation benchmark | [`benchmarks/`](https://github.com/sujithq/model-evaluator/tree/main/benchmarks) | Whether a model can produce a complete .NET project that restores, builds, tests, and passes hidden acceptance checks. |
| API/SDK comparison | [`scenarios/`](https://github.com/sujithq/model-evaluator/tree/main/scenarios) | How APIs, SDKs, abstractions, request parameters, and model targets differ for synthetic controlled prompts. |

## Project-generation benchmark

[`benchmarks/v1`](https://github.com/sujithq/model-evaluator/tree/main/benchmarks/v1) is the full
project suite:

| Scenario | Project type | Main contract |
| --- | --- | --- |
| `console-task-cli` | Console | Task creation, listing, completion, deletion, validation, and JSON persistence. |
| `class-library-pricing` | Class library | Pricing rules, discounts, tax calculations, rounding, and public API boundaries. |
| `webapi-task-minimal` | Minimal API | CRUD contracts, filtering, validation, persistence, and HTTP error behavior. |
| `worker-job-processor` | Worker Service | Fixture processing, retries, cancellation, and graceful shutdown. |
| `blazor-task-app` | Blazor Web App | Browser workflows for creating, completing, and filtering tasks. |

[`benchmarks/smoke-v1`](https://github.com/sujithq/model-evaluator/tree/main/benchmarks/smoke-v1)
contains the smaller `smoke-add` task. It is intended for connectivity and pipeline checks rather
than broad model-quality claims.

### Package anatomy

```text
benchmarks/<version>/
  shared/
    instructions.md
  scenarios/<scenario-id>/
    scenario.json
    instructions.md
    contracts.md
    starter/
    acceptance/
    samples/
      good/
      broken-overlay/
```

`scenario.json` declares the framework, SDK, files, packages, budgets, preserved inputs, and sample
variants. The shared instructions, scenario instructions, and contracts form the model prompt.
Acceptance tests are evaluator-owned and are never placed in the model's workspace.

The documentation site describes the model and authoring workflow. The executable contracts,
acceptance tests, and samples stay in the benchmark directories so they cannot silently drift from
the code that evaluates a run.

!!! tip "Authoring a scenario"
    Start with the
    [`console-task-cli` package](https://github.com/sujithq/model-evaluator/tree/main/benchmarks/v1/scenarios/console-task-cli),
    then follow [Adding a scenario](../adding-scenarios.md).

## API/SDK comparison scenarios

[`scenarios/v1`](https://github.com/sujithq/model-evaluator/tree/main/scenarios/v1) defines
deterministic, synthetic checks for:

- answer quality and grounding;
- strict structured output;
- deterministic tool calling;
- multi-turn state retention; and
- unsupported-parameter classification.

These JSON definitions are consumed by the `compare` command group. They are intentionally separate
from the project-generation suite because they evaluate request and response behavior rather than
generated code.

## Versioning rules

Change the benchmark version when an alteration affects comparability, including:

- instructions or public contracts;
- acceptance rules;
- supplied starter files;
- allowed dependencies;
- required project shape; or
- scenario budgets.

Do not overwrite a published benchmark contract and report the new run as if it used the old
version. See [Adding a scenario](../adding-scenarios.md) for the package workflow and
[Reproducing runs](../reproducing-runs.md) for traceability requirements.
