# Using model-evaluator

This guide starts with a local run that makes no AI requests, then builds up to real model
comparisons, custom runners, reproducible experiments, and failure investigation.

## 1. What the project does

`model-evaluator` compares how AI models create .NET projects from the same benchmark package.
A run expands into a matrix:

```text
selected scenarios x selected models x repetitions = attempts
```

Every attempt receives a fresh workspace and the same resolved prompt for its scenario. The
evaluator then:

1. invokes the configured model adapter;
2. checks required files, dependencies, framework settings, and preserved starter files;
3. restores and builds the generated solution;
4. runs the generated solution's tests;
5. verifies formatting;
6. runs evaluator-owned acceptance tests that were not shown to the model; and
7. saves reports and per-attempt evidence.

The supplied `local-sample` adapter copies known code instead of calling a model. It is the safest
way to learn the tool and verify the harness. Real models use the `command-line` adapter.

## 2. Prerequisites

Install:

- the .NET SDK selected by [`global.json`](../global.json) (currently .NET 10);
- Git, if you want the report to record the current commit;
- the external agent CLI and its authentication only when evaluating a real model.

The evaluator can run from source or as the `modelevaluator` .NET tool. Source commands run from
the repository root. An installed tool can run from any writable directory. The examples use
PowerShell paths; Bash users can replace backslashes with forward slashes and use their shell's
line-continuation syntax.

Confirm the SDK:

```powershell
dotnet --version
```

The repository permits feature-band roll-forward, so the selected SDK can be newer than the exact
version in `global.json` when it satisfies that policy.

## 3. Install the .NET tool

The tool targets .NET 10. To build and install the current source globally:

```powershell
dotnet pack .\src\ModelEvaluator.Cli --configuration Release
dotnet tool install --global --add-source .\artifacts\packages `
  --version 0.2.0 Sujithq.ModelEvaluator.Tool
```

The installed command is `modelevaluator`:

```powershell
modelevaluator --help
modelevaluator version
modelevaluator list-presets
modelevaluator list-scenarios --preset default
modelevaluator validate --preset default
```

For a repository-local tool manifest instead:

```powershell
dotnet new tool-manifest
dotnet tool install --add-source .\artifacts\packages `
  --version 0.2.0 Sujithq.ModelEvaluator.Tool
dotnet tool run modelevaluator validate
```

Once the package is published to NuGet, omit `--add-source`. Manage a global installation with:

```powershell
dotnet tool update --global Sujithq.ModelEvaluator.Tool
dotnet tool uninstall --global Sujithq.ModelEvaluator.Tool
```

The package includes all shipped JSON configurations and both benchmark versions. Use
`modelevaluator list-presets` to discover the packaged names: `default`, `auto`, `copilot-matrix`,
`models`, `smoke`, and `smoke-reference`. For example:

```powershell
modelevaluator evaluate --preset auto `
  --scenarios console-task-cli `
  --models copilot-auto `
  --repetitions 1
```

`--preset` always uses immutable files from the installed package. `--config` always identifies a
caller-owned file; relative config paths start at the current directory and never fall back to the
package. The options are mutually exclusive. With neither option, a repository-local
`config/evaluation.json` takes precedence, otherwise the packaged `default` configuration is used.

Paths declared inside a configuration are relative to that configuration file. CLI path overrides
(`--benchmark-root`, `--output`, and `--workspace-root`) are relative to the current directory.
Bundled benchmark files remain read-only. Bundled output/workspace paths are relocated under the
caller directory; when no workspace root is configured, workspaces use the OS temporary directory.
External agent CLIs, authentication, and model entitlement are not installed with this tool.

All remaining examples use `dotnet run --project .\src\ModelEvaluator.Cli --` with repository
configuration files. With the installed tool, replace that prefix with `modelevaluator` and replace
the corresponding `--config` argument with `--preset auto`, `--preset copilot-matrix`,
`--preset smoke`, or another name shown by `list-presets`.

## 4. First entry: inspect without evaluating

Build the evaluator:

```powershell
dotnet build .\ModelEvaluator.sln --configuration Release
```

Print the CLI help and version:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- --help
dotnet run --project .\src\ModelEvaluator.Cli -- version
```

