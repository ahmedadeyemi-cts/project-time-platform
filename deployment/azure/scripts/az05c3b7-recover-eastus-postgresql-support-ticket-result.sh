#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
TICKET_NAME="${PHD_SUPPORT_TICKET_NAME:-phd-postgresql-eastus-access-20260712t212351z}"
SERVICE_NAME="06bfd9d3-516b-d5c6-5802-169c800dec89"
PROBLEM_CLASSIFICATION_NAME="af87bb6b-2275-4355-9dde-dff5f7eec887"
PROBLEM_CLASSIFICATION_ID="/providers/Microsoft.Support/services/${SERVICE_NAME}/problemClassifications/${PROBLEM_CLASSIFICATION_NAME}"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STATE_FILE="$CONFIG_DIR/az05c3b5-eastus-postgresql-support-ticket.env"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c3b7-recover-eastus-postgresql-support-ticket-result-$STAMP.log"
RECOVERED_JSON="$LOG_DIR/az05c3b7-recovered-support-ticket-$STAMP.json"
WORK_DIR="$(mktemp -d)"

mkdir -p "$CONFIG_DIR" "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

write_state() {
    local json_file="$1"

    readarray -t ticket_fields < <(
        python3 - "$json_file" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
print(obj.get("name") or "")
print(obj.get("supportTicketId") or "")
print(obj.get("status") or "")
print(obj.get("createdDate") or "")
print(obj.get("modifiedDate") or "")
print(obj.get("title") or "")
print(obj.get("serviceId") or "")
print(obj.get("problemClassificationId") or "")
print(obj.get("severity") or "")
PY
    )

    local recovered_name="${ticket_fields[0]}"
    local recovered_id="${ticket_fields[1]}"
    local recovered_status="${ticket_fields[2]}"
    local recovered_created="${ticket_fields[3]}"
    local recovered_modified="${ticket_fields[4]}"
    local recovered_title="${ticket_fields[5]}"
    local recovered_service_id="${ticket_fields[6]}"
    local recovered_classification_id="${ticket_fields[7]}"
    local recovered_severity="${ticket_fields[8]}"

    [ "$recovered_name" = "$TICKET_NAME" ] \
        || fail "Recovered ticket name '$recovered_name' does not match '$TICKET_NAME'."

    cat > "$STATE_FILE" <<EOF
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
SUPPORT_TICKET_NAME=$recovered_name
SUPPORT_TICKET_ID=$recovered_id
SUPPORT_TICKET_STATUS=$recovered_status
SUPPORT_TICKET_CREATED_DATE=$recovered_created
SUPPORT_TICKET_MODIFIED_DATE=$recovered_modified
SUPPORT_TICKET_TITLE=$recovered_title
SUPPORT_TICKET_SEVERITY=$recovered_severity
SUPPORT_SERVICE_NAME=$SERVICE_NAME
SUPPORT_SERVICE_ID=$recovered_service_id
SUPPORT_PROBLEM_CLASSIFICATION_NAME=$PROBLEM_CLASSIFICATION_NAME
SUPPORT_PROBLEM_CLASSIFICATION_ID=$recovered_classification_id
SUPPORT_TICKET_RESULT_JSON=$RECOVERED_JSON
SUPPORT_TICKET_RECOVERED_AT=$STAMP
EOF

    chmod 600 "$STATE_FILE"

    echo "SUPPORT_TICKET_NAME=$recovered_name"
    echo "SUPPORT_TICKET_ID=${recovered_id:-not-reported}"
    echo "SUPPORT_TICKET_STATUS=${recovered_status:-not-reported}"
    echo "SUPPORT_TICKET_SEVERITY=${recovered_severity:-not-reported}"
    echo "SUPPORT_TICKET_TITLE=${recovered_title:-not-reported}"
    echo "SUPPORT_TICKET_CREATED_DATE=${recovered_created:-not-reported}"
    echo "SUPPORT_TICKET_STATE_FILE=$STATE_FILE"
}

