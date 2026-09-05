#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
MANIFEST="$ROOT/release.json"
[[ -s "$MANIFEST" ]] || MANIFEST='/opt/celar-ai/deploy/release.json'
RUNTIME_TOKEN_FILE='/etc/celar-ai/gateway/runtime-token'
MAINTENANCE_TOKEN_FILE='/etc/celar-ai/gateway/maintenance-token'

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'maintenance-health-check.sh requires root.'
command -v jq >/dev/null 2>&1 || fail 'jq is required.'
HOSTNAME_VALUE="$(jq -r '.hostname' "$MANIFEST")"
[[ "$HOSTNAME_VALUE" == celarai.onenecklab.com ]] || fail 'Unexpected governed hostname.'
[[ -s "$RUNTIME_TOKEN_FILE" ]] || fail 'Runtime token file is missing.'
[[ -s "$MAINTENANCE_TOKEN_FILE" ]] || fail 'Maintenance token file is missing.'

SOCKETS="$(ss -lntH)"
grep -Eq '127\.0\.0\.1:8788([[:space:]]|$)' <<<"$SOCKETS" || fail 'Celar maintenance gateway localhost listener is missing.'
! grep -Eq '(0\.0\.0\.0|\[::\]|\*):8788([[:space:]]|$)' <<<"$SOCKETS" || fail 'Celar maintenance gateway is publicly bound.'
systemctl is-active --quiet celar-maintenance-gateway.service || fail 'Celar maintenance gateway service is not active.'
systemctl is-active --quiet celar-maintenance-reconcile.timer || fail 'Celar maintenance reconciler timer is not active.'

TMP="$(mktemp -d)"
chmod 0700 "$TMP"
trap 'rm -rf "$TMP"' EXIT
RUNTIME_CONFIG="$TMP/runtime.conf"
MAINTENANCE_CONFIG="$TMP/maintenance.conf"
RUNTIME_TOKEN="$(tr -d '\r\n' < "$RUNTIME_TOKEN_FILE")"
MAINTENANCE_TOKEN="$(tr -d '\r\n' < "$MAINTENANCE_TOKEN_FILE")"
[[ ${#RUNTIME_TOKEN} -ge 32 ]] || fail 'Runtime token is too short.'
[[ ${#MAINTENANCE_TOKEN} -ge 32 ]] || fail 'Maintenance token is too short.'
umask 0077
printf 'header = "Authorization: Bearer %s"\nheader = "X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only"\n' "$RUNTIME_TOKEN" > "$RUNTIME_CONFIG"
printf 'header = "Authorization: Bearer %s"\nheader = "X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only"\n' "$MAINTENANCE_TOKEN" > "$MAINTENANCE_CONFIG"
chmod 0600 "$RUNTIME_CONFIG" "$MAINTENANCE_CONFIG"
unset RUNTIME_TOKEN MAINTENANCE_TOKEN

RESOLVE=(--resolve "$HOSTNAME_VALUE:443:127.0.0.1")
BASE="https://$HOSTNAME_VALUE"

NO_TOKEN_STATUS="$(curl -sS --max-time 15 "${RESOLVE[@]}" -o /dev/null -w '%{http_code}' \
  -H 'X-Pulse-AI-Privacy-Boundary: private_pulse_runtime_only' \
  "$BASE/v1/maintenance/status" || true)"
[[ "$NO_TOKEN_STATUS" == 401 ]] || fail "Maintenance status without a token must return 401, got $NO_TOKEN_STATUS."

MISSING_BOUNDARY_STATUS="$(curl -sS --max-time 15 "${RESOLVE[@]}" --config "$RUNTIME_CONFIG" \
  -H 'X-Pulse-AI-Privacy-Boundary:' -o /dev/null -w '%{http_code}' \
  "$BASE/v1/maintenance/status" || true)"
[[ "$MISSING_BOUNDARY_STATUS" == 403 ]] || fail "Maintenance status without privacy boundary must return 403, got $MISSING_BOUNDARY_STATUS."

curl -fsS --max-time 30 "${RESOLVE[@]}" --config "$RUNTIME_CONFIG" \
  "$BASE/v1/maintenance/status" > "$TMP/status.json"
jq -e \
  --arg gateway "$(jq -r '.gatewayVersion' "$MANIFEST")" \
  --arg day "$(jq -r '.modelMaintenance.dayOfWeek' "$MANIFEST")" \
  --arg localTime "$(jq -r '.modelMaintenance.localTime' "$MANIFEST")" \
  --arg timeZone "$(jq -r '.modelMaintenance.timeZone' "$MANIFEST")" '
    .module == "084" and
    .status == "ready" and
    .gatewayVersion == $gateway and
    (.ollama.engineVersion | strings | length) > 0 and
    (.ollama.models | arrays | length) >= 4 and
    ([.ollama.models[] | select(.installed == true)] | length) >= 4 and
    .maintenance.desired.cadence == "weekly" and
    .maintenance.desired.dayOfWeek == $day and
    .maintenance.desired.localTime == $localTime and
    .maintenance.desired.timeZone == $timeZone and
    .security.maintenanceMutationUsesDedicatedCredential == true and
    .security.runtimeTokenMayChangeSchedule == false and
    .security.shellExecutionExposed == false and
    .security.secretValuesReturned == false
  ' "$TMP/status.json" >/dev/null || fail 'Maintenance status contract failed.'

# The normal inference/read-only token must never gain schedule mutation power.
RUNTIME_PUT_STATUS="$(curl -sS --max-time 15 "${RESOLVE[@]}" --config "$RUNTIME_CONFIG" \
  -H 'X-Celar-Maintenance-Intent: schedule_update' \
  -H 'Content-Type: application/json' \
  -d '{"enabled":true,"dayOfWeek":"Sunday","localTime":"01:00","timeZone":"America/Chicago","requestId":"health-runtime-token-must-fail"}' \
  -o /dev/null -w '%{http_code}' "$BASE/v1/maintenance/schedule" || true)"
[[ "$RUNTIME_PUT_STATUS" == 401 ]] || fail "Runtime token schedule mutation must return 401, got $RUNTIME_PUT_STATUS."

# The dedicated credential may read status, proving it is synchronized locally,
# but acceptance does not mutate the schedule merely to test the credential.
curl -fsS --max-time 30 "${RESOLVE[@]}" --config "$MAINTENANCE_CONFIG" \
  "$BASE/v1/maintenance/status" > "$TMP/maintenance-status.json"
jq -e '.module == "084" and .security.secretValuesReturned == false' "$TMP/maintenance-status.json" >/dev/null || \
  fail 'Dedicated maintenance credential readiness failed.'

echo 'CELAR_MAINTENANCE_HEALTH=PASS'
echo 'CELAR_MAINTENANCE_READ_ONLY_RUNTIME_TOKEN=PASS'
echo 'CELAR_MAINTENANCE_DEDICATED_CREDENTIAL=PASS'
echo 'CELAR_MAINTENANCE_PRIVATE_LISTENER=PASS:127.0.0.1:8788'
