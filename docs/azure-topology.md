# OrderFlow on Azure — topology, identity and the shortcuts we took

What runs where, which identity it runs as, and what a production version of this would have to
change. Everything here is derived from `infra/`, `deploy/` and the service code — if a value
disagrees with a script, the script is right and this file is stale.

All resource names come from `infra/env.sh`, built from `SUFFIX=ibs01`. Source it, don't run it.

---

## 1. The topology

```
  Browser (Angular SPA on Static Web Apps)
      │  HTTPS · Ocp-Apim-Subscription-Key · (Bearer token — Project 4)
      ▼
  API Management — CORS, rate limit, JWT switch, correlation id, header hygiene
      │  HTTP   ← shortcut, see §6
      ▼
  App Routing ingress (managed NGINX, public IP)
      ▼
  ┌─ AKS · namespace orderflow ─────────────────────────────────────────┐
  │  gateway ─▶ order ─gRPC─▶ inventory      payment      notification  │
  │   (YARP)      │                                                     │
  └───────────────┼─────────────────┬─────────────┬─────────────┬───────┘
                  │                 │             │             │
                  └──── Event Hubs (Kafka endpoint, SASL/OAUTHBEARER) ──┤
                                                                        ▼
   ACR — images                                Service Bus topic "notifications"
   Azure SQL — one serverless DB per service     ├─ email ─▶ Function SendEmail
   Key Vault — payment's gateway API key         └─ sms   ─▶ Function SendSms
```

Three properties hold across the whole picture:

- **One published address.** Only APIM is meant to be reached from outside. The services have no
  public endpoint of their own.
- **One database per service.** No shared schema, no cross-service joins, no distributed
  transaction — the saga is what makes them agree.
- **No passwords.** Every arrow into an Azure resource carries an Entra token obtained at runtime.
  The only stored secret in the system is Payment's third-party gateway key, and it lives in
  Key Vault.

## 2. Resource inventory

| Resource | Name | SKU / size | Why this one |
|---|---|---|---|
| Resource group | `rg-orderflow-dev` | — | One group, so teardown is one command |
| Container registry | `acrorderflowibs01` | **Basic** | Only geo-replication and content trust are missing, and we need neither |
| Kubernetes | `aks-orderflow-dev` | **Free** tier control plane, 2 × `Standard_D2as_v5` | Free tier has no SLA — correct for dev, not for production |
| SQL server | `sql-orderflow-ibs01` | Entra-only auth (`--enable-ad-only-auth`) | SQL logins are disabled at the server level: there is no password to leak |
| SQL databases | `orderflow-{orders,inventory,payments,notifications}` | GeneralPurpose **Serverless** Gen5, 0.5–1 vCore, auto-pause 60 min | Cost: they pause when idle. The price is a slow first request after a pause (§5, scenario 8) |
| Event Hubs | `evhns-orderflow-ibs01` | **Standard**, 1 TU, 3 partitions/hub | Standard is the cheapest tier that speaks the **Kafka protocol** — Basic does not |
| Service Bus | `sbns-orderflow-ibs01` | **Standard** | Topics (fan-out to email + sms subscriptions) need Standard; Basic has queues only |
| Key Vault | `kv-orderflow-ibs01` | RBAC authorization, 7-day retention | RBAC instead of access policies, so permissions are role assignments like everything else |
| API Management | `apim-orderflow-ibs01` | **Consumption** | Scales to zero, costs nothing idle. No VNet integration — hence the HTTP hop in §6 |
| Function app | `func-orderflow-ibs01` | Consumption | Channel work (email/SMS) gets its own retries and dead-letter queue |
| Static Web App | `orderflow-web` (`green-pond-068406b1e.6.azurestaticapps.net`) | **Free** | Static hosting with a global CDN; no server-side code in the SPA |

> The SWA lives in its own resource group (`orderflow-web-rg`), not `$RG` — the Portal created it
> that way. `az staticwebapp secrets list -g "$RG"` will not find it; pass the right group.

## 3. Identity — how a pod proves who it is

No credential is stored at any point in this chain. A **trust relationship** is configured once,
and a token is exchanged on every call.

| # | Step | Where |
|---|---|---|
| 1 | One user-assigned managed identity per service, `id-orderflow-<svc>` | `infra/identities.sh:22` |
| 2 | A **federated credential** telling Entra that a token from this AKS cluster's OIDC issuer, claiming subject `system:serviceaccount:orderflow:<svc>`, *is* that identity | `infra/identities.sh:24-29` |
| 3 | Data-plane role assignments on Event Hubs / Service Bus / Key Vault | `infra/identities.sh:33-40` |
| 4 | The ServiceAccount is annotated with the identity's client id | `deploy/helm/orderflow-service/templates/serviceaccount.yaml:9`, value from `helm-deploy.sh:27-28` |
| 5 | The pod is labelled `azure.workload.identity/use: "true"`, so the webhook injects `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and a projected token file | `deploy/helm/orderflow-service/templates/deployment.yaml:25` |
| 6 | The code creates **one** `DefaultAzureCredential` and registers it as a singleton | `src/BuildingBlocks/OrderFlow.ServiceDefaults/AzureExtensions.cs:20` |

Step 6 is the same line of code locally and in the cluster: in a pod it resolves to
`WorkloadIdentityCredential`, on a laptop it falls through to your `az login`. That is the point —
no `#if AZURE`, no separate configuration path to get wrong.

