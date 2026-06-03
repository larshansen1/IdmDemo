# IdmDemo

A simple Identity Provider and Authorization Server built for learning, private experimentation, and agentic development workflows.

Implements clean architecture, explicit security boundaries, structured observability, automated testing, and enforced quality gates from the start.

---

## What it does

Epic 1 (complete) — SCIM-inspired administrative APIs for managing users and machine clients, protected by API-key authentication.

Epic 2 (complete) — OAuth-style client credentials token issuance for machine clients authenticated with mTLS, including JWT access tokens, discovery metadata, and JWKS.

Epic 3 (complete) — Machine-client certificate lifecycle APIs, including CSR-based issuance, external certificate registration, listing, lookup, and revocation.

Epic 4 (complete) — Optional DPoP-bound access token issuance, DPoP proof validation, replay protection, and reusable downstream DPoP access-token validation services.

Epic 5 (complete) — Global role and scope catalog management, user role assignment, machine-client role/scope assignment validation, and active catalog filtering during token issuance.

Epic 6 (complete) — MCP administrative interface over stdio for user, machine-client, role, scope, certificate, discovery, and JWKS operations.

Epic 7 (complete) — Hosted MCP profiles for local and production agent workflows, DPoP-bound hosted access, MCP tool authorization, audit events, and higher-level machine-client credential workflow tools.

See [product.md](product.md) for the full roadmap and
[docs/current-architecture.md](docs/current-architecture.md) for the current
runtime and production boundary.

---

## API

Administrative SCIM, certificate, role, scope, and access-management endpoints require `Authorization: Bearer <token>` where the token has the `scim.admin` role. They use `Content-Type: application/scim+json`.

Authorization-server discovery and JWKS are public. Token requests use OAuth-style form encoding and OAuth-style error responses.

### Users

```
POST   /scim/v2/Users
GET    /scim/v2/Users?filter=userName eq "alice"
GET    /scim/v2/Users/{id}
PUT    /scim/v2/Users/{id}
PATCH  /scim/v2/Users/{id}
DELETE /scim/v2/Users/{id}
```

### Machine Clients

```
POST   /scim/v2/Clients
GET    /scim/v2/Clients?filter=clientId eq "orders-service"
GET    /scim/v2/Clients/{id}
PUT    /scim/v2/Clients/{id}
PATCH  /scim/v2/Clients/{id}
DELETE /scim/v2/Clients/{id}
```

### Global Roles

```
POST   /scim/v2/Roles
GET    /scim/v2/Roles?filter=value eq "service-admin"
GET    /scim/v2/Roles/{id}
PUT    /scim/v2/Roles/{id}
PATCH  /scim/v2/Roles/{id}
DELETE /scim/v2/Roles/{id}
```

### Global Scopes

```
POST   /scim/v2/Scopes
GET    /scim/v2/Scopes?filter=value eq "orders.read"
GET    /scim/v2/Scopes/{id}
PUT    /scim/v2/Scopes/{id}
PATCH  /scim/v2/Scopes/{id}
DELETE /scim/v2/Scopes/{id}
```

### Client Certificates

```
GET    /scim/v2/Certificates/Authority
POST   /scim/v2/Clients/{clientRecordId}/Certificates
GET    /scim/v2/Clients/{clientRecordId}/Certificates
GET    /scim/v2/Clients/{clientRecordId}/Certificates/{certificateId}
POST   /scim/v2/Clients/{clientRecordId}/Certificates/{certificateId}/Revoke
```

`clientRecordId` is the internal GUID returned as `id` by `/scim/v2/Clients`. It is not the external OAuth `clientId` string.

### Authorization Server

```
GET  /.well-known/openid-configuration
GET  /.well-known/jwks.json
POST /connect/token
```

