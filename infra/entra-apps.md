# Entra ID app registrations

App registrations and role assignments live in the tenant, not in `rg-orderflow-dev`. Deleting the
resource group doesn't remove them. Recreating it creates a new `id-orderflow-order` identity,
and its `Inventory.Read` assignment has to be redone. `bash infra/entra-apps.sh` creates or fixes
everything here except the test user, and it's safe to run again.

Tenant: `345f3725-4a76-4514-b265-f36d116154cc`

| Registration | Client id | Exposes | App roles | Notes |
|---|---|---|---|---|
| `orderflow-api` | `28b01914-9a4a-4555-8626-d516a358da1b` | `Orders.ReadWrite` (delegated) | `OrderFlow.Customer`, `OrderFlow.Support` | v2 tokens, assignment required. Pre-authorized for `orderflow-spa` and Azure CLI. |
| `orderflow-spa` | `74acbc9d-f79e-4499-b669-19392d3dfdde` | | | SPA platform (auth code + PKCE). Redirects: `http://localhost:4200` and the Static Web App URL. |
| `orderflow-inventory` | `INVENTORY_APP_ID` in `infra/env.sh` | `api://<client id>` | `Inventory.Read` (applications) | only called by services |

## Role assignments

| Who | App | Role | How |
|---|---|---|---|
| me | `orderflow-api` | Customer, Support | Enterprise applications -> Users and groups, or the script |
| `shopper@<tenant>` (test user) | `orderflow-api` | Customer | manually in the Portal |
| `id-orderflow-order` | `orderflow-inventory` | `Inventory.Read` | Graph only, the Portal can't assign app roles to managed identities |

Nobody else has `Inventory.Read`. Payment or Notification can get a token for
`orderflow-inventory`, but it has no roles so Inventory rejects it.

## Cleanup

Delete the three registrations (and permanently delete them from Deleted applications) and the
`shopper` user.
