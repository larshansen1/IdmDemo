#!/usr/bin/env bash

PROTOCOL_VERSION="${PROTOCOL_VERSION:-2025-06-18}"
API="${API_BASE_URL:-http://localhost:5000}"
AUTH="${AUTH_BASE_URL:-$API}"
MCP="${MCP_BASE_URL:-http://localhost:5100}"
MCP_AUDIENCE="${MCP_AUDIENCE:-idm-demo-mcp}"
AUTH_DPOP="${AUTH_DPOP_BASE_URL:-$AUTH}"
ADMIN_CLIENT_ID="${ADMIN_CLIENT_ID:-idm-admin}"
ADMIN_CERT_PATH="${ADMIN_CERT_PATH:-admin-client.pem}"
ADMIN_TOKEN=""

WORKDIR="${WORKDIR:-$(mktemp -d)}"
BODY_FILE="$WORKDIR/body.txt"

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

    if [ "${VERBOSE:-0}" -eq 1 ]; then
        echo ""
        echo "  |- Request -------------------------------------------"
        echo "  | $method $url"
        local i=0 req_body=""
        while [ $i -lt ${#args[@]} ]; do
            case "${args[$i]}" in
                -H) i=$((i+1)); echo "  | ${args[$i]}" ;;
                -d|--data-urlencode) i=$((i+1)); req_body="${req_body}${req_body:+ }${args[$i]}" ;;
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

    local curl_exit=0
    : > "$BODY_FILE"
    if _STATUS=$(curl -sS -o "$BODY_FILE" -w "%{http_code}" -X "$method" "${args[@]}" "$url"); then
        curl_exit=0
    else
        curl_exit=$?
    fi

    if [ "$curl_exit" -ne 0 ]; then
        _STATUS=000
        _BODY="curl failed with exit code $curl_exit while requesting $method $url"
        if [ -s "$BODY_FILE" ]; then
            _BODY="$_BODY
$(cat "$BODY_FILE")"
        fi
    else
        _BODY=$(cat "$BODY_FILE")
    fi

    if [ "${VERBOSE:-0}" -eq 1 ]; then
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
    if [ "${VERBOSE:-0}" -eq 0 ] && [ -n "$body" ]; then
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

