#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   SMOKE_CLIENT_ID=idm-mcp-smoke \
#   SMOKE_CERT_PATH=./idmdemo-prod-mcp-smoke-client.pem \
#   MCP_REMOTE_SCOPE=idm.mcp.read \
#   bash scripts/demo-mcp-remote-production-smoke.sh [-v|--verbose]
#
# Remote-only production smoke test:
#   - uses public HTTPS token endpoint with real client certificate auth
#   - uses public HTTPS MCP endpoint with DPoP
#   - skips private health and private SCIM setup
#
# The smoke client certificate must already be registered and assigned any
# requested scope. Use scripts/bootstrap-mcp-production-smoke.sh from the
# deployment host to create or update the persistent low-privilege smoke client.

VERBOSE=0
for arg in "$@"; do
    case "$arg" in
        -v|--verbose) VERBOSE=1 ;;
        -h|--help)
            sed -n '3,17p' "$0" | sed 's/^# \{0,3\}//'
            exit 0
            ;;
    esac
done

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib/mcp-demo-helpers.sh"
trap 'rm -rf "$WORKDIR"' EXIT

REMOTE_SCOPE="${MCP_REMOTE_SCOPE:-idm.mcp.read}"
REMOTE_CLIENT_ID="${SMOKE_CLIENT_ID:-${ADMIN_CLIENT_ID:-idm-mcp-smoke}}"
REMOTE_CERT_PATH="${SMOKE_CERT_PATH:-${ADMIN_CERT_PATH:-idmdemo-prod-mcp-smoke-client.pem}}"
INITIALIZE_PAYLOAD='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"idmdemo-remote-production-smoke","version":"1.0.0"}}}'
TOOLS_PAYLOAD='{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
CALL_READ_PAYLOAD='{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"idm_list_machine_clients","arguments":{"filter":null,"instance":null}}}'

print_token_failure_hint() {
    local body="$1"
    local error
    error=$(echo "$body" | json_field error)

    case "$error" in
        invalid_scope)
            echo "  HINT Token endpoint returned invalid_scope."
            echo "       Confirm '$REMOTE_CLIENT_ID' is assigned MCP_REMOTE_SCOPE='${REMOTE_SCOPE:-<none>}'."
            echo "       Bootstrap from the deployment host with scripts/bootstrap-mcp-production-smoke.sh --apply."
            ;;
        invalid_target)
            echo "  HINT Token endpoint returned invalid_target."
            echo "       Confirm MCP_AUDIENCE='$MCP_AUDIENCE' matches AuthorizationServer__McpAudience."
            echo "       Hosted MCP tokens also need an idm.mcp.* scope such as idm.mcp.read."
            ;;
        invalid_client)
            echo "  HINT Token endpoint returned invalid_client."
            echo "       Confirm '$REMOTE_CERT_PATH' matches the public certificate registered for '$REMOTE_CLIENT_ID'."
            ;;
    esac
    [ -n "$error" ] && echo ""
}

echo "IdmDemo MCP RemoteProduction smoke"
echo "Auth URL    : $AUTH"
echo "Auth DPoP   : $AUTH_DPOP"
echo "MCP URL     : $MCP"
echo "Client ID   : $REMOTE_CLIENT_ID"
echo "Client cert : $REMOTE_CERT_PATH"
echo "Scope       : ${REMOTE_SCOPE:-<none>}"
[ "$VERBOSE" -eq 1 ] && echo "Mode        : verbose"

if [[ "$AUTH" != https://* ]]; then
    echo "ERROR: AUTH_BASE_URL must be public HTTPS for a real remote auth smoke test." >&2
    exit 1
fi

if [[ "$MCP" != https://* ]]; then
    echo "ERROR: MCP_BASE_URL must be public HTTPS for a real remote MCP smoke test." >&2
    exit 1
fi

if [ -z "$REMOTE_CERT_PATH" ] || [ ! -f "$REMOTE_CERT_PATH" ]; then
    echo "ERROR: SMOKE_CERT_PATH='$REMOTE_CERT_PATH' not found." >&2
    exit 1
fi

cp "$REMOTE_CERT_PATH" "$WORKDIR/client.pem"
CLIENT_CERT_DER_BASE64=$(openssl x509 -in "$WORKDIR/client.pem" -outform DER | base64 | tr -d '\n')
CLIENT_CERT_THUMBPRINT=$(openssl x509 -in "$WORKDIR/client.pem" -outform DER \
    | openssl dgst -sha256 -binary \
    | xxd -p -c 256 \
    | tr '[:lower:]' '[:upper:]')
cp "$WORKDIR/client.pem" "$WORKDIR/client.crt"
cp "$WORKDIR/client.pem" "$WORKDIR/client.key"

header "Public discovery"
do_request "OIDC discovery" GET "$AUTH/.well-known/openid-configuration"
check "GET /.well-known/openid-configuration -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Discovery advertises client certificate auth" "self_signed_tls_client_auth" "$_BODY"

header "Remote DPoP token"
issue_dpop_token "$REMOTE_CLIENT_ID" "$REMOTE_SCOPE"
if [ "$_STATUS" -ne 200 ]; then
    print_token_failure_hint "$_BODY"
fi
check_value "Token type is DPoP" "DPoP" "$TOKEN_TYPE"
check_value "Token audience is $MCP_AUDIENCE" "$MCP_AUDIENCE" "$TOKEN_AUDIENCE"
check_value "JWT cnf.jkt matches proof key" "$DPOP_JKT" "$TOKEN_JKT"
check_value "Token scope is $REMOTE_SCOPE" "$REMOTE_SCOPE" "$TOKEN_SCOPE"

header "Public MCP DPoP protocol"
mcp_post_with_auth "MCP initialize with DPoP" "$INITIALIZE_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp initialize -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Initialize response names idm-demo-mcp" "idm-demo-mcp" "$_BODY"

mcp_post_with_auth "MCP tools/list with DPoP" "$TOOLS_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp tools/list -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Tool list includes idm_list_machine_clients" "idm_list_machine_clients" "$_BODY"

mcp_post_with_auth "MCP read tool call with DPoP" "$CALL_READ_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp tools/call read -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Read tool call succeeds" "\"isError\":false" "$_BODY"

header "Negative authentication checks"
mcp_post_with_auth "MCP initialize with bearer scheme" "$INITIALIZE_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp initialize with bearer scheme -> 401" 401 "$_STATUS" "$_BODY"

mcp_post_without_auth "MCP initialize without token" "$INITIALIZE_PAYLOAD"
check "POST /mcp initialize without token -> 401" 401 "$_STATUS" "$_BODY"

print_result_summary
