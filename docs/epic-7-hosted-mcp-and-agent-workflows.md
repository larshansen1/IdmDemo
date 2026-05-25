# Epic 7: Hosted MCP and Agent Workflows

## Purpose

Epic 7 extends MCP support beyond the local developer-machine stdio server. The goal
is to support remote agent clients without turning MCP into a privileged backdoor
around the existing API, authorization, audit, and deployment boundaries.

The production infrastructure default is reverse-proxy TLS termination with IdmDemo
services running privately behind the proxy. The proxy owns transport security and
edge controls. IdmDemo owns application security: token validation, tool policy,
audit attribution, and certificate/thumbprint validation.

## Decisions

### Deployment Model

Hosted MCP should run as a separately deployable service/process for the first
implementation. The existing stdio MCP server remains the local development mode.

Recommended production shape:

```text
Remote agent client
        |
        | HTTPS / DPoP-bound access token
        v
Reverse proxy / ingress
        |
        | private network HTTP or HTTPS
        v
Backend.Mcp hosted transport
        |
        | internal administrative credential
        v
Backend.Api
```

The IdmDemo services should not be directly reachable from the public internet.
Any forwarded headers used by the application must be stripped and re-created by a
trusted proxy only. Direct Kestrel TLS remains useful for local development and
focused mTLS experiments, but it is not the primary production target.

### Hosted MCP Authentication

Remote MCP callers must authenticate with access tokens issued by IdmDemo. DPoP-bound
access tokens are the preferred credential for hosted agent clients. Bearer tokens
may be allowed only through explicit development or test configuration.

The existing administrative API key may remain as an internal MCP-to-API credential
until a later API authentication upgrade replaces it with scoped service tokens. That
key must not be accepted from or exposed to hosted MCP callers.

### Tool Authorization

Hosted MCP must enforce per-tool authorization before invoking the underlying API.
The authorization guard should evaluate:

- configured read-only mode,
- authenticated subject,
- token scopes,
- tool metadata,
- destructive confirmation.

Initial MCP scopes:

| Scope | Allows |
|---|---|
| `idm.mcp.read` | Read-only MCP tools |
| `idm.mcp.write` | Non-destructive mutating tools |
| `idm.mcp.destructive` | Destructive tools when `confirm: true` is supplied |
| `idm.mcp.certificates` | Certificate issuance and revocation workflows |

Certificate tools that mutate state require `idm.mcp.certificates` plus the matching
write or destructive scope.

### Audit Attribution

Hosted MCP calls must produce structured audit events. Each event should include:

- tool name,
- result status,
- authenticated subject,
- client id,
- scopes,
- target IdM API instance,
- correlation id,
- affected resource id when available.

Denied hosted MCP attempts are security-relevant audit events, not ordinary validation
noise.

### Approval Safety

The first hosted approval model remains intentionally simple:

- destructive tools require `confirm: true`,
- the caller must have `idm.mcp.destructive`,
- the audit event must capture explicit confirmation.

Multi-party approvals and persisted approval tickets are out of scope for the first
Epic 7 implementation.

## Phased Implementation Plan

### Phase 1: Hosted Transport Foundation

- Add a hosted MCP server mode alongside the existing stdio mode.
- Make transport config-driven with `Mcp:Transport = Stdio | Http`.
- Keep `Stdio` as the local development default.
- Add hosted MCP health/readiness endpoints for process health, configured API
  instance reachability, and auth configuration validity.
- Preserve current stdio behavior and tool registration.
- Document reverse-proxy deployment as the production default.

### Phase 2: Authentication and Tool Policy

- Require hosted callers to authenticate with IdmDemo-issued access tokens.
- Prefer DPoP-bound tokens for remote agents.
- Add MCP-specific global scopes.
- Extend the current mutation guard into a hosted-aware authorization guard.
- Ensure hosted callers cannot pass, override, or observe the internal API key.

Phase 2 implementation decisions:

- `Backend.Mcp` validates IdmDemo-issued JWT access tokens locally instead of
  calling back to `Backend.Api` for introspection.
- Production hosted MCP requires DPoP-bound access tokens. Bearer tokens are
  accepted only when `Mcp:Hosted:AllowBearerTokensForDevelopment = true`.
