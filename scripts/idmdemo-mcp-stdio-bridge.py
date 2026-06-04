#!/usr/bin/env python3
"""Stdio MCP bridge for IdmDemo HostedProduction.

Codex speaks MCP over stdio to this process. The bridge obtains a DPoP-bound
client-credentials token from IdmDemo and forwards each JSON-RPC request to the
hosted MCP endpoint with a fresh resource DPoP proof.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from typing import Any


AUTH_BASE_URL = os.environ["IDMDEMO_AUTH_BASE_URL"].rstrip("/")
MCP_URL = os.environ["IDMDEMO_MCP_URL"]
CLIENT_ID = os.environ["IDMDEMO_CLIENT_ID"]
CLIENT_CERT = os.environ["IDMDEMO_CLIENT_CERT"]
CLIENT_KEY = os.environ["IDMDEMO_CLIENT_KEY"]
DPOP_KEY = os.environ["IDMDEMO_DPOP_KEY"]
SCOPE = os.environ.get("IDMDEMO_SCOPE", "idm.mcp.read")
AUDIENCE = os.environ.get("IDMDEMO_AUDIENCE", "idm-demo-mcp")
PROTOCOL_VERSION = os.environ.get("IDMDEMO_PROTOCOL_VERSION", "2025-06-18")

TOKEN_ENDPOINT = f"{AUTH_BASE_URL}/connect/token"
_TOKEN: str | None = None
_TOKEN_EXPIRES_AT = 0


def b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("ascii").rstrip("=")


def b64url_json(value: dict[str, Any]) -> str:
    return b64url(json.dumps(value, separators=(",", ":")).encode("utf-8"))


def openssl_text(*args: str) -> str:
    return subprocess.check_output(["openssl", *args], text=True, stderr=subprocess.DEVNULL).strip()


def rsa_public_jwk(key_path: str) -> dict[str, str]:
    modulus_hex = openssl_text("rsa", "-in", key_path, "-noout", "-modulus").split("=", 1)[1]
    key_text = openssl_text("rsa", "-in", key_path, "-text", "-noout")
    exponent = 65537
    for line in key_text.splitlines():
        if "publicExponent:" in line:
            exponent = int(line.split("publicExponent:", 1)[1].split()[0])
            break

    exponent_bytes = exponent.to_bytes(max(1, (exponent.bit_length() + 7) // 8), "big")
    return {
        "kty": "RSA",
        "n": b64url(bytes.fromhex(modulus_hex)),
        "e": b64url(exponent_bytes),
    }


def sign_rs256(key_path: str, signing_input: str) -> str:
    with tempfile.NamedTemporaryFile("wb", delete=False) as handle:
        handle.write(signing_input.encode("ascii"))
        input_path = handle.name

    try:
        signature = subprocess.check_output(
            ["openssl", "dgst", "-sha256", "-sign", key_path, "-binary", input_path],
            stderr=subprocess.DEVNULL,
        )
        return b64url(signature)
    finally:
        Path(input_path).unlink(missing_ok=True)


def create_dpop_proof(method: str, uri: str, access_token: str | None = None) -> str:
    header = {
        "typ": "dpop+jwt",
        "alg": "RS256",
        "jwk": rsa_public_jwk(DPOP_KEY),
    }
    payload: dict[str, Any] = {
        "htm": method,
        "htu": uri,
        "jti": str(uuid.uuid4()),
        "iat": int(time.time()),
    }
    if access_token:
        payload["ath"] = b64url(hashlib.sha256(access_token.encode("ascii")).digest())

    signing_input = f"{b64url_json(header)}.{b64url_json(payload)}"
    return f"{signing_input}.{sign_rs256(DPOP_KEY, signing_input)}"


def jwt_payload(token: str) -> dict[str, Any]:
    payload = token.split(".")[1]
    payload += "=" * (-len(payload) % 4)
    return json.loads(base64.urlsafe_b64decode(payload.encode("ascii")))


def request_token() -> str:
    global _TOKEN, _TOKEN_EXPIRES_AT

    now = int(time.time())
    if _TOKEN and now < _TOKEN_EXPIRES_AT - 60:
        return _TOKEN

    form = urllib.parse.urlencode(
        {
            "grant_type": "client_credentials",
            "client_id": CLIENT_ID,
            "scope": SCOPE,
            "resource": AUDIENCE,
        }
    ).encode("ascii")
    context = ssl.create_default_context()
    context.load_cert_chain(CLIENT_CERT, CLIENT_KEY)
    request = urllib.request.Request(
        TOKEN_ENDPOINT,
        data=form,
        method="POST",
        headers={
            "Content-Type": "application/x-www-form-urlencoded",
            "DPoP": create_dpop_proof("POST", TOKEN_ENDPOINT),
        },
    )
    with urllib.request.urlopen(request, context=context, timeout=20) as response:
        body = json.loads(response.read().decode("utf-8"))

    _TOKEN = body["access_token"]
    _TOKEN_EXPIRES_AT = int(jwt_payload(_TOKEN).get("exp", now + 300))
    return _TOKEN


def parse_mcp_response(body: str) -> str:
    data_lines = []
    for line in body.splitlines():
        if line.startswith("data:"):
            value = line[5:].strip()
            if value and value != "[DONE]":
                data_lines.append(value)
    return "\n".join(data_lines) if data_lines else body


def post_mcp(payload: str) -> str:
    token = request_token()
    request = urllib.request.Request(
        MCP_URL,
        data=payload.encode("utf-8"),
        method="POST",
        headers={
            "Accept": "application/json, text/event-stream",
            "Content-Type": "application/json",
            "MCP-Protocol-Version": PROTOCOL_VERSION,
            "Authorization": f"DPoP {token}",
            "DPoP": create_dpop_proof("POST", MCP_URL, token),
        },
    )
    with urllib.request.urlopen(request, timeout=20) as response:
        return parse_mcp_response(response.read().decode("utf-8"))


def error_response(request_id: Any, message: str) -> str:
    return json.dumps(
        {
            "jsonrpc": "2.0",
            "id": request_id,
            "error": {"code": -32000, "message": message},
        },
        separators=(",", ":"),
    )


def main() -> int:
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        request_id = None
        try:
            request_id = json.loads(line).get("id")
            response = post_mcp(line)
        except Exception as exc:  # noqa: BLE001 - bridge must report errors as JSON-RPC.
            response = error_response(request_id, str(exc))

        for item in response.splitlines():
            if item:
                print(item, flush=True)

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
