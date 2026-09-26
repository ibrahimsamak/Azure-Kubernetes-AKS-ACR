# OrderFlow — security

What we protect, where the trust boundaries are, what stops each threat today, and the gaps we
accept knowingly (with the production fix for each). Decisions behind this: ADR-0018 (authorization
model) and ADR-0019 (AKS access). Evidence: `bash deploy/scripts/secrets-audit.sh`.

## Assets
Customer orders and identity (`oid`), payment references, stock levels, the payment-gateway API key
(Key Vault), the ability to deploy to production.

## Trust boundaries
1. Internet → APIM                 2. APIM → ingress → gateway        3. gateway → Order
4. Order → Inventory (gRPC)        5. services ↔ Event Hubs / Service Bus / SQL / Key Vault
6. GitHub Actions → Azure / AKS    7. Humans → Azure / AKS / Entra

## Who may do what

| Caller | May | Token must carry | Enforced by |
|---|---|---|---|
| Signed-in **customer** | place orders; read **their own** orders | `aud`=orderflow-api · `scp` ∋ `Orders.ReadWrite` · `roles` ∋ `OrderFlow.Customer` | APIM (aud, scp) → gateway (valid user token) → Order (scope + role + ownership) |
| Signed-in **support** agent | read **any** order's status | … `roles` ∋ `OrderFlow.Support` | same; Order skips the ownership check |
| **Order** service (managed identity) | call Inventory's `CheckAvailability` | `aud`=orderflow-inventory · `roles` ∋ `Inventory.Read` | Inventory gRPC endpoint |
| Payment, Notification | — (no inbound API) | — | nothing to call; their trust boundary is Event Hubs RBAC |
| Anyone else | nothing | — | 401 at the first hop that sees them |

Reading someone else's order returns **404, not 403**: a 403 would confirm the order id exists.

## Threats and mitigations (STRIDE)

| # | Boundary | Threat (STRIDE) | Mitigation in place | Accepted gap → production fix |
|---|---|---|---|---|
| 1 | 1 | **S**poofing a user | Entra ID, MSAL auth code + PKCE; APIM validate-jwt (sig, iss, aud, exp, scp) | — |
| 2 | 1 | **D**oS / abuse | APIM rate-limit per subscription; subscription key identifies the app | Key is public in the SPA → per-user rate limit (`rate-limit-by-key` on `oid`, needs a non-Consumption tier) + Front Door WAF |
| 3 | 2 | Bypassing APIM (ingress IP is public) | Gateway validates the JWT again | Ingress should be internal; APIM in a VNet (Premium/Standard v2) with private backend |
| 4 | 3 | **E**levation: act as another customer (IDOR) | customerId = token `oid`; ownership check; 404 for others' orders | — |
| 5 | 3 | **E**levation: customer reads everything | App roles; Support role required to read any order | — |
| 6 | 4 | Rogue pod calls Inventory | Managed-identity token with Inventory.Read; NetworkPolicy allows only Order | Token sent over plaintext h2c → service mesh mTLS (Istio add-on) |
| 7 | 5 | Stolen connection string / key | No keys exist: local auth disabled (EH, SB, AI), Entra-only SQL, KV RBAC; Workload Identity | — |
| 8 | 5 | **T**ampering with events (forged OrderPlaced) | Only each service's identity has Data Sender; consumers dedup by MessageId | Events unsigned → per-topic send rights (topic-scoped roles) + message signing if required |
| 9 | 5 | **I**nformation disclosure in telemetry | Structured logs without PII payloads; App Insights Entra-only ingestion | Customer ids (`oid`) are in telemetry → retention policy, access via RBAC on the workspace |
| 10 | 6 | Stolen CI credential | OIDC federation, no secrets in GitHub; two identities; deploy needs env approval; namespace-scoped RBAC | Actions pinned by SHA; branch ruleset requires PR |
| 11 | 6 | Vulnerable dependency / base image | Trivy gate (HIGH/CRITICAL fixable), SBOMs, Dependabot, chiseled non-root images | Image signing + admission policy (Notation + Ratify / Azure Policy) |
| 12 | 7 | Cluster admin via leaked kubeconfig | Entra ID + Azure RBAC, local accounts disabled, kubelogin (short-lived tokens) | Conditional Access + PIM for Cluster Admin (just-in-time) |
| 13 | all | **R**epudiation: who deployed / changed what | GitHub Deployments + required reviewer; Azure Activity Log; App Insights traces with `oid` | Export Activity Log to the workspace with long retention |
| 14 | 5 | Compromised pod exfiltrates data | Egress is open (pods need Entra, SQL, Event Hubs, Service Bus, Key Vault, App Insights) | NetworkPolicy is ingress-only → FQDN egress policies (Cilium) or Azure Firewall |
| 15 | 3–4 | Role removed, but still in a cached token | Tokens are short-lived (user ~60–90 min; managed identity up to ~24 h) | Continuous Access Evaluation for high-risk changes; restart the caller after removing a service's role |

## Token validation, per hop

| Check | APIM | Gateway | Order | Inventory |
|---|---|---|---|---|
| Signature (tenant's signing keys) | ✔ `openid-config` | ✔ Microsoft.Identity.Web | ✔ | ✔ |
| Issuer = our tenant | ✔ `issuers` | ✔ | ✔ | ✔ |
| Audience = this API | ✔ `audiences` | ✔ orderflow-api | ✔ orderflow-api | ✔ orderflow-inventory |
| Lifetime (`exp`, `nbf`) | ✔ | ✔ (5 min skew) | ✔ | ✔ |
| Delegated scope | ✔ `scp` ∋ Orders.ReadWrite | — | ✔ `RequireScope` | n/a (app-only) |
| Role | — | — | ✔ Customer/Support | ✔ Inventory.Read |
| Ownership | — | — | ✔ `oid` = saga.CustomerId | n/a |

Must **not** be possible: starting any service with `Auth__Mode=Local` while
`ASPNETCORE_ENVIRONMENT=Production` (the process throws at startup), and reaching `/health/*` from
the internet (the ingress routes `/api` only). `deploy/scripts/test-day3.sh` checks each hop.

## Accepted findings from scanners

`trivy config .` — nothing at HIGH/CRITICAL (the CI gate). Accepted lower findings:

| Finding | Where | Why accepted | Review by |
|---|---|---|---|
| KSV-0011 CPU not limited (LOW) | Helm chart | CFS throttling causes latency spikes even on idle nodes; CPU requests already guarantee our share | 2026-12 |
| KSV-0020 / KSV-0021 UID/GID ≤ 10000 (LOW) | Helm chart / chiseled image | The chiseled `app` user is 1654: non-root, no shell; `runAsNonRoot` enforced | 2026-12 |
| KSV-0110 workload in the default namespace (LOW) | Helm chart | The chart sets no namespace; every release goes to `orderflow` via `--namespace` | 2026-12 |
| DS-0026 no HEALTHCHECK (LOW) | Dockerfiles | Kubernetes probes (`/health/live`, `/health/ready`) replace Docker's HEALTHCHECK, which the kubelet ignores | 2026-12 |

`docker-compose.yml` holds local-only credentials (SQL Server SA, RabbitMQ, a fake payment key) for
containers on a developer machine or CI runner; `.gitleaks.toml` allowlists that one file, with the
reason.