List the benchmark scenarios:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- list-scenarios
```

This prints each scenario's project type, benchmark version, framework, SDK, sample variants, and
resolved prompt hash. The prompt hash identifies the exact shared instructions, scenario
instructions, and contracts supplied to a model.

Validate the default setup:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- validate
```

`validate` checks that scenario resources and configured adapter names exist. It does **not** run
models, authenticate an external CLI, verify model entitlement, build generated projects, or run
acceptance tests.

## 5. First evaluation: no AI calls

Run one scenario once with both reference variants:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --scenarios console-task-cli `
  --models reference-good,reference-broken `
  --repetitions 1
```

The one-repetition warning is expected: one attempt is useful for learning and debugging, while a
publishable baseline should use at least three attempts. This command creates two attempts:

```text
1 scenario x 2 models x 1 repetition = 2 attempts
```

Expected result:

- `reference-good` is `Success`;
- `reference-broken` is `ModelFailure`.

The command can still exit with code `0` when a generated solution has a model failure. Exit code
`0` means the evaluation itself completed without a usage, validation, infrastructure, or
cancellation error; inspect `report.md` or `results.json` for model outcomes.

### Faster harness smoke check

The smoke benchmark edits one supplied method instead of creating a full project:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.smoke.reference.json
```

This also uses local samples and makes no AI requests.

## 6. Find the output

At completion, the CLI prints the absolute paths to:

```text
<outputDirectory>/
  run-<timestamp>-<id>/
    results.json
    report.md
  attempts/
    <scenario>__<model>__repNN__<id>/
      prompt.md
      transcript.log
      result.json
      generated/
      commands/
        *.log
      generated-tests/
        *.trx
      acceptance-results/
        *.trx
      usage.json                 # when the runner supplies it
```

The run report and attempt artifacts share the configured output root, but attempts are stored in
the root's `attempts` directory rather than inside the run directory. Use:

- `report.md` for the human-readable comparison and failure summary;
- `results.json` for automation and complete structured data;
- `result.json` and command logs to diagnose one attempt;
- `prompt.md` to verify exactly what that attempt received;
- `generated/` to inspect the persisted generated solution.

See [Interpreting results](interpreting-results.md) for outcome and ranking rules.

## 7. Choose scenarios and models

### See which model IDs exist

`--models` only accepts IDs that the configuration declares, so start by listing them:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- list-models
dotnet run --project .\src\ModelEvaluator.Cli -- list-models --config .\config\evaluation.copilot-matrix.example.json
```

```text
ID                   ENABLED  ADAPTER       PROVIDER MODEL  RUNNER
copilot-gpt-6-astra  no       command-line  gpt-6-astra     github-copilot-cli
copilot-gpt-6-luna   yes      command-line  gpt-6-luna      github-copilot-cli
```

- `ID` is what you pass to `--models`; disabled IDs are listed too, because `--models` can select them.
- `ENABLED` shows which models run when you omit `--models`.
- `PROVIDER MODEL` is parsed from the runner's `--model` argument. It documents intent only - it does
  not prove that your account may use that model.

`--models` also filters the listing, which is a cheap way to check an ID before a paid run:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- list-models --models copilot-gpt-6-luna
```

### Check configured models against GitHub's catalog

The evaluator embeds the generated official GitHub model catalog. The opt-in probe compares each
configured Copilot provider model with that catalog without starting Copilot CLI or making a model
request:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- list-models `
  --config .\config\evaluation.copilot-matrix.example.json `
  --models copilot-gpt-6-luna,copilot-gpt-6-astra `
  --probe
```

```text
Checking 2 model(s) against GitHub's published catalog.
No runner or provider request will be made.

copilot-gpt-6-astra: supported (GPT-6 Astra); Copilot CLI: Yes - OpenAI; GA.
copilot-gpt-6-luna: supported (GPT-6 Luna); Copilot CLI: Yes - OpenAI; GA.

2 supported, 0 scheduled for retirement, 0 retired, 0 not listed, 0 not applicable.
```

