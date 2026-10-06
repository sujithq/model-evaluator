# Benchmark inputs (do not modify)

This directory is a benchmark workspace. The following files are supplied by the evaluator and must be kept
byte-for-byte identical:

- `global.json` - pins the .NET SDK version used for grading.
- `.editorconfig` - the formatting and analyzer configuration that `dotnet format` verifies.
- `BENCHMARK.md` - this file.
- Everything under `fixtures/` - the read-only job files the acceptance suite uses.

Build your solution around these files: add a solution file at the root, application code under `src/` and
xUnit tests under `tests/`. Your worker reads jobs from the directory pointed to by `JOBS_DIRECTORY`; the
evaluator will point that variable at a copy of `fixtures/jobs/` during grading, but you must not read
from `fixtures/` directly at run time and must not modify anything inside `fixtures/`.
