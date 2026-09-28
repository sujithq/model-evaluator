# Scenario: worker job processor

Build a .NET Worker Service that processes job files from a local directory, retries transient failures and
shuts down gracefully.

- Target framework: `net9.0`, pinned SDK `9.0.100` (see `global.json`).
- Project type: Worker Service (`Microsoft.NET.Sdk.Worker`) with a `BackgroundService` implementation.
- Allowed dependencies: `Microsoft.Extensions.Hosting` (and, if referenced explicitly,
  `Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`) for the
  worker project. The test project may reference `Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio` and `coverlet.collector`. Use `System.Text.Json` for JSON. Do not add any
  other NuGet package.
- Layout: solution file at the root, worker project under `src/JobWorker/`, xUnit test project under
  `tests/JobWorker.Tests/`.
- The application must run fully offline and must not read or write anything outside the directories
  supplied through environment variables.

Configuration is provided at start-up through environment variables:

| Variable | Required | Default | Meaning |
| --- | --- | --- | --- |
| `JOBS_DIRECTORY` | yes | – | Directory to read `*.json` job files from. |
| `OUTPUT_DIRECTORY` | yes | – | Directory to write `results.json` to. Created if missing. |
| `MAX_ATTEMPTS` | no | `3` | Maximum processing attempts per job. Must be a positive integer. |
| `RETRY_DELAY_MS` | no | `50` | Delay in milliseconds between retries. Must be a non-negative integer. |

The exact behaviour, output text, exit codes, log lines and `results.json` shape are defined in the
contract below and are graded literally.
