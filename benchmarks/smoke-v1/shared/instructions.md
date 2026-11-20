# Smoke benchmark instructions

Work only inside the supplied workspace. This is a tiny edit, not a scaffolding task.
Use the existing .NET 10 solution and dependencies. Do not add packages, projects or features.
Preserve all supplied files except the implementation file identified by the scenario.
Do not use subagents, network calls or external services. Do not inspect evaluator files.
The evaluator runs restore, Release build, tests, formatting and hidden acceptance checks after
you finish. You do not need to run those commands yourself. Make the requested edit and stop.
