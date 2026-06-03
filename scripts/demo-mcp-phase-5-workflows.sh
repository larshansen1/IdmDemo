#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   bash scripts/demo-mcp-phase-5-workflows.sh [-v|--verbose]
#
# Expected services:
#   Backend.Api running with AuthorizationServer__Issuer matching API_BASE_URL
#     and AuthorizationServer__Audience=idm-demo-mcp
#   Backend.Mcp running with Mcp__Profile=LocalHostedDevelopment,
#     Mcp__Hosted__Audience=idm-demo-mcp, and bearer tokens enabled by profile.

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
API_BASE_URL="${API_BASE_URL:-http://127.0.0.1:5000}"
MCP_BASE_URL="${MCP_BASE_URL:-http://127.0.0.1:5100}"
source "$SCRIPT_DIR/lib/mcp-demo-helpers.sh"

cleanup_workflow_client() {
    if [ -n "${WORKFLOW_CLIENT_RECORD_ID:-}" ]; then
        do_request "Delete workflow target client" DELETE "$API/scim/v2/Clients/$WORKFLOW_CLIENT_RECORD_ID" \
            -H "Authorization: Bearer $ADMIN_TOKEN"
        if [ "$_STATUS" -eq 204 ] || [ "$_STATUS" -eq 404 ]; then
            echo "  OK   Workflow target client cleanup (HTTP $_STATUS)"
            pass=$((pass + 1))
        else
            echo "  FAIL Workflow target client cleanup - HTTP $_STATUS"
            fail=$((fail + 1))
        fi
        echo ""
    fi
}

json_string() {
    python3 - "$1" <<'PY'
import json
import sys

print(json.dumps(sys.argv[1]))
PY
}

json_array() {
    python3 - "$@" <<'PY'
import json
import sys

print(json.dumps(sys.argv[1:]))
PY
}

mcp_first_text() {
    python3 - "$1" <<'PY'
import json
import sys

try:
    body = json.loads(sys.argv[1])
    print(body["result"]["content"][0]["text"])
except Exception:
    pass
PY
}

ensure_demo_role() {
    local role="$1"

    do_request "Create role $role" POST "$API/scim/v2/Roles" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H "Content-Type: application/scim+json" \
        -d "{\"value\":\"$role\",\"displayName\":\"$role\",\"active\":true}"

    if [ "$_STATUS" -eq 201 ] || [ "$_STATUS" -eq 409 ]; then
        echo "  OK   Role $role is available (HTTP $_STATUS)"
        pass=$((pass + 1))
    else
        echo "  FAIL Role $role setup failed - HTTP $_STATUS"
        fail=$((fail + 1))
    fi
    echo ""
}

generate_workflow_csr() {
    local name="$1" key_path="$2" csr_path="$3"

    openssl req \
        -newkey rsa:2048 \
        -nodes \
        -sha256 \
        -subj "/CN=$name" \
        -keyout "$key_path" \
        -out "$csr_path" >/dev/null 2>&1
}

trap 'cleanup_workflow_client; cleanup_mcp_demo_client; rm -rf "$WORKDIR"' EXIT

CALLER_CLIENT_ID="mcp-phase5-caller-$(date +%s)"
WORKFLOW_CLIENT_ID="phase5-workload-$(date +%s)"
INITIALIZE_PAYLOAD='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"idmdemo-phase-5-workflows-demo","version":"1.0.0"}}}'
TOOLS_PAYLOAD='{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

echo "IdmDemo MCP Phase 5 workflow demo"
echo "API URL      : $API"
echo "MCP URL      : $MCP"
echo "Admin client : $ADMIN_CLIENT_ID"
echo "Profile      : LocalHostedDevelopment"
[ "$VERBOSE" -eq 1 ] && echo "Mode    : verbose"

header "Admin token"
acquire_admin_token
echo "  OK   Acquired scim.admin bearer token for '$ADMIN_CLIENT_ID'"
echo ""

header "Health and access setup"
do_request "MCP readiness" GET "$MCP/health/ready"
check "GET /health/ready -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Readiness reports LocalHostedDevelopment" "\"profile\":\"LocalHostedDevelopment\"" "$_BODY"

ensure_scope "idm.mcp.read"
ensure_scope "idm.mcp.write"
ensure_scope "idm.mcp.destructive"
ensure_scope "idm.mcp.certificates"
ensure_scope "orders.read"
ensure_scope "orders.write"
ensure_demo_role "service"
create_mcp_demo_client "$CALLER_CLIENT_ID" "MCP Phase 5 Workflow Demo Caller"

header "Hosted MCP bearer session"
issue_bearer_token "$CALLER_CLIENT_ID" "idm.mcp.read idm.mcp.write idm.mcp.destructive idm.mcp.certificates"
check_value "Token audience is $MCP_AUDIENCE" "$MCP_AUDIENCE" "$TOKEN_AUDIENCE"
check_value "Token has workflow scopes" "idm.mcp.read idm.mcp.write idm.mcp.destructive idm.mcp.certificates" "$TOKEN_SCOPE"

mcp_post_with_auth "MCP initialize with bearer" "$INITIALIZE_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp initialize -> 200" 200 "$_STATUS" "$_BODY"

mcp_post_with_auth "MCP tools/list with bearer" "$TOOLS_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/list -> 200" 200 "$_STATUS" "$_BODY"
check_contains "Tool list includes certificate rotation workflow" "idm_rotate_machine_client_certificate" "$_BODY"
check_contains "Tool list includes DPoP instructions workflow" "idm_prepare_dpop_client_credential_instructions" "$_BODY"
check_contains "Tool list includes deployment preflight workflow" "idm_preflight_machine_client_deployment" "$_BODY"

