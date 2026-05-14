# Epic 3: Certificate Lifecycle Design

## Purpose

Epic 3 adds certificate lifecycle management for machine clients. The goal is to let administrators, AI chat interfaces, and AI agents perform the full certificate workflow through authenticated APIs while preserving the security boundary that client private keys should normally stay outside the server.

This design unblocks implementation by defining the first certificate lifecycle model for the learning version of IdmDemo.

## Decisions

### Issuance Model

The primary issuance model is CSR-based.

Clients generate their own private key and submit a Certificate Signing Request. The server signs the CSR with a local development Certificate Authority and returns the issued public certificate. This avoids server-side client private key handling.

To support agentic workflows, the API may also support externally issued certificate registration. In that flow, the caller submits public certificate material and metadata, and the system stores it for token authentication. The server does not need access to the private key.

Server-generated client key pairs are out of scope for the first Epic 3 implementation. They increase private key custody and delivery risk. They may be reconsidered later only with an explicit secure delivery design.

### Certificate Authority Model

The first implementation uses a local development CA.

The local CA is intended for development and private experimentation only. It should be generated and persisted locally, similar to the JWT signing key store, so issued certificates remain verifiable across restarts.

The API should make the local-dev nature explicit in configuration and documentation. Production CA integration is out of scope for this epic.

### API Shape

Certificate lifecycle endpoints are administrative APIs under the existing SCIM-shaped client surface:

```http
POST /scim/v2/Clients/{id}/Certificates
GET  /scim/v2/Clients/{id}/Certificates
GET  /scim/v2/Clients/{id}/Certificates/{certificateId}
POST /scim/v2/Clients/{id}/Certificates/{certificateId}/Revoke
```

All certificate endpoints use the existing administrative API-key protection, correlation ID behavior, structured logging, and SCIM-style error envelopes.

### Create Certificate Inputs

`POST /scim/v2/Clients/{id}/Certificates` supports two modes:

1. CSR issuance: submit a PEM-encoded CSR and optional validity request.
2. External registration: submit a PEM-encoded public certificate and optional metadata.

The request must explicitly identify the mode so validation and audit logs are unambiguous.

Example CSR issuance request:

```json
{
  "mode": "csr",
  "certificateSigningRequestPem": "-----BEGIN CERTIFICATE REQUEST-----...",
  "displayName": "orders-service rotation 2026-05",
  "validityDays": 30
}
```

Example external registration request:

```json
{
  "mode": "external",
  "certificatePem": "-----BEGIN CERTIFICATE-----...",
  "displayName": "orders-service external cert",
  "expiresAt": "2026-08-12T00:00:00Z"
}
```

### Multiple Active Certificates

A machine client may have multiple active certificates. This supports certificate rotation overlap.

Token issuance should accept a presented certificate when all of these are true:

- The client is active.
- The certificate belongs to that client.
- The certificate thumbprint matches the presented certificate.
- The certificate is active.
- The certificate is not expired.
- The certificate is not revoked.

### Lifecycle States

The first implementation uses the minimal lifecycle states:

- `Active`
- `Revoked`
- `Expired`

`Expired` may be derived from `expiresAt` rather than eagerly persisted by a background job. API responses may report an expired certificate as `Expired` when `expiresAt` is in the past.

### Revocation Semantics

Revocation affects token issuance only.

Revoking a certificate prevents that certificate from being used to obtain future access tokens. Already-issued JWT access tokens remain valid until their normal expiration. Token introspection, token deny-lists, and downstream JWT revocation checks are out of scope for this epic.

Revocation is idempotent. Revoking an already revoked certificate should return a successful response with the certificate still in the `Revoked` state.

### Validity

Default certificate validity is 30 days.

Maximum certificate validity is 90 days.

The server should reject invalid validity requests, malformed CSRs, malformed certificates, certificates without a usable public key, and certificates that are already expired.

### Migration From Single Certificate Fields

The current `MachineClient` model stores a single certificate thumbprint, subject, and expiry directly on the client. Epic 3 should migrate toward a separate machine-client certificate collection.

Recommended implementation path:

1. Add a `MachineClientCertificate` entity and table.
2. Update token authentication to validate against the certificate collection.
3. Deprecate the existing direct certificate fields on client create/update.
4. Stop treating the existing fields as the source of truth after the new certificate API is available.
5. Remove or deprecate direct certificate updates through the client CRUD surface in a later cleanup.

During the transition, client create/update responses may continue to expose the direct certificate fields for compatibility, but new certificate lifecycle behavior should use the certificate collection.

### Certificate Response Shape

Certificate create, list, and get responses include full public certificate PEM material. The API must never return private keys.

### Local CA Public Certificate

The local development CA public certificate should be available through an administrative endpoint. It is not public discovery metadata for the first implementation.

## Domain Model

Add a machine-client certificate entity with at least:

- `Id`
- `MachineClientId`
- `DisplayName`
- `ThumbprintSha256`
- `Subject`
- `Issuer`
- `SerialNumber`
- `NotBefore`
- `ExpiresAt`
- `CertificatePem`
- `Status`
- `RevokedAt`
- `RevocationReason`
- `CreatedAt`
- `UpdatedAt`

The certificate table should have an index on `MachineClientId` and `ThumbprintSha256`. A unique constraint on `ThumbprintSha256` is reasonable unless a future external CA scenario requires duplicate public certificates across clients, which should generally be rejected.

## Application Services

Add a certificate lifecycle service responsible for:

- Issuing a certificate from a CSR using the local development CA.
- Registering an externally issued certificate.
- Listing certificates for a client.
- Reading one certificate for a client.
- Revoking a certificate.
- Normalizing and computing SHA-256 thumbprints.
- Validating certificate expiry and state.
- Writing audit logs for create and revoke operations.

The authorization server should depend on repository/service behavior that can find an active matching certificate for a client by SHA-256 thumbprint.

## Security Notes

The server must not log private key material, CSR contents, full certificate PEM bodies, or bearer tokens.

CSR-based issuance preserves key ownership by keeping the private key with the caller. Agentic workflows can still automate the full operation by generating the key locally in the client or agent environment, submitting the CSR, and storing the returned certificate and private key in the caller-controlled location.

The local development CA private key is sensitive. It should be stored locally, excluded from source control, and never exposed through API responses.

Forwarded client certificate handling from Epic 2 remains a deployment boundary. Epic 3 changes certificate registration and revocation, not the trust requirement that forwarded certificates are accepted only from a trusted proxy when explicitly enabled.

## Testing Requirements

Unit and integration tests should cover:

- CSR issuance creates an active certificate for the client.
- External registration stores public certificate material and metadata.
- A client can have multiple active certificates.
- Token issuance accepts any active matching certificate for the client.
- Token issuance rejects revoked certificates.
- Token issuance rejects expired certificates.
- Token issuance rejects certificates registered to another client.
- Revocation is idempotent.
- Certificate endpoints require the administrative API key.
- Certificate endpoints return SCIM-style error envelopes.
- Certificate list and get responses include full public certificate PEM material.
- The local development CA public certificate endpoint requires the administrative API key.
- Issued certificates use the configured local development CA.
- The CA private key and client private keys are never returned by API responses.
