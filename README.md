# .NET AI model evaluator

`model-evaluator` is a .NET 10 console/CLI harness for comparing AI model behavior separately from the
API, SDK, package version, authentication mode, and request configuration used to call it.

The first scenario package targets a generic production healthcare-insurance assistant migration from
GPT-5.1 to a newer model. Every fixture is synthetic: the repository and default evaluation contain no
Independer, customer, member, claim, endpoint, or credential data.

## What the first version evaluates

The versioned `scenarios/v1` package runs the same prompts through every selected target:

| Scenario | What it validates |
| --- | --- |
| `general-answer-quality` | Grounded, bounded insurance guidance without invented eligibility or diagnosis. |
| `structured-output` | Strict JSON schema adherence and exact synthetic values. |
| `deterministic-tool-calling` | The model must call a declared mock tool with exact arguments, then use only its result. |
| `multi-turn-consistency` | A synthetic value is retained across two turns without external state. |
| `unsupported-parameter-handling` | An unsupported parameter is surfaced as a classified result instead of crashing; a provider that accepts it can pass with a normal response. |

The package is discovered at runtime. A scenario must be versioned, have a stable ID, include a dry-run
fixture, and declare its validators. The CLI records a prompt/scenario fingerprint, so changed prompts are
not accidentally compared with earlier runs.

## Integration adapters

| Adapter ID | Live path | Authentication | Notable measurements |
| --- | --- | --- | --- |
| `direct-rest` | Chat Completions over direct `HttpClient`/REST for OpenAI or Azure OpenAI | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | End-to-end latency, TTFB through response headers, status, retries, usage, reasoning/cached tokens, finish reason |
| `openai-sdk` | Official `OpenAI` .NET Chat Completions client; Azure uses the documented `/openai/v1` endpoint | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | SDK-captured latency, usage, finish reason, errors, and tool calls; TTFB is explicitly unavailable |
| `direct-rest-responses` | Responses API over direct `HttpClient`/REST | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | End-to-end latency, TTFB, usage, reasoning/cached tokens, response status, and function calls |
| `openai-sdk-responses` | Official `OpenAI` .NET Responses client | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | SDK-captured latency, usage, response status, structured output, and function calls; TTFB is explicitly unavailable |
| `microsoft-extensions-ai` | `IChatClient` over the OpenAI .NET Chat Completions client | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | Abstraction-converted usage, finish reason, structured output, and manual function-call round trips |
| `microsoft-extensions-ai-responses` | `IChatClient` over the OpenAI .NET Responses client | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | Abstraction-converted Responses usage, structured output, and function calls |
| `semantic-kernel` | Semantic Kernel OpenAI Chat Completions connector | OpenAI API key; Azure OpenAI API key or `DefaultAzureCredential` | Connector latency, usage, structured output, and exact-scenario tool replay |
| `copilot-cli` | GitHub Copilot CLI programmatic prompt mode with an explicitly selected model | Existing Copilot CLI sign-in or `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, or `GITHUB_TOKEN` | End-to-end agent-CLI latency, selected model, CLI-reported usage, finish state, prompt-mediated tool calls |
| `local-fixture` | In-process deterministic responses; no network | None | Harness/scenario/reporting validation only |

The pinned integration packages are `OpenAI` 2.15.0 and `Azure.Identity` 1.21.0. Azure uses the same
coherent OpenAI SDK package and v1 endpoint rather than mixing the separately versioned `Azure.AI.OpenAI`
extension package with newer OpenAI types.
The abstraction paths are pinned to `Microsoft.Extensions.AI.OpenAI` 11.0.0 and
`Microsoft.SemanticKernel.Connectors.OpenAI` 1.82.0.
The SDK adapter disables SDK-internal retries and the harness applies the configured retry count, so
retry totals are observed consistently rather than hidden inside a client.

Framework adapters are intended only as controlled comparison surfaces. A changed result must be
compared with the direct REST or native SDK adapter before attributing it to the model.

## Four independent readiness gates

A model target is never reduced to one boolean:

1. **Technically compatible**: the selected adapter/provider/model/deployment combination completed or
   explicitly classified its expected unsupported parameter. A missing model, credential, endpoint, or
   transport failure is not compatible.
2. **Functionally validated**: every selected deterministic scenario check passed.
3. **Deployable in EU Data Zone**: requires explicit target evidence such as a deployment-region or
   service-policy reference.
4. **Capacity confirmed**: requires explicit quota, reservation, or deployment-capacity evidence.

`euDataZone` or `capacity` values left as `unknown` are shown as `unknown`, never as `confirmed`.
`Readiness.Ready` is true only when all four gates pass. Dry-run execution deliberately leaves technical
compatibility as `not-evaluated`.

## Prerequisites

- .NET 10 SDK 10.0.404 (pinned by `global.json`, with patch roll-forward).
- No live AI credential is needed for build, tests, or the end-to-end dry run.
- NuGet restore uses the required Microsoft proxy through [`NuGet.Config`](NuGet.Config):
  `https://packagefeedproxy.microsoft.io/nuget/v3/index.json`. Project lock files pin resolved
  transitive versions after restore.

