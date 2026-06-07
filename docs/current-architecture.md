# Current Architecture

This document reflects the current deployed IdmDemo architecture as of the
IdP-as-system-of-record refactor, OAuth-admin work, and hosted MCP work.

It is the current runtime source of truth. `product.md` remains the
epic-oriented product roadmap and original requirement record. Documents under
`docs/archive/` are historical planning and deployment notes; they may explain
why earlier decisions were made, but they should not override this file or the
root README for current boundaries.

## Services

IdmDemo currently has three product/architecture roles:

- **Identity Provider / IdP Admin API**: manages users, machine clients, global
  roles, global scopes, and machine-client certificates through private
  SCIM-shaped administrative endpoints.
- **Authorization Server**: exposes public discovery, JWKS, and token issuance.
  Machine clients authenticate with client certificates, and issued JWT access
  tokens use explicit resource audiences.
- **MCP Resource Server / IdP Admin Interface**: exposes IdP administration as
  MCP tools. Hosted MCP validates caller tokens for the MCP audience, enforces
  MCP scopes, and calls the private IdP admin API with a separate configured
  machine-client credential.

`Backend.Api` currently hosts both the Identity Provider admin API and the
Authorization Server endpoints.

`Backend.Mcp` hosts the MCP Resource Server / IdP Admin Interface.

The ownership rule is:

> Anything that writes identity, credential, or catalog state goes through the
> IdP. The Authorization Server and MCP Resource Server are consumers.

The project graph enforces that split:

```text
Backend.Idp.Domain          -> no project dependencies
Backend.As.Domain           -> no project dependencies
Backend.Application         -> Backend.Idp.Domain + Backend.As.Domain
Backend.Infrastructure      -> Backend.Idp.Domain + Backend.As.Domain
Backend.Api                 -> Backend.Application + Backend.Infrastructure
Backend.Mcp                 -> Backend.Application
```

`Backend.Mcp` does not reference `Backend.Infrastructure`; it reaches IdP state
only through the private admin API.

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

MCP validates caller access tokens with the AS public keys exposed at
`/.well-known/jwks.json`. The hosted MCP service fetches those keys through
`IIdmApiClient.GetJwksAsync` and adapts them with `JwksJwtSigningKeyStore`; it
does not read the AS private signing-key file.

## Private Boundary

Administrative SCIM, role, scope, certificate, and access-management endpoints
are private. They are reachable through the internal API listener, not through
the public `auth.idp.madmetal.org` nginx server block.

Administrative API calls require:

- `Authorization: DPoP <token>` with a matching `DPoP` proof header
- token role `scim.admin`

The `scim.admin` role and the configured admin machine client are seeded by
`ScimAdminSeeder`. In production the seeded client is `idm-mcp-backend` by
default and its certificate is stored in the Docker `idmdemo-keys` volume at
`/keys/mcp-client.pem`. This credential is for the MCP service to call the
private admin API; it is not the normal public MCP smoke-test identity.

Production MCP smoke tests use a separate low-privilege machine client,
`idm-mcp-smoke` by default. That client has no roles and only the MCP scope
needed by the smoke test, normally `idm.mcp.read`. Its private key stays with
the operator or runner through `SMOKE_CERT_PATH`; IdmDemo stores only the public
certificate metadata and thumbprint.

## Production Endpoint Matrix

| Host | Route | Exposure | Expected auth |
| --- | --- | --- | --- |
| `auth.idp.madmetal.org` | `/.well-known/openid-configuration` | Public | None |
| `auth.idp.madmetal.org` | `/.well-known/jwks.json` | Public | None |
| `auth.idp.madmetal.org` | `/connect/token` | Public | Client certificate, optional DPoP |
| `auth.idp.madmetal.org` | `/scim/v2/*` | Not public | Returns nginx `404` publicly |
| `mcp.idp.madmetal.org` | `/mcp` | Public | DPoP access token |
| `mcp.idp.madmetal.org` | `/health/live`, `/health/ready` | Private/ops only | nginx allows localhost only |
| `127.0.0.1:5000` on deploy host | API admin routes | Private | `scim.admin` DPoP token |
| `127.0.0.1:5100` on deploy host | MCP health/routes | Private | Profile-dependent |

## Token Flow

1. A registered machine client presents its certificate to
   `POST /connect/token`.
2. The AS asks the IdP-owned `IIssuanceContextProvider` read port to resolve
   current client validity, certificate status, active scopes, and active roles.
3. The AS assembles token claims, resource audience, confirmation binding, and
   lifetime, then signs the JWT with the AS signing key store.