The token is then used in three ways:

| Resource | Mechanism | Where |
|---|---|---|
| Azure SQL | `Authentication=Active Directory Default` in the connection string — no `User Id`, no `Password` | `deploy/helm/values/azure.generated.yaml:12-15` |
| Event Hubs | Kafka SASL/OAUTHBEARER; the "password" is a fresh token for `https://<namespace>/.default`, set on a refresh callback | `src/BuildingBlocks/OrderFlow.Messaging/Kafka/AzureKafkaAuth.cs:29-40` |
| Key Vault, Service Bus | The injected `TokenCredential`, passed to the SDK client | `AzureExtensions.cs:20-37` |

### 3.1 Permission matrix

One identity per service, so a compromised pod reaches only what that service needs.

| Identity | Event Hubs | Service Bus | Key Vault | SQL |
|---|---|---|---|---|
| `id-orderflow-order` | Data Sender + Receiver | — | — | `orderflow-orders`: datareader, datawriter, **ddladmin** |
| `id-orderflow-inventory` | Data Sender + Receiver | — | — | `orderflow-inventory`: same |
| `id-orderflow-payment` | Data Sender + Receiver | — | **Secrets User** | `orderflow-payments`: same |
| `id-orderflow-notification` | Data Sender + Receiver | **Data Sender** | — | `orderflow-notifications`: same |

SQL users are created from the identities themselves — `CREATE USER [id-orderflow-order] FROM
EXTERNAL PROVIDER` (`infra/sql/create-service-users.sql`). There is no login, no password, and
nothing to rotate.

Note what is absent: no management-plane role anywhere. These identities can read and write data;
they cannot create, delete or reconfigure a single Azure resource.

### 3.2 Why Key Vault holds exactly one secret

Key Vault is wired for every service in shared code (`AzureExtensions.cs:23-37`) but enabled for
only one: `helm-deploy.sh:33` sets `KeyVault__Uri` for Payment, and `identities.sh:40` grants
`Key Vault Secrets User` to Payment alone.

That is deliberate. SQL, Event Hubs and Service Bus are Azure resources that *accept Entra tokens*,
so they need no secret at all. A third-party payment gateway does not — it wants an API key. Key
Vault is the escape hatch for the secrets you cannot eliminate, not the default home for
configuration.

Two behaviours of that integration are intentional:

- **The vault is added last**, so `PaymentGateway--ApiKey` overrides `appsettings.json` and
  environment variables rather than being shadowed by them.
- **Startup fails if the vault is unreachable.** A pod that cannot read its secrets should crash
  visibly, not run half-configured and decline every payment.
- `ReloadInterval` is 5 minutes, so a rotated secret is picked up without a restart.

## 4. The edge — API Management

The policy is `deploy/apim/orderflow-api-policy.xml`. Its values are **named values**, so the
policy document itself contains no environment-specific data:

| Named value | Used for | Why it is not inline |
|---|---|---|
| `spa-origin` | the CORS allowed origin | The SWA hostname is random and unknown until the resource exists |
| `require-jwt` | a feature flag around `validate-jwt` | Turns authentication on or off in seconds, with no redeploy |
| `tenant-id` | the OpenID configuration URL | Environment-specific |
| `api-client-id` | the accepted `aud` | The Entra app registration is created after the policy is written |

Order matters in the inbound pipeline:

1. **`cors` first.** A browser's preflight carries no subscription key and no token, so anything
   that rejects unauthenticated requests must come after it, or the preflight fails and the browser
   never sends the real request.
2. **`rate-limit`** — 30 calls / 60 s per subscription.
3. **`validate-jwt`**, wrapped in `<when condition="@("{{require-jwt}}" == "true")">`. Currently
   `false`: the block is skipped on every request. Project 4 Day 5 makes it permanent.
4. **`X-Correlation-Id`** — reuse the caller's or mint one; it flows into service logs and, from
   Project 4 Day 3, into traces.
5. **Strip `Ocp-Apim-Subscription-Key`** before the backend sees it. The backend must not trust a
   client-supplied key; the key is APIM's business.

**APIM is not the security boundary.** The ingress has a public IP and `kubectl port-forward`
reaches any pod, so the edge can be bypassed. Project 4 Day 5 therefore validates the token again
at the gateway *and* at Order — defence at every hop, not one front door.

### 4.1 The SPA and the subscription key