## Offline quick start

```powershell
dotnet restore --configfile NuGet.Config
dotnet build --configuration Release
dotnet test --configuration Release

dotnet run --project .\src\ModelEvaluator.Cli -- list-scenarios
dotnet run --project .\src\ModelEvaluator.Cli -- list-adapters
dotnet run --project .\src\ModelEvaluator.Cli -- validate
dotnet run --project .\src\ModelEvaluator.Cli -- run --dry-run
```

The final command runs all five scenarios against the two synthetic model targets and writes:

- `artifacts/evaluations/<run-id>/results.json`
- `artifacts/evaluations/<run-id>/report.md`

It makes no external AI request. Unsupported-parameter and mock-tool calls use deterministic local
fixtures.

## CLI commands

```text
model-evaluator list-scenarios [--scenario-root <path>]
model-evaluator list-adapters
model-evaluator validate [--config <path>] [--live]
model-evaluator run [--config <path>] [--dry-run] [--output <path>]
                    [--scenario-root <path>] [--scenarios <id,id,...>]
                    [--targets <id,id,...>] [--repetitions <count>]
```

Exit codes are `0` for a completed passing run, `1` for completed attempts with a failed/unsupported
outcome, `2` for invalid input/configuration/runtime setup, and `130` for cancellation.

Pack and install the current source as a local tool:

```powershell
dotnet pack .\src\ModelEvaluator.Cli --configuration Release --output .\artifacts\packages
dotnet tool install --tool-path .\artifacts\tools `
  --add-source .\artifacts\packages `
  --version 0.1.0 Sujithq.ModelEvaluator
.\artifacts\tools\model-evaluator run --dry-run
```

## Configuration

[`config/evaluation.offline.json`](config/evaluation.offline.json) is the safe default. It contains only
synthetic local targets. [`config/evaluation.live.example.json`](config/evaluation.live.example.json)
shows a model-migration pair and an adapter comparison; copy it to an ignored
`config/evaluation.local.json` before adding local model identifiers or non-committed evidence.
[`config/evaluation.copilot.json`](config/evaluation.copilot.json) selects the six GitHub Copilot
models shown in the supplied EU Data Zone availability matrix.
[`config/evaluation.foundry.example.json`](config/evaluation.foundry.example.json) configures the
matching native REST and OpenAI .NET SDK paths, plus `gpt-5.1` as the migration baseline, against
an Entra-authenticated EU Data Zone Foundry resource.
[`config/evaluation.foundry.responses.example.json`](config/evaluation.foundry.responses.example.json)
uses the same models and deployment evidence through the native Responses API.
[`config/evaluation.foundry.abstractions.example.json`](config/evaluation.foundry.abstractions.example.json)
compares direct clients with `Microsoft.Extensions.AI` and Semantic Kernel.

A target records:

