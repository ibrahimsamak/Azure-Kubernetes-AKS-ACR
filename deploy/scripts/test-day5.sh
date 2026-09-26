#!/usr/bin/env bash
# Day 5 checks: APIM edge (401 / order / 429) and the Function fan-out (send, dedup, DLQ).
# Usage (Git Bash, from OrderFlow/):
#   KEY=<spa subscription primary key> bash deploy/scripts/test-day5.sh
# Since Day 3 APIM always requires a JWT: steps 2-4 send one for orderflow-api from your az login.
# The JWT checks themselves are in deploy/scripts/test-day3.sh.
set -uo pipefail
source infra/env.sh
: "${KEY:?set KEY to the primary key of the APIM 'spa' subscription}"

APIM_URL="https://${APIM}.azure-api.net/orderflow"
ZERO=00000000-0000-0000-0000-000000000000
SB_URL="https://${SB_NS}.servicebus.windows.net/notifications/messages"
TOKEN=$(az account get-access-token --resource "api://$API_APP_ID" --query accessToken -o tsv)
AUTH="Authorization: Bearer $TOKEN"

uuid() { powershell -NoProfile -Command '[guid]::NewGuid().ToString()' | tr -d '\r'; }
check() {  # $1 label, $2 expected, $3 actual
  if [[ "$3" == "$2" ]]; then echo "PASS  $1 ($3)"; else echo "FAIL  $1 (expected $2, got $3)"; fi
}

echo "== 1. APIM rejects a request without a subscription key"
code=$(curl -s -o /dev/null -w "%{http_code}" "$APIM_URL/api/v1/orders/$ZERO/status")
check "no key" 401 "$code"

echo "== 2. Place an order: APIM -> ingress -> gateway -> order"
body=$(curl -s -w "\n%{http_code}" -X POST "$APIM_URL/api/v1/orders" \
  -H "Ocp-Apim-Subscription-Key: $KEY" -H "$AUTH" -H "Content-Type: application/json" \
  -H "Idempotency-Key: $(uuid)" \
  -d '{"currency":"CAD","street":"1 King St W","city":"Toronto","postalCode":"M5H 1A1","country":"CA","lines":[{"sku":"SKU-1","quantity":1,"unitPrice":29.99}]}')
code=$(tail -n1 <<<"$body")
check "place order" 202 "$code"
ORDER_ID=$(sed -n 's/.*"orderId":"\([^"]*\)".*/\1/p' <<<"$body" | head -n1)
echo "      orderId=$ORDER_ID"

echo "== 3. Order reaches Confirmed (polls up to 60 s)"
status=""
for _ in $(seq 1 20); do
  status=$(curl -s -H "Ocp-Apim-Subscription-Key: $KEY" -H "$AUTH" "$APIM_URL/api/v1/orders/$ORDER_ID/status")
  grep -q -E 'Confirmed|Cancelled' <<<"$status" && break
  sleep 3
done
echo "      $status"
grep -q Confirmed <<<"$status" && echo "PASS  saga confirmed" || echo "FAIL  saga not confirmed"

echo "== 4. Rate limit: 40 requests, expect 429s near the end"
codes=""
for _ in $(seq 1 40); do
  codes+="$(curl -s -o /dev/null -w "%{http_code}" -H "Ocp-Apim-Subscription-Key: $KEY" -H "$AUTH" \
    "$APIM_URL/api/v1/orders/$ZERO/status") "
done
echo "      $codes"
grep -q 429 <<<"$codes" && echo "PASS  rate limited" || echo "FAIL  no 429 seen"

echo "== 5. Function fan-out: send to the notifications topic with an Entra token"
SB_TOKEN=$(az account get-access-token --resource https://servicebus.azure.net --query accessToken -o tsv)
send() {  # $1 message id, $2 customer id
  curl -s -o /dev/null -w "%{http_code}" -X POST "$SB_URL" \
    -H "Authorization: Bearer $SB_TOKEN" \
    -H "Content-Type: application/json" \
    -H "BrokerProperties: {\"MessageId\":\"$1\"}" \
    -d "{\"orderId\":\"11111111-1111-1111-1111-111111111111\",\"customerId\":\"$2\",\"kind\":\"OrderConfirmed\",\"channels\":[\"email\",\"sms\"]}"
}
DEDUP_ID="day5-$(uuid)"
check "send #1" 201 "$(send "$DEDUP_ID" CUST-1)"
check "send #2, same MessageId (dropped by duplicate detection)" 201 "$(send "$DEDUP_ID" CUST-1)"
check "send POISON" 201 "$(send "poison-$(uuid)" POISON)"

echo "== 6. Wait for the POISON message to exhaust 10 deliveries, then count the DLQ"
sleep 60
for sub in email sms; do
  dlq=$(az servicebus topic subscription show -g "$RG" --namespace-name "$SB_NS" \
    --topic-name notifications -n "$sub" --query countDetails.deadLetterMessageCount -o tsv)
  echo "      $sub dead-letter count: $dlq"
done

cat <<EOF

Now look at the function logs. Expect ONE "EMAIL -> customer CUST-1" and ONE "SMS -> customer CUST-1"
for the test message (not two: the duplicate was dropped), plus lines for order $ORDER_ID:
  az webapp log tail -g "$RG" -n "$FUNC"
or App Insights -> Logs:
  traces | where message startswith "EMAIL" or message startswith "SMS" | order by timestamp desc

EOF
