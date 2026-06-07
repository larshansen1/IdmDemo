# IdmDemo

IdmDemo is a learning-oriented identity system for private experimentation and
agentic development workflows. It combines a small Identity Provider, an
OAuth-style Authorization Server, and an MCP Resource Server / IdP Admin
Interface behind explicit runtime boundaries.

The project is not a production-grade identity platform, but it is built with
clean architecture, structured observability, automated testing, and enforced
quality gates so the security and deployment tradeoffs are visible.

---

## What it does

### Identity Provider

The IdP manages user identities, machine-client identities, global roles, global
scopes, and machine-client certificates. Administrative APIs are SCIM-shaped and
include lifecycle operations for users, clients, roles, scopes, certificate
issuance, external certificate registration, and certificate revocation.

Administrative API routes are private/internal in the deployed architecture.
They require an IdmDemo-issued DPoP-bound token with the `scim.admin` role. The
admin machine client is seeded for deployment and is used by trusted internal
callers such as the hosted MCP Resource Server.

### Authorization Server

The authorization server exposes discovery metadata, JWKS, and a client
credentials token endpoint. Machine clients authenticate at the token endpoint
with mTLS client certificates; local and trusted-proxy deployments may forward
the presented certificate through `X-Client-Cert` only when explicitly enabled.

Issued JWT access tokens use `typ: at+jwt`, explicit resource audiences, roles,
and scopes. Token issuance supports certificate-bound bearer tokens and
DPoP-bound tokens with replay protection.

### MCP Resource Server / IdP Admin Interface

`Backend.Mcp` exposes IdP administration as MCP tools. `LocalStdio` is the
developer-machine profile and trusts the local OS process boundary. Hosted MCP
profiles validate caller tokens, enforce MCP scopes per tool, emit audit events,
and call the private IdP admin API with a separate configured machine-client
credential.

In production, public traffic reaches only the hosted MCP resource and the
explicitly exposed authorization-server discovery/token routes. Private IdP
admin routes are not published through the public `auth` host.

### Documentation map

Use [docs/current-architecture.md](docs/current-architecture.md) for the current
runtime and production boundary. Use [product.md](product.md) for the product
roadmap and original epic-oriented requirements. Older epic and deployment notes
under [docs/archive](docs/archive/) are historical context, not the current
operational source of truth.

---

## Security standards

The system is built around established RFCs and security standards rather than
custom protocol design. The goal is that the tradeoffs at each layer are
traceable to a published specification.

### Token security