```json
{
  "id": "azure-newer-sdk",
  "adapter": "openai-sdk",
  "apiKind": "azure-openai",
  "modelId": "exact-model-id",
  "deploymentId": "optional-exact-deployment-name",
  "endpointEnvironmentVariable": "AZURE_OPENAI_ENDPOINT",
  "apiKeyEnvironmentVariable": "AZURE_OPENAI_API_KEY",
  "authentication": "api-key",
  "tenantId": "optional-tenant-guid-for-default-credential",
  "parameters": {
    "maxOutputTokens": 800,
    "reasoningEffort": "low"
  },
  "evidence": {
    "euDataZone": "confirmed",
    "euDataZoneReference": "internal evidence reference",
    "capacity": "confirmed",
    "capacityReference": "quota/capacity evidence reference"
  }
}
```

Supported target combinations:

- `direct-rest` + `openai` + `api-key`
- `direct-rest` + `azure-openai` + `api-key` or `default-credential`
- `openai-sdk` + `openai` + `api-key`
- `openai-sdk` + `azure-openai` + `api-key` or `default-credential`
- `direct-rest-responses` + `openai` + `api-key`
- `direct-rest-responses` + `azure-openai` + `api-key` or `default-credential`
- `openai-sdk-responses` + `openai` + `api-key`
- `openai-sdk-responses` + `azure-openai` + `api-key` or `default-credential`
- `microsoft-extensions-ai` + `openai` + `api-key`
- `microsoft-extensions-ai` + `azure-openai` + `api-key` or `default-credential`
- `microsoft-extensions-ai-responses` + `openai` + `api-key`
- `microsoft-extensions-ai-responses` + `azure-openai` + `api-key` or `default-credential`
- `semantic-kernel` + `openai` + `api-key`
- `semantic-kernel` + `azure-openai` + `api-key` or `default-credential`
- `copilot-cli` + `github-copilot` + `cli-session`
- `local-fixture` + `local` + `none`

An unsupported pair is emitted as `unsupported-combination` in the JSON/Markdown report. The CLI
does not silently substitute another provider or API.

### Endpoints and secrets

Keep real endpoints and credentials out of Git. The live example reads them from standard environment
variables:

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://<resource>.openai.azure.com"
$env:AZURE_OPENAI_API_KEY = "<local secret>"
```

The endpoint is expected to be the Azure resource root. `direct-rest` uses the Azure `/openai/v1`
surface unless `apiVersion` is set, in which case it uses the deployment-specific REST route. OpenAI
direct/SDK endpoints should include `/v1`.

Configuration also supports .NET user secrets. The two project files share the
`Sujithq.ModelEvaluator.Cli` secrets ID:

```powershell
dotnet user-secrets set --project .\src\ModelEvaluator.Cli `
  "Targets:0:Endpoint" "https://<resource>.openai.azure.com"
dotnet user-secrets set --project .\src\ModelEvaluator.Cli `
  "Targets:0:ApiKey" "<local secret>"
```

User secrets and `MODEL_EVALUATOR_` environment configuration override the JSON file. For example,
`MODEL_EVALUATOR_Targets__0__ApiKey` is the environment form of `Targets:0:ApiKey`. Reports intentionally
exclude endpoint and credential values.

For Microsoft Entra ID, set `authentication` to `default-credential`. `DefaultAzureCredential` is used
for Azure OpenAI; set `tenantId` when the process may have another Azure tenant configured. The default
token scope is
`https://cognitiveservices.azure.com/.default` and can be overridden per target. The evaluator does not
create or alter an identity, role assignment, deployment, quota, or Azure resource.

### GitHub Copilot CLI targets

The committed [`config/evaluation.copilot.json`](config/evaluation.copilot.json) keeps the adapter,
authentication mode, scenarios, and request parameters fixed while changing only these model IDs:

- `gpt-6.2-sol`
- `gpt-6.2-astra`
- `gpt-6.1-sol`
- `gpt-6.1-terra`
- `gpt-6-sol`
- `gpt-5.6-sol`

Authenticate Copilot CLI once with `copilot login`, or supply one of the token environment variables
documented by GitHub. The adapter starts `copilot` from `PATH`; set
`MODEL_EVALUATOR_COPILOT_CLI_PATH` when a different executable must be selected. Automatic CLI updates
are disabled for a run so the measured CLI version is stable and recorded in the report.

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  validate --config .\config\evaluation.copilot.json --live

