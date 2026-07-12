#!/usr/bin/env bash
set -Eeuo pipefail

LOCATION="eastus"
EXPECTED_SKU="Standard_D2ds_v4"
EXPECTED_TIER="GeneralPurpose"
EXPECTED_VERSION="16"
EXPECTED_STORAGE_GIB="128"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RAW_JSON="$LOG_DIR/az05c3a1-eastus-postgresql-skus-$STAMP.json"
LOG="$LOG_DIR/az05c3a1-eastus-postgresql-replica-sku-diagnostic-$STAMP.log"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-05C3A1 - East US PostgreSQL Replica SKU Diagnostic"

    echo "TIME=$(date -u -Is)"
    echo "LOCATION=$LOCATION"
    echo "EXPECTED_SKU=$EXPECTED_SKU"
    echo "EXPECTED_TIER=$EXPECTED_TIER"
    echo "EXPECTED_VERSION=$EXPECTED_VERSION"
    echo "EXPECTED_STORAGE_GIB=$EXPECTED_STORAGE_GIB"
    echo "READ_ONLY_DIAGNOSTIC=true"
    echo "BILLABLE_REPLICA_CREATED=false"

    section "Capturing current East US PostgreSQL SKU metadata"

    az postgres flexible-server list-skus \
        --location "$LOCATION" \
        --output json > "$RAW_JSON"

    [ -s "$RAW_JSON" ] || {
        echo "ERROR: East US PostgreSQL SKU response is empty."
        exit 1
    }

    echo "RAW_SKU_METADATA=$RAW_JSON"
    echo "RAW_SKU_METADATA_BYTES=$(stat -c '%s' "$RAW_JSON")"

    section "Analyzing response schema and normalized compatibility"

    python3 - \
        "$RAW_JSON" \
        "$EXPECTED_SKU" \
        "$EXPECTED_TIER" \
        "$EXPECTED_VERSION" \
        "$EXPECTED_STORAGE_GIB" <<'PY'
import json
import re
import sys
from pathlib import Path

path = Path(sys.argv[1])
expected_sku = sys.argv[2]
expected_tier = sys.argv[3]
expected_version = int(sys.argv[4])
expected_storage = int(sys.argv[5])

document = json.loads(path.read_text(encoding="utf-8"))


def normalize_sku(value):
    text = str(value or "").strip()
    if not text:
        return ""
    lowered = text.lower()
    if lowered.startswith("standard_"):
        return lowered
    if re.match(r"^[a-z]\d", lowered):
        return "standard_" + lowered
    return lowered


def blocked(value):
    return str(value or "").strip().lower() in {
        "disabled",
        "unavailable",
        "restricted",
        "notavailable",
        "not_available",
    }


