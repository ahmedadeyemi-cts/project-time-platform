#!/usr/bin/env bash
# Retry only explicitly throttled reads and the exact idempotent job PUT.
# Never replay a start/stop/delete operation or expose command arguments.
azure_migration_retry() (
  set -Eeuo pipefail
  local operation="${1:-}" status=0 attempt delay retry_after elapsed
  shift || return 64
  case "$operation:${*:1:5}" in
    account_read:'az account show '*) ;;
    identity_read:'az identity show '*) ;;
    api_read:'az containerapp show '*) ;;
    token_read:'az account get-access-token '*) ;;
    executions_read:'az containerapp job execution list') ;;
    job_put:'az rest --method put --uri') ;;
    *) echo 'MIGRATION_AZURE_RETRY=REJECTED_OPERATION' >&2; return 64 ;;
  esac
  local started=$SECONDS temporary
  temporary="$(mktemp -d)"
  chmod 0700 "$temporary"
  trap 'rm -rf -- "$temporary"' EXIT
  for attempt in 1 2 3 4; do
    printf 'MIGRATION_AZURE_OPERATION=%s attempt=%s\n' "$operation" "$attempt" >&2
    status=0
    "$@" >"$temporary/stdout" 2>"$temporary/stderr" || status=$?
    if (( status == 0 )); then
      cat "$temporary/stdout"
      return 0
    fi
    # Authentication, permissions, missing resources and ambiguous transport
    # failures are not throttling and cannot be retried by this helper.
    if ! grep -Eiq '(Too[ _-]*Many[ _-]*Requests|HTTP/[0-9.]+[[:space:]]+429|Status[ _-]*Code[ :=]+429)' "$temporary/stderr"; then
      printf 'MIGRATION_AZURE_FAILURE=%s exit=%s retry=false\n' "$operation" "$status" >&2
      cat "$temporary/stderr" >&2
      return "$status"
    fi
    if (( attempt == 4 )); then
      printf 'MIGRATION_AZURE_THROTTLE_EXHAUSTED=%s attempts=4\n' "$operation" >&2
      cat "$temporary/stderr" >&2
      return "$status"
    fi
    delay=$((15 * (1 << (attempt - 1))))
    retry_after="$(sed -nE 's/.*[Rr][Ee][Tt][Rr][Yy][ _-]?[Aa][Ff][Tt][Ee][Rr][ :=]+([0-9]+).*/\1/p' "$temporary/stderr" | sed -n '1p')"
    if [[ -n "$retry_after" ]]; then
      # A server delay beyond this bounded attempt is terminal, not permission
      # to retry earlier than requested. Avoid overflow on malformed headers.
      if [[ ! "$retry_after" =~ ^[0-9]{1,3}$ ]] || (( 10#$retry_after > 120 )); then
        printf 'MIGRATION_AZURE_RETRY_AFTER_EXCEEDS_BUDGET=%s\n' "$operation" >&2
        return "$status"
      fi
      (( 10#$retry_after <= delay )) || delay=$((10#$retry_after))
    fi
    elapsed=$((SECONDS - started))
    if (( elapsed + delay > 300 )); then
      printf 'MIGRATION_AZURE_RETRY_BUDGET_EXHAUSTED=%s\n' "$operation" >&2
      return "$status"
    fi
    printf 'MIGRATION_AZURE_THROTTLE=%s wait_seconds=%s next_attempt=%s\n' "$operation" "$delay" "$((attempt + 1))" >&2
    sleep "$delay"
  done
  return "$status"
)
