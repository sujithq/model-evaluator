# model-evaluator

`model-evaluator` runs repeatable .NET benchmark tasks and controlled API/SDK comparisons for AI
models. It records the exact prompt, model configuration, runner, environment, budget, checks, and
result artifacts for every attempt.

<div class="grid cards" markdown>

-   :material-rocket-launch: **Start with a safe local run**

    ---

    Use the reference implementations to verify the harness without making AI provider requests.

    [Open the usage guide](usage-guide.md)

-   :material-package-variant: **Understand the benchmark packages**

    ---

    See what a scenario contains, how versions work, and how benchmark evidence is produced.

    [Browse benchmark suites](benchmarks/index.md)

-   :material-tune: **Configure a comparison**

    ---

    Choose a packaged preset, create a caller-owned configuration, or connect an external runner.

    [Read the configuration reference](configuration/index.md)

-   :material-chart-box-outline: **Interpret the evidence**

    ---

    Learn how outcomes, acceptance accuracy, rankings, usage, and infrastructure failures are
    represented.

    [Read the results guide](interpreting-results.md)

</div>

## Run the documentation site locally

=== "PowerShell"

    ```powershell
    py -m venv .venv
    .\.venv\Scripts\python.exe -m pip install `
      --index-url https://packagefeedproxy.microsoft.io/pypi/simple `
      --requirement requirements-docs.txt
    .\.venv\Scripts\python.exe -m mkdocs serve
    ```

=== "Bash"

    ```bash
    python3 -m venv .venv
    ./.venv/bin/python -m pip install \
      --index-url https://packagefeedproxy.microsoft.io/pypi/simple \
      --requirement requirements-docs.txt
    ./.venv/bin/python -m mkdocs serve
    ```

Then open `http://127.0.0.1:8000/`. The development server rebuilds the site when Markdown or the
MkDocs configuration changes.

To verify every page and internal link before publishing:

=== "PowerShell"

    ```powershell
    .\.venv\Scripts\python.exe -m mkdocs build --strict
    ```

=== "Bash"

    ```bash
    ./.venv/bin/python -m mkdocs build --strict
    ```

## Documentation model

The site contains curated documentation. The repository remains the source of truth for executable
and generated content:

- [Benchmark definitions and acceptance tests](https://github.com/sujithq/model-evaluator/tree/main/benchmarks)
  are documented here, not copied page-for-page.
- [Evaluation configurations](https://github.com/sujithq/model-evaluator/tree/main/config) remain
  JSON so the CLI and the installed tool consume exactly the same files.
- [Source code](https://github.com/sujithq/model-evaluator/tree/main/src) keeps implementation-level
  details, while the [architecture guide](development/architecture.md) explains its boundaries and
  extension points.

This keeps the site useful for people while avoiding a second copy of benchmark contracts,
configuration, or C# implementation.
