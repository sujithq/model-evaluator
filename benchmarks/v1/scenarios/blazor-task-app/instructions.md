# Scenario: Blazor task-management app

Build a Blazor Web App that manages a personal task list through a browser-facing task board and persists
the tasks as JSON on disk.

- Target framework: `net10.0`, pinned SDK `10.0.100` (see `global.json`).
- Application project: `src/TaskBoard/TaskBoard.csproj`, created from the Blazor Web App template
  (`dotnet new blazor -f net10.0`). The task pages must use **static server-side rendering** — no
  WebAssembly, and every user journey must work without JavaScript, driven by plain HTML `<form>` posts.
- Allowed dependencies: only the .NET/ASP.NET Core framework references for the application project. The
  test project may reference `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
  `coverlet.collector`, `bunit` and `Microsoft.AspNetCore.Mvc.Testing`. Do not add any other NuGet package.
- Layout: application project under `src/`, xUnit test project under `tests/`, solution file at the root.
- The application must run fully offline and must not read or write anything outside its working directory
  (the JSON store path is controlled by `TASKS_FILE`, see the contract).

Required user journeys: add, list, filter, complete, edit and delete tasks. The exact HTTP contract, HTML
markers, status codes, redirects, validation messages and storage format are defined in the contract below
and are graded literally.
