#!/usr/bin/env bash
set -euo pipefail

THRESHOLD="${1:-10}"

echo "==> Checking cyclomatic complexity (threshold: $THRESHOLD)..."

if ! command -v lizard &>/dev/null; then
    echo "ERROR: 'lizard' not found. Install with: pip install lizard"
    exit 1
fi

lizard src/ \
    --language csharp \
    --CCN "$THRESHOLD" \
    --warnings_only \
    --length 1000

EXIT_CODE=$?
if [ $EXIT_CODE -ne 0 ]; then
    echo ""
    echo "ERROR: One or more methods exceed complexity threshold of $THRESHOLD."
    exit 1
fi

echo "Complexity check passed."
