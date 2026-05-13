# .NET Project Template

A GitHub template repository for new .NET 8 projects with opinionated quality gates enforced via pre-commit hooks, a `Makefile`, and GitHub Actions CI.

## Quality gates

| Gate | Tool | Threshold |
|---|---|---|
| Test coverage | coverlet | ≥ 80% line coverage |
| Cyclomatic complexity | lizard | < 10 per method |
| Code formatting / lint | `dotnet format` + Roslyn analyzers | zero violations |
| Duplicate code | jscpd | < 5% |
| Security issues | SecurityCodeScan (Roslyn) + StyleCop | build-time, zero warnings |
| Hardcoded credentials | gitleaks | zero secrets detected |
| Vulnerable dependencies | `dotnet list package --vulnerable` | zero known CVEs |

---

## Using this template

### 1. Create a new repository from this template

Click **"Use this template"** on GitHub, or clone and re-init:

```bash
git clone https://github.com/your-org/dotnet-template my-project
cd my-project
git remote set-url origin https://github.com/your-org/my-project.git
```

### 2. Rename the sample project

> **Note:** .NET naming rules require PascalCase with no underscores for project and namespace names (e.g. `MyApp`, `Backend`, `OrderService`). The analyzers will reject names like `my_app` or `idm_demo`.

Run the rename script with your project name:

```bash
bash scripts/rename-project.sh MyApp
```

This renames directories, `.csproj` files, namespaces, solution references, and verifies the build in one step.

### 3. Install prerequisites

The quality gates rely on tools from three ecosystems. Install them before running `make`:

| Tool | Purpose | Install |
|---|---|---|
| `dotnet` 8+ | Build, test, format | [dot.net/download](https://dot.net/download) |
| `lizard` | Complexity check | `pipx install lizard` |
| `jscpd` | Duplicate detection | `npm install -g jscpd` |
| `gitleaks` | Secret scanning | `brew install gitleaks` / [releases](https://github.com/gitleaks/gitleaks/releases) |
| `pre-commit` | Git hooks | `pipx install pre-commit` |

> **pipx vs pip:** Use `pipx` for CLI tools (`lizard`, `pre-commit`) — it installs each tool in its own isolated virtualenv so they don't pollute your global Python environment. Install pipx with `pip install pipx` if you don't have it.

Then run:

```bash
make install-tools   # installs .NET local tools and activates pre-commit hooks
```

---

## Daily workflow

### Run all quality checks

```bash
make check
```

### Individual targets

```bash
make build           # Build the solution (warnings as errors)
make test            # Run tests
make coverage        # Run tests + enforce 80% line coverage
make coverage-report # Generate HTML coverage report in coverage/html/
make lint            # Fail if code is not formatted
make format          # Auto-format code
make complexity      # Fail if any method CCN ≥ 10
make duplicates      # Fail if duplicate code exceeds 5%
make security        # Run Roslyn security analyzers
make secrets         # Scan for hardcoded credentials (gitleaks)
make vulnerabilities # Fail if any package has a known CVE
make clean           # Delete bin/, obj/, coverage/, TestResults/
```

### Verify the template itself

```bash
make test-template
```

Runs every check against the sample code and prints a pass/fail/skip summary.

---

## Pre-commit hooks

Hooks run automatically on `git commit`. They check:

1. Trailing whitespace / end-of-file (pre-commit standard hooks)
2. YAML validity
3. Hardcoded credentials (`gitleaks`)
4. .NET code formatting (`dotnet format --verify-no-changes`)
5. Build succeeds (`dotnet build -warnaserror`)
6. Tests pass and coverage ≥ 80%
7. No vulnerable NuGet packages

Skip hooks only in emergencies:

```bash
git commit --no-verify   # not recommended
```

---

## Repository structure

```
.
├── src/
│   └── Template.Core/          # Production code — rename this
├── tests/
│   └── Template.Core.Tests/    # xUnit tests — rename this
├── scripts/
│   ├── check-complexity.sh
│   ├── check-duplicates.sh
│   ├── check-vulnerabilities.sh
│   └── test-template.sh        # End-to-end template self-test
├── .github/
│   ├── workflows/ci.yml        # GitHub Actions pipeline
│   └── dependabot.yml          # Automated dependency updates
├── .config/
│   └── dotnet-tools.json       # Pinned .NET local tools
├── .editorconfig               # Formatting rules
├── .pre-commit-config.yaml     # Git pre-commit hooks
├── .gitleaks.toml              # gitleaks rules and allowlist
├── Directory.Build.props       # Shared MSBuild properties + analyzers
├── global.json                 # Pinned .NET SDK version
├── Makefile
└── Template.sln
```

---

## Adjusting thresholds

Edit the top of `Makefile`:

```makefile
COVERAGE_THRESHOLD   := 80   # minimum % line coverage
COMPLEXITY_THRESHOLD := 10   # maximum cyclomatic complexity per method
DUPLICATE_THRESHOLD  := 5    # maximum % duplicate code allowed
```

For the pre-commit coverage hook, update the `Threshold=80` value in `.pre-commit-config.yaml`.

---

## CI/CD

GitHub Actions runs on every push and pull request to `main` (see `.github/workflows/ci.yml`). It runs the same gates as `make check`.

Dependabot automatically opens PRs for outdated NuGet packages and GitHub Actions weekly.

---

## Tuning the secrets scan

Gitleaks inherits 100+ built-in rules (AWS keys, GitHub tokens, connection strings, etc.) via `useDefault = true` in `.gitleaks.toml`. If a legitimate value triggers a false positive:

```toml
[allowlist]
description = "Global allowlist"
regexes = [
    # Suppress a specific pattern
    '''EXAMPLE_PLACEHOLDER_[A-Z0-9]{20}''',
]
paths = [
    # Suppress an entire file (use sparingly — prefer environment variables)
    "tests/Fixtures/fake-credentials.json",
]
```

Never suppress a real secret — move it to an environment variable or secret manager instead.

---

## Adding security scanning (optional hardening)

For deeper SAST beyond the built-in Roslyn analyzers, add [SecurityCodeScan](https://security-code-scan.github.io/) as a NuGet analyzer in `Directory.Build.props`:

```xml
<PackageReference Include="SecurityCodeScan.VS2019" Version="5.6.7">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

Or integrate [Snyk](https://snyk.io) / [GitHub Advanced Security](https://docs.github.com/en/code-security) for supply-chain and secret scanning.
