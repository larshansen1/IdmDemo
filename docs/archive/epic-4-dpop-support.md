# Epic 4: DPoP Support Design

## Purpose

Epic 4 adds Demonstrating Proof-of-Possession (DPoP) support to the authorization server.
The goal is to let machine clients obtain access tokens that are bound to a client-held
DPoP key while preserving the existing mTLS machine-client authentication model.

This design defines the first DPoP implementation for the learning version of IdmDemo.

## Implementation Status

Implemented in the current Epic 4 slice:

- DPoP configuration on `AuthorizationServerOptions`.
- Discovery metadata via `dpop_signing_alg_values_supported`.
- Token endpoint `DPoP` header handling.
- Optional DPoP rollout mode with `RequireDpop=false` by default.
- Required DPoP mode via `AuthorizationServer:RequireDpop=true`.
- DPoP proof validation for token endpoint requests.
- In-memory DPoP replay cache behind `IDpopReplayCache`.
- DPoP-bound JWT access tokens using `cnf.jkt`.
- Existing mTLS-only bearer-token behavior using `cnf.x5t#S256`.
- Reusable downstream access-token and DPoP-bound-token validation services.
- Demo coverage in `scripts/demo-auth.sh`.
- Unit and integration coverage for token issuance, proof validation, replay handling,
  and downstream validation.

Still intentionally out of scope for this first implementation:

- DPoP nonce challenge support.
- Distributed replay cache storage.
- A production protected-resource API surface.
- Replacing mTLS client authentication with DPoP-only client authentication.
- Access-token revocation or introspection.

## Decisions

### Relationship to mTLS

The first implementation keeps mTLS client authentication as the required client
authentication mechanism for `/connect/token`.

DPoP does not replace mTLS in this epic. Instead, a machine client authenticates with
its registered client certificate and may also prove possession of a DPoP private key.
When the DPoP proof is valid, the issued access token is bound to the DPoP public key.

This keeps the current machine-client trust model intact and limits Epic 4 to
access-token sender constraint behavior.

### DPoP Rollout Mode

DPoP should be supported alongside the current mTLS-only token behavior.

Recommended configuration:

```json
{
  "AuthorizationServer": {
    "RequireDpop": false,
    "DpopProofLifetimeSeconds": 300,
    "DpopReplayCacheSeconds": 300,
    "DpopSupportedAlgorithms": [ "ES256", "RS256" ]
  }
}
```

When `RequireDpop` is `false`:

- Requests without a `DPoP` header continue to receive mTLS certificate-bound bearer
  tokens using `cnf.x5t#S256`.
- Requests with a valid `DPoP` header receive DPoP-bound tokens using `cnf.jkt`.
- Requests with an invalid `DPoP` header fail with an OAuth-style error response.

When `RequireDpop` is `true`:

- Token requests must include a valid `DPoP` header.
- Missing or invalid DPoP proof returns an OAuth-style error response.

### Token Response

An mTLS-only token response keeps the existing shape:

```json
{
  "access_token": "<jwt>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "orders.read"
}
```

A DPoP-bound token response uses the DPoP token type:

```json
{
  "access_token": "<jwt>",
  "token_type": "DPoP",
  "expires_in": 3600,
  "scope": "orders.read"
}
```

### Access Token Confirmation Claim

DPoP-bound access tokens include a `cnf.jkt` claim containing the base64url-encoded
SHA-256 JWK thumbprint of the public key from the DPoP proof.

Example:

```json
{
  "cnf": {
    "jkt": "<base64url-sha256-jwk-thumbprint>"
  }
}
```

For the first implementation, a token should use one sender constraint form:

- mTLS-only token: `cnf.x5t#S256`
- DPoP-bound token: `cnf.jkt`

The authenticated client certificate is still required for DPoP-bound token issuance,
but the issued access token is bound to the DPoP key.

### Discovery Metadata

Authorization server metadata should continue to advertise mTLS-bound access token
support and should add DPoP capabilities.

Add at least:

```json
{
  "dpop_signing_alg_values_supported": [ "ES256", "RS256" ]
}
```

If `RequireDpop` is enabled, this should be documented through configuration and tests.
There is no standard discovery field that simply means "DPoP is required for this
server" across all clients.

### DPoP Proof Input

The token endpoint accepts the DPoP proof JWT from the `DPoP` request header.

The proof JWT header must include:

- `typ`: `dpop+jwt`
- `alg`: an allowed asymmetric signing algorithm
- `jwk`: the public JWK for the proof key

The proof JWT payload must include:

- `htm`: HTTP method, expected to be `POST`
- `htu`: token endpoint URI without query or fragment
- `jti`: unique proof identifier
- `iat`: proof issued-at timestamp

The token endpoint should not require the `ath` claim. The `ath` claim is used when a
client presents an access token to a protected resource, not when requesting the token.

### Proof Validation

The server should validate all of the following:

