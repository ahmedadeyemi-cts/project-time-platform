#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
STATE_FILE="$CONFIG_DIR/az05c3b5-eastus-postgresql-support-ticket.env"

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

[ -s "$STATE_FILE" ] || fail "Support ticket state file is missing: $STATE_FILE"

# shellcheck disable=SC1090
source "$STATE_FILE"

[ -n "${SUPPORT_TICKET_NAME:-}" ] || fail "Support ticket name is missing from state."

az account set --subscription "$SUBSCRIPTION_ID"

TICKET_JSON="$(az support in-subscription tickets show \
    --subscription "$SUBSCRIPTION_ID" \
    --ticket-name "$SUPPORT_TICKET_NAME" \
    --only-show-errors \
    --output json)"

python3 - "$TICKET_JSON" <<'PY'
import json
import sys

obj = json.loads(sys.argv[1])
print(f"SUPPORT_TICKET_NAME={obj.get('name') or ''}")
print(f"SUPPORT_TICKET_ID={obj.get('supportTicketId') or ''}")
print(f"SUPPORT_TICKET_STATUS={obj.get('status') or ''}")
print(f"SUPPORT_TICKET_SEVERITY={obj.get('severity') or ''}")
print(f"SUPPORT_TICKET_TITLE={obj.get('title') or ''}")
print(f"SUPPORT_TICKET_CREATED_DATE={obj.get('createdDate') or ''}")
print(f"SUPPORT_TICKET_MODIFIED_DATE={obj.get('modifiedDate') or ''}")
print(f"SUPPORT_TICKET_SERVICE_ID={obj.get('serviceId') or ''}")
print(f"SUPPORT_TICKET_PROBLEM_CLASSIFICATION_ID={obj.get('problemClassificationId') or ''}")
status = str(obj.get('status') or '').lower()
if status == 'closed':
    print('SUPPORT_TICKET_RESULT=CLOSED')
elif status:
    print('SUPPORT_TICKET_RESULT=ACTIVE')
else:
    print('SUPPORT_TICKET_RESULT=UNKNOWN')
PY