`/connect/token` supports `grant_type=client_credentials` for registered machine clients. The client certificate is supplied by the TLS layer in production. Local development and private deployment-host smoke tests may use `X-Client-Cert` with a base64 DER certificate when `AuthorizationServer:EnableForwardedClientCertificate` is enabled and the caller is inside the trusted proxy boundary.

When a token request omits `DPoP`, the server issues a certificate-bound bearer token with `cnf.x5t#S256`. When a request includes a valid `DPoP` proof JWT, the server issues a DPoP-bound token with `token_type=DPoP` and `cnf.jkt`.

Issued access tokens use JWT header `typ: at+jwt`, and validators require that
type to reduce JWT type-confusion risk.

Swagger UI is available at `/swagger` when running in Development mode.

---

## MCP server

`Backend.Mcp` exposes the administrative API as an MCP server named `idm-demo-mcp`. The default profile is `LocalStdio` for local agent workflows. Hosted HTTP profiles call `Backend.Api` using a configured machine-client identity and admin bearer token.

Start the API first:

```bash
AuthorizationServer__Issuer=http://localhost:5000 \
AuthorizationServer__Audience=idm-demo-mcp \
AuthorizationServer__SigningKeyPath=/tmp/idmdemo-signing-key.json \
AuthorizationServer__EnableForwardedClientCertificate=true \
ScimAdmin__SeedClientId=idm-admin \
ScimAdmin__SeedCertPath=/tmp/idmdemo-dev/admin-client.pem \
ScimAdmin__GenerateCertIfMissing=true \
ConnectionStrings__Default="Data Source=idm-demo.db" \
dotnet run --project src/Backend.Api --urls http://localhost:5000
```

Run the local stdio MCP profile with a configured IdM API instance:

```bash
IdmApiInstances__local__BaseUrl=http://127.0.0.1:5000 \
IdmApiInstances__local__ClientId=idm-admin \
IdmApiInstances__local__ClientCertificatePath=/tmp/idmdemo-dev/admin-client.pem \
Mcp__Profile=LocalStdio \
Mcp__DefaultInstance=local \
Mcp__ReadOnly=false \
dotnet run --project src/Backend.Mcp
```

Set `Mcp__ReadOnly=true` to block mutating and destructive tools. Destructive tools also require `confirm: true`.

Run the local hosted development profile:

```bash
IdmApiInstances__local__BaseUrl=http://127.0.0.1:5000 \
IdmApiInstances__local__ClientId=idm-admin \
IdmApiInstances__local__ClientCertificatePath=/tmp/idmdemo-dev/admin-client.pem \
AuthorizationServer__Issuer=http://localhost:5000 \
AuthorizationServer__SigningKeyPath=/tmp/idmdemo-signing-key.json \
Mcp__Profile=LocalHostedDevelopment \
Mcp__DefaultInstance=local \
Mcp__ReadOnly=false \
Mcp__Hosted__Audience=idm-demo-mcp \
dotnet run --project src/Backend.Mcp --urls http://localhost:5100
```

Run the hosted production profile:

```bash
IdmApiInstances__local__BaseUrl=http://127.0.0.1:5000 \
IdmApiInstances__local__ClientId=idm-admin \
IdmApiInstances__local__ClientCertificatePath=/tmp/idmdemo-dev/admin-client.pem \
AuthorizationServer__Issuer=http://localhost:5000 \
AuthorizationServer__SigningKeyPath=/tmp/idmdemo-signing-key.json \
Mcp__Profile=HostedProduction \
Mcp__DefaultInstance=local \
Mcp__Hosted__Audience=idm-demo-mcp \
dotnet run --project src/Backend.Mcp --urls http://localhost:5100
```

Profile demo scripts:

```bash
bash scripts/demo-mcp-local-stdio.sh
bash scripts/demo-mcp-local-hosted-development.sh
bash scripts/demo-mcp-phase-5-workflows.sh
bash scripts/demo-mcp-hosted-production.sh
```

