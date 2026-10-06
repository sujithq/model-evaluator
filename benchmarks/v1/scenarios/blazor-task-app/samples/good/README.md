# TaskBoard

A Blazor Web App that manages a personal task list. Pages render with static server-side rendering; every
user journey works with plain HTML form posts (no JavaScript required).

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
dotnet run --project src/TaskBoard
```

Then browse to `/tasks`. Set the environment variable `TASKS_FILE` to control the location of the JSON
store; when unset the app uses `tasks.json` in the content root.

## Test

```bash
dotnet test --configuration Release
```
