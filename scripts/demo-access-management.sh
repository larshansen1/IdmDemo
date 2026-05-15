#!/usr/bin/env bash
set -euo pipefail

# - Usage -
#
#   bash scripts/demo-access-management.sh [-v|--verbose]
#
#   -v / --verbose   Print full request and response for every call.
#
#   Environment overrides:
#     API_BASE_URL   default: http://localhost:5000
#     API_KEY        default: changeme-development-key

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

API="${API_BASE_URL:-http://localhost:5000}"
KEY="${API_KEY:-changeme-development-key}"

TS=$(date +%s)
ROLE_ACTIVE="service-admin-${TS}"
ROLE_INACTIVE="legacy-admin-${TS}"
SCOPE_READ="orders.read.${TS}"
SCOPE_WRITE="orders.write.${TS}"
SCOPE_INACTIVE="orders.legacy.${TS}"
USER_NAME="alice-access-${TS}"
CLIENT_ID="orders-access-${TS}"
WORKDIR=$(mktemp -d)
BODY_FILE="$WORKDIR/body.json"
trap 'rm -rf "$WORKDIR"' EXIT

pass=0
fail=0

_STATUS=""
_BODY=""

do_request() {
    local method="$1" path="$2"; shift 2
    local args=("$@")

    if [ "$VERBOSE" -eq 1 ]; then
        echo ""
        echo "  ┌─ Request ────────────────────────────────────────────────────"
        echo "  │ $method $API$path"
        local i=0 req_body=""
        while [ $i -lt ${#args[@]} ]; do
            case "${args[$i]}" in
                -H) i=$((i+1)); echo "  │ ${args[$i]}" ;;
                -d|--data-urlencode) i=$((i+1)); req_body="${req_body}${req_body:+&}${args[$i]}" ;;
            esac
            i=$((i+1))
        done
        if [ -n "$req_body" ]; then
            echo "  │"
            echo "$req_body" | python3 -m json.tool --indent 2 2>/dev/null \
                | sed 's/^/  │ /' \
                || echo "  │ $req_body"
        fi
    fi

    _STATUS=$(curl -s -o "$BODY_FILE" -w "%{http_code}" \
        -X "$method" "${args[@]}" "$API$path")
    _BODY=$(cat "$BODY_FILE")

    if [ "$VERBOSE" -eq 1 ]; then
        echo "  ├─ Response ───────────────────────────────────────────────────"
        echo "  │ HTTP $_STATUS"
        if [ -n "$_BODY" ]; then
            echo "  │"
            echo "$_BODY" | python3 -m json.tool --indent 2 2>/dev/null \
                | sed 's/^/  │ /' \
                || echo "  │ $_BODY"
        fi
        echo "  └──────────────────────────────────────────────────────────────"
    fi
}

check() {
    local label="$1" expected="$2" actual="$3" body="${4:-}"
    if [ "$actual" -eq "$expected" ]; then
        echo "  ✔  $label (HTTP $actual)"
        pass=$((pass + 1))
    else
        echo "  ✘  $label — expected HTTP $expected, got $actual"
        fail=$((fail + 1))
    fi
    if [ "$VERBOSE" -eq 0 ] && [ -n "$body" ]; then
        local first_line
        first_line=$(echo "$body" | head -1 | cut -c1-120)
        [ -n "$first_line" ] && echo "     $first_line"
    fi
    echo ""
}

check_value() {
    local label="$1" expected="$2" actual="$3"
    if [ "$actual" = "$expected" ]; then
        echo "  ✔  $label"
        pass=$((pass + 1))
    else
        echo "  ✘  $label — expected '$expected', got '$actual'"
        fail=$((fail + 1))
    fi
    echo ""
}

stop_demo() {
    echo "Stopping demo: $1"
    echo ""
    echo "══════════════════════════════════════════"
    echo "  Results: $pass passed, $fail failed"
    echo "══════════════════════════════════════════"
    exit 1
}

require_status() {
    local label="$1" expected="$2" actual="$3" body="${4:-}"
    check "$label" "$expected" "$actual" "$body"
    [ "$actual" -eq "$expected" ] || stop_demo "$label did not return HTTP $expected."
}

json_field() {
    python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('$1',''))" 2>/dev/null || echo ""
}

json_list_count() {
    python3 -c 'import sys,json; d=json.load(sys.stdin); print(len(d.get("resources", [])))' 2>/dev/null || echo "0"
}

jwt_payload_field() {
    python3 - "$1" "$2" <<'PY'
import base64
import json
import sys

token = sys.argv[1]
path = sys.argv[2].split(".")
payload = token.split(".")[1]
payload += "=" * (-len(payload) % 4)
data = json.loads(base64.urlsafe_b64decode(payload.encode()))
value = data
for segment in path:
    value = value[segment]
if isinstance(value, list):
    print(" ".join(value))
else:
    print(value)
PY
}