`scripts/demo-hosted-mcp.sh` remains as a compatibility wrapper for the local hosted development demo.

Profile security posture:

| Profile | Transport | Caller token | DPoP | Bearer tokens | Default read-only |
| --- | --- | --- | --- | --- | --- |
| `LocalStdio` | stdio | Not required | Not used | Not used | `false` |
| `LocalHostedDevelopment` | HTTP on localhost, or development/test only | Required | Accepted | Allowed for development/testing | `false` |
| `HostedProduction` | HTTP behind a trusted reverse proxy | Required | Required | Rejected | `true` |

`LocalHostedDevelopment` allows non-local HTTP bindings only when the host environment is `Development`, `Test`, or `Testing`, or when `Mcp__Hosted__AllowNonLocalDevelopmentBinding=true` is set deliberately for development/test scenarios.

Hosted MCP tool authorization uses scopes from the caller's access token:

| Scope | Allows |
| --- | --- |
| `idm.mcp.read` | Read-only tools |
| `idm.mcp.write` | Non-destructive mutating tools |
| `idm.mcp.destructive` | Destructive tools when `confirm: true` is supplied |
| `idm.mcp.certificates` | Certificate issuance, registration, revocation, onboarding with certificates, and certificate rotation workflows |

Environment overrides for hosted demo scripts:

```bash
API_BASE_URL=http://localhost:5000 \
MCP_BASE_URL=http://localhost:5100 \
ADMIN_CERT_PATH=/tmp/idmdemo-dev/admin-client.pem \
MCP_AUDIENCE=idm-demo-mcp \
bash scripts/demo-mcp-local-hosted-development.sh --verbose
```

Hosted MCP endpoints:

```
POST /mcp
GET  /health/live
GET  /health/ready
```

`/health/ready` validates hosted auth configuration and checks each configured IdM API instance for reachability. The readiness response includes both raw MCP configuration values and the resolved effective profile posture so operators can see which values were supplied and which values the runtime is enforcing.

Production endpoint boundary:

| Component | Endpoint class | Exposure | Authentication |
| --- | --- | --- | --- |
| `Backend.Mcp` | `/mcp` | Public resource behind trusted reverse proxy | IdmDemo-issued MCP access token with `aud` equal to `Mcp:Hosted:Audience`; `HostedProduction` requires DPoP |
| `Backend.Mcp` | `/health/live`, `/health/ready` | Operational endpoint, deployment-controlled | Protected by infrastructure policy when exposed |
| `Backend.Api` | discovery and token issuance | Public when explicitly exposed by nginx | Client certificate authentication for token issuance; optional DPoP proof binds issued tokens |
| `Backend.Api` | SCIM, certificate, role, scope, and access-management administration | Private/internal only | `scim.admin` bearer token |

MCP tokens and API administrative access are separate resource boundaries in this phase. MCP callers present tokens for the MCP audience; hosted MCP then calls private `Backend.Api` with its configured machine-client identity and a `scim.admin` bearer token. Public production traffic should reach only the hosted MCP resource and explicitly exposed OAuth discovery/token endpoints, not API administrative routes. Strip and recreate forwarded headers at the trusted proxy boundary only.

### MCP tools

User tools:

```
idm_create_user
idm_get_user
idm_update_user
idm_delete_user
```

Machine-client tools:

```
idm_create_machine_client
idm_get_machine_client
idm_list_machine_clients
idm_update_machine_client
idm_delete_machine_client
```

Role and scope tools:

```
idm_create_global_role
idm_update_global_role
idm_delete_global_role
idm_create_global_scope
idm_update_global_scope
idm_delete_global_scope
```

Certificate and authorization-server tools:

```
idm_register_external_client_certificate
idm_issue_client_certificate_from_csr
idm_list_client_certificates
idm_get_client_certificate
idm_revoke_client_certificate
idm_inspect_client_credential_status
idm_get_certificate_authority
idm_get_authorization_server_metadata
idm_get_jwks
```

