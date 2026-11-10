# Contributing

## Projects

The solution contains seven projects:

| Project | Responsibility |
| --- | --- |
| `Tuck.Mod` | Game capture, solver adapter, plan execution, controls and Debug recording. |
| `Tuck.Mod.Tests` | Game-boundary and execution tests using explicit plans, plus Debug recording tests. No solver discovery or optimization assertions. |
| `Tuck.PackSolver` | Detached packing contracts, derivation, replay and solver implementations. |
| `Tuck.PackSolver.Tests` | All solver correctness, search, policy and replay tests, including scenarios imported from recordings. |
| `Tuck.PackSolver.Benchmarks` | Solver performance and differential workloads. |
| `Tuck.ProbablyStolenTestModel` | Independent, headless game inventory model for test fixtures. No solver or mod dependency. |
| `Tuck.ProbablyStolenTestModel.Tests` | Model behavior and optional parity checks against fetched game assemblies. |
