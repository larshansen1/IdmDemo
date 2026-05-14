#!/usr/bin/env bash
set -euo pipefail

# - Usage -
#
#   bash scripts/demo-certificates.sh [-v|--verbose]
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
CLIENT_ID="orders-certs-${TS}"
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
        echo "  --- Request ---"
        echo "  - $method $API$path"
        local i=0 req_body=""
        while [ $i -lt ${#args[@]} ]; do
            case "${args[$i]}" in
                -H) i=$((i+1)); echo "  - ${args[$i]}" ;;
                -d|--data-urlencode) i=$((i+1)); req_body="${req_body}${req_body:+&}${args[$i]}" ;;
            esac
            i=$((i+1))
        done
        if [ -n "$req_body" ]; then
            echo "  -"
            echo "$req_body" | python3 -m json.tool --indent 2 2>/dev/null \
                | sed 's/^/  - /' \
                || echo "  - $req_body"
        fi
    fi

    _STATUS=$(curl -s -o "$BODY_FILE" -w "%{http_code}" \
        -X "$method" "${args[@]}" "$API$path")
    _BODY=$(cat "$BODY_FILE")

    if [ "$VERBOSE" -eq 1 ]; then
        echo "  --- Response ---"
        echo "  - HTTP $_STATUS"
        if [ -n "$_BODY" ]; then
            echo "  -"
            echo "$_BODY" | python3 -m json.tool --indent 2 2>/dev/null \
                | sed 's/^/  - /' \
                || echo "  - $_BODY"
        fi
        echo "  -"
    fi
}

check() {
    local label="$1" expected="$2" actual="$3" body="${4:-}"
    if [ "$actual" -eq "$expected" ]; then
        echo "  [OK] $label (HTTP $actual)"
        pass=$((pass + 1))
    else
        echo "  [FAIL] $label - expected HTTP $expected, got $actual"
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
        echo "  [OK] $label"
        pass=$((pass + 1))
    else
        echo "  [FAIL] $label - expected '$expected', got '$actual'"
        fail=$((fail + 1))
    fi
    echo ""
}

stop_demo() {
    echo "Stopping demo: $1"
    echo ""
    echo "------------------------------------------"
    echo "  Results: $pass passed, $fail failed"
    echo "------------------------------------------"
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

json_first_resource_field() {
    python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('resources', [{}])[0].get('$1',''))" 2>/dev/null || echo ""
}

json_field_to_file() {
    local field="$1" output="$2"
    python3 -c "import sys,json; d=json.load(sys.stdin); open('$output','w',encoding='utf-8').write(d.get('$field',''))" <<<"$_BODY" 2>/dev/null
}

json_string_file() {
    python3 - "$1" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    print(json.dumps(handle.read()))
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
    echo "-"
    echo "  $1"
    echo "-"
}

auth_h=(-H "X-Api-Key: $KEY")
scim_ct_h=(-H "Content-Type: application/scim+json")

echo "IdmDemo certificate lifecycle demo"
echo "Base URL : $API"
echo "API key  : $KEY"
[ "$VERBOSE" -eq 1 ] && echo "Mode     : verbose"

header "Create machine client"

do_request POST /scim/v2/Clients "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"clientId\":\"$CLIENT_ID\",\"displayName\":\"Orders Certificate Demo\",\"active\":true,\"assignedScopes\":[\"orders.read\"],\"assignedRoles\":[\"service-admin\"]}"
require_status "POST /scim/v2/Clients -> 201" 201 "$_STATUS" "$_BODY"
CLIENT_RECORD_ID=$(echo "$_BODY" | json_field id)

header "Read local development CA"

do_request GET /scim/v2/Certificates/Authority "${auth_h[@]}"
require_status "GET /scim/v2/Certificates/Authority -> 200" 200 "$_STATUS" "$_BODY"
CA_SUBJECT=$(echo "$_BODY" | json_field subject)
CA_PEM=$(echo "$_BODY" | json_field certificatePem)
check_value "CA subject is local development CA" "CN=IdmDemo Local Development CA" "$CA_SUBJECT"
case "$CA_PEM" in
    *"BEGIN CERTIFICATE"*)
        echo "  [OK] CA response includes public certificate PEM"
        pass=$((pass + 1))
        ;;
    *)
        echo "  [FAIL] CA response did not include public certificate PEM"
        fail=$((fail + 1))
        ;;
esac
echo ""

header "Issue certificate from CSR"

openssl req \
    -new \
    -newkey rsa:2048 \
    -nodes \
    -sha256 \
    -subj "/CN=$CLIENT_ID" \
    -keyout "$WORKDIR/csr-client.key" \
    -out "$WORKDIR/csr-client.csr" >/dev/null 2>&1

CSR_JSON=$(json_string_file "$WORKDIR/csr-client.csr")
do_request POST "/scim/v2/Clients/$CLIENT_RECORD_ID/Certificates" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"mode\":\"csr\",\"certificateSigningRequestPem\":$CSR_JSON,\"displayName\":\"CSR issued cert\",\"validityDays\":30}"
require_status "POST /scim/v2/Clients/{id}/Certificates with CSR -> 201" 201 "$_STATUS" "$_BODY"
CSR_CERT_ID=$(echo "$_BODY" | json_field id)
CSR_CERT_STATUS=$(echo "$_BODY" | json_field status)
CSR_CERT_THUMBPRINT=$(echo "$_BODY" | json_field thumbprintSha256)
json_field_to_file certificatePem "$WORKDIR/csr-client.crt"
check_value "CSR-issued certificate status is Active" "Active" "$CSR_CERT_STATUS"
check_value "CSR-issued certificate has SHA-256 thumbprint length" "64" "${#CSR_CERT_THUMBPRINT}"

