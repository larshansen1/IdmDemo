#!/usr/bin/env bash
set -euo pipefail

# ── Usage ─────────────────────────────────────────────────────────────────────
#
#   bash scripts/demo-auth.sh [-v|--verbose]
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
CLIENT_ID="orders-auth-${TS}"
WORKDIR=$(mktemp -d)
trap 'rm -rf "$WORKDIR" /tmp/_demo_auth_body.json' EXIT

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
        [ -n "$req_body" ] && echo "  │" && echo "  │ $req_body"
    fi

    _STATUS=$(curl -s -o /tmp/_demo_auth_body.json -w "%{http_code}" \
        -X "$method" "${args[@]}" "$API$path")
    _BODY=$(cat /tmp/_demo_auth_body.json)

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

json_field() {
    python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('$1',''))" 2>/dev/null || echo ""
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

header() {
    echo ""
    echo "──────────────────────────────────────────"
    echo "  $1"
    echo "──────────────────────────────────────────"
}

auth_h=(-H "X-Api-Key: $KEY")
scim_ct_h=(-H "Content-Type: application/scim+json")

echo "IdmDemo auth server demo"
echo "Base URL : $API"
echo "API key  : $KEY"
[ "$VERBOSE" -eq 1 ] && echo "Mode     : verbose"

header "Generate client certificate"

openssl req \
    -x509 \
    -newkey rsa:2048 \
    -nodes \
    -sha256 \
    -days 1 \
    -subj "/CN=$CLIENT_ID" \
    -keyout "$WORKDIR/client.key" \
    -out "$WORKDIR/client.crt" >/dev/null 2>&1

CERT_DER_BASE64=$(openssl x509 -in "$WORKDIR/client.crt" -outform DER | base64 | tr -d '\n')
CERT_THUMBPRINT=$(openssl x509 -in "$WORKDIR/client.crt" -outform DER \
    | openssl dgst -sha256 -binary \
    | xxd -p -c 256 \
    | tr '[:lower:]' '[:upper:]')
CERT_BOUND_THUMBPRINT=$(openssl x509 -in "$WORKDIR/client.crt" -outform DER \
    | openssl dgst -sha256 -binary \
    | python3 -c 'import base64,sys; print(base64.urlsafe_b64encode(sys.stdin.buffer.read()).decode().rstrip("="))')

echo "  ✔  Generated self-signed certificate for CN=$CLIENT_ID"
echo "     SHA-256 thumbprint: $CERT_THUMBPRINT"
pass=$((pass + 1))
echo ""

header "Discovery and JWKS"

do_request GET /.well-known/openid-configuration
check "GET /.well-known/openid-configuration → 200" 200 "$_STATUS" "$_BODY"

TOKEN_ENDPOINT=$(echo "$_BODY" | json_field token_endpoint)
AUTH_METHODS=$(python3 -c 'import sys,json; d=json.load(sys.stdin); print(" ".join(d.get("token_endpoint_auth_methods_supported", [])))' <<<"$_BODY")
MTLS_BOUND=$(python3 -c 'import sys,json; d=json.load(sys.stdin); print(str(d.get("tls_client_certificate_bound_access_tokens", False)).lower())' <<<"$_BODY")
check_value "Discovery advertises self_signed_tls_client_auth" "self_signed_tls_client_auth" "$AUTH_METHODS"
check_value "Discovery advertises certificate-bound access tokens" "true" "$MTLS_BOUND"

do_request GET /.well-known/jwks.json
check "GET /.well-known/jwks.json → 200" 200 "$_STATUS" "$_BODY"
KEY_COUNT=$(python3 -c 'import sys,json; d=json.load(sys.stdin); print(len(d.get("keys", [])))' <<<"$_BODY")
check_value "JWKS exposes one active RSA signing key" "1" "$KEY_COUNT"

header "Register machine client"

do_request POST /scim/v2/Clients "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"clientId\":\"$CLIENT_ID\",\"displayName\":\"Orders Auth Demo\",\"active\":true,\"certificateThumbprintSha256\":\"$CERT_THUMBPRINT\",\"certificateSubject\":\"CN=$CLIENT_ID\",\"assignedScopes\":[\"orders.read\",\"orders.write\"],\"assignedRoles\":[\"service-admin\"]}"
check "POST /scim/v2/Clients with certificate metadata → 201" 201 "$_STATUS" "$_BODY"
CLIENT_RECORD_ID=$(echo "$_BODY" | json_field id)

header "Issue certificate-bound JWT"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "POST /connect/token with matching client certificate → 200" 200 "$_STATUS" "$_BODY"

ACCESS_TOKEN=$(echo "$_BODY" | json_field access_token)
TOKEN_TYPE=$(echo "$_BODY" | json_field token_type)
TOKEN_SCOPE=$(echo "$_BODY" | json_field scope)
check_value "Token response type is Bearer" "Bearer" "$TOKEN_TYPE"
check_value "Token response scope is narrowed to orders.read" "orders.read" "$TOKEN_SCOPE"

JWT_ISSUER=$(jwt_payload_field "$ACCESS_TOKEN" iss)
JWT_CLIENT_ID=$(jwt_payload_field "$ACCESS_TOKEN" client_id)
JWT_SUBJECT=$(jwt_payload_field "$ACCESS_TOKEN" sub)
JWT_SCOPE=$(jwt_payload_field "$ACCESS_TOKEN" scope)
JWT_ROLES=$(jwt_payload_field "$ACCESS_TOKEN" roles)
JWT_CNF=$(jwt_payload_field "$ACCESS_TOKEN" 'cnf.x5t#S256')

check_value "JWT client_id matches registered client" "$CLIENT_ID" "$JWT_CLIENT_ID"
check_value "JWT subject is SCIM client record id" "$CLIENT_RECORD_ID" "$JWT_SUBJECT"
check_value "JWT scope claim contains granted scope" "orders.read" "$JWT_SCOPE"
check_value "JWT roles claim contains assigned role" "service-admin" "$JWT_ROLES"
check_value "JWT cnf.x5t#S256 matches presented certificate" "$CERT_BOUND_THUMBPRINT" "$JWT_CNF"

if [ "$VERBOSE" -eq 0 ]; then
    echo "  JWT issuer       : $JWT_ISSUER"
    echo "  Token endpoint   : $TOKEN_ENDPOINT"
    echo "  Access token head: ${ACCESS_TOKEN:0:80}..."
    echo ""
fi

header "OAuth error behavior"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "Missing certificate → 401" 401 "$_STATUS" "$_BODY"
ERROR_CODE=$(echo "$_BODY" | json_field error)
check_value "Missing certificate returns invalid_client" "invalid_client" "$ERROR_CODE"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=payments.read"
check "Unassigned scope → 400" 400 "$_STATUS" "$_BODY"
ERROR_CODE=$(echo "$_BODY" | json_field error)
check_value "Unassigned scope returns invalid_scope" "invalid_scope" "$ERROR_CODE"

header "Cleanup"

do_request DELETE "/scim/v2/Clients/$CLIENT_RECORD_ID" "${auth_h[@]}"
check "DELETE /scim/v2/Clients/{id} → 204" 204 "$_STATUS" "$_BODY"

echo "══════════════════════════════════════════"
echo "  Results: $pass passed, $fail failed"
echo "══════════════════════════════════════════"
echo ""

[ "$fail" -eq 0 ]
