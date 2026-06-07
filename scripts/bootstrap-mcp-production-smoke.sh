#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ADMIN_CLIENT_ID=idm-mcp-backend \
#   ADMIN_CERT_PATH=/path/to/admin-client.pem \
#   API_BASE_URL=http://127.0.0.1:5000 \
#   AUTH_BASE_URL=http://127.0.0.1:5000 \
#   SMOKE_CERT_PATH=./idmdemo-prod-mcp-smoke-client.pem \
#   bash scripts/bootstrap-mcp-production-smoke.sh --apply
#
# Without --apply, prints the planned production smoke identity setup and exits.

APPLY=0
VERBOSE=0
for arg in "$@"; do
    case "$arg" in
        --apply) APPLY=1 ;;
        -v|--verbose) VERBOSE=1 ;;
        -h|--help)
            sed -n '3,15p' "$0" | sed 's/^# \{0,3\}//'
            exit 0
            ;;
    esac
done

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
source "$SCRIPT_DIR/lib/mcp-demo-helpers.sh"
trap 'rm -rf "$WORKDIR"' EXIT

SMOKE_CLIENT_ID="${SMOKE_CLIENT_ID:-idm-mcp-smoke}"
SMOKE_CLIENT_DISPLAY_NAME="${SMOKE_CLIENT_DISPLAY_NAME:-MCP Production Smoke}"
SMOKE_SCOPE="${SMOKE_SCOPE:-idm.mcp.read}"
SMOKE_CERT_PATH="${SMOKE_CERT_PATH:-idmdemo-prod-mcp-smoke-client.pem}"
SMOKE_CERT_DAYS="${SMOKE_CERT_DAYS:-365}"

echo "IdmDemo MCP production smoke bootstrap"
echo "Admin API URL     : $API"
echo "Auth URL          : $AUTH"
echo "Admin client ID   : $ADMIN_CLIENT_ID"
echo "Admin cert path   : $ADMIN_CERT_PATH"
echo "Smoke client ID   : $SMOKE_CLIENT_ID"
echo "Smoke scope       : $SMOKE_SCOPE"
echo "Smoke cert path   : $SMOKE_CERT_PATH"
echo "Smoke cert days   : $SMOKE_CERT_DAYS"
echo "Apply             : $APPLY"
echo ""

if [ "$APPLY" -ne 1 ]; then
    echo "Planned changes:"
    echo "  - Ensure global scope '$SMOKE_SCOPE' exists and is active."
    echo "  - Ensure machine client '$SMOKE_CLIENT_ID' exists and is active."
    echo "  - Assign only '$SMOKE_SCOPE' to '$SMOKE_CLIENT_ID'."
    echo "  - Assign no roles to '$SMOKE_CLIENT_ID'."
    echo "  - Generate '$SMOKE_CERT_PATH' if it does not already exist."
    echo "  - Register the public certificate from '$SMOKE_CERT_PATH'."
    echo ""
    echo "Re-run with --apply to make these changes."
    exit 0
fi

if [ -z "$ADMIN_CERT_PATH" ] || [ ! -f "$ADMIN_CERT_PATH" ]; then
    echo "ERROR: ADMIN_CERT_PATH='$ADMIN_CERT_PATH' not found." >&2
    exit 1
fi

if [ ! -f "$SMOKE_CERT_PATH" ]; then
    header "Generate smoke client certificate"
    SMOKE_CERT_DIR=$(dirname "$SMOKE_CERT_PATH")
    if [ -n "$SMOKE_CERT_DIR" ] && [ "$SMOKE_CERT_DIR" != "." ]; then
        mkdir -p "$SMOKE_CERT_DIR"
    fi
    openssl req \
        -x509 \
        -newkey rsa:2048 \
        -nodes \
        -sha256 \
        -days "$SMOKE_CERT_DAYS" \
        -subj "/CN=$SMOKE_CLIENT_ID" \
        -keyout "$WORKDIR/smoke-client.key" \
        -out "$WORKDIR/smoke-client.crt" >/dev/null 2>&1
    cat "$WORKDIR/smoke-client.crt" "$WORKDIR/smoke-client.key" > "$SMOKE_CERT_PATH"
    chmod 600 "$SMOKE_CERT_PATH"
    echo "  OK   Generated smoke client PEM at $SMOKE_CERT_PATH"
    echo ""
