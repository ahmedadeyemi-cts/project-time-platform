#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/common.sh
source "${SCRIPT_DIR}/lib/common.sh"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="${LOG_DIR}/dev04-postgresql-flexible-server-${STAMP}.log"

# Firewall rule name for the "allow Azure services" sentinel rule
# (start = end = 0.0.0.0). See the long comment below the environment check
# for why this, and not a rule scoped to the ACA environment's own outbound
# IP, is what this script actually creates.
FIREWALL_RULE_NAME="allow-azure-services-aca-egress"

validate_server_json() {
    local json_file="$1"
    python3 - "$json_file" <<'PY'
import json
import sys
from pathlib import Path

server = json.loads(Path(sys.argv[1]).read_text())
errors = []

state = str(server.get("state", ""))
version = str(server.get("version", ""))
tier = str((server.get("sku") or {}).get("tier", ""))
sku_name = str((server.get("sku") or {}).get("name", ""))
storage = server.get("storage") or {}
network = server.get("network") or {}

if state.lower() != "ready":
    errors.append(f"server state is {state!r}, expected 'Ready'")
if not version.startswith("16"):
    errors.append(f"PostgreSQL version is {version!r}, expected version 16")
if tier != "Burstable":
    errors.append(f"tier is {tier!r}, expected 'Burstable'")
if sku_name != "Standard_B1ms":
    errors.append(f"SKU is {sku_name!r}, expected 'Standard_B1ms'")
if int(storage.get("storageSizeGb") or 0) < 32:
    errors.append("storage is smaller than 32 GiB")
if str(network.get("publicNetworkAccess", "")).lower() != "enabled":
    errors.append("public network access is not enabled")

if errors:
    for item in errors:
        print(f"ERROR: {item}", file=sys.stderr)
    raise SystemExit(1)

print("PostgreSQL DEV server validation passed.")
PY
}

