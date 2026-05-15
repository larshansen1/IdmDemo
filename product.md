# Product Description: Simple IdP and Authorization Server

## Purpose

Build a simple Identity Provider and Authorization Server for learning, private experimentation, and agentic development workflows.

The system is primarily intended as a technical learning project, not as a production-grade identity platform. However, it should be designed using clean architecture, explicit security boundaries, observability, automated testing, and maintainable engineering practices from the start.

The system should support:

- Federated user records
- Machine client identities
- Machine-to-machine authentication using mTLS
- Certificate lifecycle support for machine clients
- OAuth/OIDC-style token issuance
- DPoP-bound access tokens
- Global roles and scopes
- Administrative APIs exposed directly and via MCP

---

## Epic 1: Identity Provider Foundation

Implement the core identity model and administrative identity management APIs.

### Scope

The IdP should manage:

- Users
- Machine clients
- Global roles
- Global scopes

Users are included as identity records and are expected to support future federation scenarios. There is no interactive user login flow at this stage.

Machine clients represent non-human clients that can authenticate using mTLS and later use DPoP-bound access tokens.

### User Management API

Expose a SCIM-compatible or SCIM-inspired API for user lifecycle management.

```http
POST   /scim/v2/Users
GET    /scim/v2/Users?filter=userName eq "alice"
GET    /scim/v2/Users/{id}
PUT    /scim/v2/Users/{id}
PATCH  /scim/v2/Users/{id}
DELETE /scim/v2/Users/{id}
```

### Client Management API

Expose a custom SCIM-shaped resource API for machine client lifecycle management.

Clients are not standard SCIM resources unless explicitly defined as a custom SCIM resource type. Therefore, this API should be described as SCIM-shaped rather than SCIM-standard.

```http
POST   /scim/v2/Clients
GET    /scim/v2/Clients?filter=clientId eq "orders-service"
GET    /scim/v2/Clients/{id}
PUT    /scim/v2/Clients/{id}
PATCH  /scim/v2/Clients/{id}
DELETE /scim/v2/Clients/{id}
```

### Initial Authentication

For bootstrap purposes, administrative access may initially be protected using a simple API key.

This is acceptable for the learning version, but should be isolated behind an explicit administrative authentication mechanism so it can later be replaced with stronger authentication.

### Validation Requirements

The system should validate:

- Required fields
- Unique identifiers
- Identifier formats
- Active/inactive lifecycle state
- Role and scope references
- Client credential and certificate references
- Invalid state transitions

---

## Epic 2: Basic Authorization Server

Implement the basic authorization server capabilities required for machine-to-machine authentication.

### Scope

The authorization server should provide:

```http
GET  /.well-known/openid-configuration
GET  /.well-known/jwks.json
POST /connect/token
```

The token endpoint should support the OAuth 2.0 client credentials flow for registered machine clients using mutual TLS client authentication, aligned with RFC 8705.

The first implementation should issue JWT access tokens. Tokens should be certificate-bound using the SHA-256 thumbprint of the client certificate in the JWT confirmation claim:

```json
{
  "cnf": {
    "x5t#S256": "<base64url-sha256-certificate-thumbprint>"
  }
}
```

### Requirements

The authorization server should:

- Expose OAuth/OIDC-style authorization server metadata from `/.well-known/openid-configuration`
- Expose public JWT signing keys from `/.well-known/jwks.json`
- Accept `application/x-www-form-urlencoded` token requests at `/connect/token`
- Support `grant_type=client_credentials`
- Require `client_id` on token requests
- Authenticate registered machine clients using mutual TLS
- Validate the presented client certificate against registered certificate metadata for the `client_id`
- Match registered certificates using the certificate SHA-256 thumbprint
- Reject inactive clients
- Reject missing, malformed, expired, or unregistered client certificates
- Issue short-lived JWT access tokens for authenticated clients
- Bind issued JWT access tokens to the presented client certificate using `cnf.x5t#S256`
- Include granted global roles and scopes in issued tokens
- Reject requests for scopes not assigned to the client
- Return OAuth-style token endpoint errors instead of SCIM error envelopes
- Keep administrative SCIM APIs protected by API-key authentication
- Keep discovery and JWKS endpoints public