def scalar_walk(value, current="$"):
    if isinstance(value, dict):
        for key, child in value.items():
            yield from scalar_walk(child, f"{current}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from scalar_walk(child, f"{current}[{index}]")
    else:
        yield current, value


roots = document if isinstance(document, list) else [document]
roots = [item for item in roots if isinstance(item, dict)]

print(f"SKU_RESPONSE_TOP_LEVEL_TYPE={type(document).__name__}")
print(f"SKU_RESPONSE_ROOT_COUNT={len(roots)}")
if roots:
    print("SKU_RESPONSE_ROOT_KEYS=" + ",".join(sorted(roots[0].keys())))

expected_normalized = normalize_sku(expected_sku)
case_insensitive_occurrences = 0
normalized_occurrences = 0
candidate_paths = []
all_sku_candidates = set()

for scalar_path, value in scalar_walk(document):
    if not isinstance(value, str):
        continue
    text = value.strip()
    if text.lower() == expected_sku.lower():
        case_insensitive_occurrences += 1
    normalized = normalize_sku(text)
    if normalized == expected_normalized:
        normalized_occurrences += 1
        candidate_paths.append((scalar_path, text))
    if re.match(r"^(?:standard_)?d\d", text, re.I):
        all_sku_candidates.add(text)

fast_matches = []
standard_matches = []
fast_candidate_rows = []
standard_candidate_rows = []

for root_index, root in enumerate(roots):
    for entry_index, entry in enumerate(root.get("supportedFastProvisioningEditions") or []):
        if not isinstance(entry, dict):
            continue
        sku = entry.get("supportedSku")
        tier = entry.get("supportedTier")
        version = entry.get("supportedServerVersions")
        storage = entry.get("supportedStorageGb")
        status = entry.get("status")
        row = {
            "root": root_index,
            "index": entry_index,
            "sku": sku,
            "tier": tier,
            "version": version,
            "storage": storage,
            "status": status,
        }
        if re.match(r"^(?:standard_)?d2", str(sku or ""), re.I):
            fast_candidate_rows.append(row)
        try:
            version_ok = int(version) == expected_version
        except (TypeError, ValueError):
            version_ok = False
        try:
            storage_ok = int(storage) >= expected_storage
        except (TypeError, ValueError):
            storage_ok = False
        if (
            normalize_sku(sku) == expected_normalized
            and str(tier or "").lower() == expected_tier.lower()
            and version_ok
            and storage_ok
            and not blocked(status)
        ):
            fast_matches.append(row)

    for edition_index, edition in enumerate(root.get("supportedServerEditions") or []):
        if not isinstance(edition, dict):
            continue
        edition_name = edition.get("name")
        for sku_index, sku_entry in enumerate(edition.get("supportedServerSkus") or []):
            if not isinstance(sku_entry, dict):
                continue
            sku = sku_entry.get("name")
            status = sku_entry.get("status")
            row = {
                "root": root_index,
                "edition_index": edition_index,
                "sku_index": sku_index,
                "sku": sku,
                "tier": edition_name,
                "vcores": sku_entry.get("vCores"),
                "status": status,
                "ha_modes": sku_entry.get("supportedHaMode") or [],
            }
            if re.match(r"^(?:standard_)?d2", str(sku or ""), re.I):
                standard_candidate_rows.append(row)
            if (
                normalize_sku(sku) == expected_normalized
                and str(edition_name or "").lower() == expected_tier.lower()
                and not blocked(status)
            ):
                standard_matches.append(row)

print(f"EXPECTED_SKU_CASE_INSENSITIVE_OCCURRENCES={case_insensitive_occurrences}")
print(f"EXPECTED_SKU_NORMALIZED_OCCURRENCES={normalized_occurrences}")
print(f"FAST_PROVISIONING_MATCH_COUNT={len(fast_matches)}")
print(f"STANDARD_EDITION_MATCH_COUNT={len(standard_matches)}")

print("EXPECTED_SKU_MATCH_PATHS_BEGIN")
for scalar_path, value in candidate_paths:
    print(f"{scalar_path}={value}")
print("EXPECTED_SKU_MATCH_PATHS_END")

print("FAST_PROVISIONING_D2_CANDIDATES_BEGIN")
for row in fast_candidate_rows:
    print(
        "sku={sku};tier={tier};version={version};storageGiB={storage};status={status}".format(
            **row
        )
    )
print("FAST_PROVISIONING_D2_CANDIDATES_END")

print("STANDARD_EDITION_D2_CANDIDATES_BEGIN")
for row in standard_candidate_rows:
    print(
        "sku={sku};tier={tier};vcores={vcores};status={status};haModes={ha}".format(
            sku=row["sku"],
            tier=row["tier"],
            vcores=row["vcores"],
            status=row["status"],
            ha=",".join(str(item) for item in row["ha_modes"]),
        )
    )
print("STANDARD_EDITION_D2_CANDIDATES_END")

print("ALL_D_FAMILY_SKU_STRINGS_BEGIN")
for value in sorted(all_sku_candidates, key=str.lower):
    print(value)
print("ALL_D_FAMILY_SKU_STRINGS_END")

if len(fast_matches) + len(standard_matches) > 0:
    print("CURRENT_EASTUS_SKU_DECISION=EXPECTED_CONFIGURATION_ADVERTISED")
    print("REPLICA_SKU_DIAGNOSTIC_RESULT=PASSED")
else:
    print("CURRENT_EASTUS_SKU_DECISION=EXPECTED_CONFIGURATION_NOT_ADVERTISED")
    print("REPLICA_SKU_DIAGNOSTIC_RESULT=REVIEW_REQUIRED")
PY

    section "AZ-05C3A1 complete"

    echo "No Azure resource was created or modified."
    echo "The replica remains uncreated."
    echo
    echo "************************************************************"
    echo "EASTUS POSTGRESQL SKU DIAGNOSTIC COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "SKU diagnostic log: $LOG"
echo "Raw SKU metadata: $RAW_JSON"
