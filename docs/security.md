# Security

What needs protecting, where the trust boundaries are, and the known gaps. Related decisions are
in [ADR 0006](adr/0006-entra-id-authorization.md) and [ADR 0007](adr/0007-aks-entra-rbac.md).
`bash deploy/scripts/secrets-audit.sh` checks the no-secrets setup.

## What's worth protecting

Orders and customer identity (`oid`), payment references, stock levels, the payment provider API
key in Key Vault, and the ability to deploy to production.

## Trust boundaries

1. internet -> APIM
2. APIM -> ingress -> gateway
3. gateway -> Order
4. Order -> Inventory (gRPC)
5. services -> Event Hubs / Service Bus / SQL / Key Vault
6. GitHub Actions -> Azure / AKS
7. people -> Azure / AKS / Entra

## Who can do what

| Caller | Can | Token needs | Checked by |
|---|---|---|---|
| customer | place orders, read own orders | aud orderflow-api, scp Orders.ReadWrite, role OrderFlow.Customer | APIM, gateway, Order |
| support | read any order | role OrderFlow.Support | same, no ownership check |
| Order service | call Inventory `CheckAvailability` | aud orderflow-inventory, role Inventory.Read | Inventory |
| Payment, Notification | nothing (no inbound API) | | |
| anyone else | nothing | | 401 at the first hop |

Someone else's order gives 404 instead of 403, so you can't tell whether it exists.

## Threats

| # | Where | Threat | What's in place | Gap / fix |
|---|---|---|---|---|
| 1 | 1 | fake user | Entra sign-in, APIM validate-jwt | |
| 2 | 1 | abuse / DoS | APIM rate limit per subscription | key is public, so per-user limits + WAF |
| 3 | 2 | skipping APIM | gateway checks the token again | internal ingress, APIM in a VNet |
| 4 | 3 | reading other customers' orders | customer id from `oid`, ownership check | |
| 5 | 3 | customer reading everything | Support role needed | |
| 6 | 4 | another pod calling Inventory | Inventory.Read token, NetworkPolicy allows only Order | h2c is plaintext, mesh mTLS |
| 7 | 5 | leaked keys | there are none: local auth off, Entra-only SQL, workload identity | |
| 8 | 5 | forged events | only each service's identity can send, consumers dedup by MessageId | events aren't signed |
| 9 | 5 | data in telemetry | no payloads in logs, Entra-only App Insights ingestion | `oid` is in telemetry, set retention |
| 10 | 6 | stolen CI credentials | OIDC, no secrets in GitHub, approval for prod, namespace-scoped RBAC | |
| 11 | 6 | vulnerable dependencies | Trivy, SBOMs, Dependabot, chiseled non-root images | image signing + admission policy |
| 12 | 7 | leaked kubeconfig | Entra + Azure RBAC, no local accounts | PIM for cluster admin |
| 13 | all | "who deployed this?" | GitHub deployments, Activity Log, traces with `oid` | longer Activity Log retention |
| 14 | 5 | compromised pod sending data out | egress is open | egress policies or Azure Firewall |
| 15 | 3-4 | removed role still in a cached token | tokens are short lived | CAE, restart the caller |

## Token checks per hop

| Check | APIM | Gateway | Order | Inventory |
|---|---|---|---|---|
| signature | yes | yes | yes | yes |
| issuer | yes | yes | yes | yes |
| audience | orderflow-api | orderflow-api | orderflow-api | orderflow-inventory |
| lifetime | yes | yes | yes | yes |
| scope | Orders.ReadWrite | | Orders.ReadWrite | n/a |
| role | | | Customer / Support | Inventory.Read |
| ownership | | | `oid` = order's customer | n/a |

It should not be possible to start a service with `Auth__Mode=Local` in Production (it throws), or
to reach `/health/*` from the internet (the ingress only routes `/api`). `deploy/scripts/test-auth.sh`
checks each hop.

## Accepted scanner findings

`trivy config .` has nothing at HIGH/CRITICAL. Lower ones I've accepted:

| Finding | Where | Why |
|---|---|---|
| KSV-0011 no CPU limit | Helm chart | CPU limits cause throttling, requests are set |
| KSV-0020/0021 UID/GID <= 10000 | chart / image | chiseled `app` user (1654), non-root, no shell |
| KSV-0110 default namespace | chart | releases go to `orderflow` via `--namespace` |
| DS-0026 no HEALTHCHECK | Dockerfiles | Kubernetes probes are used instead |

Review these again in December 2026.

`docker-compose.yml` has local-only passwords (SQL SA, RabbitMQ, fake payment key) for running on
a dev machine or CI. `.gitleaks.toml` allowlists only that file.
