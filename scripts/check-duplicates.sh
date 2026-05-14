#!/usr/bin/env bash
set -euo pipefail

THRESHOLD="${1:-5}"

echo "==> Checking for duplicated code (threshold: ${THRESHOLD}%)..."

if ! command -v jscpd &>/dev/null; then
    echo "ERROR: 'jscpd' not found. Install with: npm install -g jscpd"
    exit 1
fi

OUTPUT=$(jscpd src/ \
    --ignore "**/obj/**,**/bin/**,**/Migrations/**" \
    --min-lines 5 \
    --min-tokens 50 \
    --threshold "$THRESHOLD" \
    --reporters console 2>&1)

echo "$OUTPUT"

if echo "$OUTPUT" | grep -q "too many duplicates"; then
    exit 1
fi

echo "Duplicate code check passed."
