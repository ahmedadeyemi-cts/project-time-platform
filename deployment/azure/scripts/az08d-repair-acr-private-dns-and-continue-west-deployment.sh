#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
REPOSITORY="ahmedadeyemi-cts/project-time-platform"
MIGRATION_BRANCH="azure-migration/project-health-dashboard-foundation"
CONTINUATION_PATH="deployment/azure/scripts/az08c-continue-west-deployment-keyvault-bootstrap.sh"

RG_SHARED="rg-project-health-dashboard-shared-global"
RG_WEST_NETWORK="rg-project-health-dashboard-test-network-westus3"
WEST_VNET="vnet-phd-test-westus3"
ACR_NAME="acrphdtest7825cc"
ACR_PRIVATE_ENDPOINT="pe-phd-test-acr-westus3"
ACR_PRIVATE_DNS_ZONE="privatelink.azurecr.io"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
CONFIG_DIR="$BASE_DIR/config"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az08d-repair-acr-private-dns-and-continue-$STAMP.log"
STATE_FILE="$CONFIG_DIR/az08d-acr-private-dns-repair.env"
WORK_DIR="$(mktemp -d /tmp/phd-az08d-XXXXXX)"
CONTINUATION_SCRIPT="$WORK_DIR/az08c-continuation.sh"
NIC_JSON="$WORK_DIR/acr-private-endpoint-nic.json"
RECORDS_FILE="$WORK_DIR/acr-private-dns-records.tsv"

mkdir -p "$LOG_DIR" "$CONFIG_DIR"
chmod 700 "$WORK_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    return 1
}

