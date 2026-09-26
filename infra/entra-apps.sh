#!/usr/bin/env bash
# Week 4 Day 3 (3B): the Entra app registrations and role assignments. Run from OrderFlow/:
#   bash infra/entra-apps.sh
# Idempotent: adds what is missing, never regenerates role ids (an assigned role can't be replaced).
# Needs Application Administrator / Cloud Application Administrator (or Global Administrator), and jq
# (Windows: winget install jqlang.jq).
# App registrations are NOT in the resource group — teardown doesn't remove them. See infra/entra-apps.md.
set -euo pipefail
source "$(dirname "$0")/env.sh"

GRAPH="https://graph.microsoft.com/v1.0"
AZURE_CLI_APP_ID="04b07795-8ddb-461a-bbee-02f9e1bf7b46"   # first-party, the same in every tenant
TMP="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
new_id() { cat /proc/sys/kernel/random/uuid 2>/dev/null || powershell -NoProfile -Command "[guid]::NewGuid().ToString()" | tr -d '\r'; }

obj_id() { az ad app show --id "$1" --query id -o tsv; }                 # application object id
sp_id()  { az ad sp show --id "$1" --query id -o tsv 2>/dev/null || az ad sp create --id "$1" --query id -o tsv; }

# $1 app id  $2 role value  $3 display name  $4 description  $5 member type (User|Application)
ensure_role() {
  local roles
  roles=$(az ad app show --id "$1" --query appRoles -o json)
  if jq -e --arg v "$2" 'any(.[]; .value == $v)' <<<"$roles" >/dev/null; then
    echo "   = role $2"; return
  fi
  jq --arg v "$2" --arg d "$3" --arg desc "$4" --arg t "$5" --arg id "$(new_id)" \
    '{appRoles: (. + [{allowedMemberTypes: [$t], displayName: $d, value: $v, description: $desc, isEnabled: true, id: $id}])}' \
    <<<"$roles" > "$TMP/roles.json"
  az rest --method PATCH --uri "$GRAPH/applications/$(obj_id "$1")" \
    --headers "Content-Type=application/json" --body "@$TMP/roles.json" -o none
  echo "   + role $2"
}

# $1 resource app id  $2 role value  $3 principal object id  $4 label
ensure_assignment() {
  local rsp rid
  rsp=$(sp_id "$1")
  rid=$(az ad sp show --id "$1" --query "appRoles[?value=='$2'].id" -o tsv)
  if az rest --method GET --uri "$GRAPH/servicePrincipals/$rsp/appRoleAssignedTo" \
       --query "value[?principalId=='$3' && appRoleId=='$rid'] | length(@)" -o tsv | grep -qx '[1-9][0-9]*'; then
    echo "   = $4 has $2"; return
  fi
  az rest --method POST --uri "$GRAPH/servicePrincipals/$rsp/appRoleAssignedTo" \
    --headers "Content-Type=application/json" \
    --body "{\"principalId\":\"$3\",\"resourceId\":\"$rsp\",\"appRoleId\":\"$rid\"}" -o none
  echo "   + $4 -> $2"
}

ME=$(az ad signed-in-user show --query id -o tsv)

# ---------------------------------------------------------------- orderflow-api
echo "==> orderflow-api ($API_APP_ID)"
ensure_role "$API_APP_ID" OrderFlow.Customer Customer "Places and reads their own orders" User
ensure_role "$API_APP_ID" OrderFlow.Support  Support  "Reads any order's status"          User
API_SP=$(sp_id "$API_APP_ID")
az ad sp update --id "$API_APP_ID" --set appRoleAssignmentRequired=true
echo "   + assignment required"
ensure_assignment "$API_APP_ID" OrderFlow.Customer "$ME" "you"
ensure_assignment "$API_APP_ID" OrderFlow.Support  "$ME" "you"

