#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/demo-mcp-local-stdio.sh [-v|--verbose]
#
# Prerequisite:
#   dotnet build
#
# Runs the stdio MCP smoke test against the LocalStdio profile and prints the
# behavior being demonstrated.

VERBOSE=0
for arg in "$@"; do
    case "$arg" in
        -v|--verbose) VERBOSE=1 ;;
        -h|--help)
            sed -n '3,8p' "$0" | sed 's/^# \{0,3\}//'
            exit 0
            ;;
    esac
done

pass=0
fail=0

header() {
    echo ""
    echo "------------------------------------------"
    echo "  $1"
    echo "------------------------------------------"
}

check() {
    local label="$1"
    echo "  OK   $label"
    pass=$((pass + 1))
    echo ""
}

fail_check() {
    local label="$1"
    echo "  FAIL $label"
    fail=$((fail + 1))
    echo ""
}

print_result_summary() {
    echo "=========================================="
    echo "  Results: $pass passed, $fail failed"
    echo "=========================================="
    echo ""

    [ "$fail" -eq 0 ]
}

echo "IdmDemo MCP LocalStdio demo"
echo "Profile : LocalStdio"
echo "Transport: stdio"
[ "$VERBOSE" -eq 1 ] && echo "Mode    : verbose"

args=(
    test
    tests/Backend.Tests/Backend.Tests.csproj
    --no-build
    --filter
    "FullyQualifiedName~McpStdioSmokeTests"
)

if [ "$VERBOSE" -eq 1 ]; then
    args+=(--logger "console;verbosity=detailed")
fi

header "Local stdio behavior"
check "Uses prebuilt test and MCP binaries; run dotnet build first if needed"
check "Starts Backend.Mcp through stdio using dotnet run --project src/Backend.Mcp --no-build"
check "Uses LocalStdio defaults: no hosted HTTP listener and no caller bearer/DPoP auth"
check "Configures the IdM API instance to http://127.0.0.1:1 so the API call fails predictably"

header "MCP protocol smoke test"
check "Initializes an MCP client over stdio"
check "Lists MCP tools"
check "Verifies idm_list_machine_clients is advertised"
check "Calls idm_list_machine_clients with the API port closed"
check "Verifies the failed API call is returned as a tool error, not an MCP protocol failure"

header "Test execution"
if dotnet "${args[@]}"; then
    check "McpStdioSmokeTests passed"
else
    fail_check "McpStdioSmokeTests failed"
fi

print_result_summary
