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

### Phase 3: Audit and Destructive Action Safety

- Add `McpToolInvoked`, `McpToolDenied`, `McpToolFailed`, and `McpToolSucceeded`
  audit events.
- Preserve `confirm: true` for destructive tools.
- Require destructive scope and explicit confirmation for deletes and certificate
  revocation.
- Log denied attempts with security-relevant context.

### Phase 4: Higher-Level Workflow Tools

- Add workflow tools only after hosted auth, policy, and audit are in place.
- Implement workflows as orchestration over the existing API contract.
- Initial workflow tools:
  - rotate machine-client certificate,
  - prepare DPoP-bound client credential instructions,
  - onboard machine client with roles, scopes, and optional certificate,
  - preflight machine-client credential status before deployment.
- Return structured step results that are easy for agents and tests to inspect.

### Phase 5: Deployment and Operations Hardening

- Add container and reverse-proxy examples for `Backend.Api` and hosted
  `Backend.Mcp`.
- Default hosted MCP to read-only unless explicitly configured otherwise.
- Supply secrets through environment variables or a deployment config provider.
- Document TLS termination, trusted forwarded headers, DPoP requirements, MCP
  scopes, secret rotation, health checks, and audit review.
- Keep SQLite supported for the learning version without blocking a later PostgreSQL
  migration.

## Configuration Additions

Recommended new configuration:

```json
{
  "Mcp": {
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

## Testing Plan

Unit tests should cover:

- transport option validation,
- hosted authorization guard scope checks,
- read-only mode enforcement,
- destructive confirmation enforcement,
- certificate-tool scope requirements,
- audit event field construction.

Integration tests should cover:

- hosted MCP rejects unauthenticated requests,
- hosted MCP rejects missing or insufficient scopes,
- hosted MCP accepts valid scoped DPoP-bound tokens,
- destructive tools require both scope and `confirm: true`,
- stdio MCP still works as before,
- hosted health endpoint reports API connectivity.

Security regression tests should cover:

- callers cannot pass or override the internal API key,
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
- Multi-user attribution is based on token claims in the first implementation; no
  separate interactive user login flow is added in this epic.
