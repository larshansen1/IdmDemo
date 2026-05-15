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

Planned: MCP administrative interface.

See [product.md](product.md) for the full roadmap.

---

## API

All endpoints require `X-Api-Key: <key>` (default: `changeme-development-key`).  
All requests and responses use `Content-Type: application/scim+json`.

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
POST   /scim/v2/Clients/{clientId}/Certificates
GET    /scim/v2/Clients/{clientId}/Certificates
GET    /scim/v2/Clients/{clientId}/Certificates/{certificateId}
POST   /scim/v2/Clients/{clientId}/Certificates/{certificateId}/Revoke
```

### Authorization Server

Discovery and JWKS are public. Token requests use OAuth-style form encoding and OAuth-style error responses.

```
GET  /.well-known/openid-configuration
GET  /.well-known/jwks.json
POST /connect/token
```

`/connect/token` supports `grant_type=client_credentials` for registered machine clients. The client certificate is supplied by the TLS layer in production; the local demo and tests use `X-Client-Cert` with a base64 DER certificate to simulate that boundary.

When a token request omits `DPoP`, the server issues a certificate-bound bearer token with `cnf.x5t#S256`. When a request includes a valid `DPoP` proof JWT, the server issues a DPoP-bound token with `token_type=DPoP` and `cnf.jkt`.

Swagger UI is available at `/swagger` when running in Development mode.

---

## Running locally

```bash
dotnet run --project src/Backend.Api
```

The API starts on `http://localhost:5000`. The SQLite database (`idm.db`) is created automatically on first run via EF Core migrations.

For the demo scripts, use an explicit URL, API key, issuer, and disposable database path so the scripts and token issuer agree on the local origin:

```bash
AdminApi__ApiKey=changeme-development-key \
AuthorizationServer__Issuer=http://localhost:5000 \
AuthorizationServer__EnableForwardedClientCertificate=true \
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
```

Environment overrides:

```bash
API_BASE_URL=https://your-host API_KEY=your-key bash scripts/demo-api.sh
API_BASE_URL=https://your-host API_KEY=your-key bash scripts/demo-certificates.sh
API_BASE_URL=https://your-host API_KEY=your-key bash scripts/demo-auth.sh
API_BASE_URL=https://your-host API_KEY=your-key bash scripts/demo-access-management.sh
```

---

## Repository structure

```
.
├── src/
│   ├── Backend.Api/            # ASP.NET Core Web API, controllers, middleware, Program.cs
│   ├── Backend.Application/    # Services, DTOs, SCIM filter parser
│   ├── Backend.Domain/         # Entities, repository interfaces, domain exceptions
│   └── Backend.Infrastructure/ # EF Core DbContext, SQLite, repositories, migrations
├── tests/
│   ├── Backend.Tests/          # Unit tests (xUnit + NSubstitute)
│   └── Backend.IntegrationTests/ # End-to-end tests (WebApplicationFactory + SQLite)
├── scripts/
│   ├── demo-api.sh             # curl-based API demo with optional verbose mode
│   ├── demo-certificates.sh    # certificate lifecycle demo
│   ├── demo-auth.sh            # OAuth mTLS and DPoP token demo
│   ├── demo-access-management.sh # role/scope catalog and assignment demo
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
  "AdminApi": { "ApiKey": "changeme-development-key" },
  "ConnectionStrings": { "Default": "Data Source=idm.db" },
  "AuthorizationServer": {
    "Issuer": "https://idmdemo.test",
    "Audience": "idm-demo-api",
    "AccessTokenLifetimeSeconds": 3600,
    "RequireDpop": false,
    "DpopProofLifetimeSeconds": 300,
    "DpopReplayCacheSeconds": 300,
    "DpopSupportedAlgorithms": [ "ES256", "RS256" ]
  }
}
```

Override via environment variables for deployment:

```bash
AdminApi__ApiKey=secret \
ConnectionStrings__Default="Data Source=/data/idm.db" \
AuthorizationServer__Issuer=https://issuer.example.test \
dotnet run
```

---

## CI/CD

GitHub Actions runs on every push and pull request to `main`. It executes the same gates as `make check`. See `.github/workflows/ci.yml`.

Dependabot automatically opens PRs for outdated NuGet packages and GitHub Actions weekly.
