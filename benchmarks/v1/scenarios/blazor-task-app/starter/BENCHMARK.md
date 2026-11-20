# Benchmark inputs (do not modify)

This directory is a benchmark workspace. The following files are supplied by the evaluator and must be kept
byte-for-byte identical:

- `global.json` - pins the .NET SDK version used for grading.
- `.editorconfig` - the formatting and analyzer configuration that `dotnet format` verifies.
- `BENCHMARK.md` - this file.
- everything under `fixtures/` when the scenario provides fixtures.

Build your solution around these files: add a solution file at the root, application code under `src/` and
xUnit tests under `tests/`.
