#!/usr/bin/env bash
# Day 3: core platform. Run from OrderFlow/:  bash infra/provision.sh
set -euo pipefail
source "$(dirname "$0")/env.sh"

ME_OID=$(az ad signed-in-user show --query id -o tsv)
ME_UPN=$(az ad signed-in-user show --query userPrincipalName -o tsv)

echo "==> Resource group";   az group create -n "$RG" -l "$LOCATION" --tags project=orderflow week=3 -o none
echo "==> ACR";              az acr create -g "$RG" -n "$ACR" --sku Basic -o none

echo "==> AKS (~10 min)"
if ! az aks show -g "$RG" -n "$AKS" -o none 2>/dev/null; then
  az aks create -g "$RG" -n "$AKS" -l "$LOCATION" --tier free \
    --node-count 2 --node-vm-size Standard_D2as_v5 \
    --enable-cluster-autoscaler --min-count 1 --max-count 3 \
    --network-plugin azure --network-plugin-mode overlay \
    --enable-oidc-issuer --enable-workload-identity --enable-app-routing \
    --attach-acr "$ACR" --auto-upgrade-channel patch --generate-ssh-keys -o none
fi
az aks get-credentials -g "$RG" -n "$AKS" --overwrite-existing

echo "==> Azure SQL"
if ! az sql server show -g "$RG" -n "$SQL_SERVER" -o none 2>/dev/null; then
  az sql server create -g "$RG" -n "$SQL_SERVER" -l "$LOCATION" --enable-ad-only-auth \
    --external-admin-principal-type User --external-admin-name "$ME_UPN" --external-admin-sid "$ME_OID" \
    --minimal-tls-version 1.2 -o none
fi
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 -o none
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create -g "$RG" -s "$SQL_SERVER" -n dev-client \
  --start-ip-address "$MY_IP" --end-ip-address "$MY_IP" -o none
for db in $DATABASES; do
  az sql db create -g "$RG" -s "$SQL_SERVER" -n "$db" --edition GeneralPurpose --compute-model Serverless \
    --family Gen5 --capacity 1 --min-capacity 0.5 --auto-pause-delay 60 --backup-storage-redundancy Local -o none
done

echo "==> Event Hubs"
az eventhubs namespace create -g "$RG" -n "$EH_NS" -l "$LOCATION" --sku Standard --capacity 1 \
  --disable-local-auth true --minimum-tls-version 1.2 -o none
for t in $TOPICS; do
  for name in "$t" "$t.dlq"; do
    az eventhubs eventhub create -g "$RG" --namespace-name "$EH_NS" -n "$name" --partition-count 3 -o none
  done
done

echo "==> Service Bus"
az servicebus namespace create -g "$RG" -n "$SB_NS" -l "$LOCATION" --sku Standard \
  --disable-local-auth true --minimum-tls-version 1.2 -o none
az servicebus topic create -g "$RG" --namespace-name "$SB_NS" -n notifications \
  --default-message-time-to-live P1D --enable-duplicate-detection true \
  --duplicate-detection-history-time-window PT10M -o none
for sub in email sms; do
  az servicebus topic subscription create -g "$RG" --namespace-name "$SB_NS" --topic-name notifications \
    -n "$sub" --max-delivery-count 10 --enable-dead-lettering-on-message-expiration true -o none
done

echo "==> Key Vault"
az keyvault create -g "$RG" -n "$KV" -l "$LOCATION" --enable-rbac-authorization true --retention-days 7 -o none

echo "==> Your own data-plane roles"
for pair in \
  "Key Vault Secrets Officer|$(az keyvault show -n "$KV" --query id -o tsv)" \
  "Azure Service Bus Data Owner|$(az servicebus namespace show -g "$RG" -n "$SB_NS" --query id -o tsv)" \
  "Azure Event Hubs Data Owner|$(az eventhubs namespace show -g "$RG" -n "$EH_NS" --query id -o tsv)"; do
  az role assignment create --assignee-object-id "$ME_OID" --assignee-principal-type User \
    --role "${pair%%|*}" --scope "${pair#*|}" -o none
done

echo "Done. OIDC issuer: $(az aks show -g "$RG" -n "$AKS" --query oidcIssuerProfile.issuerUrl -o tsv)"