### Token Request

The token endpoint should use the closest practical shape to RFC 8705 for this project:

```http
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=client_credentials&
client_id=orders-service&
scope=orders.read orders.write
```

The client certificate is supplied by the TLS layer. The request body still includes `client_id` so the authorization server can locate the registered machine client and compare the presented certificate thumbprint to the expected certificate thumbprint.

### Token Response

Successful responses should follow OAuth 2.0 token response conventions:

```json
{
  "access_token": "<jwt>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "orders.read orders.write"
}
```

The first JWT claim set should include:

- `iss`: configured issuer URL
- `sub`: machine client record id or stable client subject
- `client_id`: external machine client identifier
- `aud`: configured resource audience
- `jti`: unique token id
- `iat`: issued-at timestamp
- `nbf`: not-before timestamp
- `exp`: expiration timestamp
- `scope`: space-delimited granted scopes
- `roles`: granted global roles
- `cnf.x5t#S256`: SHA-256 thumbprint of the presented client certificate

### Error Responses

The token endpoint should return OAuth-style JSON errors:

```json
{
  "error": "invalid_client",
  "error_description": "Client certificate is missing or invalid."
}
```

Initial error mapping:

| Scenario | HTTP status | Error |
|---|---:|---|
| Missing or invalid client authentication | 401 | `invalid_client` |
| Unknown or inactive client | 401 | `invalid_client` |
| Missing or unsupported grant type | 400 | `unsupported_grant_type` or `invalid_request` |
| Requested scope is not assigned to the client | 400 | `invalid_scope` |
| Malformed request | 400 | `invalid_request` |

### Signing Keys

JWT signing keys should be generated locally by the application. The implementation should provide a development-friendly local key store and expose the active public signing key through JWKS.

Key handling requirements:

- Use asymmetric signing keys.
- Persist generated keys locally so tokens remain verifiable across application restarts.
- Include `kid` in JWT headers and JWKS entries.
- Support an active signing key.
- Keep private key material out of API responses and logs.
- Leave key rotation as a later enhancement unless it is cheap to support without expanding scope.

### Issuer and Audience Recommendation

The issuer should be configured as the externally visible authorization server origin.

Recommended default:

```json
{
  "AuthorizationServer": {
    "Issuer": "https://localhost:5001",
    "Audience": "idm-demo-api",
    "AccessTokenLifetimeSeconds": 3600
  }
}
```

The audience should initially be a single configured resource audience. Per-client or per-resource audiences are out of scope for Epic 2.

### Mutual TLS Deployment Model

Development should support direct TLS termination in Kestrel for local experimentation.

Test and production should support reverse-proxy TLS termination. In that model, the proxy is responsible for validating the TLS connection and forwarding the client certificate to the API using a configured header.

Proxy-mode requirements:

- Trust forwarded client certificate headers only when explicitly enabled.
- Document that the API must only accept forwarded certificate headers from a trusted proxy.
- Decode and parse the forwarded certificate into an X.509 certificate before validation.
- Use the same certificate thumbprint validation path for direct and proxy modes.

### Minimal Certificate Registration

Certificate lifecycle remains out of scope until Epic 3. Epic 2 should add only the minimum certificate metadata needed to authenticate machine clients.

Machine clients should support storing:

- Client certificate SHA-256 thumbprint
- Optional certificate subject
- Optional certificate expiry

The SCIM-shaped client API may accept and return this public certificate metadata. It must not issue certificates, rotate certificates, revoke certificates, generate client private keys, or manage CSRs.

### Minimal Roles and Scopes

Epic 2 should include the minimum model needed to issue useful access tokens:

- Global scope definitions
- Global role definitions
- Machine-client assigned scopes
- Machine-client assigned roles

The implementation does not need full user role assignment, tenant-specific authorization, delegated user consent, resource-specific scopes, or a policy engine. Those remain part of later access management work.

### Testing Requirements

Epic 2 tests should cover:

- Discovery metadata returns the configured issuer, token endpoint, JWKS URI, supported grant type, supported token endpoint auth method, and `tls_client_certificate_bound_access_tokens=true`
- JWKS returns the active public signing key and no private key material
- Valid mTLS client credentials request returns a JWT access token
- Token contains expected standard claims, granted roles/scopes, and `cnf.x5t#S256`
- Missing certificate returns `invalid_client`
- Unknown client returns `invalid_client`
- Thumbprint mismatch returns `invalid_client`
- Inactive client returns `invalid_client`
- Unsupported grant type returns `unsupported_grant_type`
- Unassigned requested scope returns `invalid_scope`
- Token endpoint does not require the administrative API key
- Administrative SCIM endpoints still require the administrative API key
- Proxy-forwarded certificate mode works only when explicitly enabled

### Clarification

The client management API may store or expose public certificate metadata, but certificate issuance and lifecycle should not be implemented as part of the basic authorization server epic.

---

## Epic 3: Certificate Lifecycle

Support certificate lifecycle management for machine clients.

### Scope

The system should eventually support:

- Requesting a new certificate for a registered client
- Associating certificates with machine clients
- Storing public certificate material
- Storing certificate metadata
- Storing thumbprints
- Tracking certificate expiry
- Tracking certificate lifecycle state
- Revoking certificates

Possible certificate endpoints:

```http
POST /scim/v2/Clients/{id}/Certificates
GET  /scim/v2/Clients/{id}/Certificates
GET  /scim/v2/Clients/{id}/Certificates/{certificateId}
POST /scim/v2/Clients/{id}/Certificates/{certificateId}/Revoke
```

### Design Note

A separate certificate lifecycle design must be completed before implementing this epic.
The initial design is documented in `docs/epic-3-certificate-lifecycle.md`.

The design should decide whether the system uses:

1. CSR-based issuance, where the client generates the private key locally and submits a Certificate Signing Request.
2. Server-generated key pairs, where the system generates the private key and must deliver it securely to the client.
3. External CA integration, where the system stores certificate references and metadata but does not itself issue certificates.

The preferred design is likely CSR-based issuance, because the server never handles the client private key.

This epic should not proceed until the key ownership model, trust model, certificate authority model, private key handling, revocation model, and bootstrap flow are explicitly defined.

---

## Epic 4: DPoP Support

Add support for DPoP-bound access tokens.

### Scope

The authorization server should support proof-of-possession tokens where the issued access token is bound to a client-held DPoP key.

### Requirements

The system should:

- Accept and validate DPoP proofs at the token endpoint
- Validate DPoP proof structure and claims
- Bind issued access tokens to the DPoP public key
- Include appropriate token confirmation claims
- Support downstream validation of DPoP-bound access tokens
- Reject token replay or invalid proof usage where possible

### Dependency

This epic depends on the basic authorization server and mTLS client authentication being implemented first.

### Design Note

The initial DPoP design is documented in `docs/epic-4-dpop-support.md`.

The first implementation should keep mTLS client authentication as the required
machine-client authentication mechanism and add DPoP as an optional access-token
sender constraint unless `AuthorizationServer:RequireDpop` is explicitly enabled.

---

## Epic 5: Access Management

Implement global role and scope assignment for users and machine clients.

### Scope

At this stage, roles and scopes are global. There is no tenant-specific, resource-specific, or domain-specific authorization model.

### Requirements

The system should support:

- Creating global roles
- Creating global scopes
- Assigning roles to users
- Assigning roles to machine clients
- Assigning allowed scopes to machine clients
- Including roles and scopes in issued tokens
- Validating requested scopes against assigned scopes