- Stdio MCP remains a local development transport. It keeps the existing
  read-only and destructive confirmation behavior and does not require caller
  tokens.
- Tool policy is derived from existing MCP tool metadata plus a central MCP
  scope map, not from per-tool external configuration in the first
  implementation.
- MCP global scopes are documented and testable setup data. Automatic catalog
  seeding is deferred unless operational testing shows it is needed.
- Certificate mutating tools require `idm.mcp.certificates` plus the matching
  write or destructive scope.
- Hosted callers cannot influence the internal API key. Regression tests must
  prove request headers or tool arguments cannot override or expose it.

Phase 2 work items:

1. Add hosted token validation services to `Backend.Mcp`.
   - Configure issuer, audience, signing key discovery, and token lifetime
     validation for IdmDemo-issued access tokens.
   - Validate the `aud` claim against `Mcp:Hosted:Audience`.
   - Extract subject, client id, scopes, token type, and DPoP confirmation data
     into a hosted MCP caller context.
2. Add hosted DPoP request validation.
   - Require a valid `DPoP` proof for hosted requests when
     `Mcp:Hosted:RequireDpop = true`.
   - Bind the proof to the presented access token and hosted MCP request
     method/URI.
   - Permit bearer-only validation only when the explicit development bearer
     flag is enabled.
3. Replace the mutation-only guard with a hosted-aware authorization guard.
   - Preserve the existing stdio read-only and destructive confirmation
     behavior.
   - For hosted calls, require `idm.mcp.read` for read-only tools.
   - Require `idm.mcp.write` for non-destructive mutating tools.
   - Require `idm.mcp.destructive` and `confirm: true` for destructive tools.
   - Require `idm.mcp.certificates` for certificate issuance, registration, and
     revocation tools in addition to write or destructive authorization.
4. Define MCP tool policy metadata.
   - Use the existing `ReadOnly` and `Destructive` tool attributes as the base
     policy source.
   - Add a central mapping for special requirements such as certificate tools.
   - Keep policy names aligned with the tool names exposed over MCP.
5. Isolate the internal API credential from hosted callers.
   - Keep `IdmApiInstances:*:ApiKey` as an internal MCP-to-API credential.
   - Do not accept caller-supplied API keys through hosted request headers,
     forwarded identity headers, or tool parameters.
   - Ensure error responses and tool results never disclose the configured API
     key.
6. Add Phase 2 tests.
   - Unit test token validation, DPoP enforcement, scope checks, read-only mode,
     destructive confirmation, and certificate scope requirements.
   - Integration test hosted MCP rejects unauthenticated requests and
     insufficient scopes.
   - Integration test hosted MCP accepts a valid scoped DPoP-bound token.
   - Regression test API key override attempts are ignored or rejected.
   - Keep stdio MCP smoke coverage passing unchanged.

Phase 2 exit criteria:

- Hosted `/mcp` requests without a valid IdmDemo access token are rejected.
- Production hosted configuration requires DPoP-bound access tokens.
- Development bearer-token mode works only behind the explicit configuration
  flag.
- All hosted tool calls are authorized from tool metadata and MCP scopes before
  the underlying API is called.
- Certificate mutation tools require both certificate and mutation/destructive
  authorization.
- The internal MCP-to-API key remains unobservable and non-overridable by hosted
  callers.
- Existing stdio MCP behavior remains compatible with Phase 1.

### Phase 3: Authentication Profile Simplification

Phase 2 intentionally keeps the first hosted-auth implementation close to the
underlying switches: transport, DPoP requirement, development bearer allowance,
audience, read-only mode, and internal API credentials. That is useful for
incremental delivery, but it creates too many valid-looking combinations for
operators to reason about.

Phase 3 should replace the loose authentication switch matrix with explicit
runtime profiles. A profile is a named security posture that derives the lower
level settings and rejects contradictory configuration.

`Mcp:Profile` is the source of truth for runtime security posture. Lower-level
settings are either derived effective values or explicitly documented
non-weakening overrides. Runtime code should consume resolved effective MCP
settings, not independently reason over the raw configuration switch matrix.

The intended resolution model is:

```text
raw Mcp configuration -> profile resolver -> effective runtime settings -> MCP runtime/auth/tool behavior
```

