# Configuration reference

Evaluation behavior is configuration-driven. This page explains where configuration files live,
how they are selected, and which values are safe to keep in the repository. See
[Configuring model adapters](../model-adapters.md) for runner-specific settings and
[Using model-evaluator](../usage-guide.md) for end-to-end commands.

## Repository source of truth

Keep JSON examples under
[`config/`](https://github.com/sujithq/model-evaluator/tree/main/config). The CLI, installed tool,
tests, and documentation all refer to those files.

For personal experiments, use `config/evaluation.local.json`; it is ignored by Git. Never commit
literal provider keys, tokens, cookies, or authenticated session data.

## Packaged presets

The installed tool bundles these named presets:

| Preset | Configuration | Purpose |
| --- | --- | --- |
| `default` | `evaluation.json` | Local reference implementations for harness verification. |
| `auto` | `evaluation.auto.example.json` | GitHub Copilot Auto routing. |
| `copilot-matrix` | `evaluation.copilot-matrix.example.json` | Named GitHub Copilot models. |
| `hydrafusion` | `evaluation.hydrafusion.example.json` | A fixed baseline compared with HydraFusion orchestration systems. |
| `models` | `evaluation.models.example.json` | Generic command-line runner example. |
| `smoke` | `evaluation.smoke.example.json` | Named Copilot models against the minimal smoke benchmark. |
| `smoke-reference` | `evaluation.smoke.reference.json` | Local references against the minimal smoke benchmark. |

List the presets available in a particular tool version:

```powershell
modelevaluator list-presets
```

Use `--preset` for immutable packaged configurations:

```powershell
modelevaluator evaluate --preset smoke-reference
```

Use `--config` for a caller-owned file:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- evaluate `
  --config .\config\evaluation.local.json
```

`--config` and `--preset` are mutually exclusive.

## Project-generation configuration

The normal `evaluate`, `validate`, `list-scenarios`, and `list-models` commands use the
`EvaluationConfiguration` shape:

```json
{
  "benchmarkRoot": "../benchmarks/v1",
  "outputDirectory": "../artifacts/evaluations",
  "repetitions": 3,
  "maxParallel": 1,
  "scenarios": [],
  "models": [],
  "budgets": {},
  "keepWorkspaces": false,
  "debug": false
}
```

Important selection rules:

- an empty `scenarios` array selects every scenario in the benchmark root;
- an empty `--models` option selects only model entries whose `enabled` value is `true`;
- an explicit `--models` filter can select disabled entries;
- `--scenarios`, `--models`, repetition, parallelism, budget, and debug CLI options override the
  configuration; and
- relative paths inside JSON are resolved from the configuration file's directory, while relative
  CLI override paths are resolved from the caller's working directory.

Each model has an ID, adapter, description, adapter-specific settings, and optional child-process
environment overrides. The model ID must identify the complete model-and-runner configuration when
results are compared.

## Controlled comparison configuration

The `compare` command group uses a separate schema with:

- `scenarioRoot` for the synthetic scenario package;
- `targets` for APIs, SDKs, models, deployments, and authentication modes;
- `comparisons` for explicit baseline/candidate groups; and
- `maxRetries` plus request timeout controls.

Provider endpoint and API-key fields support environment-variable references. Prefer those fields
instead of writing credentials into JSON:

```json
{
  "endpointEnvironmentVariable": "AZURE_OPENAI_ENDPOINT",
  "apiKeyEnvironmentVariable": "AZURE_OPENAI_API_KEY",
  "authentication": "api-key"
}
```

Use the environment variable names appropriate for your provider and machine. The configuration is
evidence and may be included in diagnostic output, so literals such as API keys should never be
stored there.

## Where to make changes

| Change | Source of truth |
| --- | --- |
| Add or modify a model runner | `config/*.json` plus [model adapter documentation](../model-adapters.md) |
| Change a benchmark contract | [`benchmarks/`](https://github.com/sujithq/model-evaluator/tree/main/benchmarks) |
| Change an API/SDK synthetic scenario | [`scenarios/`](https://github.com/sujithq/model-evaluator/tree/main/scenarios) |
| Change configuration parsing or validation | [`Configuration`](https://github.com/sujithq/model-evaluator/tree/main/src/ModelEvaluator.Core/Configuration) |
| Explain selection and command behavior | This site and the CLI reference in the usage guide |

Run local validation after changing configuration:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- validate `
  --config .\config\evaluation.local.json
```
