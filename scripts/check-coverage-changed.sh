#!/usr/bin/env bash
set -euo pipefail

# Get staged source .cs files covered by the unit-test coverage gate.
STAGED=$(git diff --cached --name-only --diff-filter=ACMR 2>/dev/null \
  | grep -E '^src/.*\.cs$' \
  | grep -vE '/(obj|bin)/' \
  | grep -vE '^src/Backend.Api/' \
  | grep -vE '^src/Backend.Mcp/Program\.cs$' || true)

if [ -z "$STAGED" ]; then
  echo "No staged source .cs files — skipping coverage check."
  exit 0
fi

# Map file paths to assembly names.
# Backend.Api and Backend.Infrastructure are covered by integration tests.
# This hook gates assemblies covered by Backend.Tests.
ASSEMBLIES=$(while IFS= read -r FILE; do
  if [[ "$FILE" == src/Backend.Application/* ]]; then
    echo "Backend.Application"
  elif [[ "$FILE" == src/Backend.Idp.Domain/* ]]; then
    echo "Backend.Idp.Domain"
  elif [[ "$FILE" == src/Backend.As.Domain/* ]]; then
    echo "Backend.As.Domain"
  elif [[ "$FILE" == src/Backend.Mcp/* ]]; then
    echo "Backend.Mcp"
  fi
done <<< "$STAGED" | sort -u)

if [ -z "$ASSEMBLIES" ]; then
  echo "Only coverage-exempt assemblies changed — skipping."
  exit 0
fi

# Build MSBuild Include filter; commas must be URL-encoded (%2c) on the CLI.
INCLUDE=""
while IFS= read -r FILE; do
  INCLUDE="${INCLUDE:+${INCLUDE}%2c}[${FILE}]*"
done <<< "$ASSEMBLIES"

echo "==> Coverage check for changed assemblies: $(tr '\n' ' ' <<< "$ASSEMBLIES")"

dotnet test tests/Backend.Tests/ \
  --no-build \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:Include="$INCLUDE" \
  /p:Exclude="[Backend.Mcp]Program*" \
  /p:Threshold=80 \
  /p:ThresholdType=line \
  /p:ThresholdStat=Total

echo "Coverage check passed."
