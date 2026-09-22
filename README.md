# Distributed-System-Architecture - Order

Event-driven order fulfilment across four independently deployable .NET services behind a reverse
proxy, coordinated by an orchestrated saga over Kafka, shipped as container images and run on
Azure Kubernetes Service behind API Management — without a single password or connection-string
secret anywhere in the system.

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
             │               │                            │ Service Bus topic
  orderflow.inventory.v1 ────┤                            ▼
             └───────────────┴──────────▶ Order      Azure Function: email / sms
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

Readiness checks the service's own database and nothing else. Kafka and Service Bus are
deliberately excluded — if the broker is down, orders are still accepted and wait in the outbox,
so reporting unready would turn a broker blip into a checkout outage.

## On Azure

```
  Browser / curl
      │  HTTPS · Ocp-Apim-Subscription-Key · (Bearer token)
      ▼
  API Management ── CORS, rate limit, JWT validation, header hygiene
      │  HTTP
      ▼
  App Routing ingress (managed NGINX, public IP)
      ▼
  ┌─ AKS · namespace orderflow ─────────────────────────────────────────┐
  │  gateway ─▶ order ─gRPC─▶ inventory      payment      notification  │
  └────────────────┬───────────────┬─────────────┬─────────────┬────────┘
                   │               │             │             │
                   └──── Event Hubs (Kafka endpoint) ──────────┤
                                                               ▼
   ACR — images                              Service Bus topic "notifications"
   Azure SQL — one serverless DB per service   ├─ email ─▶ Function SendEmail
   Key Vault — payment's API key               └─ sms   ─▶ Function SendSms
```

| Local (Aspire / compose) | Azure |
|---|---|
| Kafka container | **Event Hubs** Standard, spoken to over its Kafka endpoint |
| SQL Server container | **Azure SQL**, Entra-only server, four serverless databases |
| RabbitMQ fanout | **Service Bus** topic with one subscription per channel, drained by an **Azure Function** |
| User secrets | **Key Vault** |
| The gateway's port | **APIM** → App Routing ingress → gateway |
| `docker compose` | **AKS**, deployed with one Helm chart |

The code barely changed to get there. The services still speak Kafka; only the bootstrap address
and the authentication mode differ, and both come from configuration.

### Zero secrets

Every service has its own user-assigned managed identity, federated to its Kubernetes
ServiceAccount through **Workload Identity**. A pod proves who it is with a token the cluster
issues, and Entra ID trades it for an access token — there is nothing to leak, rotate or check in.

| Resource | How the pod authenticates | Role |
|---|---|---|
| Azure SQL | `Authentication=Active Directory Default` | a contained database user per identity |
| Event Hubs | SASL `OAUTHBEARER` with an Entra token | Data Sender + Data Receiver |
| Service Bus | `ServiceBusClient` + `DefaultAzureCredential` | Data Sender (notification only) |
| Key Vault | configuration provider + `DefaultAzureCredential` | Secrets User (payment only) |
| ACR | the kubelet identity | AcrPull |

Local authentication is disabled on Service Bus and Event Hubs, and the SQL server accepts Entra
logins only, so a key or password would not work even if one existed. The generated Helm values
file contains hostnames and nothing else — it could be published without exposing anything.

Least privilege shows up in configuration too: only Payment is given the Key Vault URI, so the
others cannot even try to read a secret.

### The edge

APIM answers the CORS preflight first, because a browser's `OPTIONS` request carries neither a
subscription key nor a token. After that: a rate limit per subscription (`429`), optional JWT
validation switched by a named value, a correlation id, and the subscription key stripped before
the request reaches the cluster. Environment-specific values — tenant, audience, allowed origin —
are APIM named values, so the policy in
[`deploy/apim/orderflow-api-policy.xml`](deploy/apim/orderflow-api-policy.xml) is the same in every
environment.

Each service still validates the token itself. The ingress IP is public and `kubectl port-forward`
reaches any pod, so the edge is a first line of defence, not the only one.

### Notification fan-out

Kafka carries the **fact** (`OrderConfirmed`); Service Bus carries the **task** (send an email,
send an SMS). Each channel is its own subscription with its own retries and dead-letter queue, so a
broken SMS provider never delays an email.

The message id is deterministic — `{orderId}:{kind}` — and the topic has duplicate detection on, so
a Kafka redelivery that makes Notification send twice produces one message, not two. A message
whose customer id is `POISON` fails on purpose: it is retried ten times and then parked in the
dead-letter queue, which is how that path is demonstrated.

### Kubernetes

One chart, [`deploy/helm/orderflow-service`](deploy/helm/orderflow-service), and one small values
file per service. The five services differ only in image, ports, environment variables and whether
they get an ingress; five copies of the same templates would drift within a month.

