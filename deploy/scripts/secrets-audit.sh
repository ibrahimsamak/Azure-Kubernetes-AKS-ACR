#!/usr/bin/env bash
# Evidence that no shared secret is in use — and a regression test for it. Every line is a PASS/FAIL
# against a setting that would re-introduce a shared secret if flipped.
# Run from OrderFlow/:  bash deploy/scripts/secrets-audit.sh      (needs az, kubectl, gh, git)
set -uo pipefail
source infra/env.sh
az config set extension.use_dynamic_install=yes_without_prompt -o none
FAILED=0

check() {  # $1 label  $2 actual  $3 expected
  if [[ "$2" == "$3" ]]; then
    printf "  PASS  %-46s %s\n" "$1" "$2"
  else
    printf "  FAIL  %-46s %s (expected %s)\n" "$1" "$2" "$3"; FAILED=1
  fi
}

echo "== Azure: key/password authentication is OFF"
APPI_ID=$(az monitor app-insights component show -g "$RG" -a "$APPI" --query id -o tsv)
check "Event Hubs local (SAS) auth disabled"   "$(az eventhubs namespace show -g "$RG" -n "$EH_NS" --query disableLocalAuth -o tsv)" true
check "Service Bus local (SAS) auth disabled"  "$(az servicebus namespace show -g "$RG" -n "$SB_NS" --query disableLocalAuth -o tsv)" true
check "App Insights local auth disabled"       "$(az resource show --ids "$APPI_ID" --query properties.DisableLocalAuth -o tsv)" true
check "Azure SQL Entra-only authentication"    "$(az sql server ad-only-auth get -g "$RG" -n "$SQL_SERVER" --query azureAdOnlyAuthentication -o tsv)" true
check "Key Vault uses Azure RBAC"              "$(az keyvault show -n "$KV" --query properties.enableRbacAuthorization -o tsv)" true
check "ACR admin user disabled"                "$(az acr show -n "$ACR" --query adminUserEnabled -o tsv)" false
check "AKS local accounts disabled"            "$(az aks show -g "$RG" -n "$AKS" --query disableLocalAccounts -o tsv)" true

echo "== Kubernetes: no Secrets except Helm's release records"
check "non-Helm Secrets in $K8S_NS" \
  "$(kubectl get secrets -n "$K8S_NS" --field-selector type!=helm.sh/release.v1 -o name | wc -l | tr -d ' ')" 0

echo "== GitHub: no stored Actions secrets"
check "repository secrets" "$(gh secret list -R "$GH_REPO" --json name --jq length)" 0
check "production environment secrets" "$(gh secret list -R "$GH_REPO" --env production --json name --jq length)" 0

echo "== Repo: no password-like assignments outside local-only files"
check "suspicious lines in tracked files" \
  "$(git grep -nIiE '(password|pwd|accountkey|sharedaccesskey|client_secret)[[:space:]]*[=:]' -- . \
      ':!docker-compose.yml' ':!*.md' ':!deploy/scripts/secrets-audit.sh' | wc -l | tr -d ' ')" 0

echo
if [[ $FAILED == 0 ]]; then echo "All checks passed."; else echo "Some checks FAILED."; fi
exit $FAILED
