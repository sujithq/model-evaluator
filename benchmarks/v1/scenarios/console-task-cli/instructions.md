# Scenario: console task-management CLI

Build a .NET console application that manages a personal task list from the command line and persists the
tasks as JSON.

- Target framework: `net10.0`, pinned SDK `10.0.100` (see `global.json`).
- Allowed dependencies: only the .NET base class library for the application project, and
  `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` and `coverlet.collector` for the test project.
  Use `System.Text.Json` for persistence; do not add any other NuGet package.
- Layout: application project under `src/`, xUnit test project under `tests/`, solution file at the root.
- The application must run fully offline and must not read or write anything outside its working directory.

Required commands: `add`, `list`, `complete`, `delete`. The exact behaviour, output text, exit codes and
storage format are defined in the contract below and are graded literally.