- **Probes** — a startup probe gives slow first boots (serverless SQL waking up) room without
  weakening liveness; readiness is the database check described above.
- **Resources** — CPU and memory requests, a memory limit, and deliberately **no CPU limit**:
  CFS throttling causes latency spikes even on an idle node.
- **HPA + PDB** — scale on CPU, and never let a voluntary disruption take the last replica.
- **Zero-downtime rollouts** — `maxUnavailable: 0` plus readiness, so a deploy under load drops no
  requests.

Only the gateway has an ingress. Everything else is a `ClusterIP` Service.

### Where Event Hubs differs from Kafka

"Kafka-compatible" is not "Kafka":

- **Compression.** Event Hubs Standard rejects compressed batches with
  `Message format on broker does not support request`. Locally the producer uses Snappy; on Azure
  `Kafka__CompressionType` is `None`. The payloads are small JSON, so little is lost.
- **Topic creation.** Topics are provisioned as event hubs up front, so `Kafka__ProvisionTopics` is
  `false` in Azure.

## Stack

.NET 10 · ASP.NET Core · EF Core (SQL Server) · Kafka · gRPC · YARP · .NET Aspire · Docker ·
OpenTelemetry · xUnit · Testcontainers · PactNet

**Azure:** AKS · Helm · ACR · API Management · Event Hubs · Service Bus · Azure Functions
(isolated worker) · Azure SQL (serverless) · Key Vault · Entra ID Workload Identity

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

### On Azure

Requires the Azure CLI, `kubectl` and Helm — and Git Bash on Windows. Every name lives in
[`infra/env.sh`](infra/env.sh); source it in each new shell, because an empty variable is behind
most "can't find app" errors.

```bash
source infra/env.sh

# Once: the platform, the identities and the database users
bash infra/provision.sh          # RG, ACR, AKS, SQL, Event Hubs, Service Bus, Key Vault
bash infra/identities.sh         # managed identities, federated credentials, role assignments
#   + run infra/sql/create-service-users.sql in each database (as the Entra admin)
#   + az keyvault secret set --vault-name "$KV" -n PaymentGateway--ApiKey --value <key>
az aks get-credentials -g "$RG" -n "$AKS" --overwrite-existing

# Every release
export TAG=$(date +%Y%m%d-%H%M)              # a new tag, so the pods really roll
deploy/scripts/build-images.sh acr           # builds inside ACR, no local Docker needed
deploy/scripts/write-azure-values.sh         # -> deploy/helm/values/azure.generated.yaml
bash deploy/scripts/helm-deploy.sh azure     # inventory -> payment -> notification -> order -> gateway
kubectl get pods,ingress -n orderflow
```

`helm-deploy.sh` waits for each release to be ready before starting the next. If one times out,
Helm marks it `failed` and the script stops; fix the pod and run it again — every release is an
idempotent `upgrade --install`.

The ingress address is what APIM's backend points at:

```bash
kubectl get ingress gateway -n orderflow -o jsonpath='{.status.loadBalancer.ingress[0].ip}'
```

The notification Function is published separately:

```bash
cd src/Functions/OrderFlow.Notifications.Functions && func azure functionapp publish "$FUNC"
```

The edge end to end — 401 without a key, an order reaching `Confirmed`, 429 under load, Service Bus
duplicate detection and dead-lettering — is checked by one script:

```bash
KEY=<APIM subscription key> bash deploy/scripts/test-day5.sh
```

Locally, Notification publishes to Service Bus too. Point it at the Azure namespace with
`ServiceBus__FullyQualifiedNamespace`; after `az login`, `DefaultAzureCredential` uses your identity.

## API

Through the gateway on `:8080` under compose; under Aspire, the gateway's port is on the dashboard;
on Azure, through `https://<apim-name>.azure-api.net/orderflow` with an `Ocp-Apim-Subscription-Key`
header.

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
    OrderFlow.Messaging.RabbitMq/    fanout publisher and consumer (no longer used by Notification)
    OrderFlow.ServiceDefaults/       OpenTelemetry, health checks, service discovery
  Gateway/OrderFlow.Gateway/         YARP reverse proxy, routes in configuration
  Services/
    Order/          Api -> Infrastructure -> Application -> Domain
    Inventory/      stock levels and reservations
    Payment/        pending charges, captures, refunds
    Notification/   what the customer was told; hands channel work to Service Bus
  Functions/
    OrderFlow.Notifications.Functions/  Service Bus triggers: SendEmail, SendSms
infra/
  env.sh                             every Azure resource name, in one place
  provision.sh                       the platform
  identities.sh                      one managed identity per service, and its roles
  sql/                               contained database users for the identities
deploy/
  helm/orderflow-service/            the one chart
  helm/values/                       common, local, per-service, azure.generated
  scripts/                           build-images, write-azure-values, helm-deploy, test-day5
  apim/                              the APIM policy
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


