# Configuring model adapters

An adapter turns "a model" into "a solution in a workspace". Adapters are pluggable: the evaluation
pipeline, budgets, checks and reports are identical regardless of which adapter produced the code.

## Configuration shape

`config/evaluation.json`:

```json
{
  "benchmarkRoot": "../benchmarks/v1",
  "outputDirectory": "../artifacts/evaluations",
  "workspaceRoot": null,
  "executionImage": null,
  "repetitions": 3,
  "maxParallel": 1,
  "scenarios": [],
  "models": [ { "id": "...", "adapter": "...", "description": "...", "settings": {}, "environment": {} } ],
  "budgets": {},
  "keepWorkspaces": false
}
```

Relative paths are resolved against the configuration file's directory. `scenarios: []` means every
scenario in the benchmark root. CLI options override any of these values.

## Shipped adapters

### `local-sample`

Copies a sample variant from the scenario package into the workspace. It exists to verify the
harness itself: the known-good sample must pass and the broken variant must fail.

```json
{ "id": "reference-good", "adapter": "local-sample", "settings": { "variant": "good" } }
```

`variant` names a key in the scenario's `samples` map. An unknown variant is reported as an
infrastructure failure, not as a model failure.

### `command-line`

Invokes any external agent runner. This is how real models are evaluated.

```json
{
  "id": "agent-cli-model-a",
  "adapter": "command-line",
  "settings": {
    "command": "my-agent",
    "arguments": "--model gpt-x --prompt-file {promptFile} --workdir {workspace} --usage-json {usageFile} --timeout {timeoutSeconds}",
    "runnerName": "my-agent",
    "runnerVersion": "1.4.2",
    "usageFile": "usage.json"
  },
  "environment": { "MY_AGENT_TELEMETRY": "off" }
}
```

Placeholders substituted in `arguments`: `{workspace}`, `{promptFile}`, `{prompt}`, `{artifacts}`,
`{timeoutSeconds}`, `{scenario}`, `{model}`, `{usageFile}`. The same values are also exported as
`EVAL_WORKSPACE`, `EVAL_PROMPT_FILE`, `EVAL_ARTIFACTS`, `EVAL_USAGE_FILE`, `EVAL_SCENARIO`,
`EVAL_MODEL` and `EVAL_TIMEOUT_SECONDS`.

The runner's stdout and stderr are captured to `transcript.log` in the attempt artifacts. Exceeding
the generation budget is recorded as `BudgetExceeded`, which counts as an unsuccessful attempt.

#### Discovering model IDs

The evaluator's model list is configuration-driven: a model exists because a configuration file
declares it, not because a provider advertises it. `model-evaluator list-models` prints those IDs
together with the adapter, the `--model` value parsed out of `arguments`, and the runner name.

GitHub Copilot CLI has no `models` subcommand. `list-models --probe` therefore checks configured
Copilot provider model IDs against the embedded catalog generated from GitHub's official Docs
tables. It does not start the runner, authenticate, or consume model usage. The result describes
provider-wide publication and retirement status only; it cannot verify whether the current account,
organization policy, geography, rollout, or installed CLI version permits a model.

#### GitHub Copilot CLI

[`config/evaluation.copilot-matrix.example.json`](../config/evaluation.copilot-matrix.example.json)
is the canonical Copilot CLI configuration. It contains the supported named model IDs and the
shared runner settings; select only the models you want to compare with `--models`.

Install GitHub Copilot CLI on `PATH`, authenticate with `copilot login`, and ensure your account
and organization policy allow the selected model. The evaluator passes the selected provider model
ID directly to Copilot; it does not grant model access or substitute another model.