cert_der_base64() {
    openssl x509 -in "$1" -outform DER | base64 | tr -d '\n'
}

cert_thumbprint() {
    openssl x509 -in "$1" -outform DER \
        | openssl dgst -sha256 -binary \
        | xxd -p -c 256 \
        | tr '[:lower:]' '[:upper:]'
}

header() {
    echo ""
    echo "──────────────────────────────────────────"
    echo "  $1"
    echo "──────────────────────────────────────────"
}

auth_h=(-H "X-Api-Key: $KEY")
scim_ct_h=(-H "Content-Type: application/scim+json")

echo "IdmDemo access management demo"
echo "Base URL : $API"
echo "API key  : $KEY"
[ "$VERBOSE" -eq 1 ] && echo "Mode     : verbose"

header "Create global role catalog"

do_request POST /scim/v2/Roles "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"value\":\"$ROLE_ACTIVE\",\"displayName\":\"Service Admin\",\"description\":\"Active service role\",\"active\":true}"
require_status "POST /scim/v2/Roles active role -> 201" 201 "$_STATUS" "$_BODY"
ROLE_ACTIVE_ID=$(echo "$_BODY" | json_field id)

do_request POST /scim/v2/Roles "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"value\":\"$ROLE_INACTIVE\",\"displayName\":\"Legacy Admin\",\"description\":\"Will be deactivated\",\"active\":true}"
require_status "POST /scim/v2/Roles legacy role -> 201" 201 "$_STATUS" "$_BODY"
ROLE_INACTIVE_ID=$(echo "$_BODY" | json_field id)

do_request PATCH "/scim/v2/Roles/$ROLE_INACTIVE_ID" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"active","value":false}]}'
require_status "PATCH /scim/v2/Roles/{id} active=false -> 200" 200 "$_STATUS" "$_BODY"

ENCODED_ROLE_FILTER=$(python3 -c "import urllib.parse; print(urllib.parse.quote('value eq \"$ROLE_ACTIVE\"'))")
do_request GET "/scim/v2/Roles?filter=$ENCODED_ROLE_FILTER" "${auth_h[@]}"
check "GET /scim/v2/Roles?filter=value eq active role -> 200" 200 "$_STATUS" "$_BODY"
ROLE_FILTER_COUNT=$(echo "$_BODY" | json_list_count)
check_value "Role filter returns one resource" "1" "$ROLE_FILTER_COUNT"

header "Create global scope catalog"

do_request POST /scim/v2/Scopes "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"value\":\"$SCOPE_READ\",\"displayName\":\"Read orders\",\"active\":true}"
require_status "POST /scim/v2/Scopes read scope -> 201" 201 "$_STATUS" "$_BODY"
SCOPE_READ_ID=$(echo "$_BODY" | json_field id)

do_request POST /scim/v2/Scopes "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"value\":\"$SCOPE_WRITE\",\"displayName\":\"Write orders\",\"active\":true}"
require_status "POST /scim/v2/Scopes write scope -> 201" 201 "$_STATUS" "$_BODY"
SCOPE_WRITE_ID=$(echo "$_BODY" | json_field id)

do_request POST /scim/v2/Scopes "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"value\":\"$SCOPE_INACTIVE\",\"displayName\":\"Legacy orders\",\"active\":true}"
require_status "POST /scim/v2/Scopes legacy scope -> 201" 201 "$_STATUS" "$_BODY"
SCOPE_INACTIVE_ID=$(echo "$_BODY" | json_field id)

header "Assignment validation"

do_request POST /scim/v2/Users "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"userName\":\"$USER_NAME\",\"displayName\":\"Alice Access\",\"active\":true,\"assignedRoles\":[\"$ROLE_ACTIVE\"]}"
require_status "POST /scim/v2/Users with active role -> 201" 201 "$_STATUS" "$_BODY"
USER_ID=$(echo "$_BODY" | json_field id)

USER_ROLE=$(python3 -c 'import sys,json; d=json.load(sys.stdin); print(" ".join(d.get("assignedRoles", [])))' <<<"$_BODY")
check_value "User response includes assigned role" "$ROLE_ACTIVE" "$USER_ROLE"

do_request POST /scim/v2/Users "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"userName\":\"bad-user-$TS\",\"assignedRoles\":[\"missing-role-$TS\"]}"
check "POST /scim/v2/Users with unknown role -> 400" 400 "$_STATUS" "$_BODY"

do_request POST /scim/v2/Users "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"userName\":\"inactive-role-user-$TS\",\"assignedRoles\":[\"$ROLE_INACTIVE\"]}"
check "POST /scim/v2/Users with inactive role -> 400" 400 "$_STATUS" "$_BODY"

header "Create machine client with catalog assignments"

do_request PATCH "/scim/v2/Roles/$ROLE_INACTIVE_ID" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"active","value":true}]}'
require_status "PATCH /scim/v2/Roles/{id} active=true for client assignment -> 200" 200 "$_STATUS" "$_BODY"