{
    section "AZ-08D - Repair ACR Private DNS and Continue West Deployment"
    echo "TIME=$(date -u -Is)"
    echo "ACR_IMAGE_REBUILD=false"
    echo "REUSE_EXISTING_ACR_IMAGES=true"
    echo "EAST_POSTGRESQL_REPLICA_CREATED=false"

    [ "${PHD_REPAIR_ACR_DNS_AND_CONTINUE:-}" = "YES" ] \
        || fail "Set PHD_REPAIR_ACR_DNS_AND_CONTINUE=YES to authorize the DNS repair and deployment continuation."

    command -v az >/dev/null 2>&1 || fail "Azure CLI is required."
    command -v gh >/dev/null 2>&1 || fail "GitHub CLI is required."
    command -v python3 >/dev/null 2>&1 || fail "Python 3 is required."

    gh auth status >/dev/null 2>&1 || fail "GitHub CLI is not authenticated."

    az account set --subscription "$SUBSCRIPTION_ID"
    CURRENT_SUBSCRIPTION="$(az account show --query id --output tsv)"
    echo "CURRENT_SUBSCRIPTION_MATCH=$([ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] && echo yes || echo no)"
    [ "$CURRENT_SUBSCRIPTION" = "$SUBSCRIPTION_ID" ] || fail "The active Azure subscription does not match."

    section "Inspecting ACR private endpoint"

    PE_STATE="$(az network private-endpoint show -g "$RG_WEST_NETWORK" -n "$ACR_PRIVATE_ENDPOINT" --query provisioningState -o tsv)"
    NIC_ID="$(az network private-endpoint show -g "$RG_WEST_NETWORK" -n "$ACR_PRIVATE_ENDPOINT" --query 'networkInterfaces[0].id' -o tsv)"
    CONNECTION_STATE="$(az network private-endpoint show -g "$RG_WEST_NETWORK" -n "$ACR_PRIVATE_ENDPOINT" --query 'privateLinkServiceConnections[0].privateLinkServiceConnectionState.status' -o tsv)"

    echo "ACR_PRIVATE_ENDPOINT_STATE=$PE_STATE"
    echo "ACR_PRIVATE_ENDPOINT_CONNECTION=$CONNECTION_STATE"
    echo "ACR_PRIVATE_ENDPOINT_NIC_ID=$NIC_ID"

    [ "$PE_STATE" = "Succeeded" ] || fail "The West ACR private endpoint is not ready."
    [ "$CONNECTION_STATE" = "Approved" ] || fail "The West ACR private endpoint connection is not approved."
    [ -n "$NIC_ID" ] || fail "The West ACR private endpoint NIC could not be resolved."

    az network nic show --ids "$NIC_ID" --output json > "$NIC_JSON"

    python3 - "$NIC_JSON" "$RECORDS_FILE" <<'PY'
import json
import sys
from pathlib import Path

source = Path(sys.argv[1])
target = Path(sys.argv[2])
data = json.loads(source.read_text(encoding="utf-8"))
records = {}

for config in data.get("ipConfigurations") or []:
    ip = str(config.get("privateIPAddress") or "").strip()
    props = config.get("privateLinkConnectionProperties") or {}
    member = str(props.get("requiredMemberName") or "").strip()
    fqdns = props.get("fqdns") or []

    if not ip or not fqdns:
        continue

    for fqdn in fqdns:
        fqdn = str(fqdn or "").strip().rstrip(".").lower()
        if not fqdn:
            continue

        if fqdn.endswith(".privatelink.azurecr.io"):
            name = fqdn[: -len(".privatelink.azurecr.io")]
        elif fqdn.endswith(".azurecr.io"):
            name = fqdn[: -len(".azurecr.io")]
        else:
            continue

        if name:
            records[name] = (ip, member, fqdn)

if not records:
    raise SystemExit("ERROR: No ACR private DNS records could be derived from the private endpoint NIC.")

with target.open("w", encoding="utf-8") as handle:
    for name in sorted(records):
        ip, member, fqdn = records[name]
        handle.write(f"{name}\t{ip}\t{member}\t{fqdn}\n")
PY

    RECORD_COUNT="$(wc -l < "$RECORDS_FILE" | tr -d ' ')"
    echo "DERIVED_ACR_PRIVATE_DNS_RECORD_COUNT=$RECORD_COUNT"
    [ "$RECORD_COUNT" -ge 2 ] || fail "Expected at least the registry and one regional data endpoint record."

    while IFS=$'\t' read -r record_name private_ip member_name original_fqdn; do
        echo "DERIVED_ACR_RECORD=$record_name -> $private_ip [$member_name]"
    done < "$RECORDS_FILE"

    section "Ensuring private DNS zone and West VNet link"

    ZONE_ID="$(az network private-dns zone show -g "$RG_SHARED" -n "$ACR_PRIVATE_DNS_ZONE" --query id -o tsv)"
    VNET_ID="$(az network vnet show -g "$RG_WEST_NETWORK" -n "$WEST_VNET" --query id -o tsv)"
    [ -n "$ZONE_ID" ] || fail "The ACR private DNS zone could not be resolved."
    [ -n "$VNET_ID" ] || fail "The West VNet could not be resolved."

    EXISTING_LINK="$(az network private-dns link vnet list -g "$RG_SHARED" -z "$ACR_PRIVATE_DNS_ZONE" --query "[?virtualNetwork.id=='$VNET_ID'].name | [0]" -o tsv)"

    if [ -z "$EXISTING_LINK" ]; then
        LINK_NAME="link-phd-test-westus3-acr"
        az network private-dns link vnet create \
            -g "$RG_SHARED" \
            -z "$ACR_PRIVATE_DNS_ZONE" \
            -n "$LINK_NAME" \
            -v "$VNET_ID" \
            -e false \
            --only-show-errors \
            --output none
        echo "WEST_ACR_PRIVATE_DNS_VNET_LINK=$LINK_NAME"
        echo "WEST_ACR_PRIVATE_DNS_VNET_LINK_ACTION=created"
    else
        LINK_NAME="$EXISTING_LINK"
        echo "WEST_ACR_PRIVATE_DNS_VNET_LINK=$LINK_NAME"
        echo "WEST_ACR_PRIVATE_DNS_VNET_LINK_ACTION=existing"
    fi

    section "Repairing ACR private DNS A records"

    while IFS=$'\t' read -r record_name private_ip member_name original_fqdn; do
        EXISTING_IPS="$(az network private-dns record-set a show -g "$RG_SHARED" -z "$ACR_PRIVATE_DNS_ZONE" -n "$record_name" --query 'aRecords[].ipv4Address' -o tsv 2>/dev/null || true)"

        if [ "$EXISTING_IPS" = "$private_ip" ]; then
            echo "ACR_PRIVATE_DNS_RECORD[$record_name]=existing:$private_ip"
            continue
        fi

        if [ -n "$EXISTING_IPS" ]; then
            az network private-dns record-set a delete \
                -g "$RG_SHARED" \
                -z "$ACR_PRIVATE_DNS_ZONE" \
                -n "$record_name" \
                --yes \
                --only-show-errors \
                --output none
        fi

        az network private-dns record-set a create \
            -g "$RG_SHARED" \
            -z "$ACR_PRIVATE_DNS_ZONE" \
            -n "$record_name" \
            --ttl 10 \
            --only-show-errors \
            --output none

        az network private-dns record-set a add-record \
            -g "$RG_SHARED" \
            -z "$ACR_PRIVATE_DNS_ZONE" \
            -n "$record_name" \
            -a "$private_ip" \
            --only-show-errors \
            --output none

        echo "ACR_PRIVATE_DNS_RECORD[$record_name]=repaired:$private_ip"
    done < "$RECORDS_FILE"

    section "Validating repaired ACR private DNS records"

    VALIDATED_COUNT=0
    while IFS=$'\t' read -r record_name private_ip member_name original_fqdn; do
        CURRENT_IPS="$(az network private-dns record-set a show -g "$RG_SHARED" -z "$ACR_PRIVATE_DNS_ZONE" -n "$record_name" --query 'aRecords[].ipv4Address' -o tsv)"
        echo "VALIDATED_ACR_PRIVATE_DNS_RECORD[$record_name]=$CURRENT_IPS"
        [ "$CURRENT_IPS" = "$private_ip" ] || fail "Private DNS validation failed for $record_name."
        VALIDATED_COUNT=$((VALIDATED_COUNT + 1))
    done < "$RECORDS_FILE"

    echo "VALIDATED_ACR_PRIVATE_DNS_RECORD_COUNT=$VALIDATED_COUNT"
    echo "ACR_PRIVATE_DNS_REPAIR_RESULT=READY"

    cat > "$STATE_FILE" <<EOF
ACR_NAME=$ACR_NAME
ACR_PRIVATE_ENDPOINT=$ACR_PRIVATE_ENDPOINT
ACR_PRIVATE_DNS_ZONE=$ACR_PRIVATE_DNS_ZONE
WEST_VNET=$WEST_VNET
REPAIRED_RECORD_COUNT=$VALIDATED_COUNT
ACR_PRIVATE_DNS_REPAIR_RESULT=READY
REPAIRED_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "Waiting 90 seconds for private DNS propagation and resolver cache expiration..."
    sleep 90

    section "Downloading and starting the existing-image deployment continuation"

    gh api \
        -H "Accept: application/vnd.github.raw+json" \
        "repos/${REPOSITORY}/contents/${CONTINUATION_PATH}?ref=${MIGRATION_BRANCH}" \
        > "$CONTINUATION_SCRIPT"

    [ -s "$CONTINUATION_SCRIPT" ] || fail "The AZ-08C continuation script download failed."
    chmod +x "$CONTINUATION_SCRIPT"
    bash -n "$CONTINUATION_SCRIPT"

    echo "CONTINUATION_SCRIPT_SYNTAX=passed"
    echo "NEXT_ACTION=CONTINUE_WITH_EXISTING_IMAGES"

    PHD_CONTINUE_WEST_DEPLOYMENT=YES bash "$CONTINUATION_SCRIPT"

} 2>&1 | tee "$LOG"

echo
echo "Execution log: $LOG"
