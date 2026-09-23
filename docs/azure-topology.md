# Azure setup

What runs where, which identity it uses, and the shortcuts I took for a dev environment. Resource
names come from `infra/env.sh` (`SUFFIX=ibs01`). Source it, don't run it. If this file and the
scripts disagree, trust the scripts.

## Overview

```
Browser (Angular on Static Web Apps)
   |  HTTPS + Ocp-Apim-Subscription-Key
   v
API Management (CORS, rate limit, correlation id)
   |  HTTP (see shortcuts)
   v
App Routing ingress (managed NGINX)
   v
AKS, namespace orderflow:
   gateway -> order -> (gRPC) inventory, payment, notification
   |
Event Hubs (Kafka endpoint, OAuth)      Service Bus topic "notifications"
Azure SQL (one DB per service)            -> email -> Function SendEmail
Key Vault (payment API key)               -> sms   -> Function SendSms
ACR (images)
```

- Only APIM is meant to be reachable from outside.
- One database per service, no shared tables.
- No passwords. Everything uses Entra tokens. The only stored secret is the payment provider's API
  key, which is in Key Vault.

## Resources

| Resource | Name | SKU | Notes |
|---|---|---|---|
| Resource group | `rg-orderflow-dev` | | one group so cleanup is one command |
| ACR | `acrorderflowibs01` | Basic | |
| AKS | `aks-orderflow-dev` | Free tier, 2 x `Standard_D2as_v5` | no SLA, fine for dev |
| SQL server | `sql-orderflow-ibs01` | Entra-only auth | SQL logins disabled |
| SQL databases | `orderflow-{orders,inventory,payments,notifications}` | Serverless, auto-pause 60 min | cheap, but the first request after a pause is slow |
| Event Hubs | `evhns-orderflow-ibs01` | Standard, 1 TU | Basic doesn't support the Kafka protocol |
| Service Bus | `sbns-orderflow-ibs01` | Standard | topics need Standard |
| Key Vault | `kv-orderflow-ibs01` | RBAC mode | |
| APIM | `apim-orderflow-ibs01` | Consumption | free when idle, no VNet |
| Function app | `func-orderflow-ibs01` | Consumption | |
| Static Web App | `orderflow-web` | Free | it's in `orderflow-web-rg`, not `$RG` (the Portal put it there) |

## Identity

Nothing is stored. Each pod gets a token at runtime:

1. Each service has a user-assigned managed identity, `id-orderflow-<svc>` (`infra/identities.sh`).
2. A federated credential links it to the Kubernetes service account
   `system:serviceaccount:orderflow:<svc>`.
3. Role assignments on Event Hubs, Service Bus and Key Vault.
4. The Helm chart annotates the service account with the identity's client id and labels the pod
   with `azure.workload.identity/use: "true"`.
5. The code registers one `DefaultAzureCredential` (`AzureExtensions.cs`). In a pod it uses the
   workload identity, on my laptop it uses `az login`. Same code either way.

How each resource uses the token:

| Resource | How |
|---|---|
| Azure SQL | `Authentication=Active Directory Default` in the connection string |
| Event Hubs | Kafka SASL/OAUTHBEARER with a token refresh callback (`AzureKafkaAuth.cs`) |
| Key Vault, Service Bus | the shared `TokenCredential` passed to the SDK clients |

### Permissions

| Identity | Event Hubs | Service Bus | Key Vault | SQL |
|---|---|---|---|---|
| order | Sender + Receiver | | | reader, writer, ddladmin |
| inventory | Sender + Receiver | | | same |
| payment | Sender + Receiver | | Secrets User | same |
| notification | Sender + Receiver | Sender | | same |

SQL users are created with `CREATE USER [id-orderflow-order] FROM EXTERNAL PROVIDER`
(`infra/sql/create-service-users.sql`). None of the identities have management-plane roles.

### Key Vault

Only Payment gets `KeyVault__Uri` and the Secrets User role. SQL, Event Hubs and Service Bus all
accept Entra tokens so they don't need secrets. The payment provider wants an API key, so that one
goes in Key Vault.

- Key Vault is added last as a config source, so it overrides appsettings and env vars.
- If the vault can't be reached the service fails to start, which is better than running and
  declining every payment.
- Secrets reload every 5 minutes, so rotation doesn't need a restart.

## APIM

The policy is in `deploy/apim/orderflow-api-policy.xml`. Environment-specific values are named
values (`spa-origin`, `require-jwt`, `tenant-id`, `api-client-id`), so the policy file is the same
everywhere.

Order of the inbound policies:

1. `cors` first, because the browser's preflight has no key or token.
2. `rate-limit`: 30 calls per 60s per subscription.
3. `validate-jwt`, behind the `require-jwt` flag. It's `false` for now because the SPA can't sign
   in yet.
4. `X-Correlation-Id`: reuse the caller's or create one.
5. Remove the subscription key header before it reaches the backend.

APIM isn't the real security boundary. The ingress has a public IP and port-forward reaches any
pod, so the services will have to validate tokens themselves too.

### The SPA and the subscription key

The SPA sends the subscription key on every call, so anyone can read it in devtools. It identifies
the app for rate limiting, not the user. For now `customerId` is hard-coded to `CUST-1` until real
sign-in is added.

The SPA polls `/status` with backoff (1s up to 5s) and retries on 429, since the POST and the
polling share the same rate limit.

## Test scenarios

| # | Scenario | Expected |
|---|---|---|
| 1 | SKU-1, qty 1, 29.99 | reaches Confirmed, email + sms in the Function logs |
| 2 | price 13.13 | Compensating -> Cancelled, stock released |
| 3 | same Idempotency-Key twice | same orderId, one order |
| 4 | delete the payment pod mid-saga | still reaches Confirmed |
| 5 | load | HPA scales order, autoscaler adds a node if needed |
| 6 | rolling deploy under load | no failed requests |
| 7 | remove the SQL firewall rule | pods go not ready but don't restart, recover on their own |
| 8 | first request after SQL auto-pause | slow but works |

## Shortcuts

| Shortcut | Why it's ok for now | Production fix |
|---|---|---|
| public SQL endpoint | no VNet, and Entra-only so no password to guess | private endpoint |
| HTTP from APIM to ingress | APIM Consumption has no VNet | APIM in a VNet + internal ingress, or mTLS |
| subscription key in the SPA | it's not auth anyway | MSAL sign-in |
| `db_ddladmin` for the services | migrations run on startup | run migrations in the pipeline, services get reader/writer only |
| AKS Free, 2 nodes, no zones | dev | Standard tier, zones, separate node pools |
| serverless SQL with auto-pause | cost | provisioned or a shorter pause |
| `require-jwt = false` | no sign-in yet | turn it on once MSAL is in |
| gRPC to Inventory has no auth | internal and read-only | managed identity token + `Inventory.Read` role |
| SWA deployed from CLI with a token | one-off | GitHub Actions with OIDC |

The `db_ddladmin` one is the first I'd fix since it gives the running services more rights than
they need.

## Cost / cleanup

AKS nodes and Event Hubs cost money while idle. SQL pauses, and APIM, Functions and SWA are free
when idle.

```bash
source infra/env.sh
az aks stop -g "$RG" -n "$AKS"
az aks start -g "$RG" -n "$AKS"
az group delete -n "$RG" --yes
```

Deleting the resource group doesn't remove the Entra app registration or the Static Web App in
`orderflow-web-rg`. Delete those separately.