openssl req \
    -x509 \
    -newkey rsa:2048 \
    -nodes \
    -sha256 \
    -days 1 \
    -subj "/CN=$CLIENT_ID" \
    -keyout "$WORKDIR/client.key" \
    -out "$WORKDIR/client.crt" >/dev/null 2>&1

CERT_DER_BASE64=$(cert_der_base64 "$WORKDIR/client.crt")
CERT_THUMBPRINT=$(cert_thumbprint "$WORKDIR/client.crt")

do_request POST /scim/v2/Clients "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"clientId\":\"$CLIENT_ID\",\"displayName\":\"Orders Access Demo\",\"active\":true,\"certificateThumbprintSha256\":\"$CERT_THUMBPRINT\",\"certificateSubject\":\"CN=$CLIENT_ID\",\"assignedScopes\":[\"$SCOPE_READ\",\"$SCOPE_WRITE\",\"$SCOPE_INACTIVE\"],\"assignedRoles\":[\"$ROLE_ACTIVE\",\"$ROLE_INACTIVE\"]}"
require_status "POST /scim/v2/Clients with active assignments -> 201" 201 "$_STATUS" "$_BODY"
CLIENT_RECORD_ID=$(echo "$_BODY" | json_field id)

do_request POST /scim/v2/Clients "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"clientId\":\"bad-client-$TS\",\"assignedScopes\":[\"missing-scope-$TS\"]}"
check "POST /scim/v2/Clients with unknown scope -> 400" 400 "$_STATUS" "$_BODY"

header "Delete protection for assigned catalog entries"

do_request DELETE "/scim/v2/Roles/$ROLE_ACTIVE_ID" "${auth_h[@]}"
check "DELETE assigned role -> 409" 409 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Scopes/$SCOPE_READ_ID" "${auth_h[@]}"
check "DELETE assigned scope -> 409" 409 "$_STATUS" "$_BODY"

header "Token issuance filters inactive catalog entries"

do_request PATCH "/scim/v2/Roles/$ROLE_INACTIVE_ID" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"active","value":false}]}'
require_status "PATCH /scim/v2/Roles/{id} active=false after assignment -> 200" 200 "$_STATUS" "$_BODY"

do_request PATCH "/scim/v2/Scopes/$SCOPE_INACTIVE_ID" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"active","value":false}]}'
require_status "PATCH /scim/v2/Scopes/{id} active=false -> 200" 200 "$_STATUS" "$_BODY"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID"
require_status "POST /connect/token without requested scope -> 200" 200 "$_STATUS" "$_BODY"

ACCESS_TOKEN=$(echo "$_BODY" | json_field access_token)
TOKEN_SCOPE=$(echo "$_BODY" | json_field scope)
JWT_SCOPE=$(jwt_payload_field "$ACCESS_TOKEN" scope)
JWT_ROLES=$(jwt_payload_field "$ACCESS_TOKEN" roles)
check_value "Token response includes only active assigned scopes" "$SCOPE_READ $SCOPE_WRITE" "$TOKEN_SCOPE"
check_value "JWT scope claim excludes inactive scope" "$SCOPE_READ $SCOPE_WRITE" "$JWT_SCOPE"
check_value "JWT roles claim excludes inactive role" "$ROLE_ACTIVE" "$JWT_ROLES"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=$SCOPE_INACTIVE"
check "POST /connect/token requesting inactive assigned scope -> 400" 400 "$_STATUS" "$_BODY"
ERROR_CODE=$(echo "$_BODY" | json_field error)
check_value "Inactive requested scope returns invalid_scope" "invalid_scope" "$ERROR_CODE"

header "Cleanup"

do_request DELETE "/scim/v2/Clients/$CLIENT_RECORD_ID" "${auth_h[@]}"
check "DELETE demo client -> 204" 204 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Users/$USER_ID" "${auth_h[@]}"
check "DELETE demo user -> 204" 204 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Roles/$ROLE_ACTIVE_ID" "${auth_h[@]}"
check "DELETE unassigned active role -> 204" 204 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Roles/$ROLE_INACTIVE_ID" "${auth_h[@]}"
check "DELETE unassigned inactive role -> 204" 204 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Scopes/$SCOPE_READ_ID" "${auth_h[@]}"
check "DELETE unassigned read scope -> 204" 204 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Scopes/$SCOPE_WRITE_ID" "${auth_h[@]}"
check "DELETE unassigned write scope -> 204" 204 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Scopes/$SCOPE_INACTIVE_ID" "${auth_h[@]}"
check "DELETE unassigned inactive scope -> 204" 204 "$_STATUS" "$_BODY"

echo "══════════════════════════════════════════"
echo "  Results: $pass passed, $fail failed"
echo "══════════════════════════════════════════"
echo ""

[ "$fail" -eq 0 ]
