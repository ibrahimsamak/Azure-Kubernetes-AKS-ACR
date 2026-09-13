# 0008. gRPC for internal synchronous queries

## Status
Accepted — Week 2, Day 6

## Context
Not every interaction is a fact worth broadcasting. Before accepting an order we want a
fast "is this SKU plausibly in stock?" answer to fail obviously-bad requests immediately
rather than 400ms later via an event round-trip. That is a **question**, not a fact.

## Decision
Internal synchronous calls use gRPC over HTTP/2 with `.proto` contracts, a hard client
deadline (500ms), retries only on `UNAVAILABLE`/`DEADLINE_EXCEEDED`, and a circuit breaker.

Why gRPC over REST internally: binary Protobuf (smaller/faster), generated strongly-typed
clients on both ends, HTTP/2 multiplexing, first-class deadline propagation, and streaming
when needed. `.proto` files are an explicit, versionable, reviewable contract.

**The rule we apply to every new edge:** if the caller cannot complete its own work without
the answer, it is sync (gRPC). If the caller is merely informing others, it is async (Kafka).

## Alternatives considered
- **Make it an event too.** Rejected: it would turn a 5ms check into an asynchronous
  round-trip and force the UI to poll for a validation error.
- **REST + HttpClient.** Perfectly fine; chosen against for typed contracts and deadlines.
- **Read-model replica of stock inside Order.** Considered — avoids the sync edge entirely
  and is the right answer at high scale, but the data is stale and the learning goal
  includes gRPC. Documented as the alternative in the README.

## Consequences
+ Fast failure for impossible orders; typed contracts; deadline discipline.
- A real runtime coupling: Inventory being down degrades order placement. Mitigated by
  treating the check as ADVISORY — on timeout/circuit-open we accept the order anyway and
  let the saga be the source of truth. The gRPC answer is never a reservation.
- `.proto` changes must stay backwards-compatible (never renumber fields).
