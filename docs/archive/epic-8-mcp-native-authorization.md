# Epic 8: MCP-Native Authorization and Delegated Resource Access

## Purpose

Align hosted MCP authentication with the current MCP authorization model while preserving
IdmDemo's stronger DPoP capabilities for downstream resource-server access.

The goal is to make `Backend.Mcp` interoperable with realistic MCP clients such as
Codex and OpenWebUI, without turning MCP into a lower-trust path around IdmDemo's
authorization model. Hosted MCP should behave as an OAuth protected resource: it accepts
audience-bound access tokens issued for the MCP server, enforces per-user or per-client
tool policy, and obtains separate upstream credentials when calling protected resource
servers.

This epic replaces the earlier assumption that hosted MCP itself should require DPoP
from all remote clients. DPoP remains a first-class IdmDemo capability, but its most
natural role is for resource servers that require sender-constrained access tokens.

## Problem Statement

IdmDemo currently supports DPoP-bound access tokens and hosted MCP authentication. The
existing hosted MCP design prefers DPoP-bound caller tokens for production. That provides
strong sender constraint, but it is not the most interoperable shape for MCP clients.

Realistic MCP clients fall into two categories:

- Coding assistants such as Codex, where the caller may be a local developer workflow,
  a user-authorized agent, or a machine-style automation client.
- Multi-user frontends such as OpenWebUI, where authorization must be attributable to
  the individual user, not only to the OpenWebUI deployment.

The MCP server should not accept a weaker trust boundary than downstream resource
servers, but it should also stay inside the MCP authorization model. The correct split is:

- MCP client to MCP server: OAuth 2.1 bearer access token, audience-bound to the MCP
  server, with strict validation and per-tool policy.
- MCP server to downstream resource server: separate upstream access token obtained by
  the MCP server, using DPoP when the downstream resource requires DPoP.
- No passthrough of inbound MCP caller tokens to downstream APIs.

## Standards Alignment

Hosted MCP should follow the MCP authorization model:

- `Backend.Mcp` is an OAuth protected resource / resource server.
- Hosted callers present access tokens using the HTTP `Authorization: Bearer` header.
- `Backend.Mcp` publishes OAuth Protected Resource Metadata.
- Access tokens must be audience/resource-bound to the MCP server.
- Tokens intended for other resources must be rejected.
- Inbound MCP tokens must not be passed through to downstream resource servers.
- When `Backend.Mcp` calls another protected API, it acts as an OAuth client and obtains
  a separate upstream token.

DPoP support remains part of IdmDemo's authorization server and protected-resource
validation capabilities, but DPoP is not required as the baseline MCP transport
credential.

## Target Architecture

```text
Codex / OpenWebUI / MCP client
        |
        | HTTPS
        | Authorization: Bearer <token audience = idm-demo-mcp>
        v
Backend.Mcp
        |
        | validates issuer, audience, expiry, scopes, subject, client id
        | enforces MCP tool policy
        |
        | obtains separate upstream token when needed
        | optionally DPoP-bound for resource servers that require DPoP
        v
Downstream resource server / Backend.Api / future protected APIs
```

For OpenWebUI, the preferred model is per-user OAuth. OpenWebUI may be the OAuth client,
but authorization decisions inside IdmDemo must be based on the individual user's token
claims and scopes where the client supports that flow.

For Codex and similar coding assistants, the preferred model depends on deployment mode:

- Local developer mode may continue to use stdio without hosted caller authentication.
- User-delegated remote access should use OAuth authorization code with PKCE.
- Automation or machine-style access should use MCP client credentials with strong
  client authentication, preferably asymmetric client authentication such as
  `private_key_jwt` when supported.

## Decisions

### Inbound MCP Authentication

Hosted MCP accepts MCP-spec-compatible bearer access tokens.

`Backend.Mcp` must validate:

- token signature,
- issuer,
- expiry and not-before,
- audience/resource equal to the configured MCP audience,
- subject,
- client id where present,
- scopes,
- token replay-sensitive claims where applicable,
- configured trust policy for the selected runtime profile.

DPoP-bound inbound MCP tokens may remain supported as an optional advanced profile, but
they must not be required for baseline MCP interoperability.

### Protected Resource Metadata

Hosted MCP must publish OAuth Protected Resource Metadata so clients can discover:

- the MCP resource identifier,
- authorization server issuer,
- supported scopes,
- supported authentication flows or links to authorization server metadata.

Unauthenticated hosted MCP requests should return a standards-aligned `401` challenge
that points clients toward the protected resource metadata.

### Tool Authorization

Hosted MCP authorization is evaluated per tool before invoking the underlying API.

