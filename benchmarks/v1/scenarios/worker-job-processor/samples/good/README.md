# Job Worker

A .NET Worker Service that reads JSON job files from `JOBS_DIRECTORY`, retries transient failures and
writes a summary to `OUTPUT_DIRECTORY/results.json`. Configuration is provided through environment
variables:

| Variable | Required | Default |
| --- | --- | --- |
| `JOBS_DIRECTORY` | yes | – |
| `OUTPUT_DIRECTORY` | yes | – |
| `MAX_ATTEMPTS` | no | `3` |
| `RETRY_DELAY_MS` | no | `50` |

## Setup

```bash
dotnet restore
```

## Build

```bash
dotnet build --configuration Release
```

## Run

```bash
JOBS_DIRECTORY=./jobs OUTPUT_DIRECTORY=./out \
    dotnet run --project src/JobWorker --configuration Release
```

The worker processes every `*.json` file in `JOBS_DIRECTORY` in ordinal file-name order, writes
`OUTPUT_DIRECTORY/results.json` after each job, and exits with code `0` when all jobs are done. On
`SIGINT`/`SIGTERM` it stops within 10 seconds after flushing the results collected so far.

## Test

```bash
dotnet test --configuration Release
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | All jobs processed (or shutdown handled gracefully). |
| 2 | Configuration error (missing/invalid environment variable). |
