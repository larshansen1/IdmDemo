#!/usr/bin/env bash
set -euo pipefail

# ── Usage ─────────────────────────────────────────────────────────────────────
#
#   bash scripts/demo-api.sh [-v|--verbose]
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

# Unique suffix so the script is idempotent against a persistent DB
TS=$(date +%s)
USER1="alice_${TS}"
USER2="bob_${TS}"
CLIENT1="orders-service-${TS}"

pass=0
fail=0

# ── Helpers ───────────────────────────────────────────────────────────────────

# Results are stored in these globals so do_request can also print verbose
# output without conflicting with stdout capture.
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
                -d) i=$((i+1)); req_body="${args[$i]}" ;;
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

    _STATUS=$(curl -s -o /tmp/_demo_body.json -w "%{http_code}" \
        -X "$method" "${args[@]}" "$API$path")
    _BODY=$(cat /tmp/_demo_body.json)

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
    local label="$1" expected="$2" actual="$3" body="$4"
    if [ "$actual" -eq "$expected" ]; then
        echo "  ✔  $label (HTTP $actual)"
        pass=$((pass + 1))
    else
        echo "  ✘  $label — expected HTTP $expected, got $actual"
        fail=$((fail + 1))
    fi
    if [ "$VERBOSE" -eq 0 ]; then
        local first_line
        first_line=$(echo "$body" | head -1 | cut -c1-120)
        [ -n "$first_line" ] && echo "     $first_line"
    fi
    echo ""
}

json_field() {
    python3 -c "import sys,json; d=json.load(sys.stdin); print(d.get('$1',''))" 2>/dev/null || echo ""
}

header() {
    echo ""
    echo "──────────────────────────────────────────"
    echo "  $1"
    echo "──────────────────────────────────────────"
}

auth_h=(-H "X-Api-Key: $KEY")
ct_h=(-H "Content-Type: application/scim+json")

# ── Main ──────────────────────────────────────────────────────────────────────

echo "IdmDemo API demo"
echo "Base URL : $API"
echo "API key  : $KEY"
[ "$VERBOSE" -eq 1 ] && echo "Mode     : verbose"

# ── Auth ──────────────────────────────────────────────────────────────────────

header "Authentication"

do_request GET /scim/v2/Users
check "No API key → 401" 401 "$_STATUS" "$_BODY"

do_request GET /scim/v2/Users -H "X-Api-Key: wrong-key"
check "Wrong API key → 401" 401 "$_STATUS" "$_BODY"

# ── Users — create ────────────────────────────────────────────────────────────

header "Users — create"

do_request POST /scim/v2/Users "${auth_h[@]}" "${ct_h[@]}" \
    -d "{\"userName\":\"$USER1\",\"displayName\":\"Alice Smith\",\"active\":true}"
check "POST /scim/v2/Users → 201" 201 "$_STATUS" "$_BODY"
ALICE_ID=$(echo "$_BODY" | json_field id)

do_request POST /scim/v2/Users "${auth_h[@]}" "${ct_h[@]}" \
    -d "{\"userName\":\"$USER2\",\"displayName\":\"Bob Jones\",\"active\":true}"
check "POST /scim/v2/Users (second user) → 201" 201 "$_STATUS" "$_BODY"
BOB_ID=$(echo "$_BODY" | json_field id)

# ── Users — validation errors ─────────────────────────────────────────────────

header "Users — validation errors"

do_request POST /scim/v2/Users "${auth_h[@]}" "${ct_h[@]}" \
    -d "{\"userName\":\"$USER1\",\"displayName\":\"Duplicate\",\"active\":true}"
check "Duplicate userName → 409" 409 "$_STATUS" "$_BODY"

do_request POST /scim/v2/Users "${auth_h[@]}" "${ct_h[@]}" \
    -d '{"displayName":"No Name","active":true}'
check "Missing userName → 400" 400 "$_STATUS" "$_BODY"

do_request POST /scim/v2/Users "${auth_h[@]}" "${ct_h[@]}" \
    -d '{"userName":"   ","active":true}'
check "Whitespace-only userName → 400" 400 "$_STATUS" "$_BODY"

# ── Users — list & filter ─────────────────────────────────────────────────────

header "Users — list and filter"

do_request GET /scim/v2/Users "${auth_h[@]}"
check "GET /scim/v2/Users (list all) → 200" 200 "$_STATUS" "$_BODY"