Initial scopes remain:

| Scope | Allows |
|---|---|
| `idm.mcp.read` | Read-only MCP tools |
| `idm.mcp.write` | Non-destructive mutating tools |
| `idm.mcp.destructive` | Destructive tools when `confirm: true` is supplied |
| `idm.mcp.certificates` | Certificate issuance and revocation workflows |

For user-delegated flows, permissions are derived from the individual user claims and
scopes. For client-credentials flows, permissions are derived from the machine client
identity and assigned scopes.

### OpenWebUI Multi-User Mode

OpenWebUI must not become the authorization subject for all users unless explicitly
configured as a trusted service account mode.

Preferred behavior:

- Each OpenWebUI user authorizes access individually.
- The MCP token contains a stable user subject.
- Audit events attribute actions to the individual user and the OAuth client.
- Tool authorization uses the user's roles/scopes, not only OpenWebUI's deployment
  identity.

Fallback service-account mode may be supported for demos, but it must be clearly labeled
as lower-fidelity and should use restricted scopes.

### Codex / Agent Mode

Codex and similar clients should support both local and hosted workflows:

- Local stdio remains the low-friction developer default.
- Hosted user-delegated access uses OAuth authorization code with PKCE where available.
- Hosted machine access uses client credentials with strong client authentication when
  available.
- All hosted modes use audience-bound tokens for `Backend.Mcp`.

### Downstream Resource Access

`Backend.Mcp` must not pass the inbound MCP access token to downstream resource servers.

When a tool needs to call a protected downstream resource:

1. Determine the effective caller and requested operation.
2. Evaluate MCP tool authorization locally.
3. Request a separate upstream access token for the downstream resource.
4. Use DPoP for the upstream token when the downstream resource requires DPoP.
5. Call the downstream resource with the upstream token and, if needed, DPoP proof.
6. Preserve audit linkage between the original MCP caller and the downstream action.

This allows downstream resource servers to require DPoP without forcing all MCP clients
to implement DPoP for the MCP transport itself.

## Architectural Review: Recommended Refactoring

Changing hosted MCP from "DPoP-first inbound authentication" to "MCP-native bearer
protected resource with optional DPoP profiles" touches several existing seams:
runtime profile resolution, hosted token validation, caller claim construction, per-tool
authorization, readiness reporting, and IdM API credential use. The safest path is not a
large rewrite. Refactor the authorization boundary into small, testable components first,
then change profile behavior through those components.

### Current Complexity Risks

- `McpRuntimeProfileResolver` currently encodes both deployment mode and authentication
  policy. Moving `HostedProduction` to bearer tokens while adding
  `HostedProductionDpopInbound` will make this class the center of several conditional
  branches unless the authentication requirement is made explicit.
- `McpHostedAuthenticationMiddleware` parses HTTP auth, chooses bearer or DPoP
  validation, creates `ClaimsPrincipal`, and writes challenges. Adding protected resource
  metadata and standards-aligned challenges would make it responsible for too many
  protocol details.
- `McpMutationGuard` reads raw claims directly while enforcing tool policy. That works
  for simple scopes, but it will become fragile when user-delegated tokens, machine
  clients, OAuth client ids, and service-account mode need different audit and policy
  treatment.
- Readiness checks duplicate some hosted-auth assumptions from startup validation. If
  profile semantics change in multiple places, local development may appear healthy while
  production startup rejects the same configuration, or the reverse.
- `IdmApiClient` currently uses configured internal API credentials. Phase 4 should
  introduce upstream OAuth credentials without letting MCP tool code choose between
  internal credentials, inbound caller tokens, and delegated resource tokens ad hoc.

### Recommended Refactoring Sequence

1. Introduce a resolved hosted authorization policy model.

   Add a small immutable model such as `McpHostedAuthorizationPolicy` that is produced
   from `McpRuntimeOptions` and contains explicit fields for transport, caller auth
   requirement, accepted inbound token schemes, MCP audience/resource identifier,
   read-only mode, and whether the profile is local-only. Keep profile names as inputs,
   not as the values downstream components must branch on.

   `McpRuntimeProfileResolver`, `McpRuntimeOptionsValidator`, readiness reporting, and
   hosted auth middleware should consume this policy model. This reduces the risk of
   profile semantics drifting as `HostedProduction` changes from DPoP-required to
   bearer-required.

