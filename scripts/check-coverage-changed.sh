#!/usr/bin/env bash
set -euo pipefail

# Get staged source .cs files (src/ only; obj/bin excluded)
STAGED=$(git diff --cached --name-only --diff-filter=ACMR 2>/dev/null \
  | grep -E '^src/.*\.cs$' | grep -vE '/(obj|bin)/' || true)

if [ -z "$STAGED" ]; then
  echo "No staged source .cs files — skipping coverage check."
  exit 0
fi

# Map file paths to assembly names.
# Backend.Infrastructure is exempt from the coverage gate (mirrors CI exclusions).
declare -A ASSEMBLIES
while IFS= read -r FILE; do
  case "$FILE" in
    src/Backend.Api/*)            ASSEMBLIES["Backend.Api"]=1 ;;
    src/Backend.Application/*)    ASSEMBLIES["Backend.Application"]=1 ;;
    src/Backend.Domain/*)         ASSEMBLIES["Backend.Domain"]=1 ;;
    src/Backend.Mcp/*)            ASSEMBLIES["Backend.Mcp"]=1 ;;
    src/Backend.Infrastructure/*) ;; # exempt from coverage gate
  esac
done <<< "$STAGED"

if [ "${#ASSEMBLIES[@]}" -eq 0 ]; then
  echo "Only coverage-exempt assemblies changed — skipping."
  exit 0
fi

# Build MSBuild Include filter; commas must be URL-encoded (%2c) on the CLI.
INCLUDE=""
for ASM in "${!ASSEMBLIES[@]}"; do
  INCLUDE="${INCLUDE:+${INCLUDE}%2c}[${ASM}]*"
done

echo "==> Coverage check for changed assemblies: ${!ASSEMBLIES[*]}"

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
