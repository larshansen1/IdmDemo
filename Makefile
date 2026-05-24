COVERAGE_THRESHOLD   := 80
COMPLEXITY_THRESHOLD := 10
DUPLICATE_THRESHOLD  := 5

COVERAGE_DIR  := coverage
RESULTS_DIR   := TestResults
COVERAGE_EXCLUDE := [Backend.Infrastructure]*%2c[Backend.Mcp]Program*

.PHONY: all check build test coverage lint format complexity duplicates \
        security secrets vulnerabilities install-tools _install-gitleaks \
        clean test-template help

## Run all quality checks (default target)
all: check

check: build lint test coverage complexity duplicates security secrets vulnerabilities
	@echo ""
	@echo "✔  All quality checks passed."

# ── Build ────────────────────────────────────────────────────────────────────

build:
	@echo "==> Building solution..."
	dotnet build --no-incremental -warnaserror

# ── Tests & Coverage ─────────────────────────────────────────────────────────

test:
	@echo "==> Running tests..."
	dotnet test \
		--no-build \
		--logger "trx;LogFileName=results.trx" \
		--results-directory $(RESULTS_DIR)

coverage:
	@echo "==> Running tests with coverage (threshold: $(COVERAGE_THRESHOLD)%)..."
	dotnet test \
		--no-build \
		/p:CollectCoverage=true \
		/p:CoverletOutput=./$(COVERAGE_DIR)/ \
		/p:CoverletOutputFormat=cobertura \
		/p:Exclude="$(COVERAGE_EXCLUDE)" \
		/p:Threshold=$(COVERAGE_THRESHOLD) \
		/p:ThresholdType=line \
		/p:ThresholdStat=Total
	@echo "Coverage report: $(COVERAGE_DIR)/coverage.cobertura.xml"

coverage-report: coverage
	@echo "==> Generating HTML coverage report..."
	dotnet tool run reportgenerator \
		-reports:$(COVERAGE_DIR)/coverage.cobertura.xml \
		-targetdir:$(COVERAGE_DIR)/html \
		-reporttypes:Html
	@echo "Open $(COVERAGE_DIR)/html/index.html to view the report."

# ── Formatting & Linting ─────────────────────────────────────────────────────

lint:
	@echo "==> Checking code formatting..."
	dotnet format --verify-no-changes

format:
	@echo "==> Formatting code..."
	dotnet format

# ── Complexity ───────────────────────────────────────────────────────────────

complexity:
	@bash scripts/check-complexity.sh $(COMPLEXITY_THRESHOLD)

# ── Duplicate Code ───────────────────────────────────────────────────────────

duplicates:
	@bash scripts/check-duplicates.sh $(DUPLICATE_THRESHOLD)

# ── Security ─────────────────────────────────────────────────────────────────

security:
	@echo "==> Running security analysis (Roslyn analyzers)..."
	dotnet build --no-incremental -warnaserror /p:RunAnalyzers=true
	@echo "Security analysis passed."

vulnerabilities:
	@bash scripts/check-vulnerabilities.sh

# ── Secrets / Credentials ────────────────────────────────────────────────────

secrets:
	@bash scripts/check-secrets.sh

# ── Tooling ──────────────────────────────────────────────────────────────────

install-tools:
	@echo "==> Installing .NET local tools..."
	dotnet tool restore
	@echo "==> Checking Python tools..."
	@command -v lizard >/dev/null 2>&1 || pipx install lizard
	@echo "==> Checking Node tools..."
	@command -v jscpd >/dev/null 2>&1 || npm install -g jscpd
	@echo "==> Checking gitleaks..."
	@command -v gitleaks >/dev/null 2>&1 || $(MAKE) _install-gitleaks	@echo "==> Installing pre-commit..."

	@command -v pre-commit >/dev/null 2>&1 || pipx install pre-commit
	pre-commit install
	@echo "All tools installed."

_install-gitleaks:
	@echo "==> Installing gitleaks..."
	@GITLEAKS_VERSION=8.21.2; \
	OS=$$(uname -s | tr '[:upper:]' '[:lower:]'); \
	ARCH=$$(uname -m); \
	case "$$ARCH" in \
		x86_64)  ARCH=x64 ;; \
		aarch64) ARCH=arm64 ;; \
		arm64)   ARCH=arm64 ;; \
		*) echo "ERROR: Unsupported architecture: $$ARCH"; exit 1 ;; \
	esac; \
	if [ "$$OS" = "darwin" ]; then \
		command -v brew >/dev/null 2>&1 && brew install gitleaks || { \
			echo "ERROR: brew not found. Install Homebrew or download gitleaks manually."; exit 1; \
		}; \
	elif [ "$$OS" = "linux" ]; then \
		curl -sSfL "https://github.com/gitleaks/gitleaks/releases/download/v$${GITLEAKS_VERSION}/gitleaks_$${GITLEAKS_VERSION}_$${OS}_$${ARCH}.tar.gz" \
			| sudo tar -xz -C /usr/local/bin gitleaks; \
	else \
		echo "ERROR: Unsupported OS: $$OS. Download gitleaks from https://github.com/gitleaks/gitleaks/releases"; \
		exit 1; \
	fi
	@echo "gitleaks installed: $$(gitleaks version)"

# ── Template Self-Test ───────────────────────────────────────────────────────

test-template:
	@echo "==> Running template self-test..."
	@bash scripts/test-template.sh

# ── Clean ────────────────────────────────────────────────────────────────────

clean:
	@echo "==> Cleaning build artifacts..."
	find . -type d \( -name bin -o -name obj \) -not -path './.git/*' -exec rm -rf {} + 2>/dev/null || true
	rm -rf $(COVERAGE_DIR) $(RESULTS_DIR)
	@echo "Clean complete."

# ── Help ─────────────────────────────────────────────────────────────────────

help:
	@echo "Usage: make <target>"
	@echo ""
	@echo "Quality checks:"
	@echo "  check          Run all quality gates"
	@echo "  build          Build the solution"
	@echo "  test           Run tests"
	@echo "  coverage       Run tests and enforce $(COVERAGE_THRESHOLD)% line coverage"
	@echo "  coverage-report Generate HTML coverage report"
	@echo "  lint           Check formatting (fail if not formatted)"
	@echo "  format         Auto-format code"
	@echo "  complexity     Check cyclomatic complexity (<$(COMPLEXITY_THRESHOLD))"
	@echo "  duplicates     Check for duplicated code blocks (<$(DUPLICATE_THRESHOLD)%)"
	@echo "  security       Run Roslyn security analyzers"
	@echo "  secrets        Scan for hardcoded credentials (gitleaks)"
	@echo "  vulnerabilities Check for vulnerable NuGet packages"
	@echo ""
	@echo "Utilities:"
	@echo "  install-tools  Install all required external tools"
	@echo "  test-template  Verify the template itself works end-to-end"
	@echo "  clean          Remove build and test artifacts"
	@echo "  help           Show this help"
