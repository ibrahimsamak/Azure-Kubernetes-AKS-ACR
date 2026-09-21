#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"

AKS_OIDC_ISSUER=$(az aks show -g "$RG" -n "$AKS" --query oidcIssuerProfile.issuerUrl -o tsv)
EH_ID=$(az eventhubs namespace show -g "$RG" -n "$EH_NS" --query id -o tsv)
SB_ID=$(az servicebus namespace show -g "$RG" -n "$SB_NS" --query id -o tsv)
KV_ID=$(az keyvault show -n "$KV" --query id -o tsv)

# --assignee-principal-type skips the Graph lookup, which avoids "PrincipalNotFound" errors
# when an identity was created seconds ago and hasn't replicated yet.
assign() {  # $1 principalId  $2 role  $3 scope
  az role assignment create --assignee-object-id "$1" --assignee-principal-type ServicePrincipal \
    --role "$2" --scope "$3" -o none
  echo "   + $2"
}

principal_of() { az identity show -g "$RG" -n "id-orderflow-$1" --query principalId -o tsv; }

for svc in $SERVICES; do
  name="id-orderflow-$svc"
  echo "==> $name"
  az identity create -g "$RG" -n "$name" -l "$LOCATION" -o none

  az identity federated-credential create -g "$RG" --identity-name "$name" \
    --name "fic-aks-$svc" \
    --issuer "$AKS_OIDC_ISSUER" \
    --subject "system:serviceaccount:${K8S_NS}:${svc}" \
    --audiences "api://AzureADTokenExchange" -o none
  echo "   + federated: system:serviceaccount:${K8S_NS}:${svc}"

  p=$(principal_of "$svc")
  assign "$p" "Azure Event Hubs Data Sender"   "$EH_ID"   # produce events + DLQ
  assign "$p" "Azure Event Hubs Data Receiver" "$EH_ID"   # consume
done

echo "==> service-specific roles"
assign "$(principal_of notification)" "Azure Service Bus Data Sender" "$SB_ID"
assign "$(principal_of payment)"      "Key Vault Secrets User"        "$KV_ID"

echo
echo "Client IDs (these go into ServiceAccount annotations via helm-deploy.sh):"
for svc in $SERVICES; do
  printf "  %-13s %s\n" "$svc" "$(az identity show -g "$RG" -n "id-orderflow-$svc" --query clientId -o tsv)"
done