4. Issued JWT access tokens use header `typ: at+jwt`, and token validators
   require that type.
5. Without a `DPoP` proof, the token response is a certificate-bound bearer
   token with `cnf.x5t#S256`.
6. With a valid `DPoP` proof, the token response has `token_type=DPoP` and
   `cnf.jkt`.
7. Hosted production MCP accepts only DPoP-bound calls to `/mcp`.
8. MCP validates caller tokens against AS JWKS public keys and rejects tokens
   signed by unknown keys.

## Ownership Details

### Identity Provider

The IdP owns:

- users
- machine clients
- global roles and scopes
- machine-client certificates and revocation state
- the local certificate authority
- the admin API write paths

It also implements the AS read port:

```text
IIssuanceContextProvider -> IdpIssuanceContextProvider
```

That provider resolves the current issuance context from IdP state. The AS sees
only the resolved `IssuanceContext`; it does not query identity repositories
directly.

### Authorization Server

The AS owns:

- discovery and JWKS metadata
- `client_credentials` token issuance
- token claim shape and `typ: at+jwt`
- resource audience resolution
- certificate-bound and DPoP-bound confirmation claims
- JWT signing keys and DPoP replay validation

`ResolveAudience` remains AS-side. It uses the active scopes already supplied in
`IssuanceContext`; it does not reach back into IdP persistence.

### MCP Resource Server

Hosted MCP owns:

- MCP transport and `/mcp` request handling
- caller token validation for the MCP audience
- DPoP enforcement in `HostedProduction`
- per-tool `idm.mcp.*` scope checks
- audit events and mutation guards
- private admin API calls using its configured backend machine-client
  credential

MCP is not a persistence or signing-key owner. It validates tokens with AS JWKS
public keys and performs IdP administration through the private API.

## Smoke Tests

Remote public auth smoke from any machine with the production smoke PEM:

```bash
curl -sS \
  --cert ./idmdemo-prod-mcp-smoke-client.pem \
  --key ./idmdemo-prod-mcp-smoke-client.pem \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "client_id=idm-mcp-smoke" \
  --data-urlencode "scope=idm.mcp.read" \
  --data-urlencode "resource=idm-demo-mcp" \
  https://auth.idp.madmetal.org/connect/token
```

This proves public remote client-certificate authentication and token issuance.

Bootstrap or update the persistent low-privilege smoke identity from the
deployment host. The script is dry-run by default; use `--apply` after reviewing
the planned changes.

```bash
docker compose cp backend-api:/keys/mcp-client.pem /tmp/idmdemo-prod-admin-client.pem
chmod 600 /tmp/idmdemo-prod-admin-client.pem

ADMIN_CLIENT_ID=idm-mcp-backend \
ADMIN_CERT_PATH=/tmp/idmdemo-prod-admin-client.pem \
API_BASE_URL=http://127.0.0.1:5000 \
AUTH_BASE_URL=http://127.0.0.1:5000 \
SMOKE_CLIENT_ID=idm-mcp-smoke \
SMOKE_CERT_PATH=/tmp/idmdemo-prod-mcp-smoke-client.pem \
SMOKE_SCOPE=idm.mcp.read \
bash scripts/bootstrap-mcp-production-smoke.sh --apply
```

Full hosted production smoke from the deployment host:

```bash
SMOKE_CLIENT_ID=idm-mcp-smoke \
SMOKE_CERT_PATH=/tmp/idmdemo-prod-mcp-smoke-client.pem \
MCP_REMOTE_SCOPE=idm.mcp.read \
AUTH_BASE_URL=https://auth.idp.madmetal.org \
AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org \
MCP_BASE_URL=https://mcp.idp.madmetal.org \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-remote-production-smoke.sh -v
```

Use the remote-only smoke script when private health and SCIM setup are not
available:

```bash
SMOKE_CLIENT_ID=idm-mcp-smoke \
SMOKE_CERT_PATH=./idmdemo-prod-mcp-smoke-client.pem \
MCP_REMOTE_SCOPE=idm.mcp.read \
AUTH_BASE_URL=https://auth.idp.madmetal.org \
AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org \
MCP_BASE_URL=https://mcp.idp.madmetal.org \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-remote-production-smoke.sh -v
```

That script skips private health checks and private SCIM setup. A tool call
requires the client identity to already have the required MCP scope.

After the smoke test, the DPoP-bound access token expires naturally. The
`idm-mcp-smoke` client and certificate remain for repeatable smoke checks until
the operator rotates or revokes the smoke certificate.