Effective settings should include the selected profile, transport, caller
authentication requirement, DPoP requirement, bearer-token allowance, audience,
and read-only posture. Validation and readiness should report against these
effective settings so tests verify the actual security posture rather than only
the raw input flags.

Initial profiles:

| Profile | Intended use | Effective behavior |
|---|---|---|
| `LocalStdio` | Default developer-machine workflow | `Stdio` transport, no hosted caller token, local read-only and `confirm` guards only |
| `LocalHostedDevelopment` | Local HTTP MCP testing and automated integration tests | `Http` transport on localhost, bearer tokens allowed, DPoP accepted, production exposure disallowed |
| `HostedProduction` | Remote agent access behind a trusted reverse proxy | `Http` transport, DPoP-bound access tokens required, bearer tokens rejected, hosted read-only default unless explicitly overridden |

Phase 3 work items:

1. Add `Mcp:Profile = LocalStdio | LocalHostedDevelopment | HostedProduction`.
   - Keep `LocalStdio` as the default when no profile is configured.
   - Continue supporting the lower-level settings only as derived/effective
     values or explicit advanced overrides with validation.
   - Reject lower-level overrides that contradict or weaken the selected
     profile's security guarantees.
   - Treat `Mcp:Transport` as profile-derived when a profile is selected. If the
     raw transport setting remains accepted for compatibility, it must match the
     selected profile's derived transport.