Workflow tools:

```
idm_onboard_machine_client
idm_rotate_machine_client_certificate
idm_prepare_dpop_client_credential_instructions
idm_preflight_machine_client_deployment
```

`idm_list_machine_clients` includes certificate collection summaries and active certificate metadata for each returned client. The legacy single-certificate fields on the underlying SCIM client resource are retained only for compatibility and do not describe the certificate collection.

Certificate MCP tools accept either the internal client record GUID or the external machine-client `clientId` such as `order-agent`. The MCP layer resolves external client IDs before calling the certificate API.

`idm_issue_client_certificate_from_csr` accepts optional `validityDays` from 1 to 90 and returns the signed public certificate as top-level `certificatePem`.
`idm_onboard_machine_client` can create or update a machine client, assign roles/scopes, and optionally register or issue an initial certificate from either an external certificate PEM or a CSR. CSR-issued certificates currently accept `certificateValidityDays` from 1 to 90.
`idm_rotate_machine_client_certificate` issues a replacement certificate from a CSR and can revoke a previous certificate when `confirmRevoke: true` is supplied.
`idm_prepare_dpop_client_credential_instructions` returns discovery-backed setup instructions for DPoP-bound client credentials.
`idm_preflight_machine_client_deployment` checks activation, required roles/scopes, active certificates, certificate expiry, and deployment readiness.

---

## Running locally

```bash
dotnet run --project src/Backend.Api
```

The API starts on `http://localhost:5000`. The SQLite database (`idm.db`) is created automatically on first run via EF Core migrations.

For the demo scripts, use an explicit URL, issuer, signing key path, seeded admin client, and disposable database path so the scripts and token issuer agree on the local origin:

```bash
AuthorizationServer__Issuer=http://localhost:5000 \
AuthorizationServer__Audience=idm-demo-mcp \
AuthorizationServer__SigningKeyPath=/tmp/idmdemo-signing-key.json \
AuthorizationServer__EnableForwardedClientCertificate=true \
ScimAdmin__SeedClientId=idm-admin \
ScimAdmin__SeedCertPath=/tmp/idmdemo-dev/admin-client.pem \
ScimAdmin__GenerateCertIfMissing=true \
ConnectionStrings__Default="Data Source=idm-demo.db" \
dotnet run --project src/Backend.Api --urls http://localhost:5000
```

### Demo script

```bash
bash scripts/demo-api.sh          # run all scenarios, show pass/fail
bash scripts/demo-api.sh --verbose  # show full request and response for each call
bash scripts/demo-certificates.sh # run certificate lifecycle scenarios
bash scripts/demo-auth.sh         # run OAuth mTLS and DPoP token scenarios
bash scripts/demo-access-management.sh # run role/scope catalog and assignment scenarios
bash scripts/demo-mcp-local-stdio.sh # run local stdio MCP smoke scenarios
bash scripts/demo-mcp-local-hosted-development.sh # run local hosted MCP bearer and DPoP scenarios
bash scripts/demo-mcp-phase-5-workflows.sh # run hosted MCP workflow scenarios
bash scripts/demo-mcp-hosted-production.sh # run hosted production DPoP scenarios
bash scripts/demo-mcp-remote-production-smoke.sh # run remote public auth/MCP smoke scenarios
```

Environment overrides:

```bash
API_BASE_URL=https://your-host ADMIN_CERT_PATH=./admin-client.pem bash scripts/demo-api.sh
API_BASE_URL=https://your-host ADMIN_CERT_PATH=./admin-client.pem bash scripts/demo-certificates.sh
API_BASE_URL=https://your-host ADMIN_CERT_PATH=./admin-client.pem bash scripts/demo-auth.sh
API_BASE_URL=https://your-host ADMIN_CERT_PATH=./admin-client.pem bash scripts/demo-access-management.sh
API_BASE_URL=https://your-api MCP_BASE_URL=https://your-mcp ADMIN_CERT_PATH=./admin-client.pem MCP_AUDIENCE=idm-demo-mcp bash scripts/demo-mcp-local-hosted-development.sh
API_BASE_URL=http://127.0.0.1:5000 AUTH_BASE_URL=http://127.0.0.1:5000 AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org MCP_BASE_URL=https://mcp.idp.madmetal.org MCP_HEALTH_BASE_URL=http://127.0.0.1:5100 ADMIN_CERT_PATH=/tmp/idmdemo-prod-admin-client.pem MCP_AUDIENCE=idm-demo-mcp bash scripts/demo-mcp-hosted-production.sh
AUTH_BASE_URL=https://auth.idp.madmetal.org AUTH_DPOP_BASE_URL=https://auth.idp.madmetal.org MCP_BASE_URL=https://mcp.idp.madmetal.org ADMIN_CERT_PATH=./idmdemo-prod-admin-client.pem MCP_AUDIENCE=idm-demo-mcp bash scripts/demo-mcp-remote-production-smoke.sh
```

Current production architecture and smoke-test notes are in
[`docs/current-architecture.md`](docs/current-architecture.md). Older epic and
deployment notes are archived in [`docs/archive/`](docs/archive/).

---

## Repository structure

```
.
├── src/
│   ├── Backend.Api/            # ASP.NET Core Web API, controllers, middleware, Program.cs
│   ├── Backend.Application/    # Services, DTOs, SCIM filter parser
│   ├── Backend.Domain/         # Entities, repository interfaces, domain exceptions
│   ├── Backend.Infrastructure/ # EF Core DbContext, SQLite, repositories, migrations
│   └── Backend.Mcp/            # stdio MCP server and administrative MCP tools
├── tests/
│   ├── Backend.Tests/          # Unit tests (xUnit + NSubstitute)
│   └── Backend.IntegrationTests/ # End-to-end tests (WebApplicationFactory + SQLite)
├── scripts/
│   ├── demo-api.sh             # curl-based API demo with optional verbose mode
│   ├── demo-certificates.sh    # certificate lifecycle demo
│   ├── demo-auth.sh            # OAuth mTLS and DPoP token demo
│   ├── demo-access-management.sh # role/scope catalog and assignment demo
│   ├── demo-mcp-local-stdio.sh # local stdio MCP demo
│   ├── demo-mcp-local-hosted-development.sh # local hosted MCP demo
│   ├── demo-mcp-hosted-production.sh # hosted production MCP demo
│   ├── demo-mcp-remote-production-smoke.sh # public remote production smoke demo
│   ├── demo-mcp-phase-5-workflows.sh # MCP workflow demo
│   ├── lib/mcp-demo-helpers.sh # shared hosted MCP demo helpers
│   ├── check-complexity.sh
│   ├── check-duplicates.sh
│   ├── check-secrets.sh
│   └── check-vulnerabilities.sh
├── .github/workflows/ci.yml    # GitHub Actions CI pipeline
├── .editorconfig               # Formatting and analyzer rules
├── .pre-commit-config.yaml     # Git pre-commit hooks
├── Directory.Build.props       # Shared MSBuild properties and analyzers
├── Makefile
└── product.md                  # Product roadmap and engineering standards
```

---

## Quality gates

| Gate | Tool | Threshold |
|---|---|---|
| Build | `dotnet build -warnaserror` | zero warnings |
| Lint | `dotnet format --verify-no-changes` | zero violations |
| Tests | xUnit | all pass |
| Coverage | coverlet | ≥ 80% line coverage |
| Complexity | lizard | CCN < 10 per method |
| Duplicates | jscpd | < 5% in `src/` |
| Security | Roslyn analyzers + StyleCop | zero warnings |
| Secrets | gitleaks | zero secrets detected |
| Vulnerabilities | `dotnet list package --vulnerable` | zero known CVEs |

