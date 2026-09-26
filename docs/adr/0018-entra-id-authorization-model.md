# 0018. Entra ID authorization: delegated scope + app roles, identity from the token, validate at every hop

## Status
Accepted — Week 4, Day 3.

## Context
Until now APIM could validate a JWT but did not have to, the services trusted anything that
reached them, and the customer id was a field in the request body — any caller could place or
read orders as anyone else (an IDOR). Service-to-service gRPC had no authentication at all.

## Decision
- **Users**: the SPA signs in with MSAL (auth code + PKCE) and requests the delegated scope
  `api://orderflow-api/Orders.ReadWrite`. Authorization in the API uses **app roles**
  (`OrderFlow.Customer`, `OrderFlow.Support`) assigned to users/groups in Entra; the app requires
  assignment.
- **Identity comes from the token**: the customer id is the token's `oid`, never the body.
  Reading another customer's order returns **404**, not 403 (a 403 would confirm the id exists).
- **Validate at every hop**: APIM (`validate-jwt` + required `scp`), the gateway (authenticated
  user), and Order (scope + role + ownership). Each hop can be bypassed by something (a
  misconfigured ingress, a port-forward, a compromised pod); none trusts the one before it.
- **Service to service**: Order calls Inventory's gRPC with a token from **its own managed
  identity**, audience `orderflow-inventory`, carrying the app role `Inventory.Read`. The user's
  token is not forwarded (wrong audience, and Inventory must not act on user rights).
- **Async**: events carry `CustomerId` as data. Trust on Kafka is Event Hubs RBAC: only the
  owning service's identity can publish to its topic.
- **Local development**: `Auth:Mode=Local` signs requests in as a fixed developer principal. The
  switch throws at startup if the environment is `Production`.

## Alternatives considered
- **Authorization only at APIM** — rejected: anything that reaches the ingress, gateway or a pod
  directly skips it.
- **Forward the user token to Inventory (on-behalf-of)** — unnecessary: Inventory's read is not
  user-specific; OBO adds a secret-bearing confidential client to Order.
- **Scopes for everything** — scopes describe what a *client app* may do for a user; they cannot
  distinguish a customer from a support agent. That's what roles are for.

## Consequences
- Three JWT validations per request cost microseconds each (signature check with cached keys).
- New users must be assigned a role before they can call the API (by design).
- The in-cluster gRPC hop is plaintext h2c, so call credentials need `UnsafeUseInsecureChannelCallCredentials`;
  the production fix is mesh mTLS (Istio add-on), noted in the threat model.
