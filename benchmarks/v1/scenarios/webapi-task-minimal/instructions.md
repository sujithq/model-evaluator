# Scenario: minimal Web API task manager

Build a .NET web application that exposes a task-management HTTP API using ASP.NET Core Minimal APIs and
persists tasks in a SQLite database file.

- Target framework: `net10.0`, pinned SDK `10.0.100` (see `global.json`).
- Application project SDK: `Microsoft.NET.Sdk.Web`, located at `src/TaskApi/TaskApi.csproj`.
- Allowed dependencies for the application project: only the ASP.NET Core shared framework and, for SQLite
  access, `Microsoft.Data.Sqlite` (recommended) or, alternatively, the trio
  `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite` and
  `Microsoft.EntityFrameworkCore.Design`. Do not add any other NuGet package to the application project.
- Allowed dependencies for the test project: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
  `coverlet.collector` and `Microsoft.AspNetCore.Mvc.Testing`.
- Layout: application project under `src/`, xUnit test project under `tests/`, solution file at the root.
- The application must run fully offline. It must not open any socket other than the HTTP listener configured
  by ASP.NET Core, and must not read or write anything outside its content root apart from the SQLite
  database file described below.

The database file path is taken from the environment variable `TASKS_DB_PATH`. When the variable is unset or
empty, the application uses `tasks.db` in the content root. The schema is created automatically on startup
if it does not already exist. Tasks must survive an application restart against the same database file.

The complete HTTP contract, JSON shape, status codes and validation rules are defined in the contract below
and are graded literally.