From the repository root, validate the matrix and run a small comparison:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- validate --config .\config\evaluation.copilot-matrix.example.json
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate --config .\config\evaluation.copilot-matrix.example.json --models copilot-gpt-6-astra,copilot-gpt-6-luna --scenarios console-task-cli --repetitions 3
```

With the installed .NET tool, select packaged configurations by name instead of using repository
paths:

```powershell
modelevaluator validate --preset copilot-matrix
modelevaluator evaluate --preset copilot-matrix --models copilot-gpt-6-astra,copilot-gpt-6-luna --scenarios console-task-cli --repetitions 3
modelevaluator evaluate --preset auto --models copilot-auto --scenarios console-task-cli --repetitions 1
```

Do not run an unfiltered matrix by accident: 28 models x 5 scenarios x 3 repetitions creates
420 model attempts. Use `list-models` and an explicit `--models` selection for normal comparisons.
Validation only checks local configuration and benchmark packages; it does not authenticate or
verify provider access.

The adapter starts Copilot inside each fresh workspace and passes the resolved instructions as one
`--prompt` argument. The matrix disables built-in MCP servers, custom instruction files, user
questions and automatic CLI updates. Use a clean, dedicated runner profile without personal MCP
servers, plugins or other customizations to keep comparisons consistent. Record
`runnerVersion` from `copilot --no-auto-update --version` before publishing results.

**Run only in a disposable, isolated environment.** `--allow-all-tools` lets the agent edit files
and execute commands without confirmation; a temporary workspace is not a security sandbox.

The matrix passes `--usage-output-file "{usageFile}"` and selects `"usageFormat": "copilot-cli"`.
Native usage is retained in each attempt's artifacts and read into the report, including AI
credits, input/output tokens, cache metrics and API requests when reported. Completed-run
telemetry supplements tool-call counts when available; invoice USD is not available.
The generated [Copilot model catalog](copilot-models.md) tracks GitHub's provider-wide supported
and deprecated model tables. Automation checks for updates hourly and opens a pull request only
when the generated Markdown or embedded JSON changes.

| Family | Model IDs |
| --- | --- |
| Claude | `claude-sonnet-5`, `claude-fable-5.1`, `claude-fable-5`, `claude-opus-5`, `claude-opus-4.8`, `claude-haiku-4.5`, `claude-opus-5.5` |
| GPT | `gpt-6-astra`, `gpt-6-luna`, `gpt-6-sol`, `gpt-5.6-sol`, `gpt-5.6-sol-fast`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.5`, `gpt-5.4`, `gpt-5.4-mini`, `gpt-5.3-codex`, `gpt-5-mini` |
| Gemini | `gemini-3.8-flash`, `gemini-3.7-flash` |
| Grok | `grok-4.5`, `grok-4.6`, `grok-4.7` |
| MAI | `mai-code-1.1-flash` |
| Automatic routing | `auto` |

**Availability caveats:** `gpt-5.6-sol-fast` is labeled internal-only in the app catalog. Other
model IDs can also be unavailable because of CLI version, account entitlements or organization
policy. The evaluator passes the selected ID unchanged; local validation does not verify access.
Authenticate and check the model picker in your standalone Copilot CLI before evaluating a model.

`evaluation.auto.example.json` is included separately for the app's Auto option. **Auto is a
routing strategy, not a fixed model.** Its report ID is `copilot-auto`; `reportedModels` in usage
records the contributing model IDs when exported by the CLI. Do not present Auto results as a
named-model baseline.

For example, select a model ID from the matrix and run:

```powershell
$model = "claude-sonnet-5"
$config = ".\config\evaluation.copilot-matrix.example.json"
dotnet run --project .\src\ModelEvaluator.Cli -- validate --config $config
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate --config $config --models "copilot-$model" --scenarios console-task-cli --repetitions 1
```

Auto remains a separate example because it is a routing strategy rather than a fixed model.
Its report ID is `copilot-auto`; `reportedModels` records contributing model IDs when exported.

