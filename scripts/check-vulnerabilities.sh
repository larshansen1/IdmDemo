#!/usr/bin/env bash
set -euo pipefail

echo "==> Checking for vulnerable NuGet packages..."

OUTPUT=$(dotnet list package --vulnerable --include-transitive 2>&1)
echo "$OUTPUT"

if echo "$OUTPUT" | grep -q "has the following vulnerable packages"; then
    echo ""
    echo "ERROR: Vulnerable packages detected. Please update or replace them."
    exit 1
fi

echo "No vulnerable packages found."
