# 0006. Entra ID authorization

**Status:** accepted

## Context

APIM could validate a JWT but it was switched off, the services trusted whatever reached them, and
the customer id came from the request body. So anyone could place or read orders as someone else.
The gRPC call to Inventory had no auth at all.

## Decision

- The SPA signs in with MSAL (auth code + PKCE) and asks for the `Orders.ReadWrite` scope.
- Permissions use app roles: `OrderFlow.Customer` and `OrderFlow.Support`. Users must be assigned
  a role to get a token.
- The customer id is the token's `oid`, not a field in the body. Reading someone else's order
  returns 404, not 403, so it doesn't reveal that the order exists.
- The token is checked at APIM, at the gateway and again in Order. Each of these can be bypassed
  somehow (public ingress IP, port-forward), so none of them trusts the one before.
- Order calls Inventory with a token from its own managed identity (`Inventory.Read` role). It
  doesn't forward the user's token.
- Events carry `CustomerId` as data. Only the owning service's identity can send to its topic.
- Locally, `Auth:Mode=Local` signs everything in as a dev user. It throws on startup in
  Production.

## Alternatives

- Only check at APIM: anything that reaches the cluster directly skips it.
- On-behalf-of to Inventory: not needed since the stock check isn't user specific, and it would
  need a client secret in Order.
- Scopes only: scopes can't tell a customer from a support agent. That's what roles are for.

## Consequences

- Three token checks per request, but each one is cheap since the keys are cached.
- New users need a role before they can use the API.
- The gRPC hop inside the cluster is plain h2c, so it needs
  `UnsafeUseInsecureChannelCallCredentials`. A service mesh with mTLS would fix that.
