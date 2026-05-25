#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/demo-mcp-local-hosted-development.sh [-v|--verbose]
#
# Expected services:
#   Backend.Api running with AuthorizationServer__Issuer matching API_BASE_URL
#     and AuthorizationServer__Audience=idm-demo-mcp
#   Backend.Mcp running with AuthorizationServer__Issuer matching Backend.Api,
#     Mcp__Profile=LocalHostedDevelopment, and Mcp__Hosted__Audience=idm-demo-mcp

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

cleanup_mcp_audit_user() {
    if [ -n "${AUDIT_USER_ID:-}" ]; then
        do_request "Delete MCP audit demo user" DELETE "$API/scim/v2/Users/$AUDIT_USER_ID" \
            -H "X-Api-Key: $KEY"
        if [ "$_STATUS" -eq 204 ] || [ "$_STATUS" -eq 404 ]; then
            echo "  OK   MCP audit demo user cleanup (HTTP $_STATUS)"
            pass=$((pass + 1))
        else
            echo "  FAIL MCP audit demo user cleanup - HTTP $_STATUS"
            fail=$((fail + 1))
        fi
        echo ""
    fi
}

trap 'cleanup_mcp_audit_user; cleanup_mcp_demo_client; rm -rf "$WORKDIR"' EXIT

CLIENT_ID="mcp-local-dev-$(date +%s)"
AUDIT_USER_NAME="mcp-audit-demo-$(date +%s)"
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

header "Audit and destructive action safety"
echo "  Watch the Backend.Mcp process log for these audit events:"
echo "  - McpToolInvoked"
echo "  - McpToolDenied"
echo "  - McpToolSucceeded"
echo ""

do_request "Create disposable audit demo user" POST "$API/scim/v2/Users" \
    -H "X-Api-Key: $KEY" \
    -H "Content-Type: application/scim+json" \
    -d "{\"userName\":\"$AUDIT_USER_NAME\",\"displayName\":\"MCP Audit Demo\",\"active\":true}"
check "POST /scim/v2/Users audit demo user -> 201" 201 "$_STATUS" "$_BODY"
AUDIT_USER_ID=$(echo "$_BODY" | json_field id)

issue_bearer_token "$CLIENT_ID" "idm.mcp.read idm.mcp.destructive"
check_value "Token includes destructive scope" "idm.mcp.read idm.mcp.destructive" "$TOKEN_SCOPE"

DELETE_WITHOUT_CONFIRM_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"idm_delete_user","arguments":{"id":"%s","instance":null}}}' "$AUDIT_USER_ID")
mcp_post_with_auth "MCP destructive delete without confirm" "$DELETE_WITHOUT_CONFIRM_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call delete without confirm -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Denied destructive response is an MCP error result" "\"isError\":true" "$_BODY"

DELETE_WITH_CONFIRM_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"idm_delete_user","arguments":{"id":"%s","confirm":true,"instance":null}}}' "$AUDIT_USER_ID")
mcp_post_with_auth "MCP destructive delete with confirm" "$DELETE_WITH_CONFIRM_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call delete with confirm -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Confirmed destructive delete returns operation correlationId" "correlationId" "$_BODY"
AUDIT_DELETED_USER_ID="$AUDIT_USER_ID"
AUDIT_USER_ID=""

echo "  Expected MCP log evidence:"
echo "  - McpToolDenied Tool=idm_delete_user ... Confirm= ... Reason=This destructive tool requires confirm: true."
echo "  - McpToolSucceeded Tool=idm_delete_user ... Confirm=True ResourceId=$AUDIT_DELETED_USER_ID"
echo ""

header "DPoP token"
issue_dpop_token "$CLIENT_ID" "idm.mcp.read"
check_value "Token type is DPoP" "DPoP" "$TOKEN_TYPE"
check_value "Token audience is $MCP_AUDIENCE" "$MCP_AUDIENCE" "$TOKEN_AUDIENCE"
check_value "Token scope is idm.mcp.read" "idm.mcp.read" "$TOKEN_SCOPE"
check_value "JWT cnf.jkt matches DPoP public key thumbprint" "$DPOP_JKT" "$TOKEN_JKT"

header "Hosted MCP DPoP protocol"
mcp_post_with_auth "MCP initialize with DPoP" "$INITIALIZE_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp initialize with DPoP -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Initialize DPoP response names idm-demo-mcp" "idm-demo-mcp" "$_BODY"

mcp_post_with_auth "MCP tools/list with DPoP" "$TOOLS_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp tools/list with DPoP -> 200" 200 "$_STATUS" "$_BODY"
check_contains "DPoP tool list includes idm_list_machine_clients" "idm_list_machine_clients" "$_BODY"

mcp_post_with_auth "MCP read tool call with DPoP" "$CALL_READ_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp tools/call read with DPoP -> 200" 200 "$_STATUS" "$_BODY"
check_contains "DPoP read tool call succeeds" "\"isError\":false" "$_BODY"

header "Negative authentication check"
mcp_post_without_auth "MCP initialize without token" "$INITIALIZE_PAYLOAD"
check "POST /mcp initialize without token -> 401" 401 "$_STATUS" "$_BODY"

print_result_summary
