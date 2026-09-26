#!/usr/bin/env bash
set -euo pipefail

MODE=${1:?usage: smoke-test.sh full|edge <base-url>}
BASE=${2:?usage: smoke-test.sh full|edge <base-url>}
EDGE_EXPECT=${EDGE_EXPECT:-404}
NO_SUCH_ORDER="00000000-0000-0000-0000-000000000000"
CURL_EXTRA=()

uuid() { cat /proc/sys/kernel/random/uuid 2>/dev/null || powershell -NoProfile -Command "[guid]::NewGuid().ToString()"; }

expect_status() {
  local deadline=$((SECONDS + $3)) code=""
  while (( SECONDS < deadline )); do
    code=$(curl -s -o /dev/null -w '%{http_code}' "${CURL_EXTRA[@]}" "$1" || true)
    if [[ "$code" == "$2" ]]; then echo "ok   $1 -> $code"; return 0; fi
    sleep 3
  done
  echo "FAIL $1 -> ${code:-no response}, expected $2"
  return 1
}

post_order() {
  curl -fsS -X POST "$BASE/api/v1/orders" "${CURL_EXTRA[@]}" \
    -H "Content-Type: application/json" -H "Idempotency-Key: $2" \
    -d "{\"currency\":\"CAD\",\"lines\":[{\"sku\":\"SKU-1\",\"quantity\":1,\"unitPrice\":$1}]}" \
  | jq -r .orderId
}

place_and_wait() {
  local key id again state="" deadline=$((SECONDS + 120))
  key=$(uuid)
  id=$(post_order "$1" "$key")
  echo "..   order $id at $1, expecting $2"

  again=$(post_order "$1" "$key")
  [[ "$again" == "$id" ]] || { echo "FAIL idempotency: second POST returned $again"; return 1; }

  while (( SECONDS < deadline )); do
    state=$(curl -fsS "${CURL_EXTRA[@]}" "$BASE/api/v1/orders/$id/status" | jq -r .state)
    case "$state" in
      "$2")                echo "ok   $id reached $state"; return 0 ;;
      Confirmed|Cancelled) echo "FAIL $id ended $state, expected $2"; return 1 ;;
    esac
    sleep 2
  done
  echo "FAIL $id still '$state' after 120s"
  return 1
}

case "$MODE" in
  full)
    expect_status "$BASE/health/ready" 200 240
    expect_status "$BASE/api/v1/orders/$NO_SUCH_ORDER/status" 404 240
    place_and_wait 29.99 Confirmed
    place_and_wait 13.13 Cancelled

    other=(-H "X-Dev-User: 22222222-2222-2222-2222-222222222222" -H "X-Dev-Roles: OrderFlow.Customer")
    id=$(post_order 29.99 "$(uuid)")
    code=$(curl -s -o /dev/null -w '%{http_code}' "${other[@]}" "$BASE/api/v1/orders/$id/status")
    [[ "$code" == "404" ]] || { echo "FAIL another customer got $code for order $id"; exit 1; }
    echo "ok   another customer gets 404"
    ;;
  edge)
    expect_status "$BASE/api/v1/orders/$NO_SUCH_ORDER/status" "$EDGE_EXPECT" 120
    ;;
  *)
    echo "unknown mode '$MODE'"; exit 2 ;;
esac

echo "smoke test ($MODE) passed"
