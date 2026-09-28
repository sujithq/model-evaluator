# model-evaluator

An automated framework that evaluates how well different AI models create working .NET projects
from the same fixed, versioned instructions.

Every model receives identical resolved instructions, identical starting files, identical tools and
identical time budgets for a scenario. The harness then builds the generated solution, runs the
model's own tests and runs **evaluator-owned acceptance tests that the model never sees**, and
reports functional correctness, instruction adherence, code quality, efficiency and reliability.
Every result is traceable to the exact scenario, prompt hash, benchmark version, model configuration,
runner version and execution environment.

## Benchmark scenarios (benchmark v1)

| Scenario id | Project type | Fixed task | Main acceptance checks |
| --- | --- | --- | --- |
| `console-task-cli` | Console application | Task-management CLI with `add`, `list`, `complete` and `delete`, JSON persistence. | Output, exit codes, validation, persistence across executions. |
| `class-library-pricing` | Class library | Pricing library with quantity discounts, tax calculation and defined rounding rules. | Public API, calculations, boundary cases, invalid inputs. |
| `webapi-task-minimal` | ASP.NET Core Web API | Task-management API using Minimal APIs and SQLite with CRUD, validation and filtering. | HTTP contracts, status codes, persistence, filtering, errors. |
| `worker-job-processor` | Worker Service | Background worker that processes jobs from a local fixture, retries transient failures and shuts down gracefully. | Processing results, retry limits, failure handling, cancellation. |
| `blazor-task-app` | Blazor Web App | Task-management interface with add, edit, complete, delete and filter. | Browser-based user journeys, validation, state updates. |

Each scenario is a versioned package under [`benchmarks/v1/scenarios`](benchmarks/v1/scenarios)
containing its prompt, contracts, configuration, fixtures, starter files and evaluator-owned
acceptance tests. Changing instructions or grading rules requires a new benchmark version - see
[docs/adding-scenarios.md](docs/adding-scenarios.md).

## Quick start

```bash
dotnet build --configuration Release
dotnet run --project src/ModelEvaluator.Cli -- list-scenarios
dotnet run --project src/ModelEvaluator.Cli -- validate

# One command runs the selected evaluation matrix and writes both report formats.
dotnet run --project src/ModelEvaluator.Cli -- evaluate \
  --scenarios console-task-cli \
  --models reference-good,reference-broken \
  --repetitions 3
```

The run writes `artifacts/evaluations/run-<timestamp>/results.json` (machine readable) and
`report.md` (Markdown comparison), plus a per-attempt artifact directory containing the prompt,
generated code, command output, test results and the attempt's `result.json`.

The default configuration ([`config/evaluation.json`](config/evaluation.json)) ships two
self-verification models: `reference-good` (the known-good sample, which must succeed) and
`reference-broken` (deliberately broken variants, which must fail the relevant checks). Configure
real models with the `command-line` adapter - see
[`config/evaluation.models.example.json`](config/evaluation.models.example.json) and
[docs/model-adapters.md](docs/model-adapters.md).

## CLI

```text
model-evaluator evaluate [options]        Run the evaluation matrix and write both reports.
model-evaluator list-scenarios [options]  List benchmark scenarios and prompt hashes.
model-evaluator validate [options]        Validate scenario packages and model configuration.
model-evaluator version                   Print the harness version.
```

Options: `--config`, `--benchmark-root`, `--models`, `--scenarios`, `--repetitions`, `--output`,
`--workspace-root`, `--execution-image`, `--generation-timeout`, `--build-timeout`, `--test-timeout`,
`--acceptance-timeout`, `--keep-workspaces`. Exit codes: `0` success, `1` usage or validation
problems, `2` error, `3` at least one infrastructure failure.

## How an attempt runs

1. A fresh disposable workspace is created from the scenario starter (`global.json`, `.editorconfig`,
   fixtures, `BENCHMARK.md`), and a fresh model context is created.
2. The adapter generates the solution from the resolved prompt within the generation budget.
   Provider credentials are available to the agent runner only.
3. Instruction adherence is checked (`instructions.*`): solution layout, `src/` and `tests/`,
   README, target framework, nullable reference types, allowed dependencies, pinned SDK, preserved
   files and required paths.
4. `dotnet restore`, `dotnet build -c Release`, the model's own `dotnet test`, and
   `dotnet format --verify-no-changes` run with credentials stripped from the environment.
5. Evaluator-owned acceptance tests run from **outside** the workspace against the built output.
6. Prompt, generated code, transcripts, command output, test results, configuration and version
   identifiers are persisted for the attempt.

An attempt succeeds only when every mandatory build, behaviour and instruction check passes. Skipped
mandatory checks never count as passes. Infrastructure failures are classified separately from model
failures, and a model exceeding its budget counts as an unsuccessful attempt
(`BudgetExceeded`). See [docs/interpreting-results.md](docs/interpreting-results.md).

## Continuous integration

* [`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds the harness, runs its unit tests,
  validates the benchmark packages, and runs the reference evaluation for every scenario, asserting
  that known-good samples pass and deliberately broken variants fail.
* [`.github/workflows/evaluate.yml`](.github/workflows/evaluate.yml) runs a selected evaluation
  matrix on demand and retains the reports and attempt artifacts.

## Documentation

* [Adding scenarios](docs/adding-scenarios.md)
* [Configuring model adapters](docs/model-adapters.md)
* [Reproducing an evaluation configuration](docs/reproducing-runs.md)
* [Interpreting results](docs/interpreting-results.md)

## Repository layout

```text
benchmarks/v1/          Versioned benchmark package: shared instructions, scenarios, acceptance tests
config/                 Evaluation configurations
docs/                   Documentation
src/ModelEvaluator.Core Harness: scenarios, adapters, evaluation pipeline, reporting
src/ModelEvaluator.Cli  Command line interface
tests/                  Unit tests for the harness
```

## References

* [.NET SDK selection with global.json](https://learn.microsoft.com/dotnet/core/tools/global-json)
* [`dotnet new` project templates](https://learn.microsoft.com/dotnet/core/tools/dotnet-new)
* [`dotnet test`](https://learn.microsoft.com/dotnet/core/tools/dotnet-test)
* [`dotnet format`](https://learn.microsoft.com/dotnet/core/tools/dotnet-format)
* [xUnit.net documentation](https://xunit.net/)
