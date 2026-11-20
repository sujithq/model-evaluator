# Durable Task Scheduler host

`ModelEvaluator.Durable` runs the existing evaluator through the portable Durable Task .NET SDK.
The existing `ModelEvaluator.Cli` remains available and both hosts reuse configuration, execution,
reporting, and adapter logic from `ModelEvaluator.Core`.

The durable host expands an evaluation into one activity for every
`scenario x model x repetition` attempt. It fans those activities out in batches bounded by the
existing `maxParallel` setting, waits for each batch, and then fans the results in to write the same
JSON and Markdown reports as the CLI. Any generic worker connected to the task hub can execute an
attempt; workers are not assigned to presets or models.

The scheduler persists orchestration state and lets clients disconnect and reconnect. Every
attempt has a stable logical id, and a redelivered activity reuses a completed `result.json` rather
than issuing another model request. External Copilot requests are not transactional, however, so a
worker failure after sending a request but before persisting its result can still consume credits
again.

## Local emulator

The local Durable Task Scheduler emulator uses plaintext gRPC on port `8080` and exposes its
dashboard on port `8082`. This repository defaults to:

```text
Endpoint=http://localhost:8080;TaskHub=default;Authentication=None
```

Override the connection with:

```powershell
$env:DURABLE_TASK_CONNECTION = "Endpoint=http://localhost:8080;TaskHub=default;Authentication=None"
```

Alternatively, set `ENDPOINT` and `TASKHUB` separately.

Start the durable worker in one terminal:

```powershell
dotnet run --project .\src\ModelEvaluator.Durable -- worker
```

Submit the no-credit local reference smoke evaluation from another terminal:

```powershell
dotnet run --project .\src\ModelEvaluator.Durable -- submit --preset smoke-reference
```

To submit without waiting:

```powershell
dotnet run --project .\src\ModelEvaluator.Durable -- submit --detach --preset smoke-reference
```

The command prints an orchestration instance id. Query it after either client or worker restarts:

```powershell
dotnet run --project .\src\ModelEvaluator.Durable -- status <instance-id>
```

Run the emulator integration test explicitly:

```powershell
$env:RUN_DURABLE_EMULATOR_TESTS = "1"
dotnet test .\tests\ModelEvaluator.Core.Tests\ModelEvaluator.Core.Tests.csproj --filter "FullyQualifiedName~DurableSchedulerIntegrationTests"
```

Without that environment variable, the emulator-dependent test returns without connecting so the
normal unit-test suite remains self-contained.

All arguments after `submit` (except `--detach`) use the existing evaluator option parser. For
example:

```powershell
dotnet run --project .\src\ModelEvaluator.Durable -- submit --preset hydrafusion --scenarios console-task-cli --repetitions 3 --max-parallel 1
```

That command consumes Copilot credits. Use `smoke-reference` when testing scheduler connectivity.

## Durability boundary

The scheduler durably tracks the run, each attempt activity, and final report aggregation.
Attempt artifacts are written through the same core runner as the CLI. Completed attempts are
idempotent when their artifact directory is available to a replacement worker. A crash during an
external model request remains an unavoidable at-least-once edge case because the model provider
and Durable Task Scheduler do not share a transaction.

## Azure

For Azure Durable Task Scheduler, set `DURABLE_TASK_CONNECTION` to the scheduler connection string
and use an authenticated mode supported by the Azure Managed Durable Task SDK. Host
`ModelEvaluator.Durable worker` as one or more continuously running Azure Container Apps. Persist
reports and attempt artifacts in shared durable storage before scaling beyond one worker, because
local container filesystems are not shared and completed-attempt reuse depends on those artifacts.

Official references:

* [Durable Task Scheduler](https://learn.microsoft.com/azure/durable-task/scheduler/durable-task-scheduler)
* [Portable Durable Task SDK quickstart](https://learn.microsoft.com/azure/durable-task/sdks/quickstart-portable-durable-task-sdks)
