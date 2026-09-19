# Distributed-System-Architecture - Order

Event-driven order fulfilment across four independently deployable .NET services behind a reverse
proxy, coordinated by an orchestrated saga over Kafka, and shipped as container images.

Placing an order touches stock, money and the customer's inbox — three things that live in three
different databases. OrderFlow does that without a distributed transaction: each service commits
locally, publishes a fact, and the saga remembers where it got to and what it owes.

```
                      ┌──────────────────────────────────────────────┐
   POST /api/v1/orders│                                              │
   ───────────────────▶  Gateway ── the only published address       │
        202 Accepted  │      │  YARP, routes on path                 │
                      │      ▼                                       │
                      │  Order  ── owns the order AND the saga       │
                      └──────┼───────────────────────────────────────┘
                             │  orderflow.orders.v1
             ┌───────────────┼────────────────────────────┐
             ▼               ▼                            ▼
       ┌───────────┐   ┌───────────┐               ┌──────────────┐
       │ Inventory │   │  Payment  │               │ Notification │
       └─────┬─────┘   └─────┬─────┘               └──────┬───────┘
             │               │                            │ RabbitMQ fanout
  orderflow.inventory.v1 ────┤                            ▼
             └───────────────┴──────────▶ Order      email / sms / push workers
                  orderflow.payments.v1
```

Order also calls Inventory once **synchronously**, over gRPC, to ask whether the stock is likely
to be there. That call is advisory and read-only — reservations only ever happen through events.

## The saga

```
Pending ──▶ AwaitingStock ──▶ AwaitingPayment ──▶ Paid ──▶ Confirmed ■

   any failure ──▶ Compensating ──(every compensation ACKed)──▶ Cancelled ■
```

There is no rollback. A failure after stock was reserved runs the *compensation* for that step —
release the stock, refund the charge — and the saga only goes terminal once each compensation has
been acknowledged. A success that lands after the saga gave up (a payment captured past its
deadline) still records what it owes, because silence is never treated as proof of failure.

## Reliability

| Problem | Mechanism |
|---|---|
| Save and publish are not atomic | Transactional **outbox** in the service's own database; a background dispatcher publishes it |
| Kafka delivers at least once | **Inbox** keyed on `(MessageId, Consumer)` — the unique index is the guarantee, the pre-read is only an optimisation |
| A step never answers | A per-step `DeadlineUtc` and a scanner that times sagas out |
| Two messages hit one saga at once | `rowversion` optimistic concurrency |
| Two orders want the last unit | `rowversion` on the stock item |
| Charging twice | Inbox + a unique index on `Payment.OrderId` + the gateway idempotency key |
| A double-clicked submit button | `Idempotency-Key`, deduped by primary key inside the order's own transaction |
| A dependency is down | Graceful degradation: an unknown answer from the advisory gRPC call means "proceed" |
| A poison message | Bounded retries, then a `<topic>.dlq` parallel topic — never back onto the source |

Handlers never call `SaveChanges`. The dispatcher owns the transaction, so the business change,
the inbox claim and any new outbox rows commit together or not at all.

## The gateway

Callers know one address, not five. The gateway is the only thing published to the outside; the
services are unreachable except through it, which is also where authentication and rate limiting
belong once they exist.

Routing is entirely configuration — no C# decides where a request goes — so splitting a service
later changes one route here instead of every caller:

```json
"Routes": {
  "orders": { "ClusterId": "order", "Match": { "Path": "/api/v1/orders/{**catch-all}" } }
}
```

Destinations are resolved through service discovery, so `http://order` is a logical name that
Aspire resolves locally and that is ordinary DNS anywhere it runs as a deployed service.

## Containers

Each service builds to its own image from the repository root as build context. The build is
multi-stage and the stages are split on purpose:

- The restore layer copies **only** the `.csproj` files the service transitively needs, then
  restores. Package resolution depends on nothing else, so editing C# never re-downloads NuGet —
  the expensive layer sits above the one that changes every few minutes.
- The runtime stage starts from `aspnet:10.0-noble-chiseled` and copies in just the publish
  output. No SDK, no source, no shell, no package manager: nothing to pivot to if the process is
  compromised. Roughly 110 MB against the 800 MB build image, running as a non-root user.

Nothing environment-specific is baked in. Connection strings, the gRPC address Order dials and
Inventory's listener configuration all arrive as environment variables, so the same image runs
locally and anywhere else unchanged.

Inventory listens on two ports because gRPC needs HTTP/2 and there is no TLS inside the network
to negotiate it with: 8080 is HTTP/1.1 for REST and probes, 8081 is HTTP/2 for gRPC.

## Health

Two endpoints, mapped in every environment, answering two different questions:

| | Asks | Consequence of a "no" |
|---|---|---|
| `/health/live` | Can the process execute code? | Restart it |
| `/health/ready` | Should it receive traffic right now? | Take it out of rotation |

Liveness checks nothing but itself. A dependency in there is a self-inflicted outage: one slow
database and every replica restarts at once, comes back to the same slow database, and restarts
again.

Readiness checks the service's own database and nothing else. Kafka, Redis and RabbitMQ are
deliberately excluded — if the broker is down, orders are still accepted and wait in the outbox,
so reporting unready would turn a broker blip into a checkout outage.

