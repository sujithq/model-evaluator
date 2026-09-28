# Scenario: pricing class library

Build a .NET class library that computes an itemised price breakdown for a line item from a unit price,
a quantity and a tax rate. The library must apply the documented quantity discount tiers, tax rate and
rounding rules exactly.

- Target framework: `net10.0`, pinned SDK `10.0.100` (see `global.json`).
- Allowed dependencies: the library project may reference only the .NET base class library. The test
  project may reference only `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` and
  `coverlet.collector`. Do not add any other NuGet package.
- Layout: library project under `src/Pricing/`, xUnit test project under `tests/`, solution file at the
  root, `README.md` at the root.
- The library assembly name and root namespace are both `Pricing`; the project file is
  `src/Pricing/Pricing.csproj`.
- The library must be pure code: no I/O, no static state, no threading, no network.

The public API, discount tiers, rounding rules and validation are defined in the contract below and are
graded literally.
