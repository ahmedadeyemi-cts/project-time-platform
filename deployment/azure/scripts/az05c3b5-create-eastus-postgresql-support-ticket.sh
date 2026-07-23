#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
SERVICE_NAME="06bfd9d3-516b-d5c6-5802-169c800dec89"
PROBLEM_CLASSIFICATION_NAME="af87bb6b-2275-4355-9dde-dff5f7eec887"
PROBLEM_CLASSIFICATION_ID="/providers/Microsoft.Support/services/${SERVICE_NAME}/problemClassifications/${PROBLEM_CLASSIFICATION_NAME}"

PRIMARY_SERVER="pg-phd-test-w3-7825cc"
PRIMARY_REGION="West US 3"
REPLICA_SERVER="pg-phd-test-eus-7825cc"
REPLICA_REGION="East US"

CONTACT_EMAIL="${PHD_SUPPORT_CONTACT_EMAIL:-Ahmed.Adeyemi@ussignal.com}"
CONTACT_FIRST_NAME="${PHD_SUPPORT_CONTACT_FIRST_NAME:-Ahmed}"
CONTACT_LAST_NAME="${PHD_SUPPORT_CONTACT_LAST_NAME:-Adeyemi}"
CONTACT_COUNTRY="${PHD_SUPPORT_CONTACT_COUNTRY:-USA}"
CONTACT_LANGUAGE="${PHD_SUPPORT_CONTACT_LANGUAGE:-en-us}"
CONTACT_TIMEZONE="${PHD_SUPPORT_CONTACT_TIMEZONE:-Pacific Standard Time}"

BASE_DIR="$HOME/project-health-dashboard-azure"
CONFIG_DIR="$BASE_DIR/config"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
TICKET_NAME="phd-postgresql-eastus-access-${STAMP,,}"
TITLE="Enable PostgreSQL Flexible Server provisioning in East US"
LOG="$LOG_DIR/az05c3b5-create-eastus-postgresql-support-ticket-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az05c3b5-eastus-postgresql-support-ticket.env"
RESULT_JSON="$LOG_DIR/az05c3b5-eastus-postgresql-support-ticket-$STAMP.json"
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

DESCRIPTION="$(cat <<EOF
Azure Database for PostgreSQL Flexible Server read-replica creation in East US is rejected with:

\"The location is restricted from performing this operation.\"

Subscription:
$SUBSCRIPTION_ID

The subscription was upgraded from Azure Free Trial to Pay-As-You-Go on July 12, 2026.

Source server:
$PRIMARY_SERVER

Source region:
$PRIMARY_REGION

Requested replica:
$REPLICA_SERVER

Requested region:
$REPLICA_REGION

Requested configuration:
- PostgreSQL 16
- Standard_D2ds_v4
- GeneralPurpose
- 128 GiB storage
- Private delegated subnet

The East US PostgreSQL capability response advertises one compatible supportedFastProvisioningEditions match, but also returns:

\"Provisioning is restricted in this region. Please choose a different region. For exceptions to this rule please open a support request with Issue type of 'Service and subscription limits'.\"

Please enable Azure Database for PostgreSQL Flexible Server provisioning in East US for this subscription so the planned cross-region read replica can be created.
EOF
)"