Keep these constraints in mind:

- **Probing consumes no model usage.** It reads an embedded catalog generated from GitHub Docs.
- `supported` is provider-wide publication status, not proof of authenticated-user entitlement.
- `not listed` is not proof that a model is unavailable; GitHub's CLI surface table can lag rollout.
- `list-models --probe` exits `1` when a selected model is retired or scheduled for retirement, so
  it can detect stale benchmark selections in CI.
- The hourly workflow updates the embedded JSON and Markdown catalog through a pull request only
  when the generated output changes. A checkout uses the catalog from its latest merged update.
- Without `--probe` the command does no I/O beyond reading the configuration file.

### Filter an evaluation run

Both filters accept comma-separated IDs:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --scenarios console-task-cli,class-library-pricing `
  --models reference-good `
  --repetitions 1
```

IDs are matched without regard to case. Whitespace around comma-separated values is ignored.
Unknown IDs fail explicitly and print the known choices.

Selection rules:

- no `--scenarios`: use the configuration's `scenarios`;
- an empty configured `scenarios` list: use every scenario under the benchmark root;
- no `--models`: use only models with `"enabled": true`;
- explicit `--models`: select those configured IDs even when `"enabled": false`.

Before starting a paid run, calculate the attempt count. For example, five scenarios, four models,
and three repetitions create 60 independent model invocations.

## 8. Evaluate a real model with GitHub Copilot CLI

Install GitHub Copilot CLI, authenticate it, and confirm that the desired model is available to
your account and organization. Then validate a supplied configuration:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- validate `
  --config .\config\evaluation.copilot-matrix.example.json `
  --models copilot-gpt-6-luna
```

Start with one cheap, observable attempt:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.copilot-matrix.example.json `
  --scenarios console-task-cli `
  --models copilot-gpt-6-luna `
  --repetitions 1 `
  --max-parallel 1 `
  --debug
```

This makes real model requests and consumes Copilot usage. `--debug` shows stage progress and live
child-process output; it does not change the model prompt or reasoning settings.

Once the setup works, collect a baseline:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.copilot-matrix.example.json `
  --scenarios console-task-cli `
  --models copilot-gpt-6-luna `
  --repetitions 3 `
  --max-parallel 1
```

The supplied Copilot examples allow all tools without confirmation. Run them only in a disposable,
isolated environment. A fresh workspace protects experiment independence but is not a security
sandbox.

## 9. Compare models fairly

Run compared models in the same matrix so they share benchmark content, budgets, concurrency, and
environment:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.copilot-matrix.example.json `
  --scenarios console-task-cli `
  --models copilot-gpt-6-astra,copilot-gpt-6-luna `
  --repetitions 3 `
  --max-parallel 1 `
  --execution-image windows-dotnet10
