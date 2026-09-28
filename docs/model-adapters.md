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

#### Reporting usage

If the runner writes `usageFile` as JSON, the values feed the efficiency dimension:

```json
{ "toolCalls": 42, "inputTokens": 18234, "outputTokens": 5120, "estimatedCostUsd": 0.42 }
```

Every field is optional. Missing fields are never guessed: they are listed under
`efficiency.unavailableMetrics` and surfaced in the "Unavailable measurements" section of the
Markdown report.

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
