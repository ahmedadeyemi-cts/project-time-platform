#!/usr/bin/env bash
set -Eeuo pipefail

MANIFEST='/opt/celar-ai/deploy/release.json'
DESIRED_FILE='/var/lib/celar-ai/gateway/maintenance-desired.json'
STATUS_FILE='/var/lib/celar-ai/gateway/maintenance-policy-status.json'
DROPIN_DIR='/etc/systemd/system/celar-ollama-update.timer.d'
DROPIN_FILE="$DROPIN_DIR/20-runtime-schedule.conf"

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

[[ "$(id -u)" -eq 0 ]] || fail 'maintenance-reconcile.sh requires root.'
command -v jq >/dev/null 2>&1 || fail 'jq is required.'
command -v sha256sum >/dev/null 2>&1 || fail 'sha256sum is required.'
[[ -s "$MANIFEST" ]] || fail 'Celar release manifest is missing.'
install -d -o celar-ai -g celar-ai -m 0750 /var/lib/celar-ai/gateway
install -d -m 0755 "$DROPIN_DIR"

if [[ ! -s "$DESIRED_FILE" ]]; then
  ENABLED="$(jq -r '.modelMaintenance.enabled' "$MANIFEST")"
  DAY="$(jq -r '.modelMaintenance.dayOfWeek' "$MANIFEST")"
  LOCAL_TIME="$(jq -r '.modelMaintenance.localTime' "$MANIFEST")"
  TIME_ZONE="$(jq -r '.modelMaintenance.timeZone' "$MANIFEST")"
  NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  TMP="$(mktemp /var/lib/celar-ai/gateway/maintenance-desired.XXXXXX)"
  jq -nc \
    --argjson enabled "$ENABLED" \
    --arg day "$DAY" \
    --arg localTime "$LOCAL_TIME" \
    --arg timeZone "$TIME_ZONE" \
    --arg requestedAt "$NOW" \
    '{schema:1,enabled:$enabled,cadence:"weekly",dayOfWeek:$day,localTime:$localTime,timeZone:$timeZone,requestId:"gitops-default",requestedAt:$requestedAt}' \
    > "$TMP"
  chown celar-ai:celar-ai "$TMP"
  chmod 0640 "$TMP"
  mv -f "$TMP" "$DESIRED_FILE"
fi

jq -e '
  .schema == 1 and
  (.enabled | type) == "boolean" and
  .cadence == "weekly" and
  (.dayOfWeek == "Monday" or .dayOfWeek == "Tuesday" or .dayOfWeek == "Wednesday" or .dayOfWeek == "Thursday" or .dayOfWeek == "Friday" or .dayOfWeek == "Saturday" or .dayOfWeek == "Sunday") and
  (.localTime | type) == "string" and
  (.localTime | test("^(?:[01][0-9]|2[0-3]):[0-5][0-9]$")) and
  .timeZone == "America/Chicago" and
  (.requestId | type) == "string" and (.requestId | test("^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$")) and
  (.requestedAt | type) == "string" and (.requestedAt | length) >= 20 and (.requestedAt | length) <= 64 and
  ((keys | sort) == (["cadence","dayOfWeek","enabled","localTime","requestId","requestedAt","schema","timeZone"] | sort))
' "$DESIRED_FILE" >/dev/null || fail 'Maintenance desired-state schema failed closed validation.'

ENABLED="$(jq -r '.enabled' "$DESIRED_FILE")"
DAY="$(jq -r '.dayOfWeek' "$DESIRED_FILE")"
LOCAL_TIME="$(jq -r '.localTime' "$DESIRED_FILE")"
TIME_ZONE="$(jq -r '.timeZone' "$DESIRED_FILE")"
REQUEST_ID="$(jq -r '.requestId' "$DESIRED_FILE")"
REQUESTED_AT="$(jq -r '.requestedAt' "$DESIRED_FILE")"
case "$DAY" in
  Monday) DAY_ABBR=Mon ;;
  Tuesday) DAY_ABBR=Tue ;;
  Wednesday) DAY_ABBR=Wed ;;
  Thursday) DAY_ABBR=Thu ;;
  Friday) DAY_ABBR=Fri ;;
  Saturday) DAY_ABBR=Sat ;;
  Sunday) DAY_ABBR=Sun ;;
  *) fail 'Unsupported weekly day.' ;;