```

For a fair comparison:

- use the same runner and tool access for every model;
- use the same scenario version, prompt hash, budgets, and repetition count;
- use at least three repetitions;
- keep `--max-parallel 1` for timing comparisons;
- record the runner version in `settings.runnerVersion`;
- record a meaningful CI image or machine label with `--execution-image`;
- rerun infrastructure failures before publishing.

The report ranks models separately per comparable task and runner/concurrency group. A missing
usage measurement is not treated as zero cost.

## 10. Understand configuration

A configuration has this shape:

```json
{
  "benchmarkRoot": "../benchmarks/v1",
  "outputDirectory": "../artifacts/evaluations",
  "workspaceRoot": null,
  "executionImage": null,
  "repetitions": 3,
  "maxParallel": 1,
  "scenarios": [],
  "models": [
    {
      "id": "reference-good",
      "enabled": true,
      "adapter": "local-sample",
      "description": "Known-good harness reference.",
      "settings": { "variant": "good" },
      "environment": {}
    }
  ],
  "budgets": {},
  "keepWorkspaces": false,
  "debug": false
}
```

Relative `benchmarkRoot`, `outputDirectory`, and `workspaceRoot` paths are resolved relative to the
configuration file, not the current directory. `--preset` selects a packaged configuration by name.
A relative value passed to `--config` identifies a caller-owned file from the current directory and
never falls back to packaged content. CLI path overrides are converted to absolute paths from the
current directory.

CLI options override configuration values. Boolean CLI flags only enable behavior: for example,
`--debug` can turn debugging on, but there is no CLI flag that turns a configured `"debug": true`
off.

### Budgets

Scenario packages define four stage budgets. A configuration can override them globally:

```json
{
  "budgets": {
    "generationTimeoutSeconds": 600,
    "buildTimeoutSeconds": 300,
    "testTimeoutSeconds": 180,
    "acceptanceTimeoutSeconds": 300
  }
}
```

The equivalent CLI options are:

```text
--generation-timeout <seconds>
--build-timeout <seconds>
--test-timeout <seconds>
--acceptance-timeout <seconds>
```

These are per-stage limits, not one total run deadline. Restore, build, and formatting each use the
build timeout. Queueing time does not consume a stage timeout.

## 11. Use another command-line model runner

The `command-line` adapter can invoke any non-interactive CLI:

```json
{
  "id": "agent-cli-model-a",
  "enabled": true,
  "adapter": "command-line",
  "settings": {
    "command": "my-agent",
    "arguments": "--model model-a --prompt-file \"{promptFile}\" --workdir \"{workspace}\" --usage \"{usageFile}\"",
    "runnerName": "my-agent",
    "runnerVersion": "1.4.2",
    "usageFile": "usage.json",
    "usageFormat": "normalized"
  },
  "environment": {
    "MY_AGENT_SETTING": "value"
  }
}
```

Available argument placeholders:

| Placeholder | Value |
| --- | --- |
| `{workspace}` | Fresh attempt workspace |
| `{promptFile}` | File containing the resolved prompt |
| `{prompt}` | Resolved prompt text |
| `{artifacts}` | Attempt artifact directory |
| `{timeoutSeconds}` | Effective generation budget |
| `{scenario}` | Scenario ID |
| `{model}` | Configured model ID |
| `{usageFile}` | Resolved usage file path |

The same values are exposed as `EVAL_*` environment variables. Model-specific `environment`
entries are passed to the runner process, not to generated project commands. Quote placeholders
that can contain spaces. Prefer `{promptFile}` over `{prompt}` when a runner supports files, because
large multiline prompts are safer than shell argument text.

See [Configuring model adapters](model-adapters.md) for the normalized usage schema, Copilot
telemetry handling, credentials, and writing a new in-process adapter.

## 12. Parallel and advanced runs

Use bounded concurrency to reduce elapsed wall time:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.smoke.example.json `
  --models copilot-gpt-5-mini,copilot-claude-haiku-4.5 `
  --repetitions 1 `
  --max-parallel 2
```

Concurrency applies across the whole matrix. Each attempt still runs its internal stages
sequentially and uses unique workspace and artifact paths. Parallel attempts can contend for CPU,
memory, disk, provider capacity, and rate limits, so do not compare their timing directly with a
sequential run.

Useful advanced options:

| Option | Use |
| --- | --- |
| `--benchmark-root <path>` | Evaluate another versioned benchmark package |
| `--output <path>` | Put reports and artifacts under another root |
| `--workspace-root <path>` | Control where temporary attempt workspaces are created |
| `--execution-image <label>` | Record a CI image or machine label for reproducibility |
| `--keep-workspaces` | Preserve disposable workspaces after attempts |
| `--debug` | Show detailed progress and write `scenario-details.md` |

`scenario-details.md` includes selected scenario metadata, effective budgets, constraints, samples,
and full resolved prompts. Treat it and live runner output as potentially sensitive before sharing.

## 13. Debug a failed attempt

Repeat the smallest failing model/scenario pair:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.copilot-matrix.example.json `
  --scenarios console-task-cli `
  --models copilot-gpt-6-luna `
  --repetitions 1 `
  --max-parallel 1 `
  --workspace-root .\.workspaces `
  --keep-workspaces `
  --debug