| Standard | Role in this project |
|---|---|
| [RFC 9068](https://www.rfc-editor.org/rfc/rfc9068) — JWT Profile for OAuth 2.0 Access Tokens | `typ: at+jwt` on all issued tokens; validators require this type to prevent JWT type confusion |
| [RFC 8705](https://www.rfc-editor.org/rfc/rfc8705) — OAuth 2.0 Mutual-TLS Client Authentication | mTLS client authentication at the token endpoint; certificate-bound access tokens (`cnf.x5t#S256`) |
| [RFC 9449](https://www.rfc-editor.org/rfc/rfc9449) — OAuth 2.0 Demonstrating Proof of Possession (DPoP) | DPoP-bound tokens (`cnf.jkt`) with proof-lifetime and server-side replay cache; required in hosted production |
| [RFC 7519](https://www.rfc-editor.org/rfc/rfc7519) — JSON Web Token | JWT structure for all issued access tokens |
| [RFC 7517](https://www.rfc-editor.org/rfc/rfc7517) — JSON Web Key | JWKS endpoint for public key distribution |

### Protocol

| Standard | Role in this project |
|---|---|
| [RFC 6749](https://www.rfc-editor.org/rfc/rfc6749) — OAuth 2.0 | `client_credentials` grant for machine-to-machine token requests |
| [OpenID Connect Discovery 1.0](https://openid.net/specs/openid-connect-discovery-1_0.html) | `/.well-known/openid-configuration` metadata endpoint |

### Identity management

| Standard | Role in this project |
|---|---|
| [RFC 7643](https://www.rfc-editor.org/rfc/rfc7643) + [RFC 7644](https://www.rfc-editor.org/rfc/rfc7644) — SCIM 2.0 | SCIM-shaped administrative API for user, client, role, and scope lifecycle; private/internal boundary only |

### Architecture security posture

- **Private admin boundary** — SCIM and certificate management endpoints are never exposed publicly. nginx returns `404` for any `/scim/v2/*` path on the public `auth` host.
- **Trusted reverse proxy** — nginx terminates TLS, strips and recreates forwarded headers (`X-Client-Cert`), and proxies only explicitly allowed routes to backend services.
- **Hosted production requires DPoP** — bearer tokens are accepted only in `LocalHostedDevelopment` for local testing; `HostedProduction` rejects them.
- **MCP tool authorization** — enforces scopes from the caller's access token (`idm.mcp.read`, `idm.mcp.write`, `idm.mcp.destructive`, `idm.mcp.certificates`). Destructive tools additionally require `confirm: true`.

---

## Quick start

```bash
dotnet run --project src/Backend.Api
```

The API starts on `http://localhost:5000`. The SQLite database is created on
first run via EF Core migrations. Swagger UI is available at `/swagger` in
Development mode.

Demo scripts exercise the major flows end-to-end:

```bash
bash scripts/demo-api.sh             # API lifecycle scenarios
bash scripts/demo-auth.sh            # OAuth mTLS and DPoP token scenarios
bash scripts/demo-certificates.sh    # certificate lifecycle scenarios
bash scripts/demo-mcp-local-stdio.sh # local MCP stdio scenarios
```

For full local configuration, MCP profiles, hosted deployment, and production
smoke tests see [docs/current-architecture.md](docs/current-architecture.md).

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

```bash
make check
```

Individual targets: `make build`, `make lint`, `make test`, `make coverage`,
`make complexity`, `make duplicates`, `make security`, `make secrets`,
`make vulnerabilities`.

See [docs/pipeline.md](docs/pipeline.md) for the full layer inventory — which
checks run at pre-commit, which are CI gates, and how coverage scope is
determined per-assembly.

---

## Repository structure

```
.
├── src/
│   ├── Backend.Api/              # ASP.NET Core Web API, controllers, middleware, Program.cs
│   ├── Backend.Application/      # Services, DTOs, SCIM filter parser
│   ├── Backend.Domain/           # Entities, repository interfaces, domain exceptions
│   ├── Backend.Infrastructure/   # EF Core DbContext, SQLite, repositories, migrations
│   └── Backend.Mcp/              # MCP Resource Server / IdP Admin Interface tools
├── tests/
│   ├── Backend.Tests/            # Unit tests (xUnit + NSubstitute)
│   └── Backend.IntegrationTests/ # End-to-end tests (WebApplicationFactory + SQLite)
├── scripts/                      # curl-based demo and smoke-test scripts
├── docs/
│   ├── current-architecture.md   # Runtime source of truth: services, boundaries, token flow
│   ├── pipeline.md               # CI/CD pipeline notes
│   └── archive/                  # Historical epic and deployment notes
├── .github/workflows/ci.yml      # GitHub Actions CI pipeline
├── .editorconfig                 # Formatting and analyzer rules
├── .pre-commit-config.yaml       # Git pre-commit hooks
├── Directory.Build.props         # Shared MSBuild properties and analyzers
├── Makefile
└── product.md                    # Product roadmap and engineering standards
```

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

Pre-commit hooks run on `git commit`: trailing whitespace, YAML validity,
gitleaks secret scan, `dotnet format`, build, tests + coverage ≥ 80%, and NuGet
vulnerability check.

---

## CI/CD

GitHub Actions runs on every push and pull request to `main`, executing the same
gates as `make check`. Dependabot opens weekly PRs for outdated NuGet packages
and GitHub Actions. See `.github/workflows/ci.yml` and
[docs/pipeline.md](docs/pipeline.md).
