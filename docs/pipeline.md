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
- **`deploy-production`** only rebuilds and pushes containers when `src/`, `Directory.Build.props`, or `global.json` changed. Commits that only touch docs, scripts, or CI config skip the Docker build entirely. Layer cache is persisted to `/tmp/.buildx-cache` on the self-hosted runner between runs.
- **Production MCP smoke** runs after a successful production deploy. It uses the persistent low-privilege `idm-mcp-smoke` client, requests `idm.mcp.read` for the `idm-demo-mcp` audience, and fails the deploy job if public discovery, DPoP token issuance, token claims, MCP `initialize`, `tools/list`, or a read tool call fails.
- **`make check`** mirrors the full CI pipeline for a local pre-flight run.

## Production MCP Smoke Prerequisite

Bootstrap or rotate the persistent smoke identity manually before relying on
production deploys. Run `scripts/bootstrap-mcp-production-smoke.sh --apply` from
the deployment host with the backend/admin credential, then store the resulting
smoke client PEM as the GitHub secret
`MCP_PRODUCTION_SMOKE_CLIENT_CERT_PEM`.

The deploy workflow materializes that secret into a temporary runner file and
runs:

```bash
SMOKE_CLIENT_ID=idm-mcp-smoke \
SMOKE_CERT_PATH="$RUNNER_TEMP/idmdemo-prod-mcp-smoke-client.pem" \
MCP_REMOTE_SCOPE=idm.mcp.read \
AUTH_BASE_URL=https://auth.idp.madmetal.org \
AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org \
MCP_BASE_URL=https://mcp.idp.madmetal.org \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-remote-production-smoke.sh -v
```

Deploy does not create or update the smoke client. Missing, mismatched, or
under-scoped smoke certificate material should fail the deploy job with the
remote smoke script's diagnostics.

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
