#!/usr/bin/env bash
set -euo pipefail

# ── Usage ─────────────────────────────────────────────────────────────────────
#
#   bash scripts/demo-auth.sh [-v|--verbose]
#
#   -v / --verbose   Print full request and response for every call.
#
#   Environment overrides:
#     API_BASE_URL       default: http://localhost:5000
#     ADMIN_CLIENT_ID    default: idm-admin
#     ADMIN_CERT_PATH    default: admin-client.pem  (PEM cert+key for scim.admin client)

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
ADMIN_CLIENT_ID="${ADMIN_CLIENT_ID:-idm-admin}"
ADMIN_CERT_PATH="${ADMIN_CERT_PATH:-admin-client.pem}"
ADMIN_TOKEN=""

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

ensure_scope() {
    local scope="$1"

    do_request POST /scim/v2/Scopes "${auth_h[@]}" "${scim_ct_h[@]}" \
        -d "{\"value\":\"$scope\",\"displayName\":\"$scope\",\"active\":true}"

    if [ "$_STATUS" -eq 201 ] || [ "$_STATUS" -eq 409 ]; then
        echo "  ✔  Scope $scope is available (HTTP $_STATUS)"
        pass=$((pass + 1))
    else
        echo "  ✘  Scope $scope setup failed — HTTP $_STATUS"
        [ "$VERBOSE" -eq 0 ] && [ -n "$_BODY" ] && echo "     $(echo "$_BODY" | head -1 | cut -c1-120)"
        fail=$((fail + 1))
        exit 1
    fi
    echo ""
}

