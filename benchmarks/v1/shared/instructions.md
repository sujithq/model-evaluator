# Shared instructions (benchmark v1)

You are building a complete .NET solution from scratch in the working directory you have been given.
The working directory already contains files that define the benchmark; treat them as read-only inputs.

1. Use C# and the .NET SDK and target framework pinned by the supplied `global.json` and stated in the
   scenario instructions. Reference only the dependencies the scenario lists as allowed.
2. Create a solution at the repository root, with application code under `src/` and tests under `tests/`.
3. Implement all required behaviour, including the documented validation and error handling.
4. Enable nullable reference types in every project and follow the supplied `.editorconfig` formatting and
   analyzer configuration. `dotnet format --verify-no-changes` must report no changes.
5. Add xUnit tests under `tests/` that cover successful operations, invalid inputs and the relevant boundary
   cases described by the scenario contract.
6. Run the prescribed commands and resolve any problems within the permitted execution budget:
   `dotnet restore`, `dotnet build --configuration Release`, `dotnet test --configuration Release`.
7. Include a `README.md` at the repository root with the exact setup, run and test commands for the solution.
8. Preserve the supplied requirements, fixtures and benchmark configuration exactly as provided
   (`global.json`, `.editorconfig`, `BENCHMARK.md` and everything under `fixtures/`). Do not edit, move or
   delete them.

Additional rules that apply to every scenario:

- The public contract in the scenario instructions is binding: names, signatures, output text, exit codes,
  HTTP status codes and file formats must match exactly.
- Do not add network calls, telemetry or credential usage. The generated solution must run offline.
- Do not write outside the working directory.
- Hidden acceptance tests owned by the evaluator will exercise the contract, including cases that are not
  listed as examples. Implement the contract, not only the examples.
