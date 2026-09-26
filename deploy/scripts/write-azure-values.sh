#!/usr/bin/env bash
set -euo pipefail
source infra/env.sh
: "${TENANT_ID:?}" "${API_APP_ID:?set in infra/env.sh}" "${INVENTORY_APP_ID:?set in infra/env.sh}"
az config set extension.use_dynamic_install=yes_without_prompt -o none
APPI_CS=$(az monitor app-insights component show -g "$RG" -a "$APPI" --query connectionString -o tsv 2>/dev/null || echo "")

REDIS_HOST=$(az redisenterprise show -g "$RG" -n "$REDIS" --query hostName -o tsv 2>/dev/null || echo "")
SQL_HOST="${SQL_SERVER}.database.windows.net"

sql() {
  echo "Server=tcp:${SQL_HOST},1433;Database=$1;Authentication=Active Directory Default;Encrypt=True;"
}

cat > deploy/helm/values/azure.generated.yaml <<EOF

image:
  registry: "${ACR}.azurecr.io"
  pullPolicy: IfNotPresent

ingress:
  className: "webapprouting.kubernetes.azure.com"

environment: production

env:
  APPLICATIONINSIGHTS_CONNECTION_STRING: "${APPI_CS}"

  ConnectionStrings__orderflow-orders: "$(sql orderflow-orders)"
  ConnectionStrings__orderflow-inventory: "$(sql orderflow-inventory)"
  ConnectionStrings__orderflow-payments: "$(sql orderflow-payments)"
  ConnectionStrings__orderflow-notifications: "$(sql orderflow-notifications)"

  ConnectionStrings__kafka: "${EH_NS}.servicebus.windows.net:9093"
  Kafka__AuthMode: "AzureAd"
  Kafka__ProvisionTopics: "false"
  Kafka__CompressionType: "None"

  Redis__HostName: "${REDIS_HOST:+${REDIS_HOST}:10000}"

  ServiceBus__FullyQualifiedNamespace: "${SB_NS}.servicebus.windows.net"

  AzureAd__Instance: "https://login.microsoftonline.com/"
  AzureAd__TenantId: "${TENANT_ID}"
  AzureAd__ClientId: "${API_APP_ID}"
  Inventory__TokenScope: "api://${INVENTORY_APP_ID}/.default"
EOF

echo "Wrote deploy/helm/values/azure.generated.yaml"
