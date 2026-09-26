#!/usr/bin/env bash
# Day 3 checks (3J): every hop rejects what it must, even when the hops before it are bypassed.
# Usage (Git Bash, from OrderFlow/, after az login and kubelogin):
#   KEY=<spa subscription primary key> bash deploy/scripts/test-day3.sh
# The browser checks (MSAL sign-in, shopper vs support, removing role assignments) stay manual.
set -uo pipefail
source infra/env.sh
: "${KEY:?set KEY to the primary key of the APIM 'spa' subscription}"

APIM_URL="https://${APIM}.azure-api.net/orderflow"
ZERO=00000000-0000-0000-0000-000000000000
FAILED=0

check() {  # $1 label  $2 expected  $3 actual
  if [[ "$3" == "$2" ]]; then echo "PASS  $1 ($3)"; else echo "FAIL  $1 (expected $2, got $3)"; FAILED=1; fi
}
status() {  # $1 url, rest: extra curl args
  local url=$1; shift
  curl -s -o /dev/null -w "%{http_code}" "$@" "$url"
}

API_TOKEN=$(az account get-access-token --resource "api://$API_APP_ID" --query accessToken -o tsv)
GRAPH_TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)

echo "== APIM"
check "#3 no token -> 401 from APIM" 401 \
  "$(status "$APIM_URL/api/v1/orders/$ZERO/status" -H "Ocp-Apim-Subscription-Key: $KEY")"
check "#4 valid token, WRONG audience (Graph) -> 401" 401 \
  "$(status "$APIM_URL/api/v1/orders/$ZERO/status" -H "Ocp-Apim-Subscription-Key: $KEY" -H "Authorization: Bearer $GRAPH_TOKEN")"
check "#5 right audience, scope and role -> 404 from Order (no such order)" 404 \
  "$(status "$APIM_URL/api/v1/orders/$ZERO/status" -H "Ocp-Apim-Subscription-Key: $KEY" -H "Authorization: Bearer $API_TOKEN")"

echo "== Bypass APIM: straight to the ingress"
IP=$(kubectl get ingress gateway -n "$K8S_NS" -o jsonpath='{.status.loadBalancer.ingress[0].ip}')
check "#6 no token -> 401 from the gateway" 401 "$(status "http://$IP/api/v1/orders/$ZERO/status")"
check "   Graph token -> 401 from the gateway" 401 \
  "$(status "http://$IP/api/v1/orders/$ZERO/status" -H "Authorization: Bearer $GRAPH_TOKEN")"

echo "== Bypass APIM and the gateway: port-forward to Order"
kubectl port-forward -n "$K8S_NS" svc/order 18080:8080 >/dev/null 2>&1 &
PF=$!
trap 'kill $PF 2>/dev/null' EXIT
sleep 4
check "#7 no token -> 401 from Order" 401 "$(status "http://localhost:18080/api/v1/orders/$ZERO/status")"
check "   valid token -> 404 from Order" 404 \
  "$(status "http://localhost:18080/api/v1/orders/$ZERO/status" -H "Authorization: Bearer $API_TOKEN")"

cat <<EOF

Manual (3J #1-2, #8-11): sign in to the SPA, decode the token at https://jwt.ms, try the shopper user,
look up each other's orders, and remove role assignments — see Project4-plan.md, 3J.
EOF
exit $FAILED