2. Centralize profile validation.
   - Reject `HostedProduction` when bearer tokens are enabled.
   - Reject `HostedProduction` when DPoP is disabled.
   - Reject `LocalHostedDevelopment` unless hosted HTTP binding is local-only or
     the environment is explicitly marked as development/test.
   - Reject invalid combinations such as hosted HTTP with neither DPoP nor
     development bearer enabled.
   - Define the development/test signal used by validation, such as
     `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, or an explicit MCP-specific
     escape hatch.
   - Define what counts as local-only hosted binding, including loopback hosts
     such as `localhost`, `127.0.0.1`, and `[::1]`, and excluding wildcard or
     public bindings unless explicitly allowed for development/test.
3. Make API exposure rules explicit.
   - Document that public production traffic reaches only the hosted MCP service,
     not `Backend.Api`.
   - Keep `Backend.Api` administrative routes protected by the internal
     credential while they remain private-network only.
   - Treat discovery and token issuance endpoints separately from administrative
     API routes.
4. Separate token audiences by resource.
   - Require MCP callers to present tokens whose audience matches
     `Mcp:Hosted:Audience`.
   - Document that API tokens and MCP tokens are separate resource tokens unless
     a later authorization-server change introduces first-class resource
     indicators or multi-audience issuance.
   - Clarify DPoP token issuance and resource-call behavior: the token endpoint
     validates a signed DPoP proof and embeds the proof key thumbprint in the
     issued access token `cnf.jkt`; each hosted MCP resource request must still
     present a fresh DPoP proof whose key thumbprint matches that token
     confirmation claim.
5. Update readiness and documentation.
   - Readiness should report the selected profile and derived auth posture.
   - Readiness should distinguish raw configuration from effective runtime
     settings, including profile, transport, caller authentication requirement,
     DPoP requirement, bearer-token allowance, audience, and read-only posture.
   - Deployment docs should show one local stdio profile, one local hosted test
     profile, and one production hosted profile.
6. Add a profile security test matrix.
   - Each profile must have positive and negative tests for startup validation,
     accepted authentication methods, rejected authentication methods, and tool
     scope enforcement.
   - Security posture tests should run against derived effective settings, not
     only the raw input configuration.

Phase 3 profile security test matrix:

| Profile | Positive tests | Negative tests |
|---|---|---|
| `LocalStdio` | Stdio MCP starts without caller token auth; local read-only and `confirm` guards still work | Hosted `/mcp` is not exposed by default |
| `LocalHostedDevelopment` | Hosted MCP accepts bearer tokens in development/test; hosted MCP accepts DPoP-bound tokens; MCP scopes are enforced | Bearer tokens are rejected outside development/test; missing scopes fail; public/non-local binding fails unless explicitly allowed |
| `HostedProduction` | Hosted MCP accepts valid DPoP-bound tokens with the configured MCP audience and required scopes | Bearer tokens are rejected; missing DPoP proof is rejected; `AllowBearerTokensForDevelopment = true` fails startup; `RequireDpop = false` fails startup; wrong token audience is rejected |
| API boundary | MCP can call private `Backend.Api` with the internal MCP-to-API credential | Hosted callers cannot pass, override, or observe `X-Api-Key`; production docs never describe API administrative routes as public bearer/DPoP resources |

Phase 3 exit criteria:

- There are no ambiguous production-valid bearer/DPoP combinations.
- Operators can select one profile instead of manually composing transport and
  authentication switches.
- Hosted production always requires DPoP-bound access tokens.
- Hosted development bearer support is impossible to enable accidentally in a
  production profile.
- The profile security test matrix has positive and negative coverage for every
  supported profile.
- The API/MCP endpoint authentication matrix can be documented as profile-based
  behavior, not a cross-product of independent flags.

Phase 3 implementation status:

| Status | Item | Notes |
|---|---|---|
| Done | Profile model and resolver | `Mcp:Profile` supports `LocalStdio`, `LocalHostedDevelopment`, and `HostedProduction`; runtime code resolves effective settings before selecting transport/auth behavior. |
| Done | Profile-derived transport | Startup uses resolved effective transport instead of independently branching on raw `Mcp:Transport`. |
| Done | Hosted auth consumes effective posture | Hosted authentication uses resolved DPoP and bearer-development behavior. |
| Done | Tool policy consumes effective posture | MCP tool authorization uses resolved transport and read-only posture. |
| Done | Readiness reports effective posture | Readiness includes profile, transport, caller-auth requirement, DPoP requirement, bearer allowance, audience, and read-only posture. |
| Done | Profile-oriented demo scripts | Demo scripts are split by `LocalStdio`, `LocalHostedDevelopment`, and `HostedProduction`; `demo-hosted-mcp.sh` remains a compatibility wrapper. |
| Done | Profile validation | Contradictory transport, DPoP, and bearer overrides are rejected; local-hosted public-binding checks are covered through DI with runtime configuration/environment. |
| Done | Documentation | README shows profile-based startup, demos, API/MCP audience separation, endpoint boundaries, and development/test escape-hatch guidance. |
| Done | Local hosted DPoP coverage | `LocalHostedDevelopment` demo now exercises both bearer and DPoP-bound hosted MCP calls. |
| Done | Readiness raw-vs-effective distinction | Readiness reports raw input configuration separately from resolved effective runtime posture. |
| Partial | Complete profile security test matrix | Unit coverage now includes validation wiring, DPoP/bearer auth posture, missing DPoP proof, missing scopes, and caller API-key header isolation; remaining gap is a full hosted `/mcp` integration matrix across every profile. |
| Done | API boundary profile documentation | README includes an explicit matrix showing hosted MCP as the public resource and `Backend.Api` administrative routes as private/internal-key protected. |

### Phase 4: Audit and Destructive Action Safety

- Add `McpToolInvoked`, `McpToolDenied`, `McpToolFailed`, and `McpToolSucceeded`
  audit events.
- Preserve `confirm: true` for destructive tools.
- Require destructive scope and explicit confirmation for deletes and certificate
  revocation.
- Log denied attempts with security-relevant context.

Phase 4 implementation status:

| Status | Item | Notes |
|---|---|---|
| Done | MCP tool audit events | Hosted MCP tool calls emit `McpToolInvoked`, `McpToolDenied`, `McpToolFailed`, and `McpToolSucceeded` audit events through a centralized call filter. |
| Done | Destructive confirmation preservation | Existing `confirm: true` requirements remain enforced by the mutation guard before tool execution. |
| Done | Destructive scope enforcement | Destructive delete and certificate revocation tools continue to require the configured destructive/certificate scopes. |
| Done | Denied-attempt security context | Denied tool calls are logged with caller, client, scopes, instance/resource identifiers, policy flags, confirmation state, profile, and transport context. |

### Phase 5: Higher-Level Workflow Tools

- Add workflow tools only after hosted auth, profile validation, policy, and audit
  are in place.
- Implement workflows as orchestration over the existing API contract.
- Initial workflow tools:
  - rotate machine-client certificate,
  - prepare DPoP-bound client credential instructions,
  - onboard machine client with roles, scopes, and optional certificate,
  - preflight machine-client credential status before deployment.
- Return structured step results that are easy for agents and tests to inspect.

Phase 5 implementation status:

| Status | Item | Notes |
|---|---|---|
| Done | Certificate rotation workflow | `idm_rotate_machine_client_certificate` resolves the machine client, inspects active certificates, issues a replacement certificate from a CSR, and optionally revokes a previous certificate after destructive confirmation. |
| Done | DPoP credential instructions workflow | `idm_prepare_dpop_client_credential_instructions` returns discovery-backed, structured instructions for caller-owned DPoP key, certificate CSR, token request, and hosted MCP call setup. |
| Done | Onboarding workflow | Existing `idm_onboard_machine_client` remains the workflow for creating or updating a machine client with roles, scopes, and optional CSR/external certificate. |
| Done | Deployment preflight workflow | `idm_preflight_machine_client_deployment` reports readiness, blocking issues, warnings, active certificates, assignment checks, and suggested next actions. |
| Done | Structured workflow results | New workflow result records expose step status, correlation ids, blocking issues, warnings, and next actions for agents and tests. |

### Phase 6: Deployment and Operations Hardening

- Add container and reverse-proxy examples for `Backend.Api` and hosted
  `Backend.Mcp`.
- Run `Backend.Api` and `Backend.Mcp` as separate Docker Compose services so the
  authorization-server and MCP resource-server boundaries are operationally
  visible.
- Use separate public hostnames for the two external surfaces:
  - `auth.idp.madmetal.org` for authorization-server discovery and token
    issuance,
  - `mcp.idp.madmetal.org` for hosted MCP and protected resource metadata.
- Terminate public TLS at the existing NGINX reverse proxy with Let's Encrypt
  certificates.
- Keep `Backend.Api` administrative management routes private. Public NGINX
  routing may expose only the authorization-server discovery/JWKS/token
  endpoints needed by OAuth clients; SCIM, certificate management, role/scope,
  user, and machine-client administration remain reachable only from the private
  Docker network or trusted operator access.
- Keep `Backend.Mcp` publicly reachable only through NGINX and only for hosted
  MCP, readiness as explicitly permitted by infrastructure policy, and OAuth
  protected resource metadata.
- Support pipeline-driven image updates with immutable image tags and
  server-side Compose deployment that can pull, restart, and health-check the two
  services independently.
- Default `HostedProduction` MCP to read-only unless explicitly configured
  otherwise.
- Supply secrets through environment variables or a deployment config provider.
- Document TLS termination, trusted forwarded headers, DPoP requirements, MCP
  scopes, profile selection, secret rotation, health checks, and audit review.
- Keep SQLite supported for the learning version without blocking a later PostgreSQL
  migration.

Phase 6 recommended production topology:

```text
Internet
   |
   | HTTPS, Let's Encrypt
   v
