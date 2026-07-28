#!/usr/bin/env bash
set -Eeuo pipefail

PRIMARY_LOCATION="westus3"
SECONDARY_LOCATION="eastus"

BASE_DIR="$HOME/project-health-dashboard-azure"
LOG_DIR="$BASE_DIR/logs"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"

WEST_JSON="$LOG_DIR/az05b2a-skus-westus3-$STAMP.json"
EAST_JSON="$LOG_DIR/az05b2a-skus-eastus-$STAMP.json"
SUMMARY="$LOG_DIR/az05b2a-sku-schema-summary-$STAMP.txt"

mkdir -p "$LOG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

section "AZ-05B2A - PostgreSQL SKU schema discovery"

echo "This phase is read-only."
echo "No PostgreSQL server, database, network, secret, or DNS resource will be changed."
echo "TIME=$(date -u -Is)"

section "Capturing raw West US 3 SKU response"
az postgres flexible-server list-skus \
    --location "$PRIMARY_LOCATION" \
    --output json > "$WEST_JSON"

echo "Saved: $WEST_JSON"

section "Capturing raw East US SKU response"
az postgres flexible-server list-skus \
    --location "$SECONDARY_LOCATION" \
    --output json > "$EAST_JSON"

echo "Saved: $EAST_JSON"

section "Azure CLI table output - West US 3"
az postgres flexible-server list-skus \
    --location "$PRIMARY_LOCATION" \
    --output table || true

section "Azure CLI table output - East US"
az postgres flexible-server list-skus \
    --location "$SECONDARY_LOCATION" \
    --output table || true

section "Analyzing actual JSON schema"

python3 - "$WEST_JSON" "$EAST_JSON" "$SUMMARY" <<'PY'
import json
import re
import sys
from collections import Counter
from pathlib import Path

west_path = Path(sys.argv[1])
east_path = Path(sys.argv[2])
summary_path = Path(sys.argv[3])

west = json.loads(west_path.read_text())
east = json.loads(east_path.read_text())

SKU_RE = re.compile(r"^(?:Standard_)?[A-Za-z][A-Za-z0-9_]*$")
D2_RE = re.compile(r"^(?:Standard_)?D2[A-Za-z0-9_]*$", re.I)
RELEVANT_KEY_RE = re.compile(
    r"sku|name|tier|vcore|core|cpu|family|edition|availability|zone|ha",
    re.I,
)


def walk(value, path="$"):
    if isinstance(value, dict):
        for key, child in value.items():
            yield from walk(child, f"{path}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from walk(child, f"{path}[{index}]")
    else:
        yield path, value


def dict_key_shapes(value, counter):
    if isinstance(value, dict):
        counter[tuple(sorted(value.keys()))] += 1
        for child in value.values():
            dict_key_shapes(child, counter)
    elif isinstance(value, list):
        for child in value:
            dict_key_shapes(child, counter)


def candidate_strings(value):
    result = set()
    for _, scalar in walk(value):
        if not isinstance(scalar, str):
            continue
        text = scalar.strip()
        if not text:
            continue
        if text.startswith("Standard_"):
            result.add(text)
        elif D2_RE.fullmatch(text):
            result.add(text if text.startswith("Standard_") else "Standard_" + text)
    return result


def all_name_like_strings(value):
    result = set()
    for path, scalar in walk(value):
        if isinstance(scalar, str) and RELEVANT_KEY_RE.search(path):
            text = scalar.strip()
            if text and len(text) <= 120:
                result.add((path, text))
    return sorted(result)


def describe(label, value):
    lines = []
    lines.append(f"## {label}")
    lines.append(f"Top-level type: {type(value).__name__}")
    if isinstance(value, list):
        lines.append(f"Top-level length: {len(value)}")
    elif isinstance(value, dict):
        lines.append(f"Top-level keys: {sorted(value.keys())}")

    shapes = Counter()
    dict_key_shapes(value, shapes)
    lines.append("Most common dictionary key shapes:")
    for keys, count in shapes.most_common(20):
        lines.append(f"  {count:>4} x {list(keys)}")

    candidates = sorted(candidate_strings(value))
    lines.append("Detected SKU-like strings:")
    if candidates:
        lines.extend(f"  {item}" for item in candidates[:200])
    else:
        lines.append("  <none detected>")

    lines.append("Relevant scalar paths and values:")
    relevant = all_name_like_strings(value)
    if relevant:
        for path, scalar in relevant[:300]:
            lines.append(f"  {path} = {scalar}")
    else:
        lines.append("  <none detected>")

    return lines, candidates


west_lines, west_candidates = describe("West US 3", west)
east_lines, east_candidates = describe("East US", east)

common = sorted(set(west_candidates) & set(east_candidates))
common_d2 = [item for item in common if re.match(r"^Standard_D2", item, re.I)]

lines = west_lines + [""] + east_lines + [""]
lines.append("## Cross-region comparison")
lines.append("Common detected SKU strings:")
lines.extend(f"  {item}" for item in common[:300])
if not common:
    lines.append("  <none detected>")

lines.append("Common detected D2 strings:")
lines.extend(f"  {item}" for item in common_d2)
if not common_d2:
    lines.append("  <none detected>")

summary_path.write_text("\n".join(lines) + "\n")

print("\n".join(lines[:500]))
print()
print(f"Full schema summary: {summary_path}")
PY

section "Discovery completed"

echo "Raw West JSON: $WEST_JSON"
echo "Raw East JSON: $EAST_JSON"
echo "Schema summary: $SUMMARY"
echo
echo "No Azure resources were changed."
echo
echo "************************************************************"
echo "POSTGRESQL SKU DISCOVERY READY"
echo "************************************************************"
