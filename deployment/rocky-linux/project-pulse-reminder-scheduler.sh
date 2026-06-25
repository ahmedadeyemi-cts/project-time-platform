#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-daily}"
API_BASE="${PROJECT_PULSE_API_BASE:-http://127.0.0.1:5080}"
DOW="$(date +%u)"
DAY="$(date +%d)"
LAST_DAY="$(date -d "$(date +%Y-%m-01) +1 month -1 day" +%d)"
DAYS_LEFT=$((10#$LAST_DAY - 10#$DAY))

is_last_friday() {
  [ "$DOW" = "5" ] && [ "$DAYS_LEFT" -lt 7 ]
}

case "$MODE" in
  weekly-engineer)
    curl -s -X POST "$API_BASE/api/reminders/queue-weekly-engineer" | jq . || true
    ;;
  month-end-pm)
    if is_last_friday; then
      curl -s -X POST "$API_BASE/api/reminders/queue-month-end-pm" | jq . || true
    else
      echo "Not last Friday. No month-end reminder queued."
    fi
    ;;
  daily)
    if [ "$DOW" = "5" ]; then
      curl -s -X POST "$API_BASE/api/reminders/queue-weekly-engineer" | jq . || true
    fi
    if is_last_friday; then
      curl -s -X POST "$API_BASE/api/reminders/queue-month-end-pm" | jq . || true
    fi
    ;;
  *)
    echo "Usage: $0 [daily|weekly-engineer|month-end-pm]" >&2
    exit 2
    ;;
esac
