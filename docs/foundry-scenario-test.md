# Test a new prompt against a Foundry deployment

Use the `compare` command group to test a new system/user prompt against a model that is already
deployed in Microsoft Foundry. This workflow requires only scenario JSON and a local evaluation
configuration; it does not require a new C# adapter when the deployment exposes an OpenAI-compatible
Chat Completions or Responses endpoint.

!!! note "Target, baseline, and base model"
    A target identifies one complete execution path: model, deployment, endpoint, adapter,
    authentication, and request parameters. A comparison baseline is the target referenced by
    `comparisons[].baselineTargetId`.

    In `compare` mode there is no global base-model setting and the target array order does not
    select the baseline. Changing the base model means changing the `baselineTargetId` of the
    comparison group.

## Prerequisites

Have these details for the Foundry deployment:

- the underlying model ID;
- the exact deployment name;
- the account endpoint;
- whether the deployment supports Chat Completions or Responses;
- authentication through `DefaultAzureCredential` or an API key; and
- the same request parameters for the baseline and candidate when testing the model variable.

The examples below use:

- `DefaultAzureCredential`;
- the Azure OpenAI `/openai/v1` Responses surface;
- one new baseline deployment and one candidate deployment; and
- `config/evaluation.local.json`, which is ignored by Git.

## 1. Add the scenario

Create a file under `scenarios/v1`, for example
`scenarios/v1/my-new-scenario.json`:

```json
{
  "schemaVersion": 1,
  "id": "my-new-scenario",
  "version": "1.0.0",
  "title": "My new prompt scenario",
  "category": "answer-quality",
  "description": "Tests the expected behavior for a new system and user prompt.",
  "systemPrompt": "Your system prompt goes here.",
  "turns": [
    {
      "user": "Your user prompt goes here."
    }
  ],
  "parameters": {
    "maxOutputTokens": 800
  },
  "validation": {
    "containsAnyOf": [
      {
        "id": "required-concept",
        "terms": [
          "expected phrase",
          "acceptable alternative"
        ]
      }
    ],
    "doesNotContain": [
      "forbidden claim"
    ]
  },
  "dryRun": {
    "calls": [
      {
        "turn": 0,
        "toolRound": 0,
        "text": "An offline fixture response that includes the expected phrase."
      }
    ]
  }
}
```

The dry-run response is a deterministic fixture. It verifies scenario loading and report generation
without contacting Foundry.

