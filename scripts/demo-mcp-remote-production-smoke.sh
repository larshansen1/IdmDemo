#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ADMIN_CERT_PATH=./idmdemo-prod-admin-client.pem \
#   bash scripts/demo-mcp-remote-production-smoke.sh [-v|--verbose]
#
# Remote-only production smoke test:
#   - uses public HTTPS token endpoint with real client certificate auth
#   - uses public HTTPS MCP endpoint with DPoP
#   - skips private health and private SCIM setup
#
# The client certificate must already be registered and assigned any requested
# scope. By default no scope is requested, which is enough to prove remote
# authentication and MCP protocol initialization.

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

REMOTE_SCOPE="${MCP_REMOTE_SCOPE:-}"
INITIALIZE_PAYLOAD='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"idmdemo-remote-production-smoke","version":"1.0.0"}}}'
TOOLS_PAYLOAD='{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
CALL_READ_PAYLOAD='{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"idm_list_machine_clients","arguments":{"filter":null,"instance":null}}}'

echo "IdmDemo MCP RemoteProduction smoke"
echo "Auth URL    : $AUTH"
echo "Auth DPoP   : $AUTH_DPOP"
echo "MCP URL     : $MCP"
echo "Client ID   : $ADMIN_CLIENT_ID"
echo "Client cert : $ADMIN_CERT_PATH"
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

if [ -z "$ADMIN_CERT_PATH" ] || [ ! -f "$ADMIN_CERT_PATH" ]; then
    echo "ERROR: ADMIN_CERT_PATH='$ADMIN_CERT_PATH' not found." >&2
    exit 1
fi

cp "$ADMIN_CERT_PATH" "$WORKDIR/client.pem"
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
issue_dpop_token "$ADMIN_CLIENT_ID" "$REMOTE_SCOPE"
check_value "Token type is DPoP" "DPoP" "$TOKEN_TYPE"
check_value "Token audience is $MCP_AUDIENCE" "$MCP_AUDIENCE" "$TOKEN_AUDIENCE"
check_value "JWT cnf.jkt matches proof key" "$DPOP_JKT" "$TOKEN_JKT"
if [ -n "$REMOTE_SCOPE" ]; then
    check_value "Token scope is $REMOTE_SCOPE" "$REMOTE_SCOPE" "$TOKEN_SCOPE"
fi

header "Public MCP DPoP protocol"
mcp_post_with_auth "MCP initialize with DPoP" "$INITIALIZE_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp initialize -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Initialize response names idm-demo-mcp" "idm-demo-mcp" "$_BODY"

mcp_post_with_auth "MCP tools/list with DPoP" "$TOOLS_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
check "POST /mcp tools/list -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Tool list includes idm_list_machine_clients" "idm_list_machine_clients" "$_BODY"

if [ -n "$REMOTE_SCOPE" ]; then
    mcp_post_with_auth "MCP read tool call with DPoP" "$CALL_READ_PAYLOAD" "DPoP" "$ACCESS_TOKEN"
    check "POST /mcp tools/call read -> 200" 200 "$_STATUS" "$_BODY"
    check_contains "Read tool call succeeds" "\"isError\":false" "$_BODY"
fi

header "Negative authentication checks"
mcp_post_with_auth "MCP initialize with bearer scheme" "$INITIALIZE_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp initialize with bearer scheme -> 401" 401 "$_STATUS" "$_BODY"

mcp_post_without_auth "MCP initialize without token"
check "POST /mcp initialize without token -> 401" 401 "$_STATUS" "$_BODY"

print_result_summary
