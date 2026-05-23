---
name: mcp-server-security-review
description: "Use when reviewing MCP servers, MCP clients, MCP tools, or agentic tool integrations for security risks. Covers code review, configuration review, pre-commit checks, runtime validation, and production hardening for local, stdio, HTTP, and remote MCP deployments."
---

# MCP Server Security Review

Review MCP implementations as security-sensitive automation surfaces. Treat every tool call, resource read, prompt, serialized message, and downstream model/tool output as a boundary crossing unless the code proves otherwise.

Primary references:

- NSA, "Model Context Protocol (MCP): Security Design Considerations for AI-Driven Automation", May 2026.
- MCP Security Best Practices and Authorization specification.
- OAuth 2.1, OAuth 2.0 Protected Resource Metadata, Resource Indicators, and OWASP ASVS session guidance where authorization is used.

## Review Workflow

1. Map the deployment: transport (`stdio`, HTTP/SSE/streamable HTTP), exposed endpoints, tool registry, resources, prompts, upstream APIs, downstream tools, identity provider, token store, logs, and network boundary.
2. Classify every tool: read-only, state-changing, privileged, filesystem, shell/process, database, network, browser, code execution, or external SaaS.
3. Identify trust zones: host, MCP client, server, tool implementation, authorization server, upstream APIs, model, user, tenant, and external content.
4. Review static controls first. Mark each item `pass`, `fail`, `unknown`, or `runtime-only`.
5. Run runtime checks for exposure, authorization behavior, replay/session behavior, logging, rate limits, and sandbox boundaries.
6. Report findings by severity, with separate sections for pre-commit enforceable checks and runtime/operational checks.

## High-Risk Patterns

Prioritize these issues:

- No authentication or authorization on a remote or shared MCP server.
- Bearer token accepted without issuer, audience/resource, expiry, scope, and signature validation.
- Token passthrough to downstream APIs instead of exchanging or using a separate upstream token.
- Static third-party OAuth client ID plus dynamic MCP client registration without per-client consent.
- Dynamic tool discovery, package loading, or tool-name resolution without origin pinning and allowlists.
- Tools that execute shell commands, code, browser automation, database writes, file writes, or network calls without schema validation, policy checks, sandboxing, and audit logs.
- Tool descriptions, prompts, outputs, or resources that are passed to another model/tool as trusted instructions.
- Missing replay protection, weak session IDs, sessions used as authentication, or sessions not bound to the authenticated user.
- Broad OAuth scopes or repository/service permissions where per-tool, per-resource, or CRUD permissions are feasible.
- Incomplete audit trails for tool invocations, denied actions, malformed requests, auth failures, and policy violations.

## Static Review Checklist

Authentication and authorization:

- Remote servers require auth by default; local-only exceptions are explicit and documented.
- Each protected request validates token signature, issuer, audience/resource, expiry, not-before, authorized party/client where applicable, and required scopes.
- Scope checks happen per tool or capability, not just per connection.
- State-changing tools require stronger approval, explicit policy, or human confirmation where appropriate.
- OAuth flows use PKCE, exact redirect URI matching, `state`, short-lived codes/tokens, HTTPS except localhost development, secure token storage, and refresh-token rotation where applicable.
- MCP protected resource metadata and authorization server metadata are implemented where HTTP authorization is used.

Input, context, and serialization:

- Tool input schemas are strict: required fields, type checks, enum/range checks, max lengths, path canonicalization, URL allowlists, and reject-unknown-fields where possible.
- User-supplied context is not forwarded blindly between tools, models, tenants, or trust zones.
- Deserialization avoids polymorphic or executable object loading and treats prompts/comments/metadata as data.
- Tool outputs are validated or filtered before display, storage, chaining, or downstream model use.

Tool execution and containment:

- Shell/process execution uses argument arrays and allowlisted commands; no string-concatenated command execution.
- Filesystem access is scoped to explicit roots, canonicalized, and blocks traversal, symlink escape, and sensitive file reads.
- Database/API operations use parameterized calls, least-privilege credentials, and operation-level authorization.
- Network egress is restricted by destination, protocol, and method for tools that do not need open outbound access.
- Sandboxing is documented for privileged tools: container, seccomp, AppArmor, SELinux, Windows AppContainer, separate service account, or equivalent.