NGINX on Hetzner
   |-------------------------------|
   | auth.idp.madmetal.org         | mcp.idp.madmetal.org
   | public auth endpoints only    | hosted MCP resource
   v                               v
Backend.Api container          Backend.Mcp container
   ^                               |
   | internal Docker network       | internal X-Api-Key credential
   |-------------------------------|
```

Phase 6 work items:

1. Add production Compose documentation.
   - Define separate `backend-api` and `backend-mcp` services.
   - Bind service ports to loopback or the private Docker network, not directly
     to public interfaces.
   - Persist the database and signing key material through named volumes or
     explicitly managed host paths.
   - Pass `AuthorizationServer:Issuer=https://auth.idp.madmetal.org`.
   - Pass `Mcp:Profile=HostedProduction`.
   - Pass `Mcp:Hosted:Audience` as the MCP resource audience.
   - Configure `IdmApiInstances:local:BaseUrl` to the private API service URL
     inside Compose.
2. Add NGINX deployment documentation.
   - Route `auth.idp.madmetal.org` only to the public authorization endpoints.
   - Route `mcp.idp.madmetal.org/mcp` to `Backend.Mcp`.
   - Route `mcp.idp.madmetal.org/.well-known/oauth-protected-resource` and, if
     path-specific metadata is implemented, the endpoint-specific protected
     resource metadata path to `Backend.Mcp`.
   - Strip inbound forwarded headers and recreate `X-Forwarded-Proto`,
     `X-Forwarded-Host`, and `X-Forwarded-For` at the trusted proxy boundary.
   - Do not route `Backend.Api` administrative paths from the public internet.
