#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 1 ]; then
    echo "Usage: $0 <image-tag> [backend-api-image] [backend-mcp-image]" >&2
    exit 2
fi

TAG="$1"
BACKEND_API_IMAGE="${2:-ghcr.io/larshansen1/idmdemo-backend-api:$TAG}"
BACKEND_MCP_IMAGE="${3:-ghcr.io/larshansen1/idmdemo-backend-mcp:$TAG}"
DEPLOY_DIR="${IDMDEMO_DEPLOY_DIR:-/home/admin/docker/idmdemo}"

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

compose pull volume-permissions backend-api backend-mcp
compose up -d backend-api backend-mcp

wait_for_url() {
    name="$1"
    url="$2"
    attempts="${3:-30}"
    delay_seconds="${4:-2}"

    for attempt in $(seq 1 "$attempts"); do
        if curl -fsS --max-time 2 "$url" >/dev/null; then
            echo "$name is ready."
            return 0
        fi

        echo "Waiting for $name at $url ($attempt/$attempts)..."
        sleep "$delay_seconds"
    done

    echo "$name did not become ready at $url." >&2
    compose ps >&2
    compose logs --tail=100 backend-api backend-mcp >&2
    return 1
}

wait_for_url "Backend.Api" "http://127.0.0.1:5000/.well-known/openid-configuration"
wait_for_url "Backend.Mcp" "http://127.0.0.1:5100/health/ready"
compose ps