Sessions, replay, and idempotency:

- Session IDs are high-entropy, non-deterministic, rotated/expired, and not treated as proof of identity.
- Session state is bound to authenticated user/tenant/client context.
- State-changing requests use idempotency keys or duplicate protection when retries are possible.
- Messages that need integrity include timestamp, nonce/request ID, expiry, and signature or equivalent transport/session binding.

Supply chain and configuration:

- MCP dependencies, SDKs, scanner tools, inspector tools, and server packages are pinned and vulnerability-scanned.
- Dynamic tool/plugin registries are disabled, pinned, or restricted to trusted internal registries.
- Server configuration avoids command obfuscation, unreviewed startup commands, wildcard scopes, wildcard CORS, and secrets in source.
- Tool inventory records owner, version, permissions, data classification, and patch status.

Logging and monitoring:

- Logs capture tool name, caller identity, tenant, session/request ID, parameters or safe parameter hashes, decision, result status, and policy reason.
- Logs redact tokens, credentials, secrets, authorization codes, cookies, private keys, and sensitive payload fields.
- Alerts exist for auth failures, malformed JSON-RPC, denied policy checks, unusual tool sequences, high-volume calls, and unexpected network destinations.

## Pre-Commit Enforceable Checks

These are suitable for CI, pre-commit hooks, or static analysis:

- Secret scanning for tokens, private keys, OAuth client secrets, cookies, and certificates.
- Dependency vulnerability scans and pinned package/version policy.
- Lints for string-built shell commands, unsafe deserialization, wildcard CORS, disabled TLS verification, broad file globs, and unbounded request bodies.
- Unit tests for schema validation, scope enforcement, token audience rejection, denied privileged calls, path traversal, SSRF URL blocks, and redaction.
- Snapshot or manifest checks for tool inventory changes: new tools, changed tool descriptions, changed scopes, changed startup commands, changed external domains, changed filesystem roots.
- Config policy checks for localhost-only development settings, authentication-required production settings, explicit egress allowlists, and no default admin credentials.
- Review gates for tool descriptions/prompts because malicious or changed descriptions can alter downstream agent behavior.

Do not claim these prove the server is secure. They prove selected code and config controls are present before merge.

## Runtime and Operational Checks

These require a running server, deployed environment, or security tooling:

- Port and service discovery: scan localhost, developer hosts, cluster networks, and approved external ranges for unauthorized or unauthenticated MCP servers. Repeat periodically because MCP servers may change ports.
- Auth behavior: verify unauthenticated requests fail, invalid/expired/wrong-audience tokens fail, insufficient scopes return `403`, malformed auth returns `400` or `401`, and valid least-privilege tokens only access expected tools.
- Session/replay behavior: replay prior requests, reuse stale session IDs, duplicate state-changing requests, and cross-user session IDs to verify rejection or idempotency.
- Network boundary: observe actual inbound and outbound traffic; confirm egress allowlists, proxy/DLP controls, DNS restrictions, and no unexpected external callbacks.
- Sandbox boundary: attempt blocked filesystem reads, command execution, network access, privilege escalation, and lateral movement from tool execution contexts.
- DoS and fatigue resistance: test rate limits, max input sizes, recursive tool calls, long-running tasks, malformed JSON-RPC, prompt storms, and cancellation/timeout behavior.
- Logging and detection: verify audit events are emitted, redacted, correlated, retained, and alerting on anomalous patterns.
- Tool poisoning: connect trusted and untrusted tools in a realistic chain and verify outputs are treated as untrusted input before downstream use.

Clearly label these as runtime checks in reports and do not present them as pre-commit-only work.

## Report Format

Start with findings, ordered by severity. For each finding include:

- `Severity`: critical/high/medium/low.
- `Surface`: tool, resource, transport, auth flow, config, dependency, logging, runtime environment.
- `Evidence`: file/line, config key, test result, request/response summary, or scanner result.
- `Risk`: concrete abuse path, such as data exfiltration, unauthorized action, prompt injection, replay, SSRF, RCE, or privilege escalation.
- `Fix`: specific control to add.
- `Check type`: `pre-commit`, `runtime`, or `both`.

End with:

- Pre-commit controls that can be automated now.
- Runtime checks still required before production.
- Assumptions and areas not reviewed.