2. Split hosted authentication into protocol, validation, and challenge services.

   Keep the ASP.NET middleware thin. It should identify MCP requests, call an
   `IMcpCallerAuthenticator`, assign the authenticated principal or caller context, and
   delegate failure responses to an `IMcpAuthenticationChallengeWriter`.

   The authenticator should own bearer-vs-DPoP selection and audience validation. The
   challenge writer should own `WWW-Authenticate` values and links to protected resource
   metadata. This keeps Phase 2 metadata work from expanding the middleware into a
   protocol hub.

3. Normalize caller identity before tool authorization.

   Add a normalized caller record such as `McpCallerContext` with fields for subject,
   user subject, machine client subject, OAuth client id, scopes, audience, auth scheme,
   confirmation method, and service-account indicator. Build it once after token
   validation and use it for tool authorization and audit.

   `McpMutationGuard` should ask an authorization service whether a normalized caller
   can invoke a tool policy. It should not parse raw claims itself. That keeps the
   OpenWebUI per-user path, Codex machine-client path, and service-account fallback from
   multiplying claim-parsing rules across tool code.

4. Make tool policy evaluation independent from transport mode.

   Preserve the existing local stdio behavior, read-only mode, destructive confirmation,
   and certificate scope checks, but express them in one tool authorization component.
   The component should accept the resolved hosted policy, optional caller context, tool
   policy, and arguments. Local mode becomes "no hosted caller context required" rather
   than a separate branch that bypasses most of the policy system.

5. Add protected resource metadata as a dedicated endpoint/service.

   Model the metadata from the same resolved hosted authorization policy used by startup,
   readiness, and authentication. This prevents the MCP resource identifier, issuer, and
   scopes from being copied into multiple places. Challenge responses should reference
   this endpoint rather than hand-building unrelated metadata in middleware.

6. Introduce upstream credential acquisition behind a narrow interface.

   Before adding downstream DPoP delegation, create an abstraction such as
   `IMcpUpstreamTokenProvider` or `IDownstreamResourceCredentialProvider`. Tool
   implementations should request credentials for a named downstream resource and
   operation; they should never receive or forward the inbound MCP access token.

   Keep current internal API-key access as the existing demo path until a specific
   resource-server call is migrated. The new abstraction can initially reject unsupported
   resource requests, then grow to support resource indicators, DPoP-bound upstream
   tokens, and audit correlation.

7. Keep audit enrichment close to caller context creation.

   Extend audit records from `McpCallerContext`, not by re-reading claims at each audit
   call site. This ensures denials and successes use the same subject, client id, scopes,
   token audience, auth scheme, and service-account classification.

### Refactoring Guardrails

- Do not add a general OAuth orchestration layer inside `Backend.Mcp`. It should validate
  inbound MCP tokens, enforce tool policy, and obtain downstream credentials through a
  narrow provider. Authorization-server behavior belongs in `Backend.Application` and
  `Backend.Api`.
- Do not let tool implementations inspect bearer token strings, DPoP proofs, or raw
  `Authorization` headers. Those are transport/authentication concerns.
- Do not introduce token exchange or on-behalf-of behavior as an implicit side effect of
  tool invocation. If used later, make it an explicit upstream credential provider mode
  with tests proving inbound token passthrough is impossible.
- Do not couple OpenWebUI, Codex, or any other client name into authorization logic.
  Differences should be represented by token claims, auth flow, scopes, and configured
  profile policy.
- Keep each phase reversible. Profile policy extraction and caller context normalization
  should land before changing production defaults, so behavior changes are isolated and
  easy to test.

### Suggested Implementation Order

1. Extract the resolved hosted authorization policy and update validator/readiness tests.
2. Extract caller authentication and challenge writing from middleware without changing
   behavior.
3. Add `McpCallerContext` and update audit/tool authorization to consume it while keeping
   existing scope outcomes unchanged.
4. Change `HostedProduction` to bearer-required and add the explicit
   `HostedProductionDpopInbound` profile.
5. Add protected resource metadata and standards-aligned `401` challenges.
6. Add the upstream credential provider abstraction before implementing downstream DPoP.

## Phased Implementation Plan

### Phase 1: Reframe Hosted MCP Profiles

Update the hosted MCP profile model so production hosted MCP is MCP-native by default.

Profiles:

| Profile | Intended use | Effective behavior |
|---|---|---|
| `LocalStdio` | Local developer workflow | Stdio transport, no hosted caller token |
| `LocalHostedDevelopment` | Local HTTP testing | Bearer tokens allowed, relaxed local-only settings |
| `HostedProduction` | Remote MCP access | HTTP transport, bearer access tokens required, strict audience/scope validation |
| `HostedProductionDpopInbound` | Optional advanced profile | HTTP transport, DPoP-bound inbound MCP tokens required where clients support it |

Exit criteria:

- `HostedProduction` no longer requires DPoP for inbound MCP interoperability.
- DPoP-required inbound MCP remains available only as an explicit advanced profile.
- Invalid or weakening combinations are rejected during startup/readiness validation.

### Phase 2: Protected Resource Metadata

Add MCP protected-resource metadata support.

Work items:

- Expose protected resource metadata for hosted MCP.
- Include the MCP resource identifier/audience.
- Link to IdmDemo authorization server metadata.
- Advertise MCP scopes.
- Return appropriate `WWW-Authenticate` challenges for unauthenticated requests.
- Add integration tests for metadata and challenge behavior.

Exit criteria:

- MCP clients can discover how to obtain a token for `Backend.Mcp`.
- Hosted MCP rejects tokens without the correct resource/audience.

### Phase 3: Per-Caller Authorization Model

Strengthen caller identity and policy handling.

Work items:

- Normalize hosted caller context for user-delegated and client-credentials tokens.
- Distinguish user subject, OAuth client id, and machine client subject.
- Enforce tool scopes from the normalized caller context.
- Preserve existing read-only and destructive confirmation behavior.
- Add audit fields for user subject, client id, scopes, token audience, and auth flow.

Exit criteria:

- OpenWebUI-style multi-user calls can be attributed to individual users.
- Service-account style calls are explicitly identifiable.
- Tool authorization is independent of the frontend that initiated the call.

### Phase 4: Downstream DPoP Delegation

Add the outbound credential path needed for DPoP-required resource servers.

Work items:

- Introduce an upstream token acquisition abstraction for MCP tools.
- Support resource-specific token requests using OAuth resource indicators.
- Support DPoP-bound upstream access tokens.
- Generate and attach DPoP proofs for downstream protected-resource requests.
- Ensure inbound MCP tokens are never forwarded downstream.
- Add regression tests proving token passthrough is rejected or impossible.

Exit criteria:

- MCP tools can call a DPoP-required resource server using a separate upstream token.
- Downstream calls preserve audit correlation to the original MCP caller.
- No inbound MCP token is reused as a downstream API credential.

### Phase 5: Demo and Documentation

Update demo scripts and docs to show the intended trust model.

Work items:

- Add a hosted MCP demo for bearer-token MCP auth.
- Add an OpenWebUI-style multi-user scenario if practical.
- Add a Codex/agent-oriented hosted scenario.
- Add a downstream DPoP resource-server scenario.
- Update Epic 7 language that currently implies DPoP is the production default for
  hosted MCP inbound auth.

Exit criteria:

- Demo output clearly distinguishes inbound MCP auth from outbound resource-server auth.
- Docs explain why bearer tokens are MCP-native and why DPoP still matters downstream.
- Existing stdio and hosted development flows continue to work.

## Security Requirements

- Hosted MCP must reject unauthenticated requests.
- Hosted MCP must reject expired, unsigned, malformed, or wrong-audience tokens.
- Hosted MCP must reject tokens issued for downstream APIs.
- Hosted MCP must reject insufficient scopes before invoking tools.
- Hosted MCP must never expose or accept overrides for internal API credentials.
- Hosted MCP must not pass inbound caller tokens to downstream resource servers.
- Destructive tools must require both scope and explicit confirmation.
- Audit events must distinguish user subject, OAuth client, machine client, and target
  resource where available.

## Out of Scope

- Building a full browser login UI unless required for the selected OAuth flow.
- Replacing existing mTLS machine-client authentication.
- Requiring all MCP clients to support DPoP for inbound MCP calls.
- Full enterprise policy administration UI.
- Persisted approval workflows or multi-party approvals.
- Token exchange standardization beyond the minimum needed for the demo.

## Open Questions

- Which OAuth flow should be the primary OpenWebUI demo path: per-user authorization
  code with PKCE, or a constrained service-account fallback?
- Should IdmDemo implement OAuth token exchange later, or keep upstream token acquisition
  as a simpler client-credentials/on-behalf-of demo abstraction for now?
- Should `HostedProductionDpopInbound` remain a first-class profile, or should inbound
  DPoP be treated as an experimental compatibility mode?
- What resource identifiers should be standardized for `Backend.Mcp`, `Backend.Api`,
  and future demo resource servers?

## Success Criteria

- Hosted MCP is compatible with MCP clients that implement OAuth bearer-token protected
  resource access.
- OpenWebUI-style multi-user deployments can preserve individual user authorization.
- Codex-style clients can use either local stdio or hosted OAuth access.
- DPoP remains demonstrated where it is strongest: downstream resource-server access.
- The system has a clear, documented boundary between MCP caller authentication and
  downstream resource-server credentials.