CSR_CERT_DER_BASE64=$(cert_der_base64 "$WORKDIR/csr-client.crt")

header "Register external certificate"

openssl req \
    -x509 \
    -newkey rsa:2048 \
    -nodes \
    -sha256 \
    -days 30 \
    -subj "/CN=$CLIENT_ID-external" \
    -keyout "$WORKDIR/external-client.key" \
    -out "$WORKDIR/external-client.crt" >/dev/null 2>&1

EXTERNAL_CERT_JSON=$(json_string_file "$WORKDIR/external-client.crt")
EXTERNAL_CERT_THUMBPRINT=$(cert_thumbprint "$WORKDIR/external-client.crt")
do_request POST "/scim/v2/Clients/$CLIENT_RECORD_ID/Certificates" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d "{\"mode\":\"external\",\"certificatePem\":$EXTERNAL_CERT_JSON,\"displayName\":\"External cert\"}"
check "POST /scim/v2/Clients/{id}/Certificates with external cert -> 201" 201 "$_STATUS" "$_BODY"
REGISTERED_EXTERNAL_THUMBPRINT=$(echo "$_BODY" | json_field thumbprintSha256)
check_value "External certificate thumbprint matches registration" "$EXTERNAL_CERT_THUMBPRINT" "$REGISTERED_EXTERNAL_THUMBPRINT"

EXTERNAL_CERT_DER_BASE64=$(cert_der_base64 "$WORKDIR/external-client.crt")

header "List and get certificates"

do_request GET "/scim/v2/Clients/$CLIENT_RECORD_ID/Certificates" "${auth_h[@]}"
check "GET /scim/v2/Clients/{id}/Certificates -> 200" 200 "$_STATUS" "$_BODY"
CERT_COUNT=$(echo "$_BODY" | json_list_count)
FIRST_CERT_PEM=$(echo "$_BODY" | json_first_resource_field certificatePem)
check_value "List returns both active certificates" "2" "$CERT_COUNT"
case "$FIRST_CERT_PEM" in
    *"BEGIN CERTIFICATE"*)
        echo "  [OK] List response includes full public certificate PEM"
        pass=$((pass + 1))
        ;;
    *)
        echo "  [FAIL] List response did not include full public certificate PEM"
        fail=$((fail + 1))
        ;;
esac
echo ""

do_request GET "/scim/v2/Clients/$CLIENT_RECORD_ID/Certificates/$CSR_CERT_ID" "${auth_h[@]}"
check "GET /scim/v2/Clients/{id}/Certificates/{certificateId} -> 200" 200 "$_STATUS" "$_BODY"
FETCHED_CERT_ID=$(echo "$_BODY" | json_field id)
check_value "Fetched certificate id matches CSR certificate id" "$CSR_CERT_ID" "$FETCHED_CERT_ID"

header "Issue tokens with managed certificates"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CSR_CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "Token request with CSR-issued certificate -> 200" 200 "$_STATUS" "$_BODY"
TOKEN_SCOPE=$(echo "$_BODY" | json_field scope)
check_value "CSR token response scope is orders.read" "orders.read" "$TOKEN_SCOPE"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $EXTERNAL_CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "Token request with external registered certificate -> 200" 200 "$_STATUS" "$_BODY"

header "Revoke certificate"

do_request POST "/scim/v2/Clients/$CLIENT_RECORD_ID/Certificates/$CSR_CERT_ID/Revoke" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d '{"reason":"rotation complete"}'
check "POST /Certificates/{certificateId}/Revoke -> 200" 200 "$_STATUS" "$_BODY"
REVOKED_STATUS=$(echo "$_BODY" | json_field status)
check_value "Revoked certificate status is Revoked" "Revoked" "$REVOKED_STATUS"

do_request POST "/scim/v2/Clients/$CLIENT_RECORD_ID/Certificates/$CSR_CERT_ID/Revoke" "${auth_h[@]}" "${scim_ct_h[@]}" \
    -d '{"reason":"rotation complete"}'
check "Revoking the same certificate again is idempotent -> 200" 200 "$_STATUS" "$_BODY"
REVOKED_STATUS=$(echo "$_BODY" | json_field status)
check_value "Second revoke still returns Revoked" "Revoked" "$REVOKED_STATUS"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $CSR_CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "Token request with revoked certificate -> 401" 401 "$_STATUS" "$_BODY"
ERROR_CODE=$(echo "$_BODY" | json_field error)
check_value "Revoked certificate returns invalid_client" "invalid_client" "$ERROR_CODE"

do_request POST /connect/token \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -H "X-Client-Cert: $EXTERNAL_CERT_DER_BASE64" \
    --data-urlencode "grant_type=client_credentials" \
    --data-urlencode "client_id=$CLIENT_ID" \
    --data-urlencode "scope=orders.read"
check "Other active certificate still works after revocation -> 200" 200 "$_STATUS" "$_BODY"

header "Cleanup"

do_request DELETE "/scim/v2/Clients/$CLIENT_RECORD_ID" "${auth_h[@]}"
check "DELETE /scim/v2/Clients/{id} -> 204" 204 "$_STATUS" "$_BODY"

echo "------------------------------------------"
echo "  Results: $pass passed, $fail failed"
echo "------------------------------------------"
echo ""

[ "$fail" -eq 0 ]