dotnet run --project .\src\ModelEvaluator.Cli -- `
  run --config .\config\evaluation.copilot.json
```

The live matrix makes 30 scenario attempts and, with the current scenarios, 36 model requests. It can
consume GitHub Copilot premium requests; use a selected `--targets` or `--scenarios` list for a smaller
probe.

Copilot CLI is an agent runtime, not a raw Chat Completions endpoint. Tool definitions and deterministic
tool results are translated into an explicit prompt protocol, JSON schemas are enforced through the
prompt, and `maxOutputTokens` is a soft prompt instruction. `temperature` and `topP` are reported as
`unsupported-parameter` instead of being silently discarded. Native provider tool-calling, native
structured-output enforcement, and agent-runtime results must not be treated as equivalent to direct
REST or SDK measurements. The adapter disables custom instructions and built-in MCP servers, performs
no approvals, and gives the process no repository write or network permission.

### Microsoft Foundry EU Data Zone targets

The Foundry matrices keep the endpoint, deployment type, authentication, and request parameters fixed
while changing model and adapter dimensions independently. They use `DefaultAzureCredential`, so no API
key is stored in the repository. The `tenantId` field keeps authentication pinned to the project tenant
when another tenant is selected in the machine's environment:

```powershell
$env:FOUNDRY_DATAZONE_ENDPOINT = "https://<resource>.services.ai.azure.com"

dotnet run --project .\src\ModelEvaluator.Cli -- `
  validate --config .\config\evaluation.foundry.example.json --live

dotnet run --project .\src\ModelEvaluator.Cli -- `
  validate --config .\config\evaluation.foundry.responses.example.json --live

dotnet run --project .\src\ModelEvaluator.Cli -- `
  run --config .\config\evaluation.foundry.responses.example.json

dotnet run --project .\src\ModelEvaluator.Cli -- `
  run --config .\config\evaluation.foundry.abstractions.example.json
```

The Responses command runs 70 controlled attempts: seven models, five scenarios, direct REST, and the
OpenAI .NET SDK. The model IDs are also the exact Foundry deployment names in both configurations.
Use Chat Completions for broad chat text, JSON, and parameter compatibility. GPT-6.1/6.2 reasoning
models do not support function tools through `/v1/chat/completions`; use the Responses matrix for that
native function-calling path. `--targets` can still narrow either run to a baseline and a candidate set.

## Run a controlled comparison

A comparison group identifies a baseline and candidates and declares the intended variable:

```json
{
  "id": "gpt-model-migration",
  "baselineTargetId": "azure-gpt-5-1-rest",
  "candidateTargetIds": ["azure-newer-rest"],
  "intendedVariable": "model"
}
```

The analyzer flags changes to model, deployment, adapter, API kind/version, authentication, endpoint,
or effective request parameters. A pair is marked `controlled` only if exactly one dimension changed
and it matches the declared variable. A result marked `not-controlled` remains visible, but it is not
evidence for a single-variable migration decision.

Recommended sequence:

1. Run the fixed model against REST and SDK targets to isolate the integration path.
2. Hold adapter, endpoint, authentication, and request parameters constant while changing only model or
   deployment.
3. Repeat candidates in sequential runs (`maxParallel: 1`, and the same model/region/package versions)
   before using latency/TTFB as evidence.
4. Compare model/deployment IDs and prompt fingerprints in the reports, not only display labels.
5. Attach EU Data Zone and capacity evidence outside this tool's automatic functional checks.

```powershell
dotnet run --project .\src\ModelEvaluator.Cli -- `
  run --config .\config\evaluation.local.json --dry-run

dotnet run --project .\src\ModelEvaluator.Cli -- `
  run --config .\config\evaluation.local.json `
  --targets azure-gpt-5-1-rest,azure-newer-rest
