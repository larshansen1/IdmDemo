# Current Architecture

This document reflects the current deployed IdmDemo architecture as of the
OAuth-admin and hosted MCP work.

## Services

`Backend.Api` is the authorization server and private administrative API.

`Backend.Mcp` is the public hosted MCP resource server. It validates IdmDemo
access tokens, enforces hosted MCP scopes, and calls the private administrative
API using its configured machine-client identity.

Production runs both services with Docker Compose:

- `backend-api` listens internally on `127.0.0.1:5000`.
- `backend-mcp` listens internally on `127.0.0.1:5100`.
- nginx exposes selected public routes on `auth.idp.madmetal.org` and
  `mcp.idp.madmetal.org`.

## Public Boundary

Public `https://auth.idp.madmetal.org` exposes only:

```text
GET  /.well-known/openid-configuration
GET  /.well-known/jwks.json
POST /connect/token
```

Token requests authenticate machine clients with a client certificate. nginx
terminates TLS with `ssl_verify_client optional_no_ca`, forwards the presented
certificate to `Backend.Api` as `X-Client-Cert`, and strips caller-supplied
security headers before proxying.

Public `https://mcp.idp.madmetal.org` exposes:

```text
POST /mcp
```

Hosted production MCP requires an IdmDemo access token with audience
`idm-demo-mcp` and requires DPoP for MCP calls.

## Private Boundary

Administrative SCIM, role, scope, certificate, and access-management endpoints
are private. They are reachable through the internal API listener, not through
the public `auth.idp.madmetal.org` nginx server block.

Administrative API calls require:

- `Authorization: Bearer <token>`
- token role `scim.admin`

The `scim.admin` role and the configured admin machine client are seeded by
`ScimAdminSeeder`. In production the seeded client is `idm-mcp-backend` by
default and its certificate is stored in the Docker `idmdemo-keys` volume at
`/keys/mcp-client.pem`.

## Production Endpoint Matrix

| Host | Route | Exposure | Expected auth |
| --- | --- | --- | --- |
| `auth.idp.madmetal.org` | `/.well-known/openid-configuration` | Public | None |
| `auth.idp.madmetal.org` | `/.well-known/jwks.json` | Public | None |
| `auth.idp.madmetal.org` | `/connect/token` | Public | Client certificate, optional DPoP |
| `auth.idp.madmetal.org` | `/scim/v2/*` | Not public | Returns nginx `404` publicly |
| `mcp.idp.madmetal.org` | `/mcp` | Public | DPoP access token |
| `mcp.idp.madmetal.org` | `/health/live`, `/health/ready` | Private/ops only | nginx allows localhost only |
| `127.0.0.1:5000` on deploy host | API admin routes | Private | `scim.admin` bearer token |
| `127.0.0.1:5100` on deploy host | MCP health/routes | Private | Profile-dependent |

## Token Flow

1. A registered machine client presents its certificate to
   `POST /connect/token`.
2. `Backend.Api` validates the certificate thumbprint against the machine-client
   record and optional managed certificate collection.
3. Without a `DPoP` proof, the token response is a certificate-bound bearer
   token with `cnf.x5t#S256`.
4. With a valid `DPoP` proof, the token response has `token_type=DPoP` and
   `cnf.jkt`.
5. Hosted production MCP accepts only DPoP-bound calls to `/mcp`.

## Smoke Tests

Remote public auth smoke from any machine with the production admin PEM:

```bash
curl -sS \
  --cert ./idmdemo-prod-admin-client.pem \
  --key ./idmdemo-prod-admin-client.pem \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "client_id=idm-mcp-backend" \
  https://auth.idp.madmetal.org/connect/token
```

This proves public remote client-certificate authentication and token issuance.

Full hosted production smoke from the deployment host:

```bash
docker compose cp backend-api:/keys/mcp-client.pem /tmp/idmdemo-prod-admin-client.pem
chmod 600 /tmp/idmdemo-prod-admin-client.pem

ADMIN_CLIENT_ID=idm-mcp-backend \
ADMIN_CERT_PATH=/tmp/idmdemo-prod-admin-client.pem \
API_BASE_URL=http://127.0.0.1:5000 \
AUTH_BASE_URL=http://127.0.0.1:5000 \
AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org \
MCP_BASE_URL=https://mcp.idp.madmetal.org \
MCP_HEALTH_BASE_URL=http://127.0.0.1:5100 \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-hosted-production.sh -v
```

Use the remote-only smoke script when private health and SCIM setup are not
available:

```bash
ADMIN_CLIENT_ID=idm-mcp-backend \
ADMIN_CERT_PATH=./idmdemo-prod-admin-client.pem \
AUTH_BASE_URL=https://auth.idp.madmetal.org \
AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org \
MCP_BASE_URL=https://mcp.idp.madmetal.org \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-remote-production-smoke.sh -v
```

That script skips private health checks and private SCIM setup. A tool call
requires the client identity to already have the required MCP scope.
