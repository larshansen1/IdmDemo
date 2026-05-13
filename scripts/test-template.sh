#!/usr/bin/env bash
# Verifies that all Makefile quality gates pass on this template's own code.
set -euo pipefail

PASS=0
FAIL=0
SKIP=0

pass() { echo "  [PASS] $1"; PASS=$((PASS + 1)); }
fail() { echo "  [FAIL] $1"; FAIL=$((FAIL + 1)); }
skip() { echo "  [SKIP] $1 — $2"; SKIP=$((SKIP + 1)); }

require_cmd() {
    command -v "$1" >/dev/null 2>&1
}

echo "========================================"
echo "  Template self-test"
echo "========================================"
echo ""

# ── Build ─────────────────────────────────────────────────────────────────────
echo "--- Build ---"
if dotnet build --no-incremental -warnaserror -v quiet 2>&1; then
    pass "dotnet build"
else
    fail "dotnet build"
fi

# ── Lint ──────────────────────────────────────────────────────────────────────
echo "--- Lint ---"
if dotnet format --verify-no-changes 2>&1; then
    pass "dotnet format"
else
    fail "dotnet format (run 'make format' to fix)"
fi

# ── Tests & Coverage ──────────────────────────────────────────────────────────
echo "--- Tests & Coverage ---"
if dotnet test --no-build \
        /p:CollectCoverage=true \
        /p:CoverletOutputFormat=cobertura \
        /p:Threshold=80 \
        /p:ThresholdType=line \
        /p:ThresholdStat=Total \
        -v quiet 2>&1; then
    pass "dotnet test (coverage ≥ 80%)"
else
    fail "dotnet test / coverage threshold"
fi

# ── Complexity ────────────────────────────────────────────────────────────────
echo "--- Complexity ---"
if require_cmd lizard; then
    if bash scripts/check-complexity.sh 10; then
        pass "complexity (CCN < 10)"
    else
        fail "complexity"
    fi
else
    skip "complexity" "lizard not installed (pip install lizard)"
fi

# ── Duplicates ────────────────────────────────────────────────────────────────
echo "--- Duplicates ---"
if require_cmd jscpd; then
    if bash scripts/check-duplicates.sh 5; then
        pass "duplicates (< 5%)"
    else
        fail "duplicates"
    fi
else
    skip "duplicates" "jscpd not installed (npm install -g jscpd)"
fi

# ── Secrets ───────────────────────────────────────────────────────────────────
echo "--- Secrets ---"
if require_cmd gitleaks; then
    if bash scripts/check-secrets.sh; then
        pass "secret scan"
    else
        fail "secret scan"
    fi
else
    skip "secret scan" "gitleaks not installed (see https://github.com/gitleaks/gitleaks#installing)"
fi

# ── Vulnerabilities ───────────────────────────────────────────────────────────
echo "--- Vulnerabilities ---"
if bash scripts/check-vulnerabilities.sh; then
    pass "vulnerability scan"
else
    fail "vulnerability scan"
fi

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "========================================"
echo "  Results: $PASS passed, $FAIL failed, $SKIP skipped"
echo "========================================"

if [ $FAIL -gt 0 ]; then
    exit 1
fi