check_value() {
    local label="$1" expected="$2" actual="$3"
    if [ "$actual" = "$expected" ]; then
        echo "  OK   $label"
        pass=$((pass + 1))
    else
        echo "  FAIL $label - expected '$expected', got '$actual'"
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

try:
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
except Exception:
    pass
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
    local access_token="${4:-}"
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
    payload=$(python3 - "$method" "$uri" "$access_token" <<'PY'
import base64
import hashlib
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
if sys.argv[3]:
    payload["ath"] = base64.urlsafe_b64encode(
        hashlib.sha256(sys.argv[3].encode("ascii")).digest()
    ).decode().rstrip("=")
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

acquire_admin_token() {
    if [ -z "$ADMIN_CERT_PATH" ] || [ ! -f "$ADMIN_CERT_PATH" ]; then
        echo "  ERROR Cannot acquire admin token: ADMIN_CERT_PATH='$ADMIN_CERT_PATH' not found." >&2
        echo "  Hint: start Backend.Api with ScimAdmin:SeedClientId and ScimAdmin:GenerateCertIfMissing=true" >&2
        echo "        then set ADMIN_CERT_PATH to the generated cert path (default: admin-client.pem)." >&2
        exit 1
    fi

    local cert_b64
    cert_b64=$(openssl x509 -in "$ADMIN_CERT_PATH" -outform DER | base64 | tr -d '\n')

    local status body
    body=$(curl -sS -o /dev/null -w "%{http_code}" -X POST "$AUTH/connect/token" \
        -H "X-Client-Cert: $cert_b64" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        --data-urlencode "grant_type=client_credentials" \
        --data-urlencode "client_id=$ADMIN_CLIENT_ID" 2>&1) || true
    status="$body"

    body=$(curl -sS -X POST "$AUTH/connect/token" \
        -H "X-Client-Cert: $cert_b64" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        --data-urlencode "grant_type=client_credentials" \
        --data-urlencode "client_id=$ADMIN_CLIENT_ID")

    ADMIN_TOKEN=$(echo "$body" | python3 -c 'import sys,json; print(json.load(sys.stdin)["access_token"])' 2>/dev/null || true)

    if [ -z "$ADMIN_TOKEN" ]; then
        echo "  ERROR Failed to acquire admin token (check that '$ADMIN_CLIENT_ID' is registered with scim.admin role)." >&2
        echo "  Response: $body" >&2
        exit 1
    fi
}

generate_client_certificate() {
    local client_id="$1"

    openssl req \
        -x509 \
        -newkey rsa:2048 \
        -nodes \
        -sha256 \
        -days 1 \
        -subj "/CN=$client_id" \
        -keyout "$WORKDIR/client.key" \
        -out "$WORKDIR/client.crt" >/dev/null 2>&1

    CLIENT_CERT_DER_BASE64=$(openssl x509 -in "$WORKDIR/client.crt" -outform DER | base64 | tr -d '\n')
    CLIENT_CERT_THUMBPRINT=$(openssl x509 -in "$WORKDIR/client.crt" -outform DER \
        | openssl dgst -sha256 -binary \
        | xxd -p -c 256 \
        | tr '[:lower:]' '[:upper:]')
}

ensure_scope() {
    local scope="$1"

    do_request "Create scope $scope" POST "$API/scim/v2/Scopes" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H "Content-Type: application/scim+json" \
        -d "{\"value\":\"$scope\",\"displayName\":\"$scope\",\"active\":true}"

    if [ "$_STATUS" -eq 201 ] || [ "$_STATUS" -eq 409 ]; then
        echo "  OK   Scope $scope is available (HTTP $_STATUS)"
        pass=$((pass + 1))
    else
        echo "  FAIL Scope $scope setup failed - HTTP $_STATUS"
        fail=$((fail + 1))
    fi
    echo ""
}

create_mcp_demo_client() {
    local client_id="$1"
    local display_name="$2"

    generate_client_certificate "$client_id"
    do_request "Create MCP demo client" POST "$API/scim/v2/Clients" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H "Content-Type: application/scim+json" \
        -d "{\"clientId\":\"$client_id\",\"displayName\":\"$display_name\",\"active\":true,\"certificateThumbprintSha256\":\"$CLIENT_CERT_THUMBPRINT\",\"certificateSubject\":\"CN=$client_id\",\"assignedScopes\":[\"idm.mcp.read\",\"idm.mcp.write\",\"idm.mcp.destructive\",\"idm.mcp.certificates\"],\"assignedRoles\":[]}"
    check "POST /scim/v2/Clients -> 201" 201 "$_STATUS" "$_BODY"
    CLIENT_RECORD_ID=$(echo "$_BODY" | json_field id)
}

cleanup_mcp_demo_client() {
    if [ -n "${CLIENT_RECORD_ID:-}" ]; then
        do_request "Delete MCP demo client" DELETE "$API/scim/v2/Clients/$CLIENT_RECORD_ID" \
            -H "Authorization: Bearer $ADMIN_TOKEN"
        check "DELETE /scim/v2/Clients/{id} -> 204" 204 "$_STATUS" "$_BODY"
    fi
}

issue_bearer_token() {
    local client_id="$1" scope="$2"
    local cert_args=()

    token_client_certificate_args cert_args

    do_request "Issue bearer token" POST "$AUTH/connect/token" \
        "${cert_args[@]}" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        --data-urlencode "grant_type=client_credentials" \
        --data-urlencode "client_id=$client_id" \
        --data-urlencode "scope=$scope" \
        --data-urlencode "resource=$MCP_AUDIENCE"
    check "POST /connect/token bearer -> 200" 200 "$_STATUS" "$_BODY"

    ACCESS_TOKEN=$(echo "$_BODY" | json_field access_token)
    TOKEN_TYPE=$(echo "$_BODY" | json_field token_type)
    TOKEN_SCOPE=$(echo "$_BODY" | json_field scope)
    TOKEN_AUDIENCE=$(jwt_payload_field "$ACCESS_TOKEN" aud)
}

issue_dpop_token() {
    local client_id="$1" scope="$2"
    local cert_args=()

    openssl genpkey \
        -algorithm RSA \
        -pkeyopt rsa_keygen_bits:2048 \
        -out "$WORKDIR/dpop.key" >/dev/null 2>&1

    DPOP_TOKEN_PROOF=$(create_dpop_proof POST "$AUTH_DPOP/connect/token" "$WORKDIR/dpop.key")
    DPOP_JKT=$(jwk_thumbprint "$WORKDIR/dpop.key")
    token_client_certificate_args cert_args

    do_request "Issue DPoP token" POST "$AUTH/connect/token" \
        "${cert_args[@]}" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -H "DPoP: $DPOP_TOKEN_PROOF" \
        --data-urlencode "grant_type=client_credentials" \
        --data-urlencode "client_id=$client_id" \
        --data-urlencode "scope=$scope" \
        --data-urlencode "resource=$MCP_AUDIENCE"
    check "POST /connect/token DPoP -> 200" 200 "$_STATUS" "$_BODY"

    ACCESS_TOKEN=$(echo "$_BODY" | json_field access_token)
    TOKEN_TYPE=$(echo "$_BODY" | json_field token_type)
    TOKEN_SCOPE=$(echo "$_BODY" | json_field scope)
    TOKEN_AUDIENCE=$(jwt_payload_field "$ACCESS_TOKEN" aud)
    TOKEN_JKT=$(jwt_payload_field "$ACCESS_TOKEN" 'cnf.jkt')
}

token_client_certificate_args() {
    local -n args_ref="$1"
    args_ref=(-H "X-Client-Cert: $CLIENT_CERT_DER_BASE64")
}

mcp_post_with_auth() {
    local label="$1" payload="$2" scheme="$3" token="$4"
    local auth_args=()

    if [ "$scheme" = "DPoP" ]; then
        local proof
        proof=$(create_dpop_proof POST "$MCP/mcp" "$WORKDIR/dpop.key" "$token")
        auth_args=(-H "Authorization: DPoP $token" -H "DPoP: $proof")
    else
        auth_args=(-H "Authorization: Bearer $token")
    fi

    do_request "$label" POST "$MCP/mcp" \
        -H "Accept: application/json, text/event-stream" \
        -H "Content-Type: application/json" \
        -H "MCP-Protocol-Version: $PROTOCOL_VERSION" \
        "${auth_args[@]}" \
        -d "$payload"
    _BODY=$(echo "$_BODY" | extract_mcp_json)
}

mcp_post_without_auth() {
    local label="$1" payload="$2"

    do_request "$label" POST "$MCP/mcp" \
        -H "Accept: application/json, text/event-stream" \
        -H "Content-Type: application/json" \
        -H "MCP-Protocol-Version: $PROTOCOL_VERSION" \
        -d "$payload"
    _BODY=$(echo "$_BODY" | extract_mcp_json)
}

print_result_summary() {
    echo "=========================================="
    echo "  Results: $pass passed, $fail failed"
    echo "=========================================="
    echo ""

    [ "$fail" -eq 0 ]
}
