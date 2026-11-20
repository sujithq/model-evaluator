# Task API

A minimal ASP.NET Core Web API that manages tasks in a SQLite database.

## Setup

```bash
dotnet restore
```

## Build

```bash
dotnet build --configuration Release
```

## Run

By default the database is created next to the executable in `tasks.db`. Set `TASKS_DB_PATH` to override.

```bash
dotnet run --project src/TaskApi
```

The service listens on the URL configured by `ASPNETCORE_URLS` (default `http://localhost:5000`).

### Sample requests

```bash
curl -s http://localhost:5000/health
curl -s -X POST http://localhost:5000/tasks -H 'content-type: application/json' -d '{"title":"Buy milk"}'
curl -s 'http://localhost:5000/tasks?status=pending&search=milk'
curl -s -X PUT http://localhost:5000/tasks/1 -H 'content-type: application/json' -d '{"title":"Buy milk","completed":true}'
curl -s -X DELETE http://localhost:5000/tasks/1
```

## Test

```bash
dotnet test --configuration Release
```

## Endpoints

| Method | Path            | Description                                         |
| ------ | --------------- | --------------------------------------------------- |
| GET    | `/health`       | Returns `ok` as plain text.                         |
| GET    | `/tasks`        | Lists tasks, filterable by `status` and `search`.   |
| GET    | `/tasks/{id}`   | Returns a single task or `404`.                     |
| POST   | `/tasks`        | Creates a task; returns `201` with `Location`.      |
| PUT    | `/tasks/{id}`   | Replaces `title` and `completed`; keeps `createdAt`.|
| DELETE | `/tasks/{id}`   | Removes a task; returns `204` or `404`.             |
