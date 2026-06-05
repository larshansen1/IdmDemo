# Quality Pipeline

## Layer inventory

| Check | Pre-commit | CI — build-and-test | CI — static-analysis | `make check` |
|---|---|---|---|---|
| Whitespace / EOF / YAML / merge conflict | ✓ | — | — | — |
| `dotnet-format` (verify) | ✓ | ✓ | — | ✓ (lint) |
| `dotnet build` + Roslyn analyzers | ✓ | ✓ | — | ✓ |
| Test + coverage (changed assemblies, unit only) | ✓ | — | — | — |
| Test + coverage (full suite, 80% threshold) | — | ✓ | — | ✓ (coverage) |
| Secrets — gitleaks | ✓ | ✓ | — | ✓ |
| Vulnerabilities — NuGet | ✓ | ✓ | — | ✓ |
| Complexity — lizard (CCN < 10) | — | — | ✓ | ✓ |
| Duplicates — jscpd (< 5%) | — | — | ✓ | ✓ |
| Dependency caching | — | ✓ | ✓ | n/a |

## Design principles

- **Pre-commit** gives fast author-side feedback: formatting, build errors, accidental secrets, and coverage regressions scoped to the assemblies you actually changed (unit tests only; integration tests and `Backend.Infrastructure` excluded). Skips entirely on non-source commits.
- **CI `build-and-test`** is the mandatory PR gate for correctness and security: full build with Roslyn analyzers, full test suite with 80% coverage, secrets scan, and vulnerability check.
- **CI `static-analysis`** runs in parallel with `build-and-test` and gates deployment on code health: cyclomatic complexity and duplicate code detection. Both jobs must pass before `deploy-production` triggers.
- **`make check`** mirrors the full CI pipeline for a local pre-flight run.

## Pre-commit coverage scope

`scripts/check-coverage-changed.sh` runs only when `.cs` source files are staged. It:

1. Identifies which source assemblies have staged changes.
2. Skips `Backend.Infrastructure` (exempt from the coverage gate, mirrors CI).
3. Runs `Backend.Tests` only — integration tests are excluded from pre-commit.
4. Enforces 80% line coverage threshold scoped to the changed assemblies via Coverlet's `Include` filter.

Assembly → project mapping:

| Staged path prefix | Assembly measured |
|---|---|
| `src/Backend.Api/` | `Backend.Api` |
| `src/Backend.Application/` | `Backend.Application` |
| `src/Backend.Domain/` | `Backend.Domain` |
| `src/Backend.Mcp/` | `Backend.Mcp` (excl. `Program`) |
| `src/Backend.Infrastructure/` | skipped (exempt) |

## CI caching

| Cache | Key | Covers |
|---|---|---|
| NuGet packages | `nuget-{os}-{hash of *.csproj + global.json}` | `dotnet restore` |
| .NET local tools | `dotnet-tools-{hash of dotnet-tools.json}` | `dotnet tool restore` |
| pip packages | `pip-{os}-lizard` | `pip install lizard` |
| npm download cache | `npm-{os}-jscpd` | `npm install -g jscpd` |
| gitleaks binary | `gitleaks-8.21.2-linux-x64` | `curl` install |