### Out of Scope for Now

The following are not required initially:

- Tenant-specific roles
- Resource-specific scopes
- Delegated user consent
- Interactive authorization code flow
- Fine-grained policy engine
- Attribute-based access control

---

## Epic 6: MCP Access

Expose selected administrative and operational capabilities through an MCP server so agents can interact with the system.

### Scope

The MCP server should expose controlled tools for managing and inspecting the IdP and Authorization Server.

Possible MCP tools:

- Create user
- Get user
- Update user
- Delete user
- Create machine client
- Get machine client
- Update machine client
- Delete machine client
- Assign role
- Assign scope
- List certificates
- Revoke certificate
- Inspect authorization server configuration
- Inspect client credential status

### Security Requirements

MCP access must not bypass the normal authorization model.

The MCP server should:

- Use the same administrative authorization rules as the direct API
- Avoid unsafe privileged shortcuts
- Log all administrative actions
- Trace all tool executions
- Clearly distinguish read-only tools from mutating tools
- Require explicit authorization for destructive operations

---

## Technical Stack

### Runtime and Language

- .NET 8
- C#
- ASP.NET Core Web API

### Persistence

- SQLite initially
- PostgreSQL or another relational database later

### ORM and Migrations

Use Entity Framework Core for:

- Data access
- Schema definition
- Migrations
- Database portability from SQLite to PostgreSQL

Direct database access from API handlers should be avoided.

### Architecture

Use a clean/layered architecture:

- API layer
- Application/service layer
- Domain model
- Infrastructure/persistence layer

The architecture should keep protocol concerns, business logic, and persistence concerns separated.

### Key Dependencies

| Package | Purpose |
|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | ORM + SQLite provider |
| `Swashbuckle.AspNetCore` | OpenAPI / Swagger UI |
| `OpenTelemetry.Extensions.Hosting` | Distributed tracing host integration |
| `OpenTelemetry.Instrumentation.AspNetCore` | ASP.NET Core request tracing |
| `OpenTelemetry.Exporter.Console` | Console trace exporter (dev) |
| `NSubstitute` | Mocking in unit tests |
| `Microsoft.AspNetCore.Mvc.Testing` | In-process integration testing |
| `coverlet.msbuild` | Code coverage collection |

### Observability

The system should include:

- Structured logging
- Distributed tracing
- Security-relevant audit logs
- Clear correlation identifiers for request flows

Correlation IDs are propagated via the `X-Correlation-Id` header. Requests without one get a generated UUID. The ID is echoed in the response header and included in all log scopes for that request.

Audit log events follow the pattern `{Entity}{Action}` with structured fields:

```
UserCreated   { UserId, UserName }
UserUpdated   { UserId }
UserDeleted   { UserId }
ClientCreated { ClientId, ExternalClientId }
ClientUpdated { ClientId }
ClientDeleted { ClientId }
```

---

## Engineering Standards

### Project Layer Rules

```
Backend.Api         → Backend.Application, Backend.Infrastructure
Backend.Application → Backend.Domain
Backend.Infrastructure → Backend.Domain
Backend.Tests       → Backend.Application, Backend.Domain
Backend.IntegrationTests → Backend.Api, Backend.Infrastructure
```

The domain layer has zero external dependencies. Application has no infrastructure or web dependencies. Infrastructure has no web dependencies.

### API Conventions

- All endpoints require `X-Api-Key` header authentication.
- All requests and responses use `Content-Type: application/scim+json`.
- Error responses always include a SCIM error envelope: `schemas`, `status`, `scimType`, `detail`.
- HTTP status codes: 201 Created, 200 OK, 204 No Content, 400 invalidValue, 401 Unauthorized, 404 Not Found, 409 uniqueness.
- `Location` header is set on 201 responses.
- `SuppressAsyncSuffixInActionNames = false` is required so `CreatedAtAction(nameof(GetAsync), ...)` resolves correctly.

