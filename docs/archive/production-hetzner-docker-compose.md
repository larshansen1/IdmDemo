# Production Setup: Hetzner, Docker Compose, NGINX

This deployment keeps the authorization-server surface and the hosted MCP
resource-server surface separate:

- `auth.idp.madmetal.org` -> `Backend.Api` public OAuth endpoints only.
- `mcp.idp.madmetal.org` -> `Backend.Mcp` hosted MCP endpoint.
- `Backend.Api` administrative routes stay private behind Docker networking and
  the internal MCP-to-API key.

## Files

- `src/Backend.Api/Dockerfile`
- `src/Backend.Mcp/Dockerfile`
- `deploy/production/compose.yml`
- `deploy/production/env.example`
- `deploy/production/deploy.sh`
- `deploy/production/nginx-idmdemo.conf`

## Build Images

Build and push immutable tags from the repository root:

```bash
docker build -f src/Backend.Api/Dockerfile -t ghcr.io/your-org/idmdemo-backend-api:2026-05-25 .
docker build -f src/Backend.Mcp/Dockerfile -t ghcr.io/your-org/idmdemo-backend-mcp:2026-05-25 .
docker push ghcr.io/your-org/idmdemo-backend-api:2026-05-25
docker push ghcr.io/your-org/idmdemo-backend-mcp:2026-05-25
```

## Server Layout

On the Hetzner host:

```bash
sudo mkdir -p /opt/idmdemo
sudo cp deploy/production/compose.yml /opt/idmdemo/compose.yml
sudo cp deploy/production/env.example /opt/idmdemo/.env
sudo chmod 600 /opt/idmdemo/.env
```

Edit `/opt/idmdemo/.env` and set:

- `BACKEND_API_IMAGE`
- `BACKEND_MCP_IMAGE`
- `IDM_ADMIN_API_KEY`
- `MCP_AUDIENCE`
- `MCP_READ_ONLY`

Start or update:

```bash
cd /opt/idmdemo
docker-compose pull
docker-compose up -d
docker-compose ps
```

If the host has Compose v2, use `docker compose ...` instead. The deployment
script supports both forms.

Compose binds app ports to loopback only:

- `127.0.0.1:5000` -> `Backend.Api`
- `127.0.0.1:5100` -> `Backend.Mcp`

Do not publish these ports on a public interface. NGINX is the public edge.

The `volume-permissions` service runs before the backends and then exits. It
repairs ownership on the SQLite data volume and signing-key volume so the
non-root .NET `app` user can create `/data/idm-demo.db`,
`/keys/signing-key.json`, and `/keys/certificate-authority.json`.

### Volume naming

Docker Compose prefixes named volumes with the project name, so the actual
volume names on the host are `idmdemo_idmdemo-data` and `idmdemo_idmdemo-keys`
— **not** the bare `idmdemo-data` / `idmdemo-keys` names from `compose.yml`.
Verify with `docker volume ls | grep idmdemo` before running backup or
inspection commands. Using the bare name against `docker volume inspect` will
silently operate on an empty or non-existent volume.

## NGINX

Install the config:

```bash
sudo cp deploy/production/nginx-idmdemo.conf /etc/nginx/sites-available/idmdemo.conf
sudo ln -s /etc/nginx/sites-available/idmdemo.conf /etc/nginx/sites-enabled/idmdemo.conf
sudo nginx -t
sudo systemctl reload nginx
```

Issue certificates with your preferred Let's Encrypt flow, for example:

```bash
sudo certbot --nginx -d auth.idp.madmetal.org -d mcp.idp.madmetal.org
```

The token endpoint uses forwarded client certificates. NGINX must strip inbound
`X-Client-Cert` and recreate it from TLS state:

```nginx
proxy_set_header X-Client-Cert "";
location = /connect/token {
    proxy_set_header X-Client-Cert $ssl_client_escaped_cert;
    proxy_pass http://127.0.0.1:5000;
}
```

`Backend.Api` accepts that escaped PEM form and still validates the certificate
against the registered machine-client certificate thumbprint.

## Public Routes

Allowed through `auth.idp.madmetal.org`:

- `GET /.well-known/openid-configuration`
- `GET /.well-known/jwks.json`
- `POST /connect/token`

Allowed through `mcp.idp.madmetal.org`:

- `POST /mcp`

`/health/live` and `/health/ready` are restricted to localhost in the sample
NGINX config. Expose them only through your infrastructure policy.

## Smoke Test

Run the existing hosted-production demo against the remote host:

```bash
API_BASE_URL=http://127.0.0.1:5000 \
AUTH_BASE_URL=https://auth.idp.madmetal.org \
MCP_BASE_URL=https://mcp.idp.madmetal.org \
MCP_HEALTH_BASE_URL=http://127.0.0.1:5100 \
API_KEY=<same value as IDM_ADMIN_API_KEY> \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-hosted-production.sh --verbose
```

`API_BASE_URL` is intentionally private because the script creates temporary
scopes and a temporary machine client through administrative API routes. Run the
script on the server, in CI with private network access, or through an SSH tunnel
to `127.0.0.1:5000`. `AUTH_BASE_URL` and `MCP_BASE_URL` should use the public
HTTPS hostnames so token issuance and MCP DPoP proofs are bound to the real
external URLs. For public HTTPS token issuance, the script presents the generated
client certificate with curl `--cert` and `--key`; it does not rely on a
caller-supplied `X-Client-Cert` header. `MCP_HEALTH_BASE_URL` may stay private if
readiness is not exposed publicly.

Expected behavior:

- MCP readiness reports `HostedProduction`.
- DPoP is required.
- bearer development mode is disabled.
- DPoP token issuance succeeds from `auth.idp.madmetal.org`.
- hosted MCP accepts the DPoP-bound token.
- hosted MCP rejects bearer scheme and missing tokens.

## Pipeline Deployment

Use immutable image tags. A deployment job should:

1. Build and push both images.
2. Copy or update `/opt/idmdemo/compose.yml`, `/opt/idmdemo/deploy.sh`, and
   `/opt/idmdemo/.env`.
3. Run `docker compose pull volume-permissions backend-api backend-mcp`.
4. Run `docker compose up -d backend-api backend-mcp`.
5. Wait for `Backend.Api` discovery and `Backend.Mcp` readiness to succeed.
6. Run the smoke test.
7. Prune old images only after the smoke test passes.

The repository CI deploy job expects these GitHub secrets:

- `HETZNER_HOST`
- `HETZNER_USER`
- `HETZNER_SSH_KEY`
- `HETZNER_PORT` when SSH does not use port `22`

Install the server-side deploy script once:

```bash
sudo cp deploy/production/deploy.sh /opt/idmdemo/deploy.sh
sudo chmod 755 /opt/idmdemo/deploy.sh
```

The CI job builds and pushes multi-architecture GHCR images tagged with the Git
SHA and `main`, then runs:

```bash
/opt/idmdemo/deploy.sh <git-sha>
```

Run database migrations before replacing API instances when a deployment includes
schema changes. `Backend.Api` applies EF migrations at startup today, so keep API
rollouts serialized while SQLite remains the production learning-store.

## Security Checks

- Public DNS resolves only to NGINX.
- Direct container ports are not reachable externally.
- `auth.idp.madmetal.org/scim/v2/Clients` returns `404` through NGINX.
- `mcp.idp.madmetal.org/mcp` rejects missing tokens.
- logs do not include access tokens, DPoP proofs, API keys, certificate private
  keys, or CSR private-key material.