ENCODED_FILTER=$(python3 -c "import urllib.parse; print(urllib.parse.quote('userName eq \"$USER1\"'))")
do_request GET "/scim/v2/Users?filter=$ENCODED_FILTER" "${auth_h[@]}"
check "GET ?filter=userName eq \"$USER1\" → 200" 200 "$_STATUS" "$_BODY"

do_request GET "/scim/v2/Users?filter=email%20eq%20%22x%40x.com%22" "${auth_h[@]}"
check "Unsupported filter attribute → 400" 400 "$_STATUS" "$_BODY"

# ── Users — get by ID ─────────────────────────────────────────────────────────

header "Users — get by ID"

do_request GET "/scim/v2/Users/$ALICE_ID" "${auth_h[@]}"
check "GET /scim/v2/Users/{id} → 200" 200 "$_STATUS" "$_BODY"

do_request GET "/scim/v2/Users/00000000-0000-0000-0000-000000000000" "${auth_h[@]}"
check "GET non-existent ID → 404" 404 "$_STATUS" "$_BODY"

# ── Users — PUT ───────────────────────────────────────────────────────────────

header "Users — PUT (full replace)"

do_request PUT "/scim/v2/Users/$ALICE_ID" "${auth_h[@]}" "${ct_h[@]}" \
    -d "{\"userName\":\"$USER1\",\"displayName\":\"Alice Updated\",\"active\":true}"
check "PUT /scim/v2/Users/{id} → 200" 200 "$_STATUS" "$_BODY"

# ── Users — PATCH ─────────────────────────────────────────────────────────────

header "Users — PATCH"

do_request PATCH "/scim/v2/Users/$ALICE_ID" "${auth_h[@]}" "${ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"displayName","value":"Alice Patched"}]}'
check "PATCH replace displayName → 200" 200 "$_STATUS" "$_BODY"

do_request PATCH "/scim/v2/Users/$ALICE_ID" "${auth_h[@]}" "${ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"active","value":false}]}'
check "PATCH deactivate (active=false) → 200" 200 "$_STATUS" "$_BODY"

do_request PATCH "/scim/v2/Users/$ALICE_ID" "${auth_h[@]}" "${ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"add","path":"displayName","value":"x"}]}'
check "PATCH unsupported op 'add' → 400" 400 "$_STATUS" "$_BODY"

# ── Users — DELETE ────────────────────────────────────────────────────────────

header "Users — DELETE"

do_request DELETE "/scim/v2/Users/$BOB_ID" "${auth_h[@]}"
check "DELETE /scim/v2/Users/{id} → 204" 204 "$_STATUS" "$_BODY"

do_request GET "/scim/v2/Users/$BOB_ID" "${auth_h[@]}"
check "GET deleted user → 404" 404 "$_STATUS" "$_BODY"

# ── Machine Clients ───────────────────────────────────────────────────────────

header "Clients — create"

do_request POST /scim/v2/Clients "${auth_h[@]}" "${ct_h[@]}" \
    -d "{\"clientId\":\"$CLIENT1\",\"displayName\":\"Orders Service\",\"active\":true}"
check "POST /scim/v2/Clients → 201" 201 "$_STATUS" "$_BODY"
CLIENT_ID=$(echo "$_BODY" | json_field id)

do_request POST /scim/v2/Clients "${auth_h[@]}" "${ct_h[@]}" \
    -d "{\"clientId\":\"$CLIENT1\",\"displayName\":\"Duplicate\",\"active\":true}"
check "Duplicate clientId → 409" 409 "$_STATUS" "$_BODY"

header "Clients — PATCH and DELETE"

do_request PATCH "/scim/v2/Clients/$CLIENT_ID" "${auth_h[@]}" "${ct_h[@]}" \
    -d '{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"active","value":false}]}'
check "PATCH deactivate client → 200" 200 "$_STATUS" "$_BODY"

do_request DELETE "/scim/v2/Clients/$CLIENT_ID" "${auth_h[@]}"
check "DELETE /scim/v2/Clients/{id} → 204" 204 "$_STATUS" "$_BODY"

# ── Cleanup ───────────────────────────────────────────────────────────────────

header "Cleanup"

[ -n "$ALICE_ID" ] && {
    do_request DELETE "/scim/v2/Users/$ALICE_ID" "${auth_h[@]}"
    check "DELETE alice → 204" 204 "$_STATUS" "$_BODY"
}

rm -f /tmp/_demo_body.json

# ── Summary ───────────────────────────────────────────────────────────────────

echo "══════════════════════════════════════════"
echo "  Results: $pass passed, $fail failed"
echo "══════════════════════════════════════════"
echo ""

[ "$fail" -eq 0 ]