# Pre-authorize the SPA and the Azure CLI for Orders.ReadWrite: no consent prompt for users, and
# `az account get-access-token --resource api://$API_APP_ID` works for the 3J curl tests.
SCOPE_ID=$(az ad app show --id "$API_APP_ID" --query "api.oauth2PermissionScopes[?value=='Orders.ReadWrite'].id" -o tsv)
# The whole api object goes back, so the Orders.ReadWrite scope can't be dropped by the PATCH.
az ad app show --id "$API_APP_ID" --query api -o json \
  | jq --arg scope "$SCOPE_ID" --arg spa "$SPA_APP_ID" --arg cli "$AZURE_CLI_APP_ID" '
      {api: (.preAuthorizedApplications =
        [.preAuthorizedApplications[] | select(.appId != $spa and .appId != $cli)]
        + [{appId: $spa, delegatedPermissionIds: [$scope]}, {appId: $cli, delegatedPermissionIds: [$scope]}])}' \
  > "$TMP/preauth.json"
az rest --method PATCH --uri "$GRAPH/applications/$(obj_id "$API_APP_ID")" \
  --headers "Content-Type=application/json" --body "@$TMP/preauth.json" -o none
echo "   + pre-authorized: orderflow-spa, Azure CLI"

# ---------------------------------------------------------------- orderflow-spa
echo "==> orderflow-spa ($SPA_APP_ID)"
SWA_HOST=$(az staticwebapp show -g "$RG" -n "$SWA" --query defaultHostname -o tsv 2>/dev/null || echo "")
REDIRECTS=$(jq -cn --arg swa "$SWA_HOST" '["http://localhost:4200"] + (if $swa == "" then [] else ["https://" + $swa] end)')
# Platform "SPA": auth code + PKCE. Implicit grant stays off — no token ever appears in a URL.
jq -n --argjson uris "$REDIRECTS" --arg api "$API_APP_ID" --arg scope "$SCOPE_ID" '{
    spa: {redirectUris: $uris},
    web: {implicitGrantSettings: {enableAccessTokenIssuance: false, enableIdTokenIssuance: false}},
    requiredResourceAccess: [
      {resourceAppId: "00000003-0000-0000-c000-000000000000",
       resourceAccess: [{id: "e1fe6dd8-ba31-4d61-89e7-88639da4683d", type: "Scope"}]},
      {resourceAppId: $api, resourceAccess: [{id: $scope, type: "Scope"}]}
    ]}' > "$TMP/spa.json"
az rest --method PATCH --uri "$GRAPH/applications/$(obj_id "$SPA_APP_ID")" \
  --headers "Content-Type=application/json" --body "@$TMP/spa.json" -o none
echo "   + redirect URIs $REDIRECTS"
sp_id "$SPA_APP_ID" >/dev/null
az ad app permission admin-consent --id "$SPA_APP_ID"
echo "   + admin consent (User.Read, Orders.ReadWrite)"

# ---------------------------------------------------------------- orderflow-inventory
echo "==> orderflow-inventory"
INV=$(az ad app list --display-name orderflow-inventory --query "[0].appId" -o tsv)
if [[ -z "$INV" ]]; then
  INV=$(az ad app create --display-name orderflow-inventory --sign-in-audience AzureADMyOrg --query appId -o tsv)
  echo "   + created $INV"
fi
# No scopes: nobody calls Inventory on behalf of a user. v2 tokens, like orderflow-api.
az ad app show --id "$INV" --query api -o json \
  | jq --arg inv "$INV" '{identifierUris: ["api://" + $inv], api: (.requestedAccessTokenVersion = 2)}' > "$TMP/inv.json"
az rest --method PATCH --uri "$GRAPH/applications/$(obj_id "$INV")" \
  --headers "Content-Type=application/json" --body "@$TMP/inv.json" -o none
ensure_role "$INV" Inventory.Read Inventory.Read "Query stock availability" Application
sp_id "$INV" >/dev/null

# Only Order's managed identity. Payment's identity asking for an Inventory token gets one with no
# roles, and Inventory refuses it — least privilege for east-west traffic.
ORDER_MI=$(az identity show -g "$RG" -n id-orderflow-order --query principalId -o tsv)
ensure_assignment "$INV" Inventory.Read "$ORDER_MI" "id-orderflow-order"

echo
echo "Done. Put this in infra/env.sh if it isn't there yet:"
echo "  export INVENTORY_APP_ID=\"\${INVENTORY_APP_ID:-$INV}\""
echo "Still manual (Portal): create the 'shopper' test user and assign it OrderFlow.Customer (3B step 5)."