Run all gates:

```bash
make check
```

Individual targets: `make build`, `make lint`, `make test`, `make coverage`, `make complexity`, `make duplicates`, `make security`, `make secrets`, `make vulnerabilities`.

---

## Prerequisites

| Tool | Purpose | Install |
|---|---|---|
| `dotnet` 8 | Build, test, format | [dot.net/download](https://dot.net/download) |
| `lizard` | Complexity check | `pipx install lizard` |
| `jscpd` | Duplicate detection | `npm install -g jscpd` |
| `gitleaks` | Secret scanning | `brew install gitleaks` / [releases](https://github.com/gitleaks/gitleaks/releases) |
| `pre-commit` | Git hooks | `pipx install pre-commit` |
| `sqlite3` | Inspect the database | `apt install sqlite3` / `brew install sqlite3` |

```bash
make install-tools
```

---

## Pre-commit hooks

Hooks run automatically on `git commit`:

1. Trailing whitespace / end-of-file
2. YAML validity
3. Hardcoded credentials (gitleaks)
4. Code formatting (`dotnet format --verify-no-changes`)
5. Build (`dotnet build -warnaserror`)
6. Tests + coverage ≥ 80%
7. No vulnerable NuGet packages

---

## Configuration

`src/Backend.Api/appsettings.json`:

```json
{
  "ConnectionStrings": { "Default": "Data Source=idm.db" },
  "AuthorizationServer": {
    "Issuer": "https://idmdemo.test",
    "Audience": "idm-demo-mcp",
    "AccessTokenLifetimeSeconds": 300,
    "RequireDpop": true,
    "DpopProofLifetimeSeconds": 300,
    "DpopReplayCacheSeconds": 300,
    "DpopSupportedAlgorithms": [ "ES256", "RS256" ],
    "SigningKeyPath": "signing-key.json",
    "ForwardedClientCertificateHeader": "X-Client-Cert",
    "EnableForwardedClientCertificate": false
  },
  "ScimAdmin": {
    "SeedClientId": "idm-admin",
    "SeedCertPath": "admin-client.pem",
    "GenerateCertIfMissing": true
  }
}
```

`RequireDpop` should stay enabled outside local bearer-token smoke tests. Bearer tokens are intentionally not replay-protected per request; DPoP sender-constrains the access token and the 300-second default lifetime keeps any explicitly enabled bearer fallback short-lived.

`Backend.Mcp` is configured through environment variables or configuration providers:

```json
{
  "IdmApiInstances": {
    "local": {
      "BaseUrl": "http://127.0.0.1:5000",
      "ClientId": "idm-admin",
      "ClientCertificatePath": "admin-client.pem"
    }
  },
  "Mcp": {
    "Profile": "LocalStdio",
    "DefaultInstance": "local",
    "ReadOnly": false,
    "Hosted": {
      "Audience": "idm-demo-mcp"
    }
  }
}
```

Override via environment variables for deployment:

```bash
ConnectionStrings__Default="Data Source=/data/idm.db" \
AuthorizationServer__Issuer=https://issuer.example.test \
AuthorizationServer__Audience=idm-demo-mcp \
AuthorizationServer__SigningKeyPath=/keys/signing-key.json \
ScimAdmin__SeedClientId=idm-admin \
ScimAdmin__SeedCertPath=/keys/admin-client.pem \
IdmApiInstances__local__BaseUrl=https://issuer.example.test \
IdmApiInstances__local__ClientId=idm-admin \
IdmApiInstances__local__ClientCertificatePath=/keys/admin-client.pem \
dotnet run
```

---

## CI/CD

GitHub Actions runs on every push and pull request to `main`. It executes the same gates as `make check`. See `.github/workflows/ci.yml`.

Dependabot automatically opens PRs for outdated NuGet packages and GitHub Actions weekly.