3. Add a remote production smoke path.
   - Reuse `scripts/demo-mcp-hosted-production.sh` against the two public
     hostnames.
   - Verify `GET https://mcp.idp.madmetal.org/health/ready` reports
     `HostedProduction`, DPoP required, bearer development disabled, and private
     API reachability.
   - Verify token issuance from `https://auth.idp.madmetal.org/connect/token`.
   - Verify hosted MCP rejects bearer scheme and accepts a DPoP-bound access
     token with the MCP audience.
4. Add pipeline deployment notes.
   - Build and push versioned images for `Backend.Api` and `Backend.Mcp`.
   - Deploy by updating Compose image tags, pulling images, restarting only the
     changed services, and checking health before pruning old images.
   - Keep database migrations explicit and ordered before replacing API
     instances that require the migrated schema.
5. Add operational checks.
   - Confirm public DNS resolves only through NGINX.
   - Confirm direct container ports are not reachable externally.
   - Confirm management endpoints return no public route.
   - Confirm logs and audit events do not include access tokens, DPoP proofs,
     API keys, certificate private keys, or CSRs with private-key material.

Phase 6 exit criteria:

- `auth.idp.madmetal.org` serves only the required public authorization-server
  surface over HTTPS.
- `mcp.idp.madmetal.org` serves hosted MCP over HTTPS with
  `HostedProduction`, DPoP required, and bearer development disabled.
- `Backend.Api` administrative management routes are not externally exposed.
- The remote hosted-production demo script passes against the Hetzner deployment.
- Updated container images can be deployed through the pipeline without manual
  shell edits on the server.
- The NGINX and Compose documentation is sufficient to rebuild the deployment on
  a fresh host.

Phase 6 implementation status:

| Status | Item | Notes |
|---|---|---|
| Done | Container build definitions | Separate Dockerfiles build `Backend.Api` and `Backend.Mcp` as ASP.NET runtime images. |
| Done | Production Compose example | `deploy/production/compose.yml` runs separate API and MCP services, shares signing keys, persists SQLite data, and binds app ports to loopback for NGINX. |
| Done | Environment example | `deploy/production/env.example` captures image tags, MCP audience, read-only posture, and the internal admin API key. |
| Done | NGINX example | `deploy/production/nginx-idmdemo.conf` splits `auth.idp.madmetal.org` and `mcp.idp.madmetal.org`, exposes only public auth/MCP routes, strips inbound sensitive headers, and recreates the forwarded client certificate header at the proxy boundary. |
| Done | Forwarded certificate compatibility | `Backend.Api` accepts NGINX escaped PEM client certificates in addition to existing base64 DER forwarded certificates. |
| Done | Production runbook | `docs/production-hetzner-docker-compose.md` documents build, server layout, Let's Encrypt/NGINX, smoke testing, pipeline deployment, and security checks. |
| Done | Remote smoke script inputs | `demo-mcp-hosted-production.sh` can separate private admin API setup, public authorization-server token issuance, public MCP calls, and private readiness checks. |
| Pending | Live Hetzner verification | Run the production smoke script against the actual server after DNS, certificates, images, and Compose deployment are in place. |

### Phase 7: Interactive MCP Client Compatibility

Open WebUI and similar browser-based MCP clients introduce a different problem
than the Phase 6 machine-client smoke path. They expect the MCP server to behave
like an OAuth-protected resource for an interactive user, discover the
authorization server, perform an authorization-code flow with PKCE, and obtain a
token for the MCP resource. That should be a separate phase so the production
machine-client deployment can be proven first.

Phase 7 should align with the current MCP HTTP authorization model:

- `Backend.Mcp` acts as the OAuth protected resource and exposes protected
  resource metadata with an `authorization_servers` entry pointing to
  `https://auth.idp.madmetal.org`.
- `Backend.Mcp` 401 responses include `WWW-Authenticate` metadata guidance so
  clients can discover the protected resource metadata document.
- `Backend.Api` acts as the authorization server and exposes authorization
  server metadata or OpenID Connect discovery metadata over
  `https://auth.idp.madmetal.org`.
- Interactive clients use authorization code plus PKCE, not the existing
  machine-client mTLS credential flow.