{
    section "AZ-05C3B5 - Create East US PostgreSQL Support Ticket"

    echo "TIME=$(date -u -Is)"
    echo "SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
    echo "SERVICE_NAME=$SERVICE_NAME"
    echo "PROBLEM_CLASSIFICATION_NAME=$PROBLEM_CLASSIFICATION_NAME"
    echo "TICKET_NAME=$TICKET_NAME"
    echo "SUPPORT_TICKET_WRITE_ACTION=true"
    echo "AZURE_RESOURCE_CREATION=false"

    [ "${PHD_CREATE_SUPPORT_TICKET:-}" = "YES" ] \
        || fail "Set PHD_CREATE_SUPPORT_TICKET=YES only when ready to submit the Azure support request."

    [ -n "$CONTACT_EMAIL" ] || fail "Support contact email is empty."
    [ -n "$CONTACT_FIRST_NAME" ] || fail "Support contact first name is empty."
    [ -n "$CONTACT_LAST_NAME" ] || fail "Support contact last name is empty."

    az account set --subscription "$SUBSCRIPTION_ID"

    CURRENT_SUBSCRIPTION_ID="$(az account show --query id -o tsv)"
    [ "$CURRENT_SUBSCRIPTION_ID" = "$SUBSCRIPTION_ID" ] \
        || fail "Current Azure subscription does not match the intended subscription."

    SUPPORT_PROVIDER_STATE="$(az provider show \
        --namespace Microsoft.Support \
        --query registrationState \
        -o tsv)"

    [ "$SUPPORT_PROVIDER_STATE" = "Registered" ] \
        || fail "Microsoft.Support provider is not Registered: $SUPPORT_PROVIDER_STATE"

    section "Validating live problem classification"

    CLASS_JSON="$WORK_DIR/problem-classifications.json"

    az support services problem-classifications list \
        --subscription "$SUBSCRIPTION_ID" \
        --service-name "$SERVICE_NAME" \
        --only-show-errors \
        --output json > "$CLASS_JSON"

    LIVE_CLASSIFICATION_COUNT="$(python3 - \
        "$CLASS_JSON" \
        "$PROBLEM_CLASSIFICATION_NAME" \
        "$PROBLEM_CLASSIFICATION_ID" <<'PY'
import json
import sys
from pathlib import Path

source = json.loads(Path(sys.argv[1]).read_text())
items = source if isinstance(source, list) else source.get("value", [])
wanted_name = sys.argv[2].lower()
wanted_id = sys.argv[3].lower()

print(sum(
    1 for item in items
    if str(item.get("name") or "").lower() == wanted_name
    and str(item.get("id") or "").lower() == wanted_id
))
PY
    )"

    [ "$LIVE_CLASSIFICATION_COUNT" = "1" ] \
        || fail "Expected PostgreSQL Flexible Server quota classification was not found exactly once."

    echo "CURRENT_SUBSCRIPTION_MATCH=yes"
    echo "SUPPORT_PROVIDER_STATE=$SUPPORT_PROVIDER_STATE"
    echo "LIVE_CLASSIFICATION_MATCH_COUNT=$LIVE_CLASSIFICATION_COUNT"
    echo "PROBLEM_CLASSIFICATION_ID=$PROBLEM_CLASSIFICATION_ID"

    section "Submitting Azure support ticket"

    az support in-subscription tickets create \
        --subscription "$SUBSCRIPTION_ID" \
        --ticket-name "$TICKET_NAME" \
        --title "$TITLE" \
        --description "$DESCRIPTION" \
        --problem-classification "$PROBLEM_CLASSIFICATION_ID" \
        --severity minimal \
        --advanced-diagnostic-consent No \
        --contact-country "$CONTACT_COUNTRY" \
        --contact-email "$CONTACT_EMAIL" \
        --contact-first-name "$CONTACT_FIRST_NAME" \
        --contact-last-name "$CONTACT_LAST_NAME" \
        --contact-language "$CONTACT_LANGUAGE" \
        --contact-method email \
        --contact-timezone "$CONTACT_TIMEZONE" \
        --require-24-by-7-response false \
        --no-wait false \
        --only-show-errors \
        --output json > "$RESULT_JSON"

    [ -s "$RESULT_JSON" ] || fail "Support ticket create command returned no JSON result."

    readarray -t TICKET_FIELDS < <(
        python3 - "$RESULT_JSON" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text())
print(obj.get("name") or "")
print(obj.get("status") or "")
print(obj.get("supportTicketId") or "")
print(obj.get("createdDate") or "")
print(obj.get("title") or "")
PY
    )

    CREATED_NAME="${TICKET_FIELDS[0]}"
    CREATED_STATUS="${TICKET_FIELDS[1]}"
    CREATED_SUPPORT_TICKET_ID="${TICKET_FIELDS[2]}"
    CREATED_DATE="${TICKET_FIELDS[3]}"
    CREATED_TITLE="${TICKET_FIELDS[4]}"

    [ -n "$CREATED_NAME" ] || fail "Created ticket name is empty."
    [ "$CREATED_NAME" = "$TICKET_NAME" ] || fail "Created ticket name does not match requested name."

    az support in-subscription tickets show \
        --subscription "$SUBSCRIPTION_ID" \
        --ticket-name "$TICKET_NAME" \
        --only-show-errors \
        --output none

    cat > "$STATE_FILE" <<EOF
AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID
SUPPORT_TICKET_NAME=$TICKET_NAME
SUPPORT_TICKET_ID=$CREATED_SUPPORT_TICKET_ID
SUPPORT_TICKET_STATUS=$CREATED_STATUS
SUPPORT_TICKET_CREATED_DATE=$CREATED_DATE
SUPPORT_TICKET_TITLE=$CREATED_TITLE
SUPPORT_SERVICE_NAME=$SERVICE_NAME
SUPPORT_PROBLEM_CLASSIFICATION_NAME=$PROBLEM_CLASSIFICATION_NAME
SUPPORT_PROBLEM_CLASSIFICATION_ID=$PROBLEM_CLASSIFICATION_ID
SUPPORT_TICKET_RESULT_JSON=$RESULT_JSON
EOF

    chmod 600 "$STATE_FILE"

    echo "SUPPORT_TICKET_CREATION_RESULT=CREATED"
    echo "SUPPORT_TICKET_NAME=$TICKET_NAME"
    echo "SUPPORT_TICKET_ID=${CREATED_SUPPORT_TICKET_ID:-not-reported}"
    echo "SUPPORT_TICKET_STATUS=${CREATED_STATUS:-not-reported}"
    echo "SUPPORT_TICKET_CREATED_DATE=${CREATED_DATE:-not-reported}"
    echo "SUPPORT_TICKET_STATE_FILE=$STATE_FILE"
    echo
    echo "************************************************************"
    echo "AZURE SUPPORT TICKET CREATED"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Support ticket log: $LOG"
echo "Support ticket JSON: $RESULT_JSON"
