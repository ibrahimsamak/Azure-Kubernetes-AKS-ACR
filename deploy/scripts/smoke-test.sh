#!/usr/bin/env bash
# Usage (from OrderFlow/):
#   bash deploy/scripts/smoke-test.sh full http://localhost:8080   -> drives the saga end to end (staging e2e)
#   bash deploy/scripts/smoke-test.sh edge http://<ingress-ip>     -> read-only checks, safe against production
#   EDGE_EXPECT=401 bash deploy/scripts/smoke-test.sh edge ...     -> from Day 3 on, when auth is enforced
set -euo pipefail

MODE=${1:?usage: smoke-test.sh full|edge <base-url>}
BASE=${2:?usage: smoke-test.sh full|edge <base-url>}
EDGE_EXPECT=${EDGE_EXPECT:-404}
NO_SUCH_ORDER="00000000-0000-0000-0000-000000000000"
# Extra curl args for every request. Against the Local auth mode (compose / staging e2e) no header is
# needed — every request is the default dev principal; X-Dev-User / X-Dev-Roles override it.
CURL_EXTRA=()

uuid() { cat /proc/sys/kernel/random/uuid 2>/dev/null || powershell -NoProfile -Command "[guid]::NewGuid().ToString()"; }

# $1 url  $2 expected HTTP status  $3 timeout (s). Retries: containers/pods may still be starting.
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

# $1 unit price  $2 idempotency key  -> prints the orderId
post_order() {
  curl -fsS -X POST "$BASE/api/v1/orders" "${CURL_EXTRA[@]}" \
    -H "Content-Type: application/json" -H "Idempotency-Key: $2" \
    -d "{\"currency\":\"CAD\",\"lines\":[{\"sku\":\"SKU-1\",\"quantity\":1,\"unitPrice\":$1}]}" \
  | jq -r .orderId
}

# $1 unit price  $2 expected terminal state
place_and_wait() {
  local key id again state="" deadline=$((SECONDS + 120))
  key=$(uuid)
  id=$(post_order "$1" "$key")
  echo "..   order $id at $1, expecting $2"

  # Idempotency-Key: a replayed POST must return the SAME order, not create a second one.
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
    expect_status "$BASE/health/ready" 200 240                       # gateway up
    expect_status "$BASE/api/v1/orders/$NO_SUCH_ORDER/status" 404 240 # gateway -> order routing up
    place_and_wait 29.99 Confirmed                                   # Pending -> ... -> Confirmed
    place_and_wait 13.13 Cancelled                                   # payment declines -> compensation

    # Ownership: a different customer asking for this order must get 404 — not 403, not 200.
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