## Stack

.NET 10 · ASP.NET Core · EF Core (SQL Server) · Kafka · RabbitMQ · gRPC · YARP · .NET Aspire ·
Docker · OpenTelemetry · xUnit · Testcontainers · PactNet

## Running it

Requires the .NET 10 SDK and a container runtime.

```bash
dotnet run --project src/Aspire/OrderFlow.AppHost
```

Aspire starts Kafka, RabbitMQ and SQL Server, provisions a database per service, and launches the
services and the gateway with service discovery wired up; its dashboard collects the traces, logs
and metrics for the whole system in one place. Each service migrates its own schema on startup,
and Inventory seeds a small catalogue — `SKU-1`, `SKU-2`, `SKU-3`, plus `SKU-OOS` with nothing on
hand so the reservation-failure path is reachable without editing the database by hand.

Migrating on startup is opt-out, so a dedicated migration step can take it over:

```bash
Database__MigrateOnStartup=false
```

### Everything in containers

```bash
docker compose --profile apps up --build
```

Builds all five images and runs them against the same infrastructure. Only the gateway publishes a
port, so the services are reachable exactly as they would be in a cluster — through the gateway or
not at all.

To bring up only the infrastructure and debug the services from an IDE:

```bash
docker compose up -d
```

To build a single image by hand:

```bash
docker build -f src/Services/Inventory/OrderFlow.Inventory.Api/Dockerfile \
             -t orderflow/inventory-api:local .
```

## API

Through the gateway on `:8080` under compose; under Aspire, the gateway's port is on the dashboard.

```bash
# Place an order. 202 with a poll URL: the work finishes asynchronously, so 201 would be a lie.
curl -X POST http://localhost:8080/api/v1/orders \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: 8f14e45f-ea8d-4b9a-9c1e-2b7d6f0a1c33' \
  -d '{
        "customerId": "cust-1",
        "currency": "USD",
        "lines": [{ "sku": "SKU-1", "quantity": 2, "unitPrice": 19.99 }]
      }'

# Follow the saga
curl http://localhost:8080/api/v1/orders/{id}/status
# -> { "orderId": "...", "state": "AwaitingPayment", "failureReason": null, "startedAtUtc": "..." }
```

Repeating a POST with the same `Idempotency-Key` returns the original order rather than creating a
second one.

The payment gateway stand-in is deterministic by amount, so the failure paths run end to end: a
total of **13.13** is declined permanently (order cancelled, stock released), **66.66** times out
retryably, and anything else succeeds.

## Layout

```
src/
  Aspire/OrderFlow.AppHost/          orchestration: brokers, databases, services
  BuildingBlocks/
    OrderFlow.Contracts/             the public event contracts and the topic map
    OrderFlow.Grpc.Contracts/        inventory_query.proto -> server base + client
    OrderFlow.Messaging/             outbox, inbox, Kafka, dispatcher
    OrderFlow.Messaging.RabbitMq/    fanout publisher and consumer
    OrderFlow.ServiceDefaults/       OpenTelemetry, health checks, service discovery
  Gateway/OrderFlow.Gateway/         YARP reverse proxy, routes in configuration
  Services/
    Order/          Api -> Infrastructure -> Application -> Domain
    Inventory/      stock levels and reservations
    Payment/        pending charges, captures, refunds
    Notification/   what the customer was told
docker-compose.yml                   infrastructure, plus every service under --profile apps
.dockerignore                        keeps host build output out of the build context
tests/
  OrderFlow.Order.UnitTests/         the saga state machine, no infrastructure
  OrderFlow.Messaging.UnitTests/     inbox dedup, dead-letter publisher
  OrderFlow.IntegrationTests/        real SQL Server and real Kafka via Testcontainers
  OrderFlow.ContractTests/           Pact consumer and provider verification
```

Only Order is split into layers, because only Order has real business rules; dependencies point
inward and the domain knows about nobody. The other three receive an event, change a row, and emit
an event.

There is one topic per producer, because ordering is only guaranteed within a partition of a
topic, and one order's events have to stay ordered relative to each other. Contracts carry
primitives only: a public contract has to be free to evolve separately from the internal model,
and whatever deserializes it may not be written in C#.

## Tests

```bash
dotnet build
dotnet test
```

Four suites, split by what each one can actually prove:

- **Unit** — the saga state machine and inbox duplicate detection. Pure logic, and the
  compensation races are where the bugs live.
- **Integration** — real SQL Server through Testcontainers. An in-memory provider would pass while
  production stayed broken: the outbox query is `UPDLOCK, READPAST` and duplicate detection reads
  SQL Server error codes 2627 and 2601. The fixture migrates rather than calling `EnsureCreated`,
  so a bad migration fails in a test instead of in a deployment.
- **Broker** — one full round trip across a real Kafka broker (outbox row → dispatcher → topic →
  consumer → handler → inbox claim), plus a dead-letter test that parks a poison message and
  asserts the good message behind it on the same partition still gets handled.
- **Contract** — Pact message pacts with the real code on both sides: the consumer test invokes
  Inventory's actual handler, the provider test builds the event through Order's real aggregate
  and mapper, and both match on type and shape rather than on values.


