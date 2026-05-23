#!/usr/bin/env bash
set -euo pipefail

# - Usage -
#
#   bash scripts/demo-hosted-mcp.sh [-v|--verbose]
#
#   -v / --verbose   Print full request and response for every call.
#
#   Environment overrides:
#     API_BASE_URL   default: http://localhost:5000
#     MCP_BASE_URL   default: http://localhost:5100
#     API_KEY        default: changeme-development-key

VERBOSE=0
for arg in "$@"; do
    case "$arg" in
        -v|--verbose) VERBOSE=1 ;;
        -h|--help)
            sed -n '3,12p' "$0" | sed 's/^# \{0,3\}//'
            exit 0
            ;;
    esac
done

API="${API_BASE_URL:-http://localhost:5000}"
MCP="${MCP_BASE_URL:-http://localhost:5100}"
KEY="${API_KEY:-changeme-development-key}"
PROTOCOL_VERSION="2025-06-18"

WORKDIR=$(mktemp -d)
BODY_FILE="$WORKDIR/body.txt"
trap 'rm -rf "$WORKDIR"' EXIT

pass=0
fail=0

_STATUS=""
_BODY=""

header() {
    echo ""
    echo "------------------------------------------"
    echo "  $1"
    echo "------------------------------------------"
}

print_verbose_response() {
    local status="$1" body="$2"

    echo "  |- Response ------------------------------------------"
    echo "  | HTTP $status"
    if [ -n "$body" ]; then
        echo "  |"
        echo "$body" | python3 -m json.tool --indent 2 2>/dev/null \
            | sed 's/^/  | /' \
            || echo "$body" | sed 's/^/  | /'
    fi
    echo "  ------------------------------------------------------"
}

do_request() {
    local label="$1" method="$2" url="$3"; shift 3
    local args=("$@")

    if [ "$VERBOSE" -eq 1 ]; then
        echo ""
        echo "  |- Request -------------------------------------------"
        echo "  | $method $url"
        local i=0 req_body=""
        while [ $i -lt ${#args[@]} ]; do
            case "${args[$i]}" in
                -H) i=$((i+1)); echo "  | ${args[$i]}" ;;
                -d) i=$((i+1)); req_body="${args[$i]}" ;;
            esac
            i=$((i+1))
        done
        if [ -n "$req_body" ]; then
            echo "  |"
            echo "$req_body" | python3 -m json.tool --indent 2 2>/dev/null \
                | sed 's/^/  | /' \
                || echo "  | $req_body"
        fi
    fi

    _STATUS=$(curl -s -o "$BODY_FILE" -w "%{http_code}" -X "$method" "${args[@]}" "$url")
    _BODY=$(cat "$BODY_FILE")

    if [ "$VERBOSE" -eq 1 ]; then
        print_verbose_response "$_STATUS" "$_BODY"
    fi
}

check() {
    local label="$1" expected="$2" actual="$3" body="${4:-}"
    if [ "$actual" -eq "$expected" ]; then
        echo "  OK   $label (HTTP $actual)"
        pass=$((pass + 1))
    else
        echo "  FAIL $label - expected HTTP $expected, got $actual"
        fail=$((fail + 1))
    fi
    if [ "$VERBOSE" -eq 0 ] && [ -n "$body" ]; then
        local first_line
        first_line=$(echo "$body" | head -1 | cut -c1-140)
        [ -n "$first_line" ] && echo "       $first_line"
    fi
    echo ""
}

check_contains() {
    local label="$1" needle="$2" body="$3"
    if echo "$body" | grep -Fq "$needle"; then
        echo "  OK   $label"
        pass=$((pass + 1))
    else
        echo "  FAIL $label - missing '$needle'"
        fail=$((fail + 1))
    fi
    echo ""
}

extract_mcp_json() {
    python3 -c '
import sys

body = sys.stdin.read()
data_lines = []
for line in body.splitlines():
    if line.startswith("data:"):
        data_lines.append(line[5:].strip())

if data_lines:
    print("\n".join(data_lines))
else:
    print(body)
'
}

mcp_post() {
    local label="$1" payload="$2"
    do_request "$label" POST "$MCP/mcp" \
        -H "Accept: application/json, text/event-stream" \
        -H "Content-Type: application/json" \
        -H "MCP-Protocol-Version: $PROTOCOL_VERSION" \
        -d "$payload"
    _BODY=$(echo "$_BODY" | extract_mcp_json)
}

echo "IdmDemo hosted MCP demo"
echo "API URL : $API"
echo "MCP URL : $MCP"
echo "API key : $KEY"
[ "$VERBOSE" -eq 1 ] && echo "Mode    : verbose"

header "Backend.Api reachability"

do_request "API discovery" GET "$API/.well-known/openid-configuration"
check "GET /.well-known/openid-configuration -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Discovery response includes issuer" "\"issuer\"" "$_BODY"

header "Hosted MCP health"

do_request "MCP liveness" GET "$MCP/health/live"
check "GET /health/live -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Liveness reports Backend.Mcp" "\"service\":\"Backend.Mcp\"" "$_BODY"

do_request "MCP readiness" GET "$MCP/health/ready"
check "GET /health/ready -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Readiness uses HTTP transport" "\"transport\":\"Http\"" "$_BODY"
check_contains "Readiness checks IdM API reachability" "IdM API instance 'local' is reachable." "$_BODY"
check_contains "Readiness checks hosted auth config" "Hosted MCP is configured to require DPoP-bound access tokens." "$_BODY"

header "Hosted MCP protocol"

INITIALIZE_PAYLOAD='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"idmdemo-hosted-demo","version":"1.0.0"}}}'
mcp_post "MCP initialize" "$INITIALIZE_PAYLOAD"
check "POST /mcp initialize -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Initialize response names idm-demo-mcp" "idm-demo-mcp" "$_BODY"

TOOLS_PAYLOAD='{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
mcp_post "MCP tools/list" "$TOOLS_PAYLOAD"
check "POST /mcp tools/list -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Tool list includes idm_list_machine_clients" "idm_list_machine_clients" "$_BODY"
check_contains "Tool list includes idm_get_authorization_server_metadata" "idm_get_authorization_server_metadata" "$_BODY"

echo "=========================================="
echo "  Results: $pass passed, $fail failed"
echo "=========================================="
echo ""

[ "$fail" -eq 0 ]
