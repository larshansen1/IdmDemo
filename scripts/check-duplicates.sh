#!/usr/bin/env bash
set -euo pipefail

THRESHOLD="${1:-5}"

echo "==> Checking for duplicated code (threshold: ${THRESHOLD}%)..."

if ! command -v jscpd &>/dev/null; then
    echo "ERROR: 'jscpd' not found. Install with: npm install -g jscpd"
    exit 1
fi

jscpd src/ \
    --ignore "**/obj/**,**/bin/**" \
    --min-lines 5 \
    --min-tokens 50 \
    --threshold "$THRESHOLD" \
    --reporters console \
    --exitCode

echo "Duplicate code check passed."