`web/orderflow-web/src/app/order-page.ts` sends `Ocp-Apim-Subscription-Key` on every call. That key
is in the JavaScript bundle and readable by anyone with devtools. **It is not authentication.** It
identifies the application for rate limiting and analytics; it says nothing about *who* is calling,
which is why `customerId` is still hard-coded to `CUST-1`. Project 4 Day 5 replaces that with the
`oid` claim from a real user token.

The SPA polls `/status` with exponential backoff (1s → 5s) and treats a 429 as retryable rather
than fatal, because the rate limit above is shared by the POST and every poll of a single order.

## 5. End-to-end verification

Run with four windows open: the SPA, `kubectl get pods,hpa -n orderflow -w`,
`kubectl logs -f -l app.kubernetes.io/name=order -n orderflow`, and the Function App log stream.

| # | Scenario | Expected | Result |
|---|---|---|---|
| 1 | Happy path — SKU-1, qty 1, 29.99 | `Pending → AwaitingStock → AwaitingPayment → Paid → Confirmed`; EMAIL + SMS in Function logs | _not yet recorded_ |
| 2 | Compensation — price **13.13** | `… → AwaitingPayment → Compensating → Cancelled` with a reason; Inventory releases the reservation | _not yet recorded_ |
| 3 | Idempotent POST — replay the same `Idempotency-Key` | Same `orderId`, one order row | _not yet recorded_ |
| 4 | Pod loss mid-saga — delete the payment pod | Order still reaches `Confirmed` (Kafka redelivery + Inbox) | _not yet recorded_ |
| 5 | Scale-out under load | HPA raises `order` replicas; cluster autoscaler adds a node if pods go Pending | _not yet recorded_ |
| 6 | Rolling deploy under traffic | Zero failed requests (`maxUnavailable: 0` + readiness) | _not yet recorded_ |
| 7 | Dependency blip — remove the SQL firewall rule | Pods go **not Ready** but are **not restarted**; APIM returns 502/503; recovery is automatic | _not yet recorded_ |
| 8 | Serverless wake-up — idle > 1 h | First request slow but succeeds; no pod restarts | _not yet recorded_ |

Scenario 7 is the one that proves the health split: liveness checks nothing but the process, so a
slow database takes replicas out of rotation instead of restarting them into a crash loop.

## 6. Shortcuts taken, and their production fix

Every one of these is a deliberate dev-environment trade, not an oversight.

| # | Shortcut | Why it is acceptable here | Production fix |
|---|---|---|---|
| 1 | **Public SQL endpoint** with `AllowAzureServices` + a dev-client firewall rule | No VNet to peer into; the server is Entra-only, so there is no password to brute-force | Private Endpoint + deny public network access; AKS reaches SQL over the VNet |
| 2 | **HTTP between APIM and the ingress** | APIM Consumption has no VNet integration | Premium/Developer APIM in a VNet with internal ingress, or mTLS to the ingress |
| 3 | **Subscription key in the SPA bundle** | Public by definition; used for rate limiting, not authorization | Entra sign-in with MSAL; the token becomes the caller's identity (Project 4 Day 5) |
| 4 | **`db_ddladmin` for runtime identities** | EF migrations run at service startup | Migrations run as a separate pipeline step with a deploy-time identity; runtime identities keep datareader + datawriter only |
| 5 | **AKS Free tier, 2 nodes, no zones** | Dev cluster, no SLA needed | Standard/Premium tier, availability zones, separate system and user node pools |
| 6 | **Serverless SQL with 60-minute auto-pause** | Cost control while learning | Provisioned compute, or a shorter pause delay, so no user pays the wake-up latency |
| 7 | **`require-jwt = false`** | The SPA cannot acquire a token yet | Flipped to `true` permanently once MSAL ships |
| 8 | **Order → Inventory gRPC is unauthenticated** | Cluster-internal; the call is advisory and read-only | App-only token from Order's managed identity, checked by Inventory for an `Inventory.Read` role |
| 9 | **SWA deployed from the CLI with a deployment token** | One-off manual deploy | GitHub Actions with OIDC federation — no token stored anywhere |

Shortcut 4 is the one most worth fixing first: it is the only place where a running service holds
more authority than its work requires.

## 7. Cost and teardown

The expensive items are AKS nodes and the Event Hubs namespace — both bill while idle. The SQL
databases auto-pause; APIM Consumption, the Function app and the Static Web App cost nothing at
rest.

```bash
source infra/env.sh
az aks stop -g "$RG" -n "$AKS"        # stop paying for nodes, keep the cluster
az aks start -g "$RG" -n "$AKS"       # back in a few minutes
az group delete -n "$RG" --yes        # everything, permanently
```

`az group delete` does **not** remove the Entra app registration (`orderflow-api`) or the Static Web
App in `orderflow-web-rg` — neither lives in the resource group. Delete those separately.