```

The first command proves the runner locally with the live configuration shape. The second makes real
model calls and may incur provider charges.

## Report interpretation

The JSON report contains:

- exact model ID, deployment ID, provider/API kind, effective API version, requested/executed adapter,
  authentication kind, and SDK/framework package version;
- scenario version and prompt fingerprint, conversation/request hashes, and exact merged request
  parameters;
- request start/end timestamps, latency, TTFB when measurable, retry count, finish reason, and classified
  error/status;
- input/output/total, cached-input, and reasoning token counts when the service returns them;
- model tool calls, deterministic tool arguments/results, and multi-turn responses;
- independent technical, functional, EU Data Zone, and capacity states.

Missing measurements are JSON `null` or a named unavailable metric, never an implied zero. The human
Markdown report places targets side-by-side, lists failed checks and errors, and repeats the readiness
and missing-evidence warnings.

Timing is end-to-end around an individual adapter call. TTFB is measured by the two direct REST adapters
after response headers are received. The non-streaming high-level SDK paths report TTFB as unavailable.
Run timing comparisons sequentially; `maxParallel > 1` adds contention and throttling caveats to the report.

## Limitations

- This is a deterministic functional harness, not a clinical or medical safety certification.
- Passing synthetic scenarios does not prove production accuracy, policy currency, prompt safety, or
  suitability for real member data.
- It does not prove Microsoft Foundry/Azure model availability, EU Data Zone support, quota, provisioned
  throughput, or production capacity. Those conditions require separately recorded evidence.
- Copilot CLI results depend on the installed Copilot agent runtime, its system prompt, available client
  tools, and service-side routing. Hold the CLI version and environment constant for comparisons and use
  direct REST/SDK adapters when native API behavior is the decision variable.
- Live validation is deliberately not performed by the offline test suite. No paid model call or Azure
  deployment is required to build or test the repository.
- Chat Completions and Responses API provide separate comparable direct/SDK surfaces. Streaming, hosted
  tools, embeddings, and multimodal evaluation remain outside v1. Function calls with reasoning on
  GPT-6.1/6.2 must use Responses API.
- `Microsoft.Extensions.AI` 11.0.0 does not expose `reasoningEffort=minimal`; Semantic Kernel 1.82.0
  Chat Completions does not expose `reasoningEffort=none`. Those combinations are reported as
  `unsupported-parameter`, not silently rewritten to another effort level.
- `ReasoningEffortLevel` and the token-authentication constructor in `OpenAI` 2.15.0 are marked
  experimental by the package. v1 uses localized compiler acknowledgments for those two paths and still
  reports the exact SDK version; ordinary messages use the stable system/user/assistant/tool types.
- Endpoint routing and model availability change over time. Keep package versions, endpoint shape, API
  version, deployment IDs, evidence references, and prompt hashes in the report alongside results.

## Official sources

- [Official OpenAI .NET library and Chat Completions examples](https://github.com/openai/openai-dotnet#readme)
- [Azure OpenAI .NET SDK language support and v1 examples](https://learn.microsoft.com/azure/foundry/openai/supported-languages)
- [Azure OpenAI Chat Completions and model compatibility](https://learn.microsoft.com/azure/foundry/openai/how-to/chatgpt)
- [Structured outputs in .NET](https://learn.microsoft.com/azure/foundry/openai/how-to/structured-outputs?pivots=programming-language-csharp)
- [Use the Azure OpenAI Responses API](https://learn.microsoft.com/azure/foundry/openai/how-to/responses)
- [Azure Identity `DefaultAzureCredential`](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains)
- [Use the `IChatClient` interface](https://learn.microsoft.com/dotnet/ai/ichatclient)
- [Semantic Kernel chat completion concepts](https://learn.microsoft.com/semantic-kernel/concepts/ai-services/chat-completion)
- [GitHub Copilot CLI programmatic reference](https://docs.github.com/copilot/reference/copilot-cli-reference/cli-programmatic-reference)
- [Supported AI models in GitHub Copilot](https://docs.github.com/copilot/reference/ai-models/supported-models)

Draft PR #2 was inspected as prior art for adapter-factory and explicit-reporting patterns. Its code
generation benchmark is a different evaluation domain and was not copied into this implementation.
