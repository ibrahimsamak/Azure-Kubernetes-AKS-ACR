# 0005. gRPC for synchronous queries

**Status:** accepted

## Context

Before accepting an order I wanted a quick check that the SKUs are probably in stock, so obviously
bad orders fail right away. That's a question, not an event.

## Decision

Internal sync calls use gRPC with `.proto` contracts, a 500ms deadline, retries only on
`UNAVAILABLE` / `DEADLINE_EXCEEDED`, and a circuit breaker.

gRPC instead of REST because of the generated typed clients, HTTP/2, built-in deadlines and the
`.proto` file as a clear contract.

Rule of thumb: if the caller can't finish without the answer, it's a sync call. If it's just
telling others something happened, it's an event.

## Alternatives

- Make it an event: turns a 5ms check into a round trip and the UI would have to poll for a
  validation error.
- REST: would work fine too.
- Keep a copy of stock levels in Order: avoids the call completely, but the data is stale. Probably
  the better option at scale.

## Consequences

- Bad orders fail fast.
- Order now depends on Inventory at runtime. To limit that, the check is only advisory: if it
  times out or the circuit is open, the order is accepted anyway and the saga decides.
- `.proto` changes have to stay backwards compatible.
