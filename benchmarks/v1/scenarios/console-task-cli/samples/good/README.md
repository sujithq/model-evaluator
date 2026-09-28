# Task CLI

A console application that manages a task list stored as JSON in `tasks.json` in the current working
directory.

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
dotnet run --project src/TaskCli -- add "Buy milk"
dotnet run --project src/TaskCli -- list --status pending
dotnet run --project src/TaskCli -- complete 1
dotnet run --project src/TaskCli -- delete 1
```

## Test

```bash
dotnet test --configuration Release
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | Usage error or unknown command |
| 2 | Validation error |
| 3 | Task not found |
| 4 | Corrupt task store |