For more detailed validations, copy an existing definition from
[`scenarios/v1`](https://github.com/sujithq/model-evaluator/tree/main/scenarios/v1). The available
schema supports required phrases, forbidden phrases, exact JSON, tool calls, multiple turns, and
classified API errors.

## 2. Create a small local comparison

Copy one of the Foundry examples into a new local configuration, but keep only the targets and
comparison groups you intend to validate. This matters because live validation checks every enabled
target in the configuration, not only targets later selected with `--targets`.

```json
{
  "schemaVersion": 1,
  "scenarioRoot": "../scenarios/v1",
  "outputDirectory": "../artifacts/evaluations/foundry-local",
  "repetitions": 3,
  "maxParallel": 1,
  "maxRetries": 0,
  "timeoutSeconds": 180,
  "targets": [
    {
      "id": "foundry-new-base-responses",
      "enabled": true,
      "adapter": "direct-rest-responses",
      "apiKind": "azure-openai",
      "modelId": "new-base-model-id",
      "deploymentId": "new-base-model-id",
      "endpointEnvironmentVariable": "FOUNDRY_ENDPOINT",
      "authentication": "default-credential",
      "tokenScope": "https://cognitiveservices.azure.com/.default",
      "parameters": {
        "maxOutputTokens": 800
      },
      "evidence": {
        "euDataZone": "unknown",
        "capacity": "unknown"
      }
    },
    {
      "id": "foundry-candidate-responses",
      "enabled": true,
      "adapter": "direct-rest-responses",
      "apiKind": "azure-openai",
      "modelId": "candidate-model-id",
      "deploymentId": "candidate-model-id",
      "endpointEnvironmentVariable": "FOUNDRY_ENDPOINT",
      "authentication": "default-credential",
      "tokenScope": "https://cognitiveservices.azure.com/.default",
      "parameters": {
        "maxOutputTokens": 800
      },
      "evidence": {
        "euDataZone": "unknown",
        "capacity": "unknown"
      }
    }
  ],
  "comparisons": [
    {
      "id": "new-foundry-base-versus-candidate",
      "baselineTargetId": "foundry-new-base-responses",
      "candidateTargetIds": [
        "foundry-candidate-responses"
      ],
      "intendedVariable": "model"
    }
  ]
}
```

Replace the placeholder IDs with the values shown by Foundry. In this controlled example,
`deploymentId` intentionally equals `modelId`. The deployment ID is the name sent to the endpoint,
so if Foundry deployed either model under a custom name, use that exact name and account for the
separate deployment dimension described below.

The evidence values above intentionally remain `unknown`. They produce readiness warnings and do
not invalidate the functional test. Set them to `confirmed` only when you can provide an accurate
EU Data Zone or capacity reference.

### Choose the matching adapter

| Deployment contract | REST adapter | SDK adapter |
| --- | --- | --- |
| Responses | `direct-rest-responses` | `openai-sdk-responses` |
| Chat Completions | `direct-rest` | `openai-sdk` |

Keep the same adapter for a model-only comparison. The Foundry Responses examples use the
`/openai/v1` endpoint shape.

## 3. Set the new model as the baseline

The comparison direction is controlled only by these two fields:

```json
{
  "baselineTargetId": "foundry-new-base-responses",
  "candidateTargetIds": [
    "foundry-candidate-responses"
  ]
}
```

To switch the base model later, point `baselineTargetId` at the other target and move the previous
baseline into `candidateTargetIds`. Reordering the `targets` array has no effect on the baseline.

## 4. Keep the comparison controlled

The analyzer compares model ID, deployment, adapter, API kind/version, authentication, token scope,
resolved endpoint, and effective request parameters. A report is `controlled` only when exactly one
dimension changes and it matches `intendedVariable`.

For a model-only comparison:

- keep adapter, endpoint, authentication, API kind, token scope, and parameters identical;
- use the same scenario package and repetition count;
- set `intendedVariable` to `model`; and
- include both target IDs in the `--targets` option.

!!! warning "Custom deployment names"
    The analyzer treats `deployment` as a distinct variable when `deploymentId` is not equal to
    `modelId`. If both the model and deployment name change, the report will correctly identify two
    changed dimensions and mark the pair `not-controlled`.

    For a model-only comparison, use deployments whose names equal their model IDs. If the deployed
    resource requires custom names, record the result as a model-plus-deployment change rather than
    claiming that only the model varied.

To compare two deployment names for the same underlying model, set both targets to the same
`modelId`, change only `deploymentId`, and use `"intendedVariable": "deployment"`.

## 5. Verify offline, then validate live access

Set the endpoint and authenticate with Azure before the live run:

```powershell
$env:FOUNDRY_ENDPOINT = "https://<account>.cognitiveservices.azure.com/"
$env:AZURE_TENANT_ID = "<tenant-id>" # if tenant selection is required
az login --tenant $env:AZURE_TENANT_ID
```

Confirm that the scenario is discoverable:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  compare list-scenarios --scenario-root .\scenarios\v1
```

Run the offline path first:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  compare run --config .\config\evaluation.local.json --dry-run `
  --targets foundry-new-base-responses,foundry-candidate-responses `
  --scenarios my-new-scenario `
  --repetitions 1
```

Validate credentials and endpoint configuration without executing an attempt:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  compare validate --config .\config\evaluation.local.json --live
```

## 6. Run the new scenario

Start with one repetition:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  compare run --config .\config\evaluation.local.json `
  --targets foundry-new-base-responses,foundry-candidate-responses `
  --scenarios my-new-scenario `
  --repetitions 1
```

After the prompt and deployment settings are verified, use the configured three repetitions or
another count appropriate for the expected variability:

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  compare run --config .\config\evaluation.local.json `
  --targets foundry-new-base-responses,foundry-candidate-responses `
  --scenarios my-new-scenario `
  --repetitions 3
```

The run writes `results.json` and `report.md` beneath the configured output directory. Inspect the
comparison entry first:

- `controlled` confirms that the declared variable was the only detected change;
- `not-controlled` lists the changed dimensions;
- functional outcome reports baseline and candidate pass/fail separately; and
- attempt details report latency, TTFB when available, usage, errors, request hashes, and selected
  adapter behavior.

Do not commit `config/evaluation.local.json`, endpoint values, API keys, or authenticated session
data.
