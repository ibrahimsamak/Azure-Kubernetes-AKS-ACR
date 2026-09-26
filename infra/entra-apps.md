# Entra ID app registrations (Week 4, Day 3)

App registrations and their role assignments live in the **tenant**, not in `rg-orderflow-dev`.
Deleting the resource group does not remove them; recreating the resource group creates a **new**
`id-orderflow-order` principal whose `Inventory.Read` assignment must be re-done.
`bash infra/entra-apps.sh` (idempotent) creates or repairs everything below except the test user.

Tenant: `345f3725-4a76-4514-b265-f36d116154cc`

| Registration | Client id | Exposes | App roles | Notes |
|---|---|---|---|---|
| `orderflow-api` | `28b01914-9a4a-4555-8626-d516a358da1b` | `api://28b01914-…/Orders.ReadWrite` (delegated) | `OrderFlow.Customer`, `OrderFlow.Support` (Users/Groups) | v2 tokens. **Assignment required = Yes**. Pre-authorized: `orderflow-spa`, Azure CLI. Audience for APIM, gateway and Order. |
| `orderflow-spa` | `74acbc9d-f79e-4499-b669-19392d3dfdde` | — | — | Public client, platform **SPA** (auth code + PKCE), implicit grant off. Redirect URIs: `http://localhost:4200`, the Static Web App URL. API permission `Orders.ReadWrite`, admin consent granted. |
| `orderflow-inventory` | *see `INVENTORY_APP_ID` in `infra/env.sh`* | `api://<client id>` (no scopes) | `Inventory.Read` (Applications) | v2 tokens. Called only by services, never on behalf of a user. |

## Role assignments

| Principal | Resource | Role | How |
|---|---|---|---|
| you | `orderflow-api` | `OrderFlow.Customer`, `OrderFlow.Support` | Enterprise applications → Users and groups (or the script) |
| `shopper@<tenant>` (test user) | `orderflow-api` | `OrderFlow.Customer` | **Manual**, Portal (3B step 5) |
| `id-orderflow-order` (managed identity) | `orderflow-inventory` | `Inventory.Read` | **Graph only** — the Portal can't assign app roles to managed identities: `POST /servicePrincipals/{inventory sp}/appRoleAssignedTo` |

Nothing else holds `Inventory.Read`. Payment's or Notification's identity can get a token for
`orderflow-inventory`, but it carries no roles, and Inventory refuses it.

## Teardown

To remove completely: delete the three registrations (Entra → App registrations → Deleted
applications → permanently delete) and the `shopper` user.
