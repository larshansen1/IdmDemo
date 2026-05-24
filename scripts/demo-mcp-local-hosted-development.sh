#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/demo-mcp-local-hosted-development.sh [-v|--verbose]
#
# Expected services:
#   Backend.Api running with AuthorizationServer__Audience=idm-demo-mcp
#   Backend.Mcp running with Mcp__Profile=LocalHostedDevelopment

VERBOSE=0
for arg in "$@"; do
    case "$arg" in
        -v|--verbose) VERBOSE=1 ;;
        -h|--help)
            sed -n '3,11p' "$0" | sed 's/^# \{0,3\}//'
            exit 0
            ;;
    esac
done

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib/mcp-demo-helpers.sh"
trap 'cleanup_mcp_demo_client; rm -rf "$WORKDIR"' EXIT

CLIENT_ID="mcp-local-dev-$(date +%s)"
INITIALIZE_PAYLOAD='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"idmdemo-local-hosted-demo","version":"1.0.0"}}}'
TOOLS_PAYLOAD='{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
CALL_READ_PAYLOAD='{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"idm_list_machine_clients","arguments":{"filter":null,"instance":null}}}'

echo "IdmDemo MCP LocalHostedDevelopment demo"
echo "API URL : $API"
echo "MCP URL : $MCP"
echo "Profile : LocalHostedDevelopment"
[ "$VERBOSE" -eq 1 ] && echo "Mode    : verbose"

header "Health and profile readiness"
do_request "MCP readiness" GET "$MCP/health/ready"
check "GET /health/ready -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Readiness reports LocalHostedDevelopment" "\"profile\":\"LocalHostedDevelopment\"" "$_BODY"
check_contains "Readiness reports bearer development mode" "\"allowBearerTokensForDevelopment\":true" "$_BODY"

header "Access setup"
ensure_scope "idm.mcp.read"
ensure_scope "idm.mcp.write"
ensure_scope "idm.mcp.destructive"
ensure_scope "idm.mcp.certificates"
create_mcp_demo_client "$CLIENT_ID" "MCP Local Hosted Development Demo"

header "Bearer token"
issue_bearer_token "$CLIENT_ID" "idm.mcp.read"
check_value "Token type is Bearer" "Bearer" "$TOKEN_TYPE"
check_value "Token audience is $MCP_AUDIENCE" "$MCP_AUDIENCE" "$TOKEN_AUDIENCE"
check_value "Token scope is idm.mcp.read" "idm.mcp.read" "$TOKEN_SCOPE"

header "Hosted MCP bearer protocol"
mcp_post_with_auth "MCP initialize with bearer" "$INITIALIZE_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp initialize -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Initialize response names idm-demo-mcp" "idm-demo-mcp" "$_BODY"

mcp_post_with_auth "MCP tools/list with bearer" "$TOOLS_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/list -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Tool list includes idm_list_machine_clients" "idm_list_machine_clients" "$_BODY"

mcp_post_with_auth "MCP read tool call with bearer" "$CALL_READ_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call read -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Read tool call succeeds" "\"isError\":false" "$_BODY"

header "Negative authentication check"
mcp_post_without_auth "MCP initialize without token" "$INITIALIZE_PAYLOAD"
check "POST /mcp initialize without token -> 401" 401 "$_STATUS" "$_BODY"

print_result_summary