For a shared per-task ranking, use
[`evaluation.copilot-matrix.example.json`](../config/evaluation.copilot-matrix.example.json) and
explicitly select the model IDs your CLI account supports:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate --config .\config\evaluation.copilot-matrix.example.json --models copilot-gpt-6-astra,copilot-gpt-6-luna --scenarios console-task-cli --repetitions 3
```

This runs six independent attempts and includes both models in one report. Without `--models`,
the matrix selects only entries marked `"enabled": true`; explicitly selecting disabled IDs is
supported. Separately executed runs are not automatically merged; use one matrix invocation when
you want a single comparison report.

#### Reporting usage

For custom runners, `usageFormat` defaults to `normalized`. If the runner writes `usageFile` as
JSON, the values feed the efficiency dimension:

```json
{ "toolCalls": 42, "inputTokens": 18234, "outputTokens": 5120, "estimatedCostUsd": 0.42 }
```

Every field is optional. Missing fields are never guessed: they are listed under
`efficiency.unavailableMetrics` and surfaced in the "Unavailable measurements" section of the
Markdown report.

For Copilot CLI, set the following settings and append the usage option to the runner arguments:

```json
{
  "arguments": "--model gpt-6-luna --prompt \"{prompt}\" --allow-all-tools --usage-output-file \"{usageFile}\"",
  "usageFile": "usage.json",
  "usageFormat": "copilot-cli"
}
```

The matrix and special-purpose example files include the isolation/reproducibility flags discussed above.
The parser uses the export shape verified with Copilot CLI `1.0.87-0`:

| Export field | Report metric | Meaning |
| --- | --- | --- |
| `totalNanoAiu` | `aiCredits` | Divide by 1,000,000,000; consumption, not an invoice charge. |
| `totalPremiumRequestCost` | `premiumRequests` | Preserve legacy request units; never substitute for credits or USD. |
| `modelMetrics.*.usage.inputTokens` / `outputTokens` | `inputTokens` / `outputTokens` | Sum across reported models, not agent breakdowns. |
| `modelMetrics.*.usage.cacheReadTokens` / `cacheWriteTokens` | `cacheReadTokens` / `cacheWriteTokens` | Cache detail, not extra tokens added to the input/output total. |
| `modelMetrics.*.usage.reasoningTokens` | `reasoningTokens` | Report separately; do not add to output tokens again. |
| `modelMetrics.*.requests.count` | `apiRequests` | Model API requests, not tool calls or user requests. |
| `totalApiDurationMs` | `apiDurationSeconds` | Model API time, not total generation or evaluator wall time. |
| `modelMetrics` keys | `reportedModels` | Model IDs contributing to the session, including routing/subagent use when exported. |

`agentMetrics` and per-model credit breakdowns are not added to session totals: that would count the
same work twice. Each token/request sum requires the field on every reported model; partial data is
unavailable rather than an artificially low total. Top-level `tokenDetails.input` is not used as total
input: the observed export excludes cached/written tokens there, unlike `modelMetrics.*.usage.inputTokens`.

An absent usage file (for example, a killed runner), malformed JSON or invalid measurements produces
a visible warning and persisted `efficiency.usageWarnings`. It does not change the functional grade.
The original JSON remains in the artifacts for inspection. Reports convert AI credits to a
**USD equivalent** at GitHub's published rate of $0.01 per credit, not an invoice charge.
Included consumption and paid overage depend on billing/account state not present in this
session export. See [GitHub's billing documentation](https://docs.github.com/en/copilot/concepts/billing-and-usage/organizations-and-enterprises/billing).

#### Usage when a runner times out or is cancelled

`--usage-output-file` is a **final** export; forcibly terminating Copilot can prevent it from being
written. For `usageFormat: "copilot-cli"`, the adapter also enables Copilot's supported OpenTelemetry
file exporter, writing `usage.telemetry.jsonl` inside each attempt's artifacts directory. It explicitly
selects the local file exporter and disables prompt/response/tool-content capture, overriding those
telemetry settings for this child process only. Authentication, model selection and generation budgets
are unchanged; no shutdown grace period or extra model request is added.

If the final export is missing, malformed or contains no recognized measurements, completed `chat`
and `execute_tool` spans provide a best-effort fallback saved as `usage.partial.json`.
The native `usage.json` is never overwritten, and valid final usage always takes precedence.
For a normally completed runner with valid final usage, telemetry supplements only the missing
`toolCalls` field. It counts distinct completed `execute_tool` spans by trace/span ID, including
subagent tool executions, and ignores cumulative metric snapshots. A count (including zero) requires
well-formed telemetry and completed root agent spans for all observed traces. Missing, truncated or
unfinished telemetry leaves `toolCalls` unavailable with a warning; it never downgrades final credits
or tokens to partial usage or disables their ranking eligibility. The count measures exported tool
executions, not model API requests or proposed tool calls.
Spans are deduplicated by trace/span ID; parent `invoke_agent` totals and periodic metric snapshots
are not added again. A truncated final JSONL line is ignored with a warning.

The fallback reports observed input/output tokens, API requests, completed tool executions, API
duration and legacy premium-request units where present. For new Copilot sessions the harness supplies
a unique `--session-id` and reads only that session's persisted `session.usage_checkpoint` records from
`COPILOT_HOME` (default: `~/.copilot`). Only usage checkpoint records are copied to
`usage.checkpoints.jsonl`, not conversation content. The highest cumulative `totalNanoAiu` checkpoint,
divided by 1,000,000,000, supplies **partial AI credits**; checkpoints are never summed or added to
per-call costs. If the CLI has not persisted a checkpoint, credits stay unavailable. Explicitly
selected/resumed/connected sessions are not inspected, since their history can include unrelated usage.

**No credits or USD are inferred from token counts or `github.copilot.cost`**: the latter is the
legacy request multiplier, not credits. Cache/reasoning breakdowns remain unavailable in the fallback.
Missing fields stay null, including a token aggregate when any captured call omits that field.

Fallback measurements carry `efficiency.usageIsPartial: true` and appear in the Markdown report's
"Partial usage measurements" section. They are excluded from complete consumption totals and the
AI-credit ranking tier, even when a partial credit checkpoint is available. A timed-out attempt still counts as unsuccessful; a cancelled attempt remains
an infrastructure failure. In-flight calls, buffered spans, disabled telemetry and CLI versions without
this exporter can still leave usage unavailable. These files are diagnostics, not a billing ledger.

The exporter shape and pre-shutdown persistence were verified with CLI `1.0.87-0` against a local mock
provider, including forced termination before `usage.json` existed. JSON stdout is not used as a usage
source: that version omits `assistant.usage` events from `--output-format json`.
See `copilot help monitoring`, the
[official telemetry guide](https://github.com/github/copilot-sdk/blob/main/docs/observability/opentelemetry.md)
and the SDK's [`session.usage_checkpoint` schema](https://github.com/github/copilot-sdk/blob/main/nodejs/src/generated/session-events.ts).

#### Runner consistency

Comparisons are only meaningful when models share tools, context and budgets. Drive every compared
model through the same runner and record `runnerName`/`runnerVersion`; each attempt stores the
runner name and version, and the reports identify the full model-and-runner configuration. When two
models must use different runners, say so explicitly when publishing the comparison.

## Credentials

Provider credentials are supplied to the agent runner only. Before running any `dotnet` command on
generated code, the harness strips environment variables whose names start with credential-ish
prefixes (`ProcessRunner.CredentialVariablePrefixes`, for example `OPENAI_`, `AZURE_OPENAI_`,
`ANTHROPIC_`, `GITHUB_TOKEN`), so generated projects and acceptance tests execute without
model-provider credentials. Never commit credentials: pass them through CI secrets as shown in
[`.github/workflows/evaluate.yml`](../.github/workflows/evaluate.yml).

## Writing a new adapter

Implement `IModelAdapter` in `src/ModelEvaluator.Core/Adapters`:

```csharp
public sealed class MyAdapter : IModelAdapter
{
    public const string AdapterKey = "my-adapter";

    public string Key => AdapterKey;

    public Task<ModelAttemptOutput> GenerateAsync(ModelAttemptContext context, CancellationToken cancellationToken);
}
```

`ModelAttemptContext` supplies the scenario, model configuration, workspace path, artifacts path,
resolved prompt, prompt file, timeout and repetition index. `ModelAttemptOutput` reports success,
timeout, infrastructure failure, runner information and any usage metrics, and must list unavailable
metrics explicitly. Register the adapter in `ModelAdapterFactory.CreateDefault()`; `validate` then
accepts it as an adapter key.

Adapters must only write inside `context.WorkspacePath` and `context.ArtifactsPath`, and must never
read the scenario's `acceptance/` or `samples/` directories - evaluator-owned tests stay outside the
model's context and control.

With `maxParallel > 1`, the same adapter instance may receive concurrent `GenerateAsync` calls.
Keep attempt state local, do not change the process-wide current directory or environment, and use
the supplied cancellation token for all external work. Independent child processes must be stopped
and their output drained before returning so artifact persistence and workspace cleanup are safe.
The shipped command-line adapter already uses per-process environments and per-attempt transcripts.