- The `DPoP` header is present when required.
- The proof is a compact JWS with a valid signature.
- `typ` is `dpop+jwt`.
- `alg` is one of the configured allowed asymmetric algorithms.
- `alg` is not `none` and is not a symmetric algorithm.
- `jwk` is present and contains only public key material.
- The proof signature validates with the public key in `jwk`.
- `htm` matches the request method.
- `htu` matches the externally visible token endpoint URI, ignoring query and fragment.
- `jti` is present and has not been used recently with the same DPoP key thumbprint.
- `iat` is present and within the configured proof lifetime window.

Malformed, missing, expired, replayed, or otherwise invalid DPoP proofs should return:

```json
{
  "error": "invalid_dpop_proof",
  "error_description": "DPoP proof is missing or invalid."
}
```

Use HTTP 400 for malformed or semantically invalid proofs. If DPoP becomes part of a
stricter client-authentication profile later, the status mapping can be revisited.

### Replay Protection

The first implementation uses an in-memory bounded replay cache keyed by:

- DPoP JWK thumbprint
- DPoP proof `jti`

Entries should expire after the configured replay cache window. The default should
match the proof lifetime window, initially 300 seconds.

The replay cache must be behind an application interface so a later implementation can
replace it with SQLite, Redis, or another distributed store.

Nonce challenge support is out of scope for the first implementation.

### Downstream Validation

The repo does not currently expose protected resource APIs. Epic 4 should therefore add
reusable downstream validation services rather than inventing a full resource API.

Add application-level services that can validate:

- JWT signature and standard claims.
- Access token `typ`/token type expectations where available.
- DPoP proof signature and claims for a protected resource request.
- `ath`, when validating a protected resource request that includes an access token.
- Match between the access token `cnf.jkt` value and the DPoP proof key thumbprint.

Integration tests may use a minimal test-only endpoint if needed to prove the HTTP
validation behavior, but a production resource API is out of scope for Epic 4.

### JOSE/JWT Library

DPoP proof validation is security-sensitive and should not be implemented with ad hoc
JSON parsing and manual cryptographic checks.

Add a JOSE/JWT library for DPoP proof validation and downstream access token validation.
The existing JWT issuer can remain simple for now, but new proof validation should use a
well-tested library.

## Implementation Plan

1. Add DPoP options to `AuthorizationServerOptions`.
2. Add DPoP metadata to `DiscoveryResponse`.
3. Add an API-layer reader for the `DPoP` header.
4. Add application models for validated DPoP proof data.
5. Add `IDpopProofValidator`.
6. Add `IDpopReplayCache` with an in-memory implementation.
7. Update token issuance to accept optional DPoP proof input.
8. Bind DPoP-bound JWTs with `cnf.jkt`.
9. Return `token_type=DPoP` for DPoP-bound tokens.
10. Add reusable downstream access token and DPoP proof validation services.
11. Add demo script coverage for obtaining a DPoP-bound token.

## Testing Requirements

Unit and integration tests should cover:

- Discovery metadata includes supported DPoP signing algorithms.
- Token request without DPoP still returns the existing mTLS-bound bearer token when
  `RequireDpop=false`.
- Token request with valid DPoP returns `token_type=DPoP`.
- DPoP-bound token contains `cnf.jkt`.
- DPoP-bound token does not use `cnf.x5t#S256` as its token binding claim.
- Missing DPoP proof fails when `RequireDpop=true`.
- Malformed DPoP proof returns `invalid_dpop_proof`.
- Unsupported DPoP algorithm returns `invalid_dpop_proof`.
- Missing proof `jwk` returns `invalid_dpop_proof`.
- Proof containing private key material is rejected.
- Invalid proof signature returns `invalid_dpop_proof`.
- Wrong `htm` returns `invalid_dpop_proof`.
- Wrong `htu` returns `invalid_dpop_proof`.
- Missing `jti` returns `invalid_dpop_proof`.
- Replayed `jti` for the same key thumbprint returns `invalid_dpop_proof`.
- Expired proof `iat` returns `invalid_dpop_proof`.
- Future proof `iat` outside clock skew tolerance returns `invalid_dpop_proof`.
- Downstream validation accepts a matching access token and DPoP proof.
- Downstream validation rejects an `ath` mismatch.
- Downstream validation rejects a `cnf.jkt` mismatch.
- The server never logs DPoP private key material, proof JWTs, or access tokens.

## Security Notes

DPoP private keys are client-owned. The server stores no DPoP private key material.

The public JWK from a DPoP proof is used only after signature validation and public-key
sanity checks. Symmetric keys and private JWK parameters must be rejected.

Replay protection is best effort in the first implementation because an in-memory cache
does not protect across multiple server instances or restarts. That is acceptable for
the learning version but should be replaced before a distributed deployment.

The externally visible issuer/token endpoint URL matters for `htu` validation. Reverse
proxy deployments must ensure forwarded host and scheme handling are configured
correctly before enabling strict DPoP validation.
