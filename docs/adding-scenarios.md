# Adding a scenario

A scenario is a self-contained, versioned package. Everything a model sees and everything used to
grade it lives inside that package.

## 1. Create the package

```text
benchmarks/v1/scenarios/<scenario-id>/
  scenario.json          Configuration consumed by the harness
  instructions.md        Project-specific instructions (given to the model)
  contracts.md           Exact public contracts and expected outputs (given to the model)
  starter/               Files copied into every fresh workspace
    global.json          SDK pin
    .editorconfig        Graded formatting and analyzer configuration
    BENCHMARK.md         Note telling the model these files must be preserved
    fixtures/            Optional fixture data
  acceptance/            Evaluator-owned xUnit project (never given to the model)
  samples/good/          Known-good reference implementation
  samples/broken-overlay/ Files copied over the good sample to create a failing variant
```

Copy [`console-task-cli`](../benchmarks/v1/scenarios/console-task-cli) as the template; it is the
reference implementation of every convention described here.

## 2. Write the prompt

The prompt supplied to a model is always
`benchmarks/v1/shared/instructions.md` + `instructions.md` + `contracts.md`, concatenated by
`ScenarioCatalog`. The shared file carries the eight rules that apply to every scenario (language,
SDK and target framework, `src/`+`tests/` layout, validation and error handling, nullable reference
types and formatting, xUnit tests, restore/build/test, README, preserve supplied files).

`contracts.md` must define the *exact* public contracts, expected outputs, dependency versions and
edge cases before any comparison is run. Anything the acceptance tests assert must be stated there;
anything left implicit is not gradeable.

## 3. Fill in `scenario.json`

```json
{
  "id": "<scenario-id>",
  "name": "Human readable name",
  "projectType": "Console | ClassLibrary | WebApi | Worker | Blazor",
  "benchmarkVersion": "1.0.0",
  "targetFramework": "net10.0",
  "sdkVersion": "10.0.100",
  "instructionsFile": "instructions.md",
  "contractsFile": "contracts.md",
  "starterDirectory": "starter",
  "acceptanceProject": "acceptance/<Name>.Acceptance.csproj",
  "allowedPackages": ["Microsoft.NET.Test.Sdk", "xunit", "xunit.runner.visualstudio", "coverlet.collector"],
  "requiredGlobs": ["*.sln", "src/**/*.csproj", "tests/**/*.csproj", "README.md"],
  "preservedPaths": ["global.json", ".editorconfig", "BENCHMARK.md"],
  "budget": {
    "generationTimeoutSeconds": 1800,
    "buildTimeoutSeconds": 600,
    "testTimeoutSeconds": 600,
    "acceptanceTimeoutSeconds": 900
  },
  "samples": {
    "good": { "path": "samples/good" },
    "broken": { "path": "samples/good", "overlay": "samples/broken-overlay" }
  }
}
```

`allowedPackages`, `requiredGlobs` and `preservedPaths` drive the automated instruction adherence
checks, so keep them consistent with what `instructions.md` demands.

## 4. Write evaluator-owned acceptance tests

The acceptance project is copied outside the model workspace before it runs, so it must not
reference the generated projects. Discover the generated artifacts through the environment variables
the runner sets:

| Variable | Meaning |
| --- | --- |
| `EVAL_WORKSPACE` | Root of the generated solution |
| `EVAL_TARGET_FRAMEWORK` | Target framework of the generated projects |
| `EVAL_FIXTURES` | Scenario fixtures directory |
| `EVAL_SCENARIO` | Scenario id |

Use the shared helpers in [`benchmarks/v1/acceptance-shared`](../benchmarks/v1/acceptance-shared):
`EvaluationContext` (locates the built assembly for a project), `AppRunner` (runs a console/worker
app and captures output and exit code) and `WebAppHost` (starts a web app on a free port and waits
for readiness). Reference them with the conditional include used by every shipped acceptance
project so the tests compile both in the repository layout and when copied next to
`acceptance-shared`:

```xml
<ItemGroup Condition="Exists('..\acceptance-shared')">
  <Compile Include="..\acceptance-shared\**\*.cs" />
</ItemGroup>
<ItemGroup Condition="!Exists('..\acceptance-shared')">
  <Compile Include="..\..\..\acceptance-shared\**\*.cs" />
</ItemGroup>
```

Include acceptance cases beyond the examples shown to the model - that is what distinguishes
adherence from memorisation.

## 5. Ship both sample variants

`samples/good` must pass every mandatory check, and `samples/broken-overlay` must break at least one
graded behaviour. `ScenarioCatalogTests` and the CI `reference-evaluation` job enforce this for
every scenario, which is how the harness validates itself.

Verify locally:

```bash
dotnet run --project src/ModelEvaluator.Cli -- validate
dotnet run --project src/ModelEvaluator.Cli -- evaluate --scenarios <scenario-id> --repetitions 1
```

`reference-good` must report `Success` and `reference-broken` must report `ModelFailure`.

## 6. Versioning

A benchmark package is immutable once comparisons are published. Any change to instructions,
contracts, fixtures, starter files, allowed packages or acceptance assertions requires a new
benchmark version: copy `benchmarks/v1` to `benchmarks/v2`, apply the change, bump
`benchmarkVersion` and point the configuration's `benchmarkRoot` at the new directory. Results from
different benchmark versions are not comparable, and both reports print the version and prompt hash
so mixing them is visible.