esac
ON_CALENDAR="$DAY_ABBR *-*-* ${LOCAL_TIME}:00 $TIME_ZONE"
POLICY_HASH="$(printf '%s\n' "$ENABLED|weekly|$DAY|$LOCAL_TIME|$TIME_ZONE" | sha256sum | awk '{print $1}')"
PREVIOUS_HASH="$(jq -r '.policyHash // empty' "$STATUS_FILE" 2>/dev/null || true)"
PREVIOUS_APPLIED_AT="$(jq -r '.lastAppliedAt // empty' "$STATUS_FILE" 2>/dev/null || true)"
CURRENT_ACTIVE="$(systemctl is-active celar-ollama-update.timer 2>/dev/null || true)"
CURRENT_ENABLED="$(systemctl is-enabled celar-ollama-update.timer 2>/dev/null || true)"
NEEDS_APPLY=false
[[ "$POLICY_HASH" == "$PREVIOUS_HASH" ]] || NEEDS_APPLY=true
if [[ "$ENABLED" == true ]]; then
  [[ "$CURRENT_ACTIVE" == active && "$CURRENT_ENABLED" == enabled ]] || NEEDS_APPLY=true
else
  [[ "$CURRENT_ACTIVE" != active && "$CURRENT_ENABLED" == disabled ]] || NEEDS_APPLY=true
fi

if [[ "$NEEDS_APPLY" == true ]]; then
  TMP_DROPIN="$(mktemp "$DROPIN_DIR/20-runtime-schedule.conf.XXXXXX")"
  cat > "$TMP_DROPIN" <<EOF
[Timer]
OnCalendar=
OnCalendar=$ON_CALENDAR
AccuracySec=1min
RandomizedDelaySec=0
EOF
  chmod 0644 "$TMP_DROPIN"
  mv -f "$TMP_DROPIN" "$DROPIN_FILE"
  systemctl daemon-reload
  if [[ "$ENABLED" == true ]]; then
    systemctl enable celar-ollama-update.timer >/dev/null
    systemctl restart celar-ollama-update.timer
  else
    systemctl disable --now celar-ollama-update.timer >/dev/null 2>&1 || true
  fi
  LAST_APPLIED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
else
  LAST_APPLIED_AT="${PREVIOUS_APPLIED_AT:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
fi

NEXT_UTC=''
if [[ "$ENABLED" == true ]]; then
  NEXT_RAW="$(systemctl show celar-ollama-update.timer --property=NextElapseUSecRealtime --value 2>/dev/null || true)"
  if [[ -n "$NEXT_RAW" && "$NEXT_RAW" != n/a ]]; then
    NEXT_UTC="$(date -u -d "$NEXT_RAW" +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || true)"
  fi
fi
ACTIVE_AFTER="$(systemctl is-active celar-ollama-update.timer 2>/dev/null || true)"
ENABLED_AFTER="$(systemctl is-enabled celar-ollama-update.timer 2>/dev/null || true)"
NOW="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
TMP_STATUS="$(mktemp /var/lib/celar-ai/gateway/maintenance-policy-status.XXXXXX)"
jq -nc \
  --argjson enabled "$ENABLED" \
  --arg day "$DAY" \
  --arg localTime "$LOCAL_TIME" \
  --arg timeZone "$TIME_ZONE" \
  --arg onCalendar "$ON_CALENDAR" \
  --arg requestId "$REQUEST_ID" \
  --arg requestedAt "$REQUESTED_AT" \
  --arg policyHash "$POLICY_HASH" \
  --arg appliedAt "$LAST_APPLIED_AT" \
  --arg reconciledAt "$NOW" \
  --arg nextUtc "$NEXT_UTC" \
  --arg unitActive "$ACTIVE_AFTER" \
  --arg unitEnabled "$ENABLED_AFTER" \
  '{schema:1,result:"applied",enabled:$enabled,cadence:"weekly",dayOfWeek:$day,localTime:$localTime,timeZone:$timeZone,systemdOnCalendar:$onCalendar,requestId:$requestId,requestedAt:$requestedAt,policyHash:$policyHash,lastAppliedAt:$appliedAt,lastReconciledAt:$reconciledAt,nextMaintenanceAtUtc:(if $nextUtc == "" then null else $nextUtc end),timerActive:$unitActive,timerEnabled:$unitEnabled}' \
  > "$TMP_STATUS"
chown celar-ai:celar-ai "$TMP_STATUS"
chmod 0640 "$TMP_STATUS"
mv -f "$TMP_STATUS" "$STATUS_FILE"

echo "CELAR_MAINTENANCE_POLICY=PASS:$DAY:$LOCAL_TIME:$TIME_ZONE:enabled=$ENABLED"