```

Then inspect, in order:

1. the attempt row and failure reason in `report.md`;
2. checks in the attempt's `result.json`;
3. `transcript.log` for generation errors;
4. stage-specific stdout/stderr and test result files;
5. `generated/` for the persisted artifact copy;
6. the retained workspace when an exact manual rebuild is needed.

Common distinctions:

- `ModelFailure`: generated output violated a mandatory check;
- `BudgetExceeded`: generation exceeded its time budget;
- `InfrastructureFailure`: runner, environment, or evaluator setup failed;
- skipped downstream checks after a failed restore/build are still unsuccessful checks.

## 14. Cancellation, cleanup, and edge cases

### Ctrl+C

Ctrl+C stops queued work and cancels active attempts. If setup progressed far enough, the evaluator
writes a partial report with `cancelled`, `plannedAttempts`, and `notStartedAttempts`, then exits
with `130`. Cancellation very early in setup can occur before a report exists.

### Workspace cleanup

Attempt workspaces normally live below the system temporary directory and are deleted after each
attempt. Generated code is still copied into attempt artifacts. Use `--keep-workspaces` only for
debugging; retained workspaces can consume substantial disk space and may contain model-produced
content.

### Disabled and empty model selections

Without `--models`, only enabled models run. If a custom config has no enabled models, evaluation
fails with "contains no models." Either enable one or select a configured disabled model explicitly.

### Validation passed, evaluation failed

This can happen when the external executable is missing, login expired, a model is unavailable,
provider policy blocks it, or generated code cannot restore/build. `validate` is intentionally a
local structural check, not a provider connectivity test.

### Timeouts and partial usage

A forced timeout can prevent a runner's final usage export. For Copilot CLI configurations, the
evaluator may recover completed-call telemetry and a persisted credit checkpoint. Such data is
marked partial and is never treated as a complete cost total or used for the AI-credit ranking
tie-breaker.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Command completed; for `evaluate`, inspect reports for model failures |
| `1` | Help invoked without a command, or validation found problems |
| `2` | Unknown command/option, missing file, invalid configuration, or another handled error |
| `3` | At least one evaluation attempt had an infrastructure failure |
| `130` | Cancelled |

### Sharing artifacts

The evaluator avoids printing environment variables and provider command arguments, and strips
credential-like environment variables before running generated code. However, runner output,
generated files, prompts, and logs can still contain sensitive data. Review artifacts before
publishing them.

## 15. Add benchmarks and reproduce runs

To define a new task, follow [Adding a scenario](adding-scenarios.md). Scenario contracts must state
every behavior the hidden acceptance suite grades. Published benchmark packages are immutable;
change their version when prompts, starter files, fixtures, constraints, or tests change.

To rerun a published experiment, follow [Reproducing an evaluation configuration](reproducing-runs.md).
Match the commit, SDK, benchmark version, prompt hash, model and runner settings, budgets,
repetitions, concurrency, and execution environment.

## Command reference

```text
model-evaluator evaluate [options]        Run the selected evaluation matrix.
model-evaluator list-scenarios [options]  List scenarios and prompt hashes.
model-evaluator list-models [options]     List the model IDs --models accepts.
model-evaluator list-presets              List packaged configuration presets.
model-evaluator validate [options]        Validate scenario and adapter configuration.
model-evaluator version                   Print the evaluator version.
```

```text
--preset <name>
--config <path>
--benchmark-root <path>
--models <id,id>
--scenarios <id,id>
--repetitions <positive integer>
--max-parallel <positive integer>
--output <path>
--workspace-root <path>
--execution-image <label>
--generation-timeout <positive seconds>
--build-timeout <positive seconds>
--test-timeout <positive seconds>
--acceptance-timeout <positive seconds>
--keep-workspaces
--debug
--probe                 (list-models only)
```

Run `dotnet run --project .\src\ModelEvaluator.Cli -- --help` for the CLI's authoritative option
summary.