fi

SMOKE_CERT_THUMBPRINT=$(openssl x509 -in "$SMOKE_CERT_PATH" -outform DER \
    | openssl dgst -sha256 -binary \
    | xxd -p -c 256 \
    | tr '[:lower:]' '[:upper:]')
SMOKE_CERT_SUBJECT=$(openssl x509 -in "$SMOKE_CERT_PATH" -noout -subject -nameopt RFC2253 \
    | sed 's/^subject=//')
SMOKE_CERT_EXPIRES_AT=$(openssl x509 -in "$SMOKE_CERT_PATH" -noout -enddate \
    | cut -d= -f2- \
    | python3 -c 'import email.utils,sys; print(email.utils.parsedate_to_datetime(sys.stdin.read().strip()).isoformat())')

header "Admin token"
acquire_admin_token
echo "  OK   Acquired scim.admin DPoP token for '$ADMIN_CLIENT_ID'"
echo ""

header "Scope setup"
ensure_scope "$SMOKE_SCOPE"

header "Smoke client setup"
FILTER=$(python3 - "$SMOKE_CLIENT_ID" <<'PY'
import sys
import urllib.parse

print(urllib.parse.quote(f'clientId eq "{sys.argv[1]}"', safe=""))
PY
)
FIND_URL="$API/scim/v2/Clients?filter=$FILTER"
find_auth_args=()
admin_auth_args find_auth_args GET "$FIND_URL"
do_request "Find smoke client" GET "$FIND_URL" "${find_auth_args[@]}"
check "GET /scim/v2/Clients?filter=clientId -> 200" 200 "$_STATUS" "$_BODY"

CLIENT_RECORD_ID=$(echo "$_BODY" | python3 -c 'import json,sys; data=json.load(sys.stdin); resources=data.get("resources", []); print(resources[0].get("id", "") if resources else "")' 2>/dev/null || true)
CLIENT_PAYLOAD=$(python3 - "$SMOKE_CLIENT_ID" "$SMOKE_CLIENT_DISPLAY_NAME" "$SMOKE_CERT_THUMBPRINT" "$SMOKE_CERT_SUBJECT" "$SMOKE_CERT_EXPIRES_AT" "$SMOKE_SCOPE" <<'PY'
import json
import sys

print(json.dumps({
    "clientId": sys.argv[1],
    "displayName": sys.argv[2],
    "active": True,
    "certificateThumbprintSha256": sys.argv[3],
    "certificateSubject": sys.argv[4],
    "certificateExpiresAt": sys.argv[5],
    "assignedScopes": [sys.argv[6]],
    "assignedRoles": [],
}, separators=(",", ":")))
PY
)

if [ -n "$CLIENT_RECORD_ID" ]; then
    update_url="$API/scim/v2/Clients/$CLIENT_RECORD_ID"
    update_auth_args=()
    admin_auth_args update_auth_args PUT "$update_url"
    do_request "Update smoke client" PUT "$update_url" \
        "${update_auth_args[@]}" \
        -H "Content-Type: application/scim+json" \
        -d "$CLIENT_PAYLOAD"
    check "PUT /scim/v2/Clients/{id} -> 200" 200 "$_STATUS" "$_BODY"
else
    create_url="$API/scim/v2/Clients"
    create_auth_args=()
    admin_auth_args create_auth_args POST "$create_url"
    do_request "Create smoke client" POST "$create_url" \
        "${create_auth_args[@]}" \
        -H "Content-Type: application/scim+json" \
        -d "$CLIENT_PAYLOAD"
    check "POST /scim/v2/Clients -> 201" 201 "$_STATUS" "$_BODY"
fi

echo "Smoke identity ready:"
echo "  SMOKE_CLIENT_ID=$SMOKE_CLIENT_ID"
echo "  SMOKE_CERT_PATH=$SMOKE_CERT_PATH"
echo "  MCP_REMOTE_SCOPE=$SMOKE_SCOPE"
echo ""

print_result_summary