header "DPoP credential instructions workflow"
PREPARE_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"idm_prepare_dpop_client_credential_instructions","arguments":{"clientId":%s,"mcpAudience":%s,"instance":null}}}' \
    "$(json_string "$WORKFLOW_CLIENT_ID")" \
    "$(json_string "$MCP_AUDIENCE")")
mcp_post_with_auth "Prepare DPoP credential instructions" "$PREPARE_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call prepare DPoP instructions -> 200" 200 "$_STATUS" "$_BODY"
PREPARE_TEXT=$(mcp_first_text "$_BODY")
check_contains "Instructions response names workflow client" "$WORKFLOW_CLIENT_ID" "$PREPARE_TEXT"
check_contains "Instructions mention hosted MCP DPoP header" "Authorization: DPoP" "$PREPARE_TEXT"

header "Onboard workflow target without a certificate"
REQUIRED_ROLES=$(json_array "service")
INITIAL_SCOPES=$(json_array "orders.read")
ONBOARD_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"idm_onboard_machine_client","arguments":{"clientId":%s,"displayName":"Phase 5 Workflow Target","assignedRoles":%s,"assignedScopes":%s,"instance":null}}}' \
    "$(json_string "$WORKFLOW_CLIENT_ID")" \
    "$REQUIRED_ROLES" \
    "$INITIAL_SCOPES")
mcp_post_with_auth "Onboard workflow target client" "$ONBOARD_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call onboard target -> 200" 200 "$_STATUS" "$_BODY"
ONBOARD_TEXT=$(mcp_first_text "$_BODY")
check_contains "Onboard workflow succeeds" "\"status\":\"succeeded\"" "$ONBOARD_TEXT"

WORKFLOW_CLIENT_RECORD_ID=$(python3 - "$ONBOARD_TEXT" <<'PY'
import json
import sys

try:
    result = json.loads(sys.argv[1])
    print(result["client"]["id"])
except Exception:
    pass
PY
)

header "Deployment preflight before certificate rotation"
REQUIRED_SCOPES=$(json_array "orders.read" "orders.write")
PREFLIGHT_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"idm_preflight_machine_client_deployment","arguments":{"clientId":%s,"requiredRoles":%s,"requiredScopes":%s,"minimumCertificateValidityDays":7,"instance":null}}}' \
    "$(json_string "$WORKFLOW_CLIENT_ID")" \
    "$REQUIRED_ROLES" \
    "$REQUIRED_SCOPES")
mcp_post_with_auth "Preflight target before certificate" "$PREFLIGHT_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call preflight before cert -> 200" 200 "$_STATUS" "$_BODY"
PREFLIGHT_TEXT=$(mcp_first_text "$_BODY")
check_contains "Preflight reports not ready" "\"ready\":false" "$PREFLIGHT_TEXT"
check_contains "Preflight reports missing certificate" "Machine client has no active certificate." "$PREFLIGHT_TEXT"
check_contains "Preflight reports missing required scope" "orders.write" "$PREFLIGHT_TEXT"

header "Certificate rotation workflow"
generate_workflow_csr "$WORKFLOW_CLIENT_ID" "$WORKDIR/workflow-client.key" "$WORKDIR/workflow-client.csr"
CSR_PEM=$(cat "$WORKDIR/workflow-client.csr")
ROTATE_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"idm_rotate_machine_client_certificate","arguments":{"clientId":%s,"certificateSigningRequestPem":%s,"displayName":"phase-5-rotation","validityDays":7,"instance":null}}}' \
    "$(json_string "$WORKFLOW_CLIENT_ID")" \
    "$(json_string "$CSR_PEM")")
mcp_post_with_auth "Rotate target certificate from CSR" "$ROTATE_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call rotate certificate -> 200" 200 "$_STATUS" "$_BODY"
ROTATE_TEXT=$(mcp_first_text "$_BODY")
check_contains "Rotation workflow succeeds" "\"status\":\"succeeded\"" "$ROTATE_TEXT"
check_contains "Rotation includes issue step" "issue_certificate" "$ROTATE_TEXT"
check_contains "Rotation returns certificate PEM" "certificatePem" "$ROTATE_TEXT"

header "Deployment preflight after certificate rotation"
PREFLIGHT_AFTER_PAYLOAD=$(printf '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"idm_preflight_machine_client_deployment","arguments":{"clientId":%s,"requiredRoles":%s,"requiredScopes":%s,"minimumCertificateValidityDays":7,"instance":null}}}' \
    "$(json_string "$WORKFLOW_CLIENT_ID")" \
    "$REQUIRED_ROLES" \
    "$INITIAL_SCOPES")
mcp_post_with_auth "Preflight target after certificate" "$PREFLIGHT_AFTER_PAYLOAD" "Bearer" "$ACCESS_TOKEN"
check "POST /mcp tools/call preflight after cert -> 200" 200 "$_STATUS" "$_BODY"
PREFLIGHT_AFTER_TEXT=$(mcp_first_text "$_BODY")
check_contains "Preflight reports ready" "\"ready\":true" "$PREFLIGHT_AFTER_TEXT"
check_contains "Preflight shows active certificate" "\"activeCertificateCount\":1" "$PREFLIGHT_AFTER_TEXT"

print_result_summary
