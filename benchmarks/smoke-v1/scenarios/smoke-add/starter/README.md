# Smoke addition

Setup: `dotnet restore`

Build: `dotnet build --configuration Release --no-restore`

Run tests: `dotnet test --configuration Release --no-build`

Format check: `dotnet format --verify-no-changes --no-restore`

This library exposes `Smoke.Calculator.Add(int, int)`. There is no executable.
The tests are supplied by the benchmark, not authored by the evaluated model.