{
    section "DEV-04 - Pulse DEV PostgreSQL Flexible Server (Burstable B1ms)"
    echo "TIME=$(date -u -Is)"
    echo "Resource group: $RG_DEV"
    echo "Server: $POSTGRES_SERVER"
    echo "Database: $POSTGRES_DATABASE"

    require_subscription
    echo "Subscription confirmed: $SUBSCRIPTION_ID"

    az group show --name "$RG_DEV" --output none \
        || fail "Resource group $RG_DEV does not exist. Run dev01-resource-group.sh first."

    provider_ready Microsoft.DBforPostgreSQL

    section "Server"

    if az postgres flexible-server show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --output none >/dev/null 2>&1; then
        echo "Existing server confirmed: $POSTGRES_SERVER"
        SERVER_JSON="$(mktemp)"
        trap 'rm -f "$SERVER_JSON"' EXIT
        az postgres flexible-server show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --output json > "$SERVER_JSON"
        validate_server_json "$SERVER_JSON"
    else
        section "Preparing the administrator password"

        # No Key Vault exists in this DEV footprint (ADR 0001 scopes DEV to
        # exactly RG + ACA env + Postgres + ACR, "nothing else"). Until a
        # later stage adds a secret store, the generated password is kept
        # only in this local, chmod 600, gitignored-by-convention file
        # (matches deployment/azure/README.md: "generated files are
        # deliberately outside the repository"). It is never printed to
        # stdout/the log.
        if [ -f "$DEV_POSTGRES_PASSWORD_FILE" ]; then
            fail "Server $POSTGRES_SERVER does not exist in Azure yet, but $DEV_POSTGRES_PASSWORD_FILE already does. Remove the stale password file only if you are certain no server was ever created with it, then re-run."
        fi

        ADMIN_PASSWORD="$(python3 - <<'PY'
import secrets
import string

alphabet = string.ascii_letters + string.digits + "!@#%*-_=+"
while True:
    value = "".join(secrets.choice(alphabet) for _ in range(40))
    if (
        any(c.islower() for c in value)
        and any(c.isupper() for c in value)
        and any(c.isdigit() for c in value)
        and any(c in "!@#%*-_=+" for c in value)
    ):
        print(value)
        break
PY
)"
        printf '%s' "$ADMIN_PASSWORD" > "$DEV_POSTGRES_PASSWORD_FILE"
        chmod 600 "$DEV_POSTGRES_PASSWORD_FILE"
        echo "Generated administrator password and stored it at: $DEV_POSTGRES_PASSWORD_FILE"

        section "Creating PostgreSQL 16 Burstable B1ms"

        # --public-access None: turns on public-access networking mode
        # without Azure auto-creating a firewall rule for us. The firewall
        # rule itself is added explicitly and idempotently below, so its
        # name and scope are auditable in this script rather than being an
        # implicit side effect of server creation.
        az postgres flexible-server create \
            --resource-group "$RG_DEV" \
            --name "$POSTGRES_SERVER" \
            --location "$LOCATION" \
            --admin-user "$POSTGRES_ADMIN_USER" \
            --admin-password "$ADMIN_PASSWORD" \
            --database-name "$POSTGRES_DATABASE" \
            --version "$POSTGRES_VERSION" \
            --tier Burstable \
            --sku-name "$POSTGRES_SKU" \
            --storage-type Premium_LRS \
            --storage-size "$POSTGRES_STORAGE_GIB" \
            --storage-auto-grow Disabled \
            --backup-retention 7 \
            --geo-redundant-backup Disabled \
            --high-availability Disabled \
            --public-access None \
            --tags \
                "application=$PRODUCT_NAME" \
                "environment=$ENVIRONMENT" \
                "resource-function=postgresql" \
                "architecture=single-region-scale-to-zero" \
            --yes \
            --only-show-errors \
            --output none

        unset ADMIN_PASSWORD

        echo "Created server: $POSTGRES_SERVER"

        az postgres flexible-server wait \
            --resource-group "$RG_DEV" \
            --name "$POSTGRES_SERVER" \
            --custom "state=='Ready'" \
            --interval 30 \
            --timeout 3600

        SERVER_JSON="$(mktemp)"
        trap 'rm -f "$SERVER_JSON"' EXIT
        az postgres flexible-server show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --output json > "$SERVER_JSON"
        validate_server_json "$SERVER_JSON"
    fi

    section "Firewall rule (known limitation - read before changing)"

    # ADR 0001 and the stage spec both want a firewall rule scoped to the
    # ACA environment's own outbound IP(s) rather than 0.0.0.0/0. That is
    # not achievable as written: per Microsoft's own documentation
    # (learn.microsoft.com/azure/container-apps/networking#ports-and-ip-addresses)
    # a Consumption-plan Container Apps environment with no VNet has *no*
    # fixed, per-environment outbound IP - "Outbound IPs might change over
    # time," and a dedicated static egress IP is only available in a
    # workload-profile environment with a customer-provided VNet + NAT
    # Gateway (learn.microsoft.com/azure/container-apps/custom-virtual-networks#azure-nat-gateway-integration).
    # ADR 0001 explicitly rejected VNet/NAT Gateway for DEV on cost grounds,
    # so there is no IP to scope this rule to without contradicting that
    # decision.
    #
    # The practical, Microsoft-documented alternative for exactly this
    # combination (Consumption ACA + PostgreSQL Flexible Server, no VNet)
    # is the special "allow all Azure services and resources within Azure"
    # firewall rule, created by setting both the start and end IP to
    # 0.0.0.0 (see learn.microsoft.com/azure/postgresql/security/security-firewall-rules#connect-from-azure
    # and the ACA+Postgres tutorial at learn.microsoft.com/azure/developer/python/tutorial-deploy-python-web-app-azure-container-apps-02).
    # This is materially broader than "just this ACA environment" - it
    # allows any Azure-hosted resource in any subscription to attempt a
    # connection, though the server still requires a valid password. It is
    # NOT the open-internet "0.0.0.0/0" CIDR the stage spec says to avoid.
    #
    # Flagging this as a deviation for human review rather than silently
    # reinterpreting the requirement: tightening this further requires
    # either accepting this Azure-services-wide rule as a known,
    # already-accepted DEV-only risk (ADR 0001's own "Consequences" section
    # already accepts "public DB access... firewall rules must be kept
    # tight"), or reopening ADR 0001 to add a workload-profile VNet + NAT
    # Gateway for a real static egress IP (the cost/complexity ADR 0001
    # rejected). See README.md in this directory for the operator-facing
    # version of this note.

    if az postgres flexible-server firewall-rule show \
        --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --rule-name "$FIREWALL_RULE_NAME" \
        --output none >/dev/null 2>&1; then
        EXISTING_START="$(az postgres flexible-server firewall-rule show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --rule-name "$FIREWALL_RULE_NAME" --query startIpAddress --output tsv)"
        EXISTING_END="$(az postgres flexible-server firewall-rule show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --rule-name "$FIREWALL_RULE_NAME" --query endIpAddress --output tsv)"
        if [ "$EXISTING_START" = "0.0.0.0" ] && [ "$EXISTING_END" = "0.0.0.0" ]; then
            echo "Confirmed firewall rule: $FIREWALL_RULE_NAME (0.0.0.0-0.0.0.0, Azure services)"
        else
            fail "Firewall rule $FIREWALL_RULE_NAME exists with unexpected range $EXISTING_START-$EXISTING_END"
        fi
    else
        az postgres flexible-server firewall-rule create \
            --resource-group "$RG_DEV" \
            --name "$POSTGRES_SERVER" \
            --rule-name "$FIREWALL_RULE_NAME" \
            --start-ip-address 0.0.0.0 \
            --end-ip-address 0.0.0.0 \
            --output none
        echo "Created firewall rule: $FIREWALL_RULE_NAME (0.0.0.0-0.0.0.0, Azure services)"
    fi

    POSTGRES_FQDN="$(az postgres flexible-server show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" --query fullyQualifiedDomainName --output tsv)"

    record_config POSTGRES_SERVER "$POSTGRES_SERVER"
    record_config POSTGRES_FQDN "$POSTGRES_FQDN"
    record_config POSTGRES_DATABASE "$POSTGRES_DATABASE"
    record_config POSTGRES_ADMIN_USER "$POSTGRES_ADMIN_USER"
    record_config POSTGRES_PORT "5432"
    record_config POSTGRES_ADMIN_PASSWORD_FILE "$DEV_POSTGRES_PASSWORD_FILE"

    section "Validation"

    az postgres flexible-server show --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" \
        --query '{name:name,fqdn:fullyQualifiedDomainName,location:location,state:state,version:version,tier:sku.tier,sku:sku.name,storageGiB:storage.storageSizeGb,publicAccess:network.publicNetworkAccess}' \
        --output table

    az postgres flexible-server firewall-rule list --resource-group "$RG_DEV" --name "$POSTGRES_SERVER" \
        --query '[].{name:name,start:startIpAddress,end:endIpAddress}' \
        --output table

    section "DEV-04 complete"
    echo "POSTGRESQL FLEXIBLE SERVER READY"
} 2>&1 | tee "$LOG"

echo
echo "Log: $LOG"
echo "Config: $DEV_CONFIG_FILE"
echo "Admin password file (keep this out of git; move to Key Vault before real data): $DEV_POSTGRES_PASSWORD_FILE"
