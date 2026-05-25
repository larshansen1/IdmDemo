#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 1 ]; then
    echo "Usage: $0 <image-tag> [backend-api-image] [backend-mcp-image]" >&2
    exit 2
fi

TAG="$1"
BACKEND_API_IMAGE="${2:-ghcr.io/larshansen1/idmdemo-backend-api:$TAG}"
BACKEND_MCP_IMAGE="${3:-ghcr.io/larshansen1/idmdemo-backend-mcp:$TAG}"
DEPLOY_DIR="${IDMDEMO_DEPLOY_DIR:-/opt/idmdemo}"

cd "$DEPLOY_DIR"

if [ ! -f .env ]; then
    echo "Missing $DEPLOY_DIR/.env. Create it from deploy/production/env.example first." >&2
    exit 1
fi

set_env() {
    key="$1"
    value="$2"

    if grep -q "^$key=" .env; then
        sed -i "s|^$key=.*|$key=$value|" .env
    else
        printf '%s=%s\n' "$key" "$value" >> .env
    fi
}

if docker compose version >/dev/null 2>&1; then
    compose() { docker compose "$@"; }
elif command -v docker-compose >/dev/null 2>&1; then
    compose() { docker-compose "$@"; }
else
    echo "Neither 'docker compose' nor 'docker-compose' is installed." >&2
    exit 1
fi

set_env BACKEND_API_IMAGE "$BACKEND_API_IMAGE"
set_env BACKEND_MCP_IMAGE "$BACKEND_MCP_IMAGE"

compose pull backend-api backend-mcp
compose up -d backend-api backend-mcp
compose ps