- Tokens issued for Open WebUI use the MCP resource identifier/audience and are
  not accepted as API administrative tokens.

Phase 7 work items:

1. Add MCP protected resource metadata.
   - Serve `/.well-known/oauth-protected-resource` from `Backend.Mcp`.
   - Include the canonical MCP resource identifier, supported MCP scopes, and
     `authorization_servers: ["https://auth.idp.madmetal.org"]`.
   - Ensure 401 and insufficient-scope responses include useful
     `WWW-Authenticate` challenge metadata.
2. Add interactive authorization-server support.
   - Add an authorization endpoint and authorization-code grant with PKCE.
   - Add exact redirect URI registration and validation.
   - Add user login/consent or a deliberately constrained first operator-user
     model.
   - Include `code_challenge_methods_supported` with `S256` in discovery
     metadata.
   - Decide whether to support pre-registered Open WebUI clients first, then
     dynamic client registration or client ID metadata documents later.
3. Add user-to-scope authorization policy.
   - Map interactive users to allowed MCP scopes.
   - Keep hosted MCP write/destructive/certificate scopes explicit and avoid
     granting them by default.
   - Preserve audit attribution with user subject, client id, scopes, tool name,
     and affected resource.
4. Validate Open WebUI integration.
   - Configure Open WebUI against `https://mcp.idp.madmetal.org/mcp`.
   - Verify metadata discovery, PKCE authorization, token refresh or
     reauthorization behavior, tool listing, read tool calls, insufficient-scope
     challenges, and denial audit events.
   - Keep DPoP for machine clients, but decide whether Open WebUI can support
     DPoP-bound resource calls before requiring it for interactive users.

Phase 7 exit criteria:

- Open WebUI can discover the MCP protected resource metadata and authorization
  server metadata without manual token copying.
- An interactive user can complete authorization-code plus PKCE and obtain an MCP
  resource token.
- Open WebUI can list tools and run read-only MCP tools with least-privilege
  scopes.
- Insufficient write/destructive scopes fail with actionable OAuth challenge
  metadata and audited denials.
- Machine-client DPoP behavior from Phase 6 remains intact.

## Configuration Additions

Recommended new configuration:

```json
{
  "Mcp": {
    "Profile": "LocalStdio",
    "Transport": "Stdio",
    "ReadOnly": false,
    "Hosted": {
      "RequireDpop": true,
      "AllowBearerTokensForDevelopment": false,
      "Audience": "idm-demo-mcp"
    }
  }
}
```

Recommended Phase 3 profile-oriented configuration:

```json
{
  "Mcp": {
    "Profile": "HostedProduction",
    "Hosted": {
      "Audience": "idm-demo-mcp"
    }
  }
}
```

## Testing Plan

Unit tests should cover:

- transport option validation,
- profile validation and derived effective settings,
- hosted authorization guard scope checks,
- read-only mode enforcement,
- destructive confirmation enforcement,
- certificate-tool scope requirements,
- audit event field construction.

Integration tests should cover:

- hosted MCP rejects unauthenticated requests,
- hosted MCP rejects missing or insufficient scopes,
- hosted MCP accepts valid scoped DPoP-bound tokens,
- hosted production rejects bearer tokens regardless of lower-level overrides,
- local hosted development accepts bearer tokens only in development/test
  posture,
- destructive tools require both scope and `confirm: true`,
- stdio MCP still works as before,
- hosted health endpoint reports API connectivity.

Security regression tests should cover:

- callers cannot pass or override the internal API key,
- production profile cannot be started with development bearer enabled,
- forwarded identity headers are ignored unless explicitly enabled in the correct
  component,
- denied hosted calls are audited.

Deployment checks should include:

- `make check`,
- hosted MCP startup smoke test,
- reverse-proxy example smoke test where feasible.

## Assumptions

- Reverse-proxy TLS termination is the production default.
- Direct Kestrel TLS remains available for local development and focused mTLS
  experiments.
- Hosted MCP is a separate service/process for the first implementation.
- DPoP-bound access tokens are the preferred remote-agent credential.
- Bearer-token hosted MCP is allowed only through explicit development or test
  configuration.
- Production hosted MCP uses the `HostedProduction` profile and never accepts
  bearer tokens.
- Multi-user attribution is based on token claims in the first implementation; no
  separate interactive user login flow is added in this epic.