### Domain Model Conventions

- Entities use private parameterless constructors (for EF Core) marked `[ExcludeFromCodeCoverage]`.
- All construction goes through static factory methods (`Create(...)`).
- State transitions use explicit methods (`Activate()`, `Deactivate()`, `Update(...)`).
- Entities never expose setters publicly.
- Repository interfaces live in `Backend.Domain`. Implementations live in `Backend.Infrastructure`.

### Service Layer Conventions

- Services validate inputs before touching the repository.
- Validation order: null check → empty/whitespace → max length → format → uniqueness (repository call).
- Uniqueness violations throw `ConflictException`. Missing resources throw `NotFoundException`. Field errors throw `ValidationException`.
- `ArgumentNullException.ThrowIfNull` is used for null guards at public method boundaries.
- Services never return domain entities — always map to response DTOs.

### Code Style

- StyleCop and all Roslyn analyzers are enabled at `AnalysisMode=All` with `TreatWarningsAsErrors=true`.
- Private fields and constants use `_camelCase` prefix (SA1309 suppressed to allow underscore prefix).
- `this.` is required for all instance member access (SA1101).
- Public members before private members within each member type (SA1202, SA1204).
- No comments unless the _why_ is non-obvious. No XML doc comments on internal types.
- No `ConfigureAwait(false)` in test methods (xUnit1030 rule). CA2007 is suppressed in `tests/**`.
- EF Core migration files are excluded from analyzer rules via `.editorconfig` (`generated_code = true`).

### Testing Conventions

- Unit tests use NSubstitute for mocking. No real database in unit tests.
- Integration tests use `WebApplicationFactory<Program>` with a unique SQLite file per factory instance.
- Test names follow `MethodName_Scenario_ExpectedOutcome` (CA1707 suppressed in tests).
- Every service method has at minimum: happy path, not-found path, validation failure path.
- Both `Activate()` and `Deactivate()` paths must be covered.
- Integration tests cover: 201, 400, 401, 404, 409, 204 for each resource type.
- `IClassFixture<T>` is used for shared factory setup within a test class.

### Quality Gate Thresholds

| Gate | Threshold |
|---|---|
| Line coverage | ≥ 80% per project |
| Cyclomatic complexity | CCN < 10 per method |
| Duplicate code | < 5% in `src/` (migrations excluded) |
| Build warnings | zero |
| Secrets | zero |
| Vulnerable packages | zero |

---

## Definition of Done

A feature is complete only when:

- Unit tests pass
- Integration tests pass
- Pre-commit checks pass
- CI pipeline checks pass
- Test coverage meets the agreed threshold
- Linting checks pass
- Static analysis checks pass
- Security checks pass
- Complexity checks pass
- Public API behavior is documented
- Relevant structured logs are implemented
- Relevant tracing spans are implemented
- Security-relevant behavior is explicitly tested
- Failure cases are tested, not only happy paths

---

## Important Design Boundaries

### Users

Users are identity records for future federation scenarios.

There is no interactive login flow for users at this stage.

### Clients

Machine clients are first-class identities.

They authenticate using mTLS initially and may later use DPoP-bound access tokens.

### SCIM

User management should be SCIM-compatible or SCIM-inspired.

Client management should be SCIM-shaped, because machine clients are not standard SCIM resources unless explicitly modeled as a custom SCIM resource type.

### Certificates

Certificate lifecycle is a separate design area and must be designed before implementation.

The preferred direction is likely CSR-based issuance, where the client owns the private key and the server stores public certificate material and metadata.

### Authorization

Roles and scopes are global for now.

More advanced authorization models are out of scope until the basic identity, token, mTLS, and DPoP flows are implemented.

### MCP

MCP is an additional administrative interface, not a privileged backdoor.

All MCP-exposed operations must respect the same authorization, logging, tracing, and validation rules as the direct API.
