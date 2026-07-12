#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
API_VERSION="2025-09-01"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="$LOG_DIR/az05c2a4-eastus-quota-rest-preflight-$STAMP.log"
WORK_DIR="$(mktemp -d)"

mkdir -p "$LOG_DIR"
trap 'rm -rf "$WORK_DIR"' EXIT

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fetch_paged() {
    local uri="$1"
    local output="$2"
    local page next_link

    printf '{"value":[]}\n' > "$output"

    while [ -n "$uri" ]; do
        page="$(mktemp)"

        az rest \
            --method get \
            --uri "$uri" \
            --output json > "$page"

        python3 - "$output" "$page" <<'PY'
import json
import sys
from pathlib import Path

output_path, page_path = map(Path, sys.argv[1:])
combined = json.loads(output_path.read_text())
page = json.loads(page_path.read_text())
combined.setdefault("value", []).extend(page.get("value") or [])
output_path.write_text(json.dumps(combined))
PY

        next_link="$(python3 - "$page" <<'PY'
import json
import sys
from pathlib import Path

page = json.loads(Path(sys.argv[1]).read_text())
print(page.get("nextLink") or "")
PY
)"

        rm -f "$page"
        uri="$next_link"
    done
}

{
    section "AZ-05C2A4 - East US Direct Compute Quota REST Preflight"

    SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
    SUBSCRIPTION_NAME="$(az account show --query name --output tsv)"
    SCOPE="/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Compute/locations/$LOCATION"

    echo "TIME=$(date -u -Is)"
    echo "Subscription: $SUBSCRIPTION_NAME"
    echo "Subscription ID: $SUBSCRIPTION_ID"
    echo "Location: $LOCATION"
    echo "Quota scope: $SCOPE"
    echo "This script is read-only and creates no Azure resource."

    section "Collecting East US VM SKUs"

    az vm list-skus \
        --location "$LOCATION" \
        --resource-type virtualMachines \
        --all \
        --output json > "$WORK_DIR/skus.json"

    echo "SKU records: $(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1]))))' "$WORK_DIR/skus.json")"

    section "Collecting Microsoft Quota limits"

    QUOTA_URI="https://management.azure.com${SCOPE}/providers/Microsoft.Quota/quotas?api-version=${API_VERSION}"
    fetch_paged "$QUOTA_URI" "$WORK_DIR/quotas.json"

    echo "Quota records: $(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1])).get("value", [])))' "$WORK_DIR/quotas.json")"

    section "Collecting Microsoft Quota usage"

    USAGE_URI="https://management.azure.com${SCOPE}/providers/Microsoft.Quota/usages?api-version=${API_VERSION}"
    fetch_paged "$USAGE_URI" "$WORK_DIR/usages.json"

    echo "Usage records: $(python3 -c 'import json,sys; print(len(json.load(open(sys.argv[1])).get("value", [])))' "$WORK_DIR/usages.json")"

    section "Joining available East US VM sizes to direct quota records"

    python3 - \
        "$WORK_DIR/skus.json" \
        "$WORK_DIR/quotas.json" \
        "$WORK_DIR/usages.json" <<'PY'
import json
import re
import sys
from pathlib import Path

sku_path, quota_path, usage_path = map(Path, sys.argv[1:])
skus = json.loads(sku_path.read_text())
quotas = json.loads(quota_path.read_text()).get("value") or []
usages = json.loads(usage_path.read_text()).get("value") or []


def record_name(item):
    props = item.get("properties") or {}
    prop_name = props.get("name") or {}
    return str(
        item.get("name")
        or prop_name.get("value")
        or prop_name.get("localizedValue")
        or ""
    )


def quota_limit(item):
    props = item.get("properties") or {}
    limit = props.get("limit") or {}
    value = limit.get("value")
    return int(value) if value is not None else None


def usage_value(item):
    props = item.get("properties") or {}
    usage = props.get("usages") or {}
    value = usage.get("value")
    return int(value) if value is not None else None


def cap(item, name):
    for entry in item.get("capabilities") or []:
        if entry.get("name") == name:
            return str(entry.get("value") or "")
    return ""


def restricted(item):
    for restriction in item.get("restrictions") or []:
        reason = str(restriction.get("reasonCode") or "")
        restriction_type = str(restriction.get("type") or "")
        if reason == "NotAvailableForSubscription":
            return True
        if restriction_type.lower() == "location":
            return True
    return False

quota_by_name = {record_name(item).lower(): item for item in quotas if record_name(item)}
usage_by_name = {record_name(item).lower(): item for item in usages if record_name(item)}

regional_names = [
    "standardCores",
    "cores",
    "Total Regional vCPUs",
]

print("Direct regional quota records:")
regional_found = False
for candidate in regional_names:
    key = candidate.lower()
    q = quota_by_name.get(key)
    u = usage_by_name.get(key)
    if q or u:
        regional_found = True
        print(
            f"  {candidate}: limit={quota_limit(q) if q else 'missing'} "
            f"usage={usage_value(u) if u else 'missing'}"
        )
if not regional_found:
    for key, item in sorted(quota_by_name.items()):
        if "core" in key and "family" not in key:
            u = usage_by_name.get(key)
            print(
                f"  {record_name(item)}: limit={quota_limit(item)} "
                f"usage={usage_value(u) if u else 'missing'}"
            )

rows = []
for item in skus:
    size = str(item.get("name") or "")
    family = str(item.get("family") or "")

    if restricted(item):
        continue
    if not re.match(r"^Standard_[DEF]", size):
        continue
    if size.startswith(("Standard_DC", "Standard_EC")):
        continue
    if "Promo" in size:
        continue

    architecture = cap(item, "CpuArchitectureType") or "unknown"
    if architecture.lower() not in {"x64", "x86_64", "amd64", "unknown"}:
        continue

    generations = cap(item, "HyperVGenerations")
    if generations and "V2" not in generations:
        continue

    try:
        vcpus = int(float(cap(item, "vCPUs") or 0))
        memory = float(cap(item, "MemoryGB") or 0)
    except ValueError:
        continue

    if not 2 <= vcpus <= 8:
        continue
    if memory < 4:
        continue

    version_match = re.search(r"_v(\d+)$", size)
    version = int(version_match.group(1)) if version_match else 0
    if version and version < 4:
        continue

    key = family.lower()
    quota_item = quota_by_name.get(key)
    usage_item = usage_by_name.get(key)
    limit = quota_limit(quota_item) if quota_item else None
    used = usage_value(usage_item) if usage_item else None

    if limit is None:
        status = "quota-limit-missing"
        remaining = None
    elif used is None:
        status = "quota-usage-missing"
        remaining = None
    else:
        remaining = limit - used
        status = "quota-ok" if remaining >= vcpus else "quota-blocked"

    rows.append(
        {
            "size": size,
            "family": family,
            "vcpus": vcpus,
            "memory": memory,
            "arch": architecture,
            "gen": generations or "unknown",
            "used": used,
            "limit": limit,
            "remaining": remaining,
            "status": status,
        }
    )

print()
print(
    f"{'Size':38} {'Family':28} {'vCPU':>5} {'GB':>7} "
    f"{'Used':>7} {'Limit':>7} {'Remain':>8} {'Status':>22}"
)
print("-" * 135)
for row in sorted(
    rows,
    key=lambda r: (
        r["status"] != "quota-ok",
        r["vcpus"],
        r["memory"],
        r["size"],
    ),
):
    used = "-" if row["used"] is None else str(row["used"])
    limit = "-" if row["limit"] is None else str(row["limit"])
    remaining = "-" if row["remaining"] is None else str(row["remaining"])
    print(
        f"{row['size']:38} {row['family']:28} {row['vcpus']:>5} "
        f"{row['memory']:>7.1f} {used:>7} {limit:>7} {remaining:>8} "
        f"{row['status']:>22}"
    )

eligible = [row for row in rows if row["status"] == "quota-ok"]

preferred = [
    "Standard_D2alds_v7",
    "Standard_D2lds_v7",
    "Standard_D2ads_v7",
    "Standard_D2ds_v7",
    "Standard_D2als_v7",
    "Standard_D2ls_v7",
    "Standard_D2as_v7",
    "Standard_D2s_v7",
    "Standard_F2alds_v7",
    "Standard_F2ads_v7",
    "Standard_E2ads_v7",
    "Standard_E2ds_v7",
]

selected = None
for candidate in preferred:
    selected = next((row for row in eligible if row["size"] == candidate), None)
    if selected:
        break

if not selected and eligible:
    selected = sorted(
        eligible,
        key=lambda r: (r["vcpus"], r["memory"], r["size"]),
    )[0]

print()
if selected:
    print("QUOTA_DECISION=DEPLOYMENT_ALLOWED")
    print(f"RECOMMENDED_VM_SIZE={selected['size']}")
    print(f"RECOMMENDED_VM_FAMILY={selected['family']}")
    print(f"RECOMMENDED_VM_VCPUS={selected['vcpus']}")
    print(f"RECOMMENDED_VM_MEMORY_GB={selected['memory']:.1f}")
    print(f"RECOMMENDED_VM_FAMILY_LIMIT={selected['limit']}")
    print(f"RECOMMENDED_VM_FAMILY_USAGE={selected['used']}")
    print(f"RECOMMENDED_VM_FAMILY_REMAINING={selected['remaining']}")
else:
    request_candidates = [
        row for row in rows
        if row["vcpus"] == 2 and row["memory"] <= 8
    ]
    request_candidate = None
    for candidate in preferred:
        request_candidate = next(
            (row for row in request_candidates if row["size"] == candidate),
            None,
        )
        if request_candidate:
            break
    if not request_candidate and request_candidates:
        request_candidate = sorted(
            request_candidates,
            key=lambda r: (r["memory"], r["size"]),
        )[0]

    print("QUOTA_DECISION=QUOTA_REQUEST_REQUIRED")
    print("RECOMMENDED_VM_SIZE=none")

    if request_candidate:
        used = request_candidate["used"] or 0
        minimum_limit = used + request_candidate["vcpus"]
        print(f"QUOTA_REQUEST_VM_SIZE={request_candidate['size']}")
        print(f"QUOTA_REQUEST_FAMILY={request_candidate['family']}")
        print(f"QUOTA_REQUEST_CURRENT_LIMIT={request_candidate['limit'] if request_candidate['limit'] is not None else 'missing'}")
        print(f"QUOTA_REQUEST_CURRENT_USAGE={request_candidate['used'] if request_candidate['used'] is not None else 'missing'}")
        print(f"QUOTA_REQUEST_MINIMUM_LIMIT={minimum_limit}")
    else:
        print("QUOTA_REQUEST_VM_SIZE=none")
        print("QUOTA_REQUEST_FAMILY=none")
        print("QUOTA_REQUEST_MINIMUM_LIMIT=unknown")
PY

    section "Preflight result"

    echo "No Azure resource was created or changed."
    echo
    echo "************************************************************"
    echo "EASTUS DIRECT COMPUTE QUOTA PREFLIGHT COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

RC="${PIPESTATUS[0]}"

echo
echo "Preflight log: $LOG"

exit "$RC"