{
    section "AZ-05C3B7 - Recover East US PostgreSQL Support Ticket Result"

    echo "TIME=$(date -u -Is)"
    echo "SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
    echo "EXPECTED_TICKET_NAME=$TICKET_NAME"
    echo "READ_ONLY_AZURE_QUERY=true"
    echo "DUPLICATE_TICKET_CREATION=false"

    az account set --subscription "$SUBSCRIPTION_ID"

    CURRENT_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION_ID" = "$SUBSCRIPTION_ID" ] \
        || fail "Current Azure subscription does not match the intended subscription."

    section "Querying exact ticket name"

    SHOW_JSON="$WORK_DIR/show.json"
    SHOW_ERR="$WORK_DIR/show.stderr"

    set +e
    az support in-subscription tickets show \
        --subscription "$SUBSCRIPTION_ID" \
        --ticket-name "$TICKET_NAME" \
        --only-show-errors \
        --output json \
        > "$SHOW_JSON" 2> "$SHOW_ERR"
    SHOW_RC=$?
    set -e

    echo "EXACT_TICKET_SHOW_RC=$SHOW_RC"
    echo "EXACT_TICKET_SHOW_STDOUT_BYTES=$(wc -c < "$SHOW_JSON")"
    echo "EXACT_TICKET_SHOW_STDERR_BYTES=$(wc -c < "$SHOW_ERR")"

    EXACT_VALID="$(python3 - "$SHOW_JSON" "$TICKET_NAME" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
wanted = sys.argv[2]

try:
    obj = json.loads(path.read_text())
    valid = isinstance(obj, dict) and str(obj.get("name") or "") == wanted
except Exception:
    valid = False

print("yes" if valid else "no")
PY
    )"

    echo "EXACT_TICKET_JSON_VALID=$EXACT_VALID"

    if [ "$SHOW_RC" -eq 0 ] && [ "$EXACT_VALID" = "yes" ]; then
        cp "$SHOW_JSON" "$RECOVERED_JSON"
        echo "SUPPORT_TICKET_LOOKUP=exact-name"
        write_state "$RECOVERED_JSON"
        echo "SUPPORT_TICKET_RECOVERY_RESULT=FOUND_AND_STATE_REBUILT"
        echo
        echo "************************************************************"
        echo "AZURE SUPPORT TICKET RECOVERED"
        echo "************************************************************"
    else
        section "Searching recent support tickets"

        LIST_JSON="$WORK_DIR/list.json"
        LIST_ERR="$WORK_DIR/list.stderr"

        set +e
        az support in-subscription tickets list \
            --subscription "$SUBSCRIPTION_ID" \
            --filter "CreatedDate ge 2026-07-12T21:20:00Z" \
            --max-items 100 \
            --only-show-errors \
            --output json \
            > "$LIST_JSON" 2> "$LIST_ERR"
        LIST_RC=$?
        set -e

        echo "RECENT_TICKET_LIST_RC=$LIST_RC"
        echo "RECENT_TICKET_LIST_STDOUT_BYTES=$(wc -c < "$LIST_JSON")"
        echo "RECENT_TICKET_LIST_STDERR_BYTES=$(wc -c < "$LIST_ERR")"

        MATCH_COUNT="$(python3 - "$LIST_JSON" "$TICKET_NAME" "$RECOVERED_JSON" <<'PY'
import json
import sys
from pathlib import Path

source_path = Path(sys.argv[1])
wanted = sys.argv[2]
out_path = Path(sys.argv[3])

try:
    source = json.loads(source_path.read_text())
except Exception:
    print(0)
    raise SystemExit(0)

if isinstance(source, list):
    items = source
elif isinstance(source, dict):
    items = source.get("value") or source.get("items") or []
else:
    items = []

matches = [item for item in items if str((item or {}).get("name") or "") == wanted]

if len(matches) == 1:
    out_path.write_text(json.dumps(matches[0], indent=2) + "\n")

print(len(matches))
PY
        )"

        echo "RECENT_EXACT_TICKET_MATCH_COUNT=$MATCH_COUNT"

        if [ "$LIST_RC" -eq 0 ] && [ "$MATCH_COUNT" = "1" ] && [ -s "$RECOVERED_JSON" ]; then
            echo "SUPPORT_TICKET_LOOKUP=recent-ticket-list"
            write_state "$RECOVERED_JSON"
            echo "SUPPORT_TICKET_RECOVERY_RESULT=FOUND_AND_STATE_REBUILT"
            echo
            echo "************************************************************"
            echo "AZURE SUPPORT TICKET RECOVERED"
            echo "************************************************************"
        else
            echo "SUPPORT_TICKET_RECOVERY_RESULT=NOT_FOUND"
            echo "SUPPORT_TICKET_STATE_FILE_WRITTEN=false"
            echo "SUPPORT_TICKET_CREATION_RETRY_ALLOWED=false"

            if [ -s "$SHOW_ERR" ]; then
                echo "EXACT_TICKET_SHOW_ERROR_BEGIN"
                tail -n 20 "$SHOW_ERR"
                echo "EXACT_TICKET_SHOW_ERROR_END"
            fi

            if [ -s "$LIST_ERR" ]; then
                echo "RECENT_TICKET_LIST_ERROR_BEGIN"
                tail -n 20 "$LIST_ERR"
                echo "RECENT_TICKET_LIST_ERROR_END"
            fi

            echo
            echo "************************************************************"
            echo "AZURE SUPPORT TICKET RECOVERY INCONCLUSIVE"
            echo "************************************************************"
        fi
    fi

} 2>&1 | tee "$LOG"

echo
echo "Support ticket recovery log: $LOG"
[ -s "$RECOVERED_JSON" ] && echo "Recovered ticket JSON: $RECOVERED_JSON"