ensure_role() {
    local role="$1"

    do_request POST /scim/v2/Roles "${auth_h[@]}" "${scim_ct_h[@]}" \
        -d "{\"value\":\"$role\",\"displayName\":\"$role\",\"active\":true}"

    if [ "$_STATUS" -eq 201 ] || [ "$_STATUS" -eq 409 ]; then
        echo "  ✔  Role $role is available (HTTP $_STATUS)"
        pass=$((pass + 1))
    else
        echo "  ✘  Role $role setup failed — HTTP $_STATUS"
        [ "$VERBOSE" -eq 0 ] && [ -n "$_BODY" ] && echo "     $(echo "$_BODY" | head -1 | cut -c1-120)"
        fail=$((fail + 1))
        exit 1
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

base64url_stdin() {
    python3 -c 'import base64,sys; print(base64.urlsafe_b64encode(sys.stdin.buffer.read()).decode().rstrip("="))'
}

base64url_hex() {
    python3 - "$1" <<'PY'
import base64
import sys

print(base64.urlsafe_b64encode(bytes.fromhex(sys.argv[1])).decode().rstrip("="))
PY
}

create_dpop_proof() {
    local method="$1" uri="$2" key_path="$3"
    local modulus_hex exponent_dec jwk_n jwk_e header payload signing_input signature

    modulus_hex=$(openssl rsa -in "$key_path" -noout -modulus 2>/dev/null | cut -d= -f2)
    exponent_dec=$(openssl rsa -in "$key_path" -text -noout 2>/dev/null \
        | awk '/publicExponent/ { print $2; exit }')
    jwk_n=$(base64url_hex "$modulus_hex")
    jwk_e=$(python3 - "$exponent_dec" <<'PY'
import base64
import sys

value = int(sys.argv[1])
length = max(1, (value.bit_length() + 7) // 8)
print(base64.urlsafe_b64encode(value.to_bytes(length, "big")).decode().rstrip("="))
PY
)

    header=$(python3 - "$jwk_n" "$jwk_e" <<'PY'
import base64
import json
import sys

header = {
    "typ": "dpop+jwt",
    "alg": "RS256",
    "jwk": {
        "kty": "RSA",
        "n": sys.argv[1],
        "e": sys.argv[2],
    },
}
print(base64.urlsafe_b64encode(json.dumps(header, separators=(",", ":")).encode()).decode().rstrip("="))
PY
)
    payload=$(python3 - "$method" "$uri" <<'PY'
import base64
import json
import sys
import time
import uuid

payload = {
    "htm": sys.argv[1],
    "htu": sys.argv[2],
    "jti": str(uuid.uuid4()),
    "iat": int(time.time()),
}
print(base64.urlsafe_b64encode(json.dumps(payload, separators=(",", ":")).encode()).decode().rstrip("="))
PY
)
    signing_input="$header.$payload"
    printf '%s' "$signing_input" > "$WORKDIR/dpop-signing-input.txt"
    signature=$(openssl dgst -sha256 -sign "$key_path" -binary "$WORKDIR/dpop-signing-input.txt" | base64url_stdin)
    printf '%s.%s' "$signing_input" "$signature"
}

jwk_thumbprint() {
    local key_path="$1" modulus_hex exponent_dec jwk_n jwk_e
    modulus_hex=$(openssl rsa -in "$key_path" -noout -modulus 2>/dev/null | cut -d= -f2)
    exponent_dec=$(openssl rsa -in "$key_path" -text -noout 2>/dev/null \
        | awk '/publicExponent/ { print $2; exit }')
    jwk_n=$(base64url_hex "$modulus_hex")
    jwk_e=$(python3 - "$exponent_dec" <<'PY'
import base64
import sys

value = int(sys.argv[1])
length = max(1, (value.bit_length() + 7) // 8)
print(base64.urlsafe_b64encode(value.to_bytes(length, "big")).decode().rstrip("="))
PY
)
    python3 - "$jwk_n" "$jwk_e" <<'PY'
import base64
import hashlib
import sys

canonical = f'{{"e":"{sys.argv[2]}","kty":"RSA","n":"{sys.argv[1]}"}}'
print(base64.urlsafe_b64encode(hashlib.sha256(canonical.encode()).digest()).decode().rstrip("="))
PY
}

header() {
    echo ""
    echo "──────────────────────────────────────────"
    echo "  $1"
    echo "──────────────────────────────────────────"
}

scim_ct_h=(-H "Content-Type: application/scim+json")

echo "IdmDemo auth server demo"
echo "Base URL     : $API"
echo "Admin client : $ADMIN_CLIENT_ID"
echo "Admin cert   : $ADMIN_CERT_PATH"
[ "$VERBOSE" -eq 1 ] && echo "Mode     : verbose"

header "Admin token"

[ -f "$ADMIN_CERT_PATH" ] || { echo "ERROR: ADMIN_CERT_PATH='$ADMIN_CERT_PATH' not found." >&2; exit 1; }
_admin_cert_b64=$(openssl x509 -in "$ADMIN_CERT_PATH" -outform DER | base64 | tr -d '\n')
_admin_resp=$(curl -sS -X POST "$API/connect/token" \
    -H "X-Client-Cert: $_admin_cert_b64" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$ADMIN_CLIENT_ID")
ADMIN_TOKEN=$(echo "$_admin_resp" | python3 -c 'import sys,json; print(json.load(sys.stdin)["access_token"])' 2>/dev/null || true)
[ -n "$ADMIN_TOKEN" ] || { echo "ERROR: Failed to acquire admin token. Response: $_admin_resp" >&2; exit 1; }
auth_h=(-H "Authorization: Bearer $ADMIN_TOKEN")
echo "  OK   Acquired scim.admin bearer token"
echo ""

header "Access catalog setup"
ensure_scope "orders.read"
ensure_scope "orders.write"
ensure_role "service-admin"

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
DPOP_ALGS=$(python3 -c 'import sys,json; d=json.load(sys.stdin); print(" ".join(d.get("dpop_signing_alg_values_supported", [])))' <<<"$_BODY")
check_value "Discovery advertises self_signed_tls_client_auth" "self_signed_tls_client_auth" "$AUTH_METHODS"
check_value "Discovery advertises certificate-bound access tokens" "true" "$MTLS_BOUND"
check_value "Discovery advertises DPoP signing algorithms" "ES256 RS256" "$DPOP_ALGS"

do_request GET /.well-known/jwks.json
check "GET /.well-known/jwks.json → 200" 200 "$_STATUS" "$_BODY"
KEY_COUNT=$(python3 -c 'import sys,json; d=json.load(sys.stdin); print(len(d.get("keys", [])))' <<<"$_BODY")
check_value "JWKS exposes one active RSA signing key" "1" "$KEY_COUNT"

header "Register machine client"

do_request POST /scim/v2/Clients "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"clientId\":\"$CLIENT_ID\",\"displayName\":\"Orders Auth Demo\",\"active\":true,\"certificateThumbprintSha256\":\"$CERT_THUMBPRINT\",\"certificateSubject\":\"CN=$CLIENT_ID\",\"assignedScopes\":[\"orders.read\",\"orders.write\"],\"assignedRoles\":[\"service-admin\"]}"
check "POST /scim/v2/Clients with certificate metadata → 201" 201 "$_STATUS" "$_BODY"
if [ "$_STATUS" -ne 201 ]; then
    echo "Stopping demo: client registration failed."
    print_result_summary
    exit 1
fi
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

header "Issue DPoP-bound JWT"

openssl genpkey \
    -algorithm RSA \
    -pkeyopt rsa_keygen_bits:2048 \
    -out "$WORKDIR/dpop.key" >/dev/null 2>&1

DPOP_PROOF=$(create_dpop_proof POST "$TOKEN_ENDPOINT" "$WORKDIR/dpop.key")
DPOP_JKT=$(jwk_thumbprint "$WORKDIR/dpop.key")

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CERT_DER_BASE64" \
    -H "DPoP: $DPOP_PROOF" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "POST /connect/token with mTLS and DPoP proof → 200" 200 "$_STATUS" "$_BODY"

DPOP_ACCESS_TOKEN=$(echo "$_BODY" | json_field access_token)
DPOP_TOKEN_TYPE=$(echo "$_BODY" | json_field token_type)
DPOP_JWT_CNF=$(jwt_payload_field "$DPOP_ACCESS_TOKEN" 'cnf.jkt')
check_value "DPoP token response type is DPoP" "DPoP" "$DPOP_TOKEN_TYPE"
check_value "JWT cnf.jkt matches DPoP public key thumbprint" "$DPOP_JKT" "$DPOP_JWT_CNF"

if [ "$VERBOSE" -eq 0 ]; then
    echo "  DPoP JWK thumb : $DPOP_JKT"
    echo "  DPoP token head: ${DPOP_ACCESS_TOKEN:0:80}..."
    echo ""
fi

header "DPoP replay protection"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CERT_DER_BASE64" \
    -H "DPoP: $DPOP_PROOF" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "Reusing the same DPoP proof is rejected → 400" 400 "$_STATUS" "$_BODY"
ERROR_CODE=$(echo "$_BODY" | json_field error)
check_value "Replayed proof returns invalid_dpop_proof" "invalid_dpop_proof" "$ERROR_CODE"

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
